# Architecture Review: VatRateCalculator.FromPrices Unit Test Coverage

## Skip Design: true
This is a test-only addition to a pure static domain function. There is no UI, no new endpoint, no schema change, and no new public interface — nothing for a designer to define.

## Architectural Fit Assessment
`VatRateCalculator` lives in `backend/src/Anela.Heblo.Domain/Features/ProductPricing/VatRateCalculator.cs` as a stateless `public static class` with no injected dependencies — it is domain logic in the purest sense (Vertical Slice `Features/ProductPricing` area of the Domain layer, per `docs/architecture/filesystem.md`). The existing test suite already has a dedicated home for this feature area at `backend/test/Anela.Heblo.Tests/Features/ProductPricing/`, containing nine test files (`GetPriceDivergenceReportHandlerTests`, `PriceComparisonServiceTests`, `SetProductPriceHandlerTests`, etc.) that consistently use `xUnit` `[Fact]`/`[Theory]` + `FluentAssertions`, and Moq where a collaborator needs stubbing. `VatRateCalculator.FromPrices` needs no collaborator at all, so it is the simplest possible addition to that folder — a pure-function test class with no setup, no mocks.

I confirmed the test project (`Anela.Heblo.Tests.csproj`) does not reference `Anela.Heblo.Domain` directly, but transitively via its `ProjectReference` to `Anela.Heblo.Application` (which itself depends on Domain) — this transitive path is already exercised today (e.g. `IProductVatRateProvider`-adjacent ProductPricing domain types are reachable from existing tests in that folder), so no `.csproj` change is needed.

I also confirmed `FromPrices` has no current production caller (only `VatRateCalculator.StandardVatRate` is referenced, from `ShoptetEshopPriceClient.cs`). That is out of scope for this coverage-gap fix — the brief and spec ask only for tests of the existing function, not for wiring it into a caller.

## Proposed Architecture

### Component Overview
```
backend/src/Anela.Heblo.Domain/Features/ProductPricing/
  VatRateCalculator.cs                 (existing, untouched — static class, FromPrices + StandardVatRate)

backend/test/Anela.Heblo.Tests/Features/ProductPricing/
  VatRateCalculatorTests.cs            (NEW — pure unit tests, no mocks)
```
No other component is touched. No DI registration change (the class is `static`, never resolved from the container). No new project reference.

### Key Design Decisions

#### Decision 1: Test placement
**Options considered:** (a) new file in the existing `Features/ProductPricing` test folder; (b) a new `Domain` test folder mirroring `src/Anela.Heblo.Domain/...` exactly.
**Chosen approach:** (a) — add `VatRateCalculatorTests.cs` to `backend/test/Anela.Heblo.Tests/Features/ProductPricing/`, matching the layout every other ProductPricing test file already uses (the test project mirrors `Features/{Area}` folders, not the source project's `Domain`/`Application`/`Persistence` split).
**Rationale:** Consistency with the nine existing files in that exact folder. Introducing a parallel `Domain/ProductPricing` test folder for one file would fragment the area's test layout for no benefit.

#### Decision 2: Test style — Theory vs. separate Facts
**Options considered:** (a) one `[Theory]` with `[InlineData]` rows covering all four spec cases (both branches, standard rate, reduced rate, rounding edge); (b) separate `[Fact]` methods per branch/case.
**Chosen approach:** A `[Theory]`/`[InlineData]` for the parametrizable branch-and-formula cases (FR-1 through FR-3, and the rounding edge from FR-4), since they are all "given these two decimals, expect this decimal" shape — the same pattern `PriceComparisonDqtAdapterTests.cs` already uses for its `[Theory]`.
**Rationale:** Matches the strongest existing precedent in the same folder (`PriceComparisonDqtAdapterTests.GetDivergencesAsync_ClassifiesEachKindAsMismatchOrNot`) and keeps the four+ cases from FR-1–FR-4 compact and easy to extend.

## Implementation Guidance

### Directory / Module Structure
- Create exactly one new file: `backend/test/Anela.Heblo.Tests/Features/ProductPricing/VatRateCalculatorTests.cs`.
- No production file is created or modified. `VatRateCalculator.cs` stays as-is.

### Interfaces and Contracts
No new interfaces. The test class calls only the existing public surface:
- `Anela.Heblo.Domain.Features.ProductPricing.VatRateCalculator.FromPrices(decimal priceWithVat, decimal priceWithoutVat) : decimal`
- `Anela.Heblo.Domain.Features.ProductPricing.VatRateCalculator.StandardVatRate : decimal` (const `21m`)

Required `using`:
```csharp
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Xunit;
```
Namespace for the new test class: `Anela.Heblo.Tests.Features.ProductPricing` (matches sibling files in the folder).

### Data Flow
None — pure function in, decimal out. No data flow beyond method call → return value → assertion.

### Rounding-edge case (must hand-verify, not assume)
`Math.Round(decimal, int)` in .NET defaults to `MidpointRounding.ToEven` (banker's rounding). Pick a `priceWithVat`/`priceWithoutVat` pair whose raw `(priceWithVat / priceWithoutVat - 1) * 100m` lands exactly on a `.5` boundary — e.g. `priceWithoutVat = 200m`, `priceWithVat = 211m` → `(211/200 - 1) * 100 = 5.5` → `Math.Round(5.5m, 0)` rounds **to even**, i.e. `6m` (since 6 is even and 5 is odd, `MidpointRounding.ToEven` picks 6). The planner/developer must compute this in a scratch REPL or by reasoning through .NET's `ToEven` rule before hardcoding the expected value in `[InlineData]` — do not guess.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Hand-computed expected value for the rounding-edge test is wrong (rounding direction reasoned incorrectly) | Medium | Developer must run the exact `Math.Round` call (e.g. via `dotnet-script`/LINQPad-style scratch, or by first writing the assertion loosely, running the test, and reading the actual value from the failure message) before hardcoding the expected literal — never hand-derive without verifying against real `Math.Round` output. |
| Transitive project reference to `Anela.Heblo.Domain` silently breaks in the future (Application drops its Domain reference) | Low | Not a concern for this change; flagged only for awareness — no action needed now since the reference already works today via existing tests in the same folder. |

## Specification Amendments
None. The spec's FR-1 through FR-4 map directly onto the file/decision above with no gaps.

## Prerequisites
None — no migration, no config, no infrastructure. The developer can start directly on the test file.
