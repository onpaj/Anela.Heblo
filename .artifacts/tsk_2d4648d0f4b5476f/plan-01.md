# Plan: Require authentication on `ManufactureSettingsController` and stop leaking the Entra ID group id

## Summary
`ManufactureSettingsController` is the only controller in the Manufacture module that opts out of the app's secure-by-default policy: it doesn't derive from `BaseApiController`, and its single action carries `[AllowAnonymous]` with no `[FeatureAuthorize]`, returning the tenant's Entra ID manufacture-group GUID to unauthenticated callers. The fix brings it in line with its four sibling controllers (`ManufacturedProductInventoryController`, `ManufactureStockTakingController`, `MaterialContainersController`, `LotsController`): derive from `BaseApiController`, drop `[AllowAnonymous]`, gate the read behind `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]`, and route the response through `HandleResponse`. This is the same class of fix as the recently merged `DiagnosticsController` finding (commit c46d864d).

## Context
The sole frontend consumer (`frontend/src/api/hooks/useManufactureSettings.ts:11-12`) already calls through `getAuthenticatedApiClient()`, so the anonymous access buys no consumer any benefit — it's a pure hole. The response DTO's only field, `ManufactureGroupId` (`ManufactureErpOptions.cs:14-19`), is documented as "Entra ID group identifier consumed by GetManufactureSettings," i.e. directory reconnaissance information that should sit behind the app's `DefaultPolicy` (`AuthenticationExtensions.cs:108-111`, `RequireAuthenticatedUser()` + `RequireRole(AccessRoles.Base)`) like every other endpoint.

Existing usages of the settings value are all on the Manufacture Orders creation/detail flow (`CreateManufactureOrderModal.tsx`, `ManufactureOrderFilters.tsx`, `BasicInfoSection.tsx` under `manufacture/detail`), which is why `Feature.Manufacture_ManufactureOrders` — the feature gating `ManufactureOrderController` — is the closest existing fit rather than inventing a new feature flag.

There's already a test, `GetManufactureSettingsEndpointTests.GetSettings_ShouldBeReachableAnonymously` (`backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsEndpointTests.cs:40-47`), that currently asserts and locks in the vulnerable behavior — it must be flipped to assert 401/403 for anonymous callers, following the `DiagnosticsControllerTests` precedent (`Controller_ShouldRequireAuthorization`, `Actions_ShouldNotAllowAnonymous`).

## Functional requirements

**FR-1: Require authentication + Base role on the endpoint**
- Remove `[AllowAnonymous]` from `GetSettings`.
- Change `ManufactureSettingsController` to derive from `BaseApiController` instead of `ControllerBase` (matches all four sibling controllers).
- Acceptance criteria:
  - An anonymous `GET /api/manufacture/settings` returns 401 (or 403, per `PermissionAuthorizationResultHandler`), not 200.
  - An authenticated caller holding the required role/permission still receives 200 with the existing `GetManufactureSettingsResponse` body shape (`ManufactureGroupId`, `Success`, etc. unchanged).

**FR-2: Gate the read behind a feature permission, consistent with sibling controllers**
- Add `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]` at the controller (class) level — read-level by default, matching the pattern used by `ManufacturedProductInventoryController`, `LotsController`, etc.
- Acceptance criteria:
  - A user with `Manufacture_ManufactureOrders` Read access can call the endpoint successfully.
  - A user authenticated but without any Manufacture permission is rejected (403), consistent with how `AccessRoles.For(feature, level)` resolves roles for the other Manufacture endpoints.

