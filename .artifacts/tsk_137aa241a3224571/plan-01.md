# Plan — DepartmentsController has no authorization attribute

## Summary

`DepartmentsController` (`backend/src/Anela.Heblo.API/Controllers/DepartmentsController.cs`) exposes `GET /api/departments` with no `[Authorize]`/`[FeatureAuthorize]`/`[AllowAnonymous]` metadata at all, so it falls outside both the app's `DefaultPolicy` (which only binds to endpoints already carrying `[Authorize]`) and the deliberate anonymous allow-list. It leaks the company's internal FlexiBee accounting department list to unauthenticated callers. The fix is to gate the controller with the multi-feature `FeatureAuthorize` OR-semantics constructor, matching its two genuine consumer features, and to lock the decision in with a reflection-based authorization test in the same style as the accepted `ManufactureSettingsController` and `StockUpOperationsController` fixes.

## Context

This is one of a batch of arch-review findings about controllers missing authorization gates (siblings already fixed: `ManufactureSettingsController` #3801, `DiagnosticsController` #3785). The project's convention (`docs/architecture/development_guidelines.md:15`) is: every controller either carries an explicit authorization gate or is explicitly `[AllowAnonymous]` — there is no third, silent state. `DepartmentsController` currently occupies that disallowed third state.

The endpoint returns `DepartmentDto[]` (id + name) sourced from `IDepartmentQueryService` → FlexiBee ERP accounting departments — internal org/accounting structure, not public data.

## Functional requirements

**FR-1 — Gate `DepartmentsController` behind authorization.**
Both actual consumers are authenticated, feature-gated frontend screens:
- `FinancialOverview.tsx` → gated by `Feature.Finance_FinancialOverview`
- `InvoiceClassification` (`RuleForm.tsx`, `RulesList.tsx`) → gated by `Feature.Purchase_InvoiceClassification`

No single feature covers both. `FeatureAuthorizeAttribute` already supports this exact shape — an OR-semantics `params Feature[]` constructor (`FeatureAuthorizeAttribute.cs:24-34`), documented for "a single capability... reached through several distinct permissions," and already used by `RecurringJobsController.cs:54`.

Apply at class level:
```csharp
[FeatureAuthorize(Feature.Finance_FinancialOverview, Feature.Purchase_InvoiceClassification)]
public class DepartmentsController : BaseApiController
```

*Acceptance criteria:*
- `DepartmentsController` carries a class-level `FeatureAuthorizeAttribute`.
- `GetDepartments` has no method-level `[AllowAnonymous]` or overriding `[Authorize]`.
- A caller holding either `Finance_FinancialOverview` or `Purchase_InvoiceClassification` (Read) can reach the endpoint; a caller holding neither gets `401`/`403`.
- No frontend regression: both `FinancialOverview` and `InvoiceClassification` screens already run behind their own feature gates, so their existing sessions already satisfy the new department gate — no frontend change needed.

**FR-2 — Add a regression test locking the gate in place.**
Add `backend/test/Anela.Heblo.Tests/Authorization/DepartmentsControllerAuthorizationTests.cs` mirroring `ManufactureSettingsControllerAuthorizationTests.cs` / `StockUpOperationsControllerAuthorizationTests.cs`:
- asserts `typeof(DepartmentsController).GetCustomAttribute<FeatureAuthorizeAttribute>()` is not null,
- asserts the attribute's `Roles` (or feature set) covers both `Finance_FinancialOverview` and `Purchase_InvoiceClassification`,
- asserts `GetDepartments` has no `AllowAnonymousAttribute`.

Since the multi-feature constructor collapses to `Feature = features[0]` (first element only) with `Roles` as the joined OR string, the test should assert on `attribute.Roles` (contains both roles) rather than `attribute.Feature`, unlike the single-feature precedent tests — note this explicitly so the test isn't written against the wrong property and give a false pass.

*Acceptance criteria:* new test file compiles, fails against the current (ungated) controller, passes after FR-1 is applied.

**FR-3 — Do not touch existing `DepartmentsControllerTests.cs` behavior tests.**
The existing unit tests (`backend/test/Anela.Heblo.Tests/Controllers/DepartmentsControllerTests.cs`) construct the controller directly and call the action method in-process — they bypass ASP.NET Core's authorization middleware entirely, so adding the attribute does not break them. Leave that file untouched; only add the new authorization test file.

## Non-functional requirements

- **Security**: no change to the FlexiBee data-access path; this only closes the authentication/authorization gap. No new PII exposure risk introduced.
- **Backward compatibility**: any actual anonymous caller of `/api/departments` (none identified — both known consumers are already authenticated, feature-gated screens) will start receiving `401`. Acceptable per finding; flag if the user knows of an undocumented anonymous consumer.

## Data model

No data model changes. `DepartmentDto { Id, Name }` and `GetDepartmentsRequest`/`GetDepartmentsResponse` are unaffected.

## Interfaces

- `GET /api/departments` — behavior unchanged for authorized callers; now returns `401 Unauthorized` (or `403 Forbidden` if authenticated but lacking both features) instead of `200` for unauthenticated/unauthorized callers.
- No new endpoints, no contract/DTO changes, no OpenAPI-client regeneration needed (route and response shape unchanged).

## Dependencies and scope

**In scope:**
- `backend/src/Anela.Heblo.API/Controllers/DepartmentsController.cs` — add class-level `[FeatureAuthorize(...)]`.
- New test file `backend/test/Anela.Heblo.Tests/Authorization/DepartmentsControllerAuthorizationTests.cs`.

**Out of scope (explicitly, per the finding):**
- Adding a global `FallbackPolicy` to `AuthenticationExtensions.cs` so omitted-attribute endpoints fail closed by default — cross-cutting change affecting all controllers, flagged by the finding as a separate, maintainer-level decision, not part of this fix.
- Any change to `IDepartmentQueryService` / `FlexiDepartmentQueryService` or the FlexiBee integration.
- Any frontend change — `useDepartments.ts` and its two consumers already run under authenticated sessions with the relevant feature flags on.

## Rough plan

1. Add `[FeatureAuthorize(Feature.Finance_FinancialOverview, Feature.Purchase_InvoiceClassification)]` to `DepartmentsController`.
2. Add `DepartmentsControllerAuthorizationTests.cs` per FR-2, asserting the attribute is present and covers both roles, and that no `[AllowAnonymous]` exists on the action.
3. Run `dotnet build` and the full backend test suite (existing `DepartmentsControllerTests.cs` + new authorization test + broader `Authorization` test folder) to confirm no regressions.
4. Run `dotnet format` per repo validation requirements.
5. No frontend build/lint needed (no frontend files touched) — confirm via `git diff --stat` that only the two backend files changed before declaring done.

## Open questions

- **Feature choice is inferred from current frontend consumers, not from an explicit product decision.** If departments are meant to be a broader, org-wide reference list (i.e., any authenticated user should see it, not just Finance/Purchase feature holders), a plain `[Authorize]` (as used by `DiagnosticsController`) would be the better fit instead of `FeatureAuthorize` with two specific features. Default taken here: scope to the two known consumer features, since that's the narrowest fix consistent with "least privilege" and with how sibling controllers (`OrgChartController`, `UserManagementController`) are gated — flag this choice for review rather than assuming broader intent.
- **Global `FallbackPolicy`** is noted in the finding as worth considering but is out of scope for this task — left as an open item for the maintainer, not actioned here.
