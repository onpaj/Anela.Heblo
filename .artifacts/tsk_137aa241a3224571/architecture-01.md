# Architecture review — Gate `DepartmentsController`

## Verdict

**Approved as designed.** This is a minimal, well-precedented fix — one class-level attribute plus a reflection test mirroring an already-accepted sibling fix. No structural concerns. I verified every load-bearing claim in `plan-01.md`/`design-01.md` against current source; all check out.

## Verification performed

- `DepartmentsController.cs` (current, on disk): confirmed no `[Authorize]`/`[FeatureAuthorize]`/`[AllowAnonymous]` anywhere — matches the finding.
- `FeatureAuthorizeAttribute.cs`: confirmed the `params Feature[]` constructor exists exactly as described — `Feature = features[0]`, `Roles = string.Join(",", features.Select(f => AccessRoles.For(f, AccessLevel.Read)))`. The design's caution (assert on `.Roles`, not `.Feature`, in the test) is correct and necessary — `.Feature` would silently collapse to only `Finance_FinancialOverview`.
- `RecurringJobsController.cs:54`: confirmed live use of the same multi-feature OR constructor (`FeatureAuthorize(Feature.Jobs_Trigger, Feature.Jobs_Disable, Feature.Admin_Administration)`) — this is a real, already-shipped pattern, not a novel mechanism being introduced for this fix.
- `AccessRoles.generated.cs`: confirmed both `FinanceFinancialOverviewRead = "finance.financial_overview.read"` and `PurchaseInvoiceClassificationRead = "purchase.invoice_classification.read"` exist — no codegen step needed before this can compile and pass.
- `ManufactureSettingsControllerAuthorizationTests.cs` (the cited precedent): read in full — the new test's structure (two `[Fact]`s: attribute-presence/coverage, and no-`[AllowAnonymous]`-on-action) mirrors it exactly, with the one intentional deviation flagged above.
- `AuthenticationExtensions.cs`: confirmed `DefaultPolicy` is defined, `FallbackPolicy` is not — the "opt-in only" authorization model described in the finding is accurate, and leaving `FallbackPolicy` out of scope is consistent with this being a narrowly-scoped, single-controller fix rather than a cross-cutting policy change.
- Frontend consumers: `App.tsx` routes `/finance/overview` and `/purchase/invoice-classification` (which render `FinancialOverview` and `InvoiceClassificationPage`, the two `useDepartments` consumers) are both wrapped in `<RequireMenuPath path={path}>`, i.e. gated by the app's menu/permission system, not reachable anonymously. This supports the plan's premise that no currently-authorized frontend flow depends on anonymous access to `/api/departments`. (The route→`Feature` enum mapping lives in backend menu/permission config, not inline in the TSX — I didn't chase it further since it isn't load-bearing for this fix: the fix only needs "these screens are gated by *something* already," not the exact enum wiring, and that's independently confirmed by the routes being wrapped in `RequireMenuPath`.)

## Alignment with existing patterns

- **Attribute placement (class-level) and OR-semantics constructor**: matches `RecurringJobsController` precedent exactly — same attribute class, same constructor overload, same "single capability reachable through multiple permissions" scenario (department list is needed by both Finance and Purchase/Invoice-Classification features).
- **"No silent third state" convention** (`development_guidelines.md:15`): the fix closes the gap correctly — after this change, every controller in the codebase either carries an authorization gate or an explicit `[AllowAnonymous]`. Confirmed by re-reading the finding's evidence: this was accurately characterized as a real, project-wide invariant, not an ad hoc rule invented for this finding.
- **Reflection-based authorization regression tests**: this project already uses this style (`ManufactureSettingsControllerAuthorizationTests.cs`) as its standard way of pinning down "this controller must stay gated" — the new test fits the established test-authoring pattern for this exact defect class, in the right directory (`backend/test/Anela.Heblo.Tests/Authorization/`).
- **DTOs-as-classes rule**: not implicated — no DTO changes in this fix.
- **No frontend/contract change**: correctly scoped — route, verb, and response shape (`GetDepartmentsResponse`/`DepartmentDto`) are untouched, so no OpenAPI client regen is needed. Confirmed no such regen step was proposed.

## Design decisions assessed

**Feature choice (`Finance_FinancialOverview` OR `Purchase_InvoiceClassification`) vs. plain `[Authorize]`.** The plan explicitly flags this as an inferred-from-usage decision rather than a confirmed product requirement, and defaults to the narrower, least-privilege option consistent with sibling controllers (`OrgChartController`, `UserManagementController`) being feature-gated rather than merely authenticated. I agree with this default — least privilege is the right bias when the product intent is undocumented, and it's trivially reversible (widen to `[Authorize]` later) if a legitimate third consumer needing broader access shows up. No architectural objection; this is a product-scoping question, not a structural one, and the plan correctly surfaces it as an open question rather than silently deciding it.

**Test assertion on `.Roles` instead of `.Feature`.** Correct and necessary given the constructor's actual behavior (verified above). This is exactly the kind of subtlety a naive copy-paste of the `ManufactureSettingsController` single-feature test would get wrong, and the plan/design both explicitly call it out.

**`FallbackPolicy` left out of scope.** Correct scoping call. Adding a `FallbackPolicy` is a cross-cutting change that would affect all 45+ controllers' behavior for any future omitted-attribute case; bundling it into a single-controller fix would inflate blast radius for no benefit to this finding. Flagging it as a separate maintainer-level follow-up (as the finding itself suggests) is the right call, not a gap in this design.

## Implementation guidance

No deviation needed from `design-01.md`. Straightforward execution:

1. Add `using Anela.Heblo.Domain.Features.Authorization;` and the class-level `[FeatureAuthorize(Feature.Finance_FinancialOverview, Feature.Purchase_InvoiceClassification)]` to `DepartmentsController`.
2. Add `backend/test/Anela.Heblo.Tests/Authorization/DepartmentsControllerAuthorizationTests.cs` exactly as drafted in `design-01.md` — it compiles against real, verified symbols (`FeatureAuthorizeAttribute`, `AccessRoles.FinanceFinancialOverviewRead`, `AccessRoles.PurchaseInvoiceClassificationRead`).
3. The optional `[ProducesResponseType(StatusCodes.Status401Unauthorized)]` Swagger annotation is fine either way — it's cosmetic and doesn't affect runtime behavior or test outcomes; leave the implementer's call as the design suggests.
4. Existing `DepartmentsControllerTests.cs` needs no change — confirmed it invokes the action in-process (bypassing ASP.NET Core's authorization middleware), so it's unaffected by an attribute-only change.

## Risks and mitigations

- **Risk: undocumented anonymous consumer of `/api/departments` breaks.** Mitigation already in the plan (flagged as an open question, backed by the frontend route-gating check I performed above, which found no anonymous path to either consumer screen). Residual risk is low and acceptable — same risk profile as the accepted `ManufactureSettingsController`/`DiagnosticsController` fixes.
- **Risk: test asserts wrong property and gives a false pass.** Mitigated by construction — the design already made the correct choice (`.Roles`, not `.Feature`) and I independently confirmed why via the attribute source.
- **No prerequisites** — no codegen, no migration, no frontend change required before implementation can start.

## Summary for implementer

Proceed exactly as `design-01.md` specifies. Two file changes, no other surface area. Verification plan in the design doc (build → tests → format → diff --stat) is sufficient; no additional steps needed from an architecture standpoint.
