# Review — Gate `DepartmentsController` with `[FeatureAuthorize]`

## Verdict: done

## What I checked

1. **Diff against plan/design/architecture** (`git diff HEAD~1 HEAD` on the two touched files): matches exactly what `development-01.md` claims — a class-level `[FeatureAuthorize(Feature.Finance_FinancialOverview, Feature.Purchase_InvoiceClassification)]` on `DepartmentsController`, an added `[ProducesResponseType(StatusCodes.Status401Unauthorized)]` on the action, and a new `DepartmentsControllerAuthorizationTests.cs`. Only these two files changed (`git diff --stat`), no unrelated edits.

2. **Fixes the reported defect.** Before: no `[Authorize]`/`[FeatureAuthorize]`/`[AllowAnonymous]` anywhere on the controller → anonymous by omission. After: gated, closing the "silent third state" the finding called out. `GET /api/departments` now requires an authenticated caller holding either `finance.financial_overview.read` or `purchase.invoice_classification.read`.

3. **`FeatureAuthorizeAttribute` semantics verified against source** (`backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs`): it subclasses `AuthorizeAttribute`; the `params Feature[]` constructor sets `Roles` to a comma-joined list of the per-feature role strings, which ASP.NET Core's `AuthorizeAttribute.Roles` evaluates as OR — matches the intended "either feature" access described in plan/design/architecture.

4. **`AccessRoles` constants exist and map correctly** (`AccessRoles.generated.cs`): `FinanceFinancialOverviewRead` and `PurchaseInvoiceClassificationRead` are both present and correctly mapped from `(Feature.Finance_FinancialOverview, AccessLevel.Read)` / `(Feature.Purchase_InvoiceClassification, AccessLevel.Read)`. No codegen gap.

5. **Precedent for the multi-feature constructor is real, not novel**: `RecurringJobsController.cs:54` already uses `[FeatureAuthorize(Feature.Jobs_Trigger, Feature.Jobs_Disable, Feature.Admin_Administration)]` — same OR-semantics pattern, already shipped.

6. **Test correctness**: the new test asserts on `attribute.Roles` (containing both constant strings), not `.Feature` — correct, since the multi-feature constructor collapses `.Feature` to only the first argument (`features[0]`); an assertion on `.Feature` would give a false pass for the second required role. Independently confirmed by reading the constructor.

7. **Independently re-ran build and tests** (not just trusting the development report):
   - `dotnet build ../Anela.Heblo.sln` → **0 errors**, 251 pre-existing warnings (none newly introduced by this diff — the new warnings in the tail of build output are all in unrelated pre-existing files).
   - `dotnet test --filter "FullyQualifiedName~DepartmentsController|FullyQualifiedName~Authorization"` → **139 passed, 1 skipped (pre-existing, unrelated `AuthorizationIntegrationTests.AdminGroups_ReturnsSeededGroups`), 0 failed** — includes both new facts (`Controller_IsGatedByFeatureAuthorize`, `GetDepartments_DoesNotAllowAnonymous`).

8. **No frontend/contract impact**: route, verb, and response DTO (`GetDepartmentsResponse`/`DepartmentDto`) unchanged, so no OpenAPI regeneration required — correctly out of scope.

## Assessment

Implementation is surgical, matches the approved design precisely, uses an already-established codebase pattern (not a new mechanism), and is independently verified to build and pass tests. The two carried-forward open items (feature choice is inferred from usage rather than confirmed product decision; global `FallbackPolicy` is out of scope) are explicitly flagged as non-blocking follow-ups in both the architecture and development docs, which is the right call — neither is a functional requirement of this finding, and both are reversible/addressable later without rework.

No correctness bugs, no architecture conflicts, no missing required tests.

```json
{"outcome": "done", "summary": "Verified the diff (class-level [FeatureAuthorize(Feature.Finance_FinancialOverview, Feature.Purchase_InvoiceClassification)] on DepartmentsController + new DepartmentsControllerAuthorizationTests.cs) matches plan/design/architecture exactly, correctly closes the anonymous-access defect using an already-shipped OR-semantics pattern (verified against RecurringJobsController precedent and FeatureAuthorizeAttribute source), and independently re-ran build (0 errors) and targeted tests (139 passed, 0 failed) to confirm. No unrelated files touched, no frontend/contract impact."}
```