**FR-3: Route the response through `HandleResponse` instead of returning the raw MediatR response**
- Change the action signature from `Task<GetManufactureSettingsResponse>` to `Task<ActionResult<GetManufactureSettingsResponse>>`, await the mediator call, and return `HandleResponse(response)` — matching every sibling controller's action shape.
- Acceptance criteria:
  - A `Success == true` response still yields HTTP 200 with the same JSON body as before.
  - If the handler ever returns `Success == false` (currently it doesn't, per `GetManufactureSettingsHandler`), the controller now maps it to the correct non-200 status via `BaseApiController.HandleResponse`, instead of always emitting HTTP 200.

**FR-4: Update/add tests to reflect the new authorization requirement**
- Flip `GetSettings_ShouldBeReachableAnonymously` in `GetManufactureSettingsEndpointTests.cs` to assert the anonymous request is now rejected (401/403), renaming it accordingly (e.g. `GetSettings_ShouldRejectAnonymousCaller`).
- Add/adjust coverage so `GetSettings_ShouldReturnSuccessAndCorrectContentType` and `GetSettings_ShouldExposeManufactureGroupIdField` run against an authenticated client (check how `HebloWebApplicationFactory` / other endpoint tests in this suite authenticate — e.g. mock-auth mode or a pre-authenticated `HttpClient` helper — and reuse that pattern rather than inventing a new one).
- Acceptance criteria:
  - All three existing tests in `GetManufactureSettingsEndpointTests.cs` pass under the new authorization requirement.
  - A new/updated test proves anonymous access is rejected.

## Non-functional requirements
- **Security**: no anonymous network path may read the Entra ID `ManufactureGroupId` after this change — closing the same class of hole as the `DiagnosticsController` fix (commit c46d864d).
- No new dependencies; change confined to one controller, its DTO usage is untouched, plus the one existing test file (and any new supporting test).

## Data model
- N/A — no persisted entities involved; `GetManufactureSettingsResponse`/`GetManufactureSettingsRequest`/`ManufactureErpOptions` are unchanged in shape.

## Interfaces
- `GET /api/manufacture/settings` — same route and response shape; now requires an authenticated caller with `Manufacture_ManufactureOrders` Read access (or higher) instead of being anonymous. This is a breaking change for any unauthenticated caller — the only known consumer (`useManufactureSettings.ts`) already authenticates, so no breakage expected there.

## Dependencies and scope
- In scope: `ManufactureSettingsController.cs` (base class, attributes, action signature/return) and `GetManufactureSettingsEndpointTests.cs` (existing test file, update + one new test).
- Out of scope:
  - Adding a new `Feature` enum value dedicated to "settings" — `Feature.generated.cs`/`AccessRoles.generated.cs`/`AccessMatrix.generated.cs` are auto-generated ("Do not edit by hand" headers) by `Anela.Heblo.AccessMatrixGen`; reusing the existing `Manufacture_ManufactureOrders` feature (the module the settings value is actually consumed by) avoids a codegen change for a one-field read endpoint.
  - Any change to `GetManufactureSettingsHandler`, `GetManufactureSettingsRequest`, or `ManufactureErpOptions` — this is purely a transport/authorization fix.
  - Confirming the frontend is unaffected beyond static code inspection — `useManufactureSettings.ts` already calls `getAuthenticatedApiClient()`, so no frontend code change is expected; no frontend build/test changes are in scope.

## Rough plan
1. Edit `ManufactureSettingsController.cs`: change base class to `BaseApiController`, add `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]` at class level, remove `[AllowAnonymous]`, change the action to `Task<ActionResult<GetManufactureSettingsResponse>>` awaiting the mediator call and returning `HandleResponse(response)`; add the `Anela.Heblo.Domain.Features.Authorization` using.
2. Inspect `HebloWebApplicationFactory` / a sibling endpoint test (e.g. one under `Features/Manufacture/`) to find this suite's existing pattern for issuing an authenticated request, and reuse it.
3. Update `GetManufactureSettingsEndpointTests.cs`: rewrite `GetSettings_ShouldBeReachableAnonymously` to assert rejection of anonymous calls; switch the other two tests' `HttpClient` to an authenticated one if they aren't already; add a case that proves an authenticated user without `Manufacture_ManufactureOrders` access is rejected, if the test harness makes that easy to express (otherwise note as an open question / skip with justification).
4. Run `dotnet build` and `dotnet format` on the backend project; run the affected test file (and the broader Manufacture test folder) to confirm green.
5. Grep the frontend for any other direct/anonymous callers of `/api/manufacture/settings` beyond `useManufactureSettings.ts` to double-check no breakage; no frontend code changes expected.

## Open questions
- Whether `Feature.Manufacture_ManufactureOrders` is the right permission bucket, versus a broader/looser one (e.g. `Manufacture_ManufactureStock`) — defaulting to `ManufactureOrders` since that's the module every known frontend consumer belongs to (order creation/detail). Flag to the user if the settings value is intended to be readable by a wider Manufacture audience than order-management users.
- Exact mechanism `HebloWebApplicationFactory` uses for authenticated test requests (mock-auth mode vs. a helper `HttpClient`) wasn't confirmed against the actual test-infra file in this pass — the development step should inspect it first and follow the established pattern rather than introduce a new one.
