# Development — Gate `DepartmentsController` with `[FeatureAuthorize]`

## Summary

Implemented exactly per `design-01.md` / `architecture-01.md`: `DepartmentsController` was carrying no authorization metadata at all, making `GET /api/departments` anonymously reachable (it exposes internal FlexiBee accounting department data). Added the class-level `[FeatureAuthorize(Feature.Finance_FinancialOverview, Feature.Purchase_InvoiceClassification)]` OR-semantics gate (the same mechanism already used by `RecurringJobsController`), plus a reflection-based regression test.

## Files changed

- **`backend/src/Anela.Heblo.API/Controllers/DepartmentsController.cs`** (modified, +3 lines)
  - Added `using Anela.Heblo.Domain.Features.Authorization;`
  - Added class-level `[FeatureAuthorize(Feature.Finance_FinancialOverview, Feature.Purchase_InvoiceClassification)]`
  - Added `[ProducesResponseType(StatusCodes.Status401Unauthorized)]` on `GetDepartments` for OpenAPI accuracy (matches the `RecurringJobsController` pattern; no behavioral effect)
  - No other changes — constructor, action body, routes, and return type are untouched.

- **`backend/test/Anela.Heblo.Tests/Authorization/DepartmentsControllerAuthorizationTests.cs`** (new)
  - `Controller_IsGatedByFeatureAuthorize` — asserts the `FeatureAuthorizeAttribute` is present and its `Roles` string contains both `AccessRoles.FinanceFinancialOverviewRead` and `AccessRoles.PurchaseInvoiceClassificationRead`. Asserts on `.Roles` rather than `.Feature`, per the plan's explicit caution: the multi-feature constructor collapses `.Feature` to only the first argument, so a `.Feature`-based assertion would give a false pass on the second role.
  - `GetDepartments_DoesNotAllowAnonymous` — asserts the action carries no `[AllowAnonymous]`.
  - Mirrors `ManufactureSettingsControllerAuthorizationTests.cs` structurally.

No frontend, DTO, contract, or database changes — route, verb, and response shape (`GetDepartmentsResponse`/`DepartmentDto`) are unchanged, so no OpenAPI client regeneration is required. `backend/test/Anela.Heblo.Tests/Controllers/DepartmentsControllerTests.cs` was left untouched as planned (it invokes the action in-process, bypassing ASP.NET Core's authorization middleware, so it's unaffected by an attribute-only change).

## Verification performed

1. `dotnet build Anela.Heblo.sln` — **0 errors** (251 pre-existing warnings, unrelated to this change).
2. `dotnet test --filter "FullyQualifiedName~DepartmentsController|FullyQualifiedName~Authorization"` — **140 passed, 1 skipped (pre-existing), 0 failed**, including both new `DepartmentsControllerAuthorizationTests` facts.
3. `dotnet test --filter "FullyQualifiedName~DepartmentsControllerTests"` — **3 passed, 0 failed** (confirms the existing behavior tests are unaffected).
4. `dotnet format Anela.Heblo.sln --include <changed files> --verify-no-changes` — exit 0, no formatting changes needed.
5. `git status --short` / `git diff --stat` — confirmed only the two intended files touched (one modified, one new); no frontend files changed.

## How to verify

```bash
export PATH="$HOME/.dotnet:$PATH"
cd backend
dotnet build ../Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DepartmentsController"
```

## Behavioral contract after this change

| Caller | Before | After |
|---|---|---|
| Unauthenticated | `200 OK` | `401 Unauthorized` |
| Authenticated, holds neither `Finance_FinancialOverview` nor `Purchase_InvoiceClassification` | `200 OK` | `403 Forbidden` |
| Authenticated, holds either feature (Read) | `200 OK` | `200 OK` (unchanged) |

## Notes / open items carried from architecture review (not actioned, out of scope)

- The feature choice (`Finance_FinancialOverview` OR `Purchase_InvoiceClassification`) is inferred from the two current frontend consumers, not from an explicit product decision — flagged for review, not assumed.
- A global `FallbackPolicy` (to make future omitted-attribute controllers fail closed by default) was explicitly out of scope for this single-controller fix, per the finding and plan.
