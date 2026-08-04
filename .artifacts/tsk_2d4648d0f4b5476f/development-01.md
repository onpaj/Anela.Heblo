# Development: Require authentication on `ManufactureSettingsController`

Implements design-01.md exactly as specified — no deviations.

## Files changed

### `backend/src/Anela.Heblo.API/Controllers/ManufactureSettingsController.cs` (modified)

- Base class changed from `ControllerBase` to `BaseApiController`.
- Added class-level `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]`
  (default `AccessLevel.Read`).
- Removed `[AllowAnonymous]` and the now-unused `Microsoft.AspNetCore.Authorization`
  using directive; added `Anela.Heblo.Domain.Features.Authorization`.
- `GetSettings` now returns `Task<ActionResult<GetManufactureSettingsResponse>>`
  and routes the mediator response through `HandleResponse`, so a future
  `Success == false` response maps to the correct HTTP status instead of
  always 200.

No changes to `GetManufactureSettingsHandler`, `GetManufactureSettingsRequest`,
`GetManufactureSettingsResponse`, or `ManufactureErpOptions` — purely a
transport/authorization change, matching the design's stated scope.

### `backend/test/Anela.Heblo.Tests/Features/Manufacture/Settings/GetManufactureSettingsEndpointTests.cs` (modified)

- Removed `GetSettings_ShouldBeReachableAnonymously` — it asserted the
  vulnerable behavior and, per the design's finding, can't be meaningfully
  repurposed under `HebloWebApplicationFactory`'s mock auth (which
  unconditionally authenticates every request as `SuperUser` regardless of
  the `Authorization` header, so an HTTP-level anonymous-access test can't
  prove or disprove the gate).
- Removed the now-unused `System.Net` using directive.
- The two content tests (`GetSettings_ShouldReturnSuccessAndCorrectContentType`,
  `GetSettings_ShouldExposeManufactureGroupIdField`) are unchanged and still
  pass, since the mock-auth `SuperUser` client satisfies the new
  `[FeatureAuthorize]` gate.

### `backend/test/Anela.Heblo.Tests/Authorization/ManufactureSettingsControllerAuthorizationTests.cs` (new)

Reflection-based authorization tests following the
`GridLayoutsControllerAuthorizationTests` / `DiagnosticsControllerTests`
pattern (the only way to verify gating given the mock-auth constraint above):

- `Controller_IsGatedByFeatureAuthorize` — asserts the class carries
  `[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]`.
- `GetSettings_DoesNotAllowAnonymous` — asserts the action has no
  `[AllowAnonymous]`.

This controller is now also covered by the existing repo-wide
`GateConsistencyTests.EveryGatedEndpoint_HasFeatureAuthorize` check (no
changes needed to that test — it just stops treating this controller as an
exception).

## Verification performed

1. `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (251 pre-existing
   warnings unrelated to this change). Note: the API project's post-build
   `AccessMatrixGen` codegen step threw a `JsonException` reading a
   generated-artifact path in this sandbox (`warning MSB3073`, exit code
   134) — this is a pre-existing environment quirk unrelated to the
   controller change (unaffected file path resolution in this checkout) and
   did not fail the build.
2. `dotnet format Anela.Heblo.sln --verify-no-changes --include <changed files>`
   — passed, no formatting changes needed.
3. `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~ManufactureSettings|FullyQualifiedName~GateConsistencyTests|FullyQualifiedName~AuthorizationIntegrationTests"`
   — 17 passed, 1 skipped (pre-existing, unrelated:
   `AdminGroups_ReturnsSeededGroups`), 0 failed. Confirmed:
   - `ManufactureSettingsControllerAuthorizationTests` (both new tests) pass.
   - `GateConsistencyTests.EveryGatedEndpoint_HasFeatureAuthorize` and
     `EveryMenuPath_FeatureHasController` pass with this controller now
     included in the gated set.
   - `GetManufactureSettingsEndpointTests`'s two remaining content tests
     pass unmodified.
   - `AuthorizationIntegrationTests` suite unaffected.
4. Frontend grep confirms `useManufactureSettingsQuery` (the sole API
   caller) is used only from `CreateManufactureOrderModal.tsx`,
   `ManufactureOrderFilters.tsx`, and `BasicInfoSection.tsx` — all
   Manufacture Orders UI, consistent with gating on
   `Feature.Manufacture_ManufactureOrders`. No anonymous/public frontend
   path exists; no frontend code changes required.

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~ManufactureSettings|FullyQualifiedName~GateConsistencyTests"
```

`GET /api/manufacture/settings` now requires an authenticated caller with
`Manufacture_ManufactureOrders` Read (or higher, or `SuperUser`) — anonymous
callers get 401, authenticated-but-unpermissioned callers get 403.
