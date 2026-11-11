# Architecture Review: ManufactureCost unit tests

## Skip Design: true
Test-only, backend, no UI.

## Architectural Fit Assessment
`ManufactureCost` is a plain domain class in `Anela.Heblo.Domain/Features/Catalog` with no dependencies. Existing Catalog tests live in `backend/test/Anela.Heblo.Tests/Features/Catalog/` (namespace `Anela.Heblo.Tests.Features.Catalog`, xUnit + FluentAssertions). Domain-level tests (e.g. `BundleProductRuleTests`) sit in that same folder, so no new project or folder is needed. The class is only referenced from its own file, so there are no consumers to regress.

## Proposed Architecture

### Component Overview
`ManufactureCostTests` (new) -> `ManufactureCost` (unchanged). Direct instantiation via object initializer; no mocks.

### Key Design Decisions

#### Decision 1: Characterize existing behaviour, no production change
**Options considered:** (a) test only; (b) make cutoff internal/injectable.
**Chosen approach:** (a). The cutoff is private static; tests hard-code 2025-08-25 literal dates.
**Rationale:** Surgical-change rule; issue asks for coverage only. Hard-coded dates also guard against accidental cutoff edits.

#### Decision 2: Boundary semantics
Exact `2025-08-25 00:00` -> purchase price (strict `>`); same day after midnight -> receipt document. Tests assert this as-is and name it explicitly.

#### Decision 3: Theory vs Facts
Use `[Theory]` with `[InlineData]` for date-only cases (strings parsed via `DateTime.Parse(..., InvariantCulture)` or separate year/month/day ints), and `[Fact]` for time-of-day boundary cases. Use distinct decimal values (e.g. receipt 100, purchase 60, handling 7) so wrong-source bugs surface.

## Implementation Guidance

### Directory / Module Structure
Create `backend/test/Anela.Heblo.Tests/Features/Catalog/ManufactureCostTests.cs`.

### Interfaces and Contracts
Public API under test: `Date`, `HandlingCost`, `MaterialCostFromReceiptDocument`, `MaterialCostFromPurchasePrice`, `MaterialCost`, `Total`.

### Data Flow
Arrange object -> read computed property -> assert.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Culture-dependent date parsing | Low | Use `new DateTime(y,m,d)` ints, not string parsing |
| Decimal comparison | Low | Exact decimals, `Should().Be` |
| Future intentional cutoff change breaks tests | Low | Intended; tests document cutoff |

## Specification Amendments
None.

## Prerequisites
None. Validate with `dotnet build` , `dotnet format`, and `dotnet test --filter ManufactureCostTests`.
