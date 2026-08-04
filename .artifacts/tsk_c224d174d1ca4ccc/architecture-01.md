# Architecture review: Remove SafeMarginCalculator / SalesCost dead code

## Verdict

**Design approved as pure deletion**, with one correction to the design's own reasoning (not to its outcome) and one added guardrail for the implementation step.

## Alignment with existing invariants

- **Module boundary / DI ownership (`development_guidelines.md`, ADR-004)**: the DI line lives in `CatalogModule.cs`, the module that owns `SafeMarginCalculator`. Removing it is a same-module edit — no cross-module wiring, no `PersistenceModule` involvement, no boundary violation.
- **`MarginCalculationService` is untouched** and remains the sole production margin pipeline for Catalog (`MarginLevel.Create`, `MarginCalculationService.cs:108-117`). Confirmed no reference to `SafeMarginCalculator` anywhere in `MarginCalculationService`.
- **No persisted/API-facing contract impact**: neither `SafeMarginCalculator`, `MarginCalculationResult` (Catalog's), nor `SalesCost` is a `Contracts/` DTO, EF entity, or MediatR request/response type, so the "DTOs live in `contracts/`" and "no shared EF entities" rules aren't implicated.
- **Test project structure**: `SafeMarginCalculatorTests.cs` is self-contained (own `Mock<ILogger<>>`, direct instantiation, no shared fixture/`IClassFixture`/xUnit `Collection`), so deleting it can't orphan a fixture other tests depend on. Confirmed via grep — no other test file references `SafeMarginCalculatorTests`.

## Correction to the design's stated reasoning (does not change the plan)

The design (design-01.md line 28) describes `MarginCalculationResult` as "used only as `SafeMarginCalculator`'s return type. No other type produces or consumes it." **This is only true within the `Catalog.Services` namespace.** There are actually **two distinct, same-named classes**:

| Class | Namespace | Status |
|---|---|---|
| `MarginCalculationResult` (nested in `SafeMarginCalculator.cs`) | `Anela.Heblo.Application.Features.**Catalog**.Services` | Dead — only `SafeMarginCalculatorTests.cs` references it |
| `MarginCalculationResult` (`Analytics/Services/MarginCalculationResult.cs`) | `Anela.Heblo.Application.Features.**Analytics**.Services` | **Live production type** — consumed by `IMarginCalculator`/`MarginCalculator`, `GetProductMarginSummaryHandler`, `MonthlyBreakdownGenerator`, and their tests |

A bare-name grep for `MarginCalculationResult` (which I ran independently) surfaces both; the plan and design only ever grepped `SafeMarginCalculator` and `SalesCost` by name, so they never actually exercised the query that would have surfaced this collision. I traced every hit by namespace/using-directive and confirmed the Analytics usages (`GetProductMarginSummaryHandler.cs`, `MonthlyBreakdownGenerator.cs`, `GetProductMarginSummaryHandlerTests.cs`) all resolve via `using Anela.Heblo.Application.Features.Analytics.Services;`, never `Catalog.Services`. The two types are unrelated and the deletion target is unambiguous. **The plan's conclusion (delete the Catalog one) is correct** — but the "no other type produces or consumes it" claim is imprecise and would mislead a future reader who trusts the design doc over the actual source.

**Implementation guardrail**: when deleting, act on `SafeMarginCalculator.cs` by **file path**, not by an IDE "rename/delete symbol by name" action across the solution — the latter risks the tool matching both same-named classes if namespace resolution is fumbled. A plain `rm backend/src/.../Catalog/Services/SafeMarginCalculator.cs` is safe and is what the plan already specifies.

## Verification performed this step

- Re-ran `grep -rn "SafeMarginCalculator"` and `grep -rn "\bSalesCost\b"` across `backend/` — same result set as plan/design (DI line + definition + own test only; zero hits for `SalesCost` beyond its own file).
- Ran `grep -rn "MarginCalculationResult"` (a query the prior steps didn't run) and traced every hit to namespace — confirms the Catalog/Analytics split above.
- Found and confirmed `CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices` (`backend/test/Anela.Heblo.Tests/Infrastructure/CompositionRootTests.cs:20`) builds the full DI container with `ValidateOnBuild = true`. This test already exists and gives a strong positive control: if the deletion leaves any dangling reference or breaks the DI graph, this test fails immediately. **Recommend the implementation step explicitly runs this test** (in addition to the full suite) as its primary correctness signal, since it's a direct, already-existing check for exactly this class of change.
- Confirmed no `Architecture/ModuleBoundariesTests.cs` reference to `SafeMarginCalculator`/`SalesCost` — the reflection-based boundary tests don't special-case these types, so no test update needed there.
- Confirmed `SafeMarginCalculatorTests.cs` (223 lines) is a single self-contained test class with no shared fixtures.

## Implementation guidance

No changes to plan-01.md / design-01.md's four-file, one-line-removal shape. Execute exactly as designed:

1. Delete `backend/src/Anela.Heblo.Application/Features/Catalog/Services/SafeMarginCalculator.cs` (file path, not symbol-search-and-delete — see guardrail above).
2. Delete `backend/src/Anela.Heblo.Application/Features/Catalog/Services/SalesCost.cs`.
3. Delete `backend/test/Anela.Heblo.Tests/Features/Catalog/SafeMarginCalculatorTests.cs`.
4. Remove `services.AddTransient<SafeMarginCalculator>();` from `CatalogModule.cs:102`.
5. `dotnet build`, then run the full backend test suite — pay particular attention to `CompositionRootTests` and anything under `Features/Analytics/` (to reconfirm the untouched `MarginCalculationResult` there still compiles/passes, since it shares a name but not a file).
6. `dotnet format`.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Name collision between Catalog's and Analytics' `MarginCalculationResult` causes confusion during implementation (wrong file edited, or a reviewer flags a false cross-reference) | Documented above with exact namespace trace; implementer deletes by file path, and should re-run the Analytics test suite as a sanity check even though no Analytics file is touched |
| Dangling DI reference after line removal | Already covered by the existing `ServiceContainer_ValidateOnBuild` test — no new test needed |
| Hidden reflection-based consumer (e.g. some generic "resolve all `IXyz` from assembly" scan) picks up `SafeMarginCalculator` | Not applicable — `SafeMarginCalculator` implements no interface (registered as a concrete `AddTransient<SafeMarginCalculator>()`), so it can't be a target of an interface-based reflection scan. Confirmed by reading the class definition (`public class SafeMarginCalculator`, no `: ISomething`). |

## Prerequisites before implementation

None outstanding. Both the "no intended caller" premise and the design's boundary/DI reasoning are now independently verified against current source, including the one gap the earlier steps left unchecked (the `MarginCalculationResult` name collision). Implementation can proceed directly from design-01.md's implementation shape.
