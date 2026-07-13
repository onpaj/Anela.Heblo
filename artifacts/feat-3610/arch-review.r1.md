# Architecture Review: Extract duplicated gift package metric calculation into a shared helper

## Skip Design: true

## Architectural Fit Assessment
This is a same-class, private-method extraction inside `GiftPackageManufactureService` (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`). No module boundary, DTO, contract, or public interface (`IGiftPackageManufactureService`) is touched. The class already follows the "extract shared calculation into a `private static` helper" pattern for `CalculateSeverity` (lines 341–356) and `CalculateStockCoveragePercent` (lines 358–369); this change simply applies the same pattern one level up, composing those two helpers into a third. There is no architectural risk — this is intra-file hygiene, fully consistent with the codebase's existing conventions and `docs/architecture/development_guidelines.md` (no new module, no DTO/contract change, no persistence change).

Existing test coverage in `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` exercises `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` through the public interface, so the refactor is verifiable by running the existing suite unmodified — no new test infrastructure is needed.

## Proposed Architecture

### Component Overview
No new components. Single file, single class:

```
GiftPackageManufactureService
├── GetAvailableGiftPackagesAsync   (public) -- calls ComputePackageMetrics (new)
├── GetGiftPackageDetailAsync       (public) -- calls ComputePackageMetrics (new)
├── CreateManufactureAsync          (public) -- unchanged
├── DisassembleGiftPackageAsync     (public) -- unchanged
├── ResolveDateRange                (private) -- unchanged
├── ComputePackageMetrics           (private, NEW) -- calls CalculateSeverity + CalculateStockCoveragePercent
├── CalculateSeverity               (private static) -- unchanged
└── CalculateStockCoveragePercent   (private static) -- unchanged
```

### Key Design Decisions

#### Decision 1: Tuple-returning private method vs. a dedicated result type
**Options considered:**
- (a) `private static` method returning a named-tuple `(decimal dailySales, int suggestedQuantity, GiftPackageSeverity severity, decimal stockCoveragePercent)`, as specified in brief and spec.
- (b) A small internal record/class (e.g. `PackageMetrics`) returned instead of a tuple.

**Chosen approach:** (a), per spec FR-1.

**Rationale:** The four values are consumed once each, immediately deconstructed into locals that already exist at both call sites (`dailySales`, `suggestedQuantity`, `severity`, `stockCoveragePercent`), and never cross a method/class boundary beyond this file. A tuple is the lowest-ceremony option and matches C# idioms already used elsewhere in this codebase for small private multi-value returns (see `ResolveDateRange`'s `(DateTime From, DateTime To, int Days)`). Introducing a new type for a private, single-file helper would be disproportionate. Note: the tuple element names in the spec (`dailySales`, `suggestedQuantity`, `severity`, `stockCoveragePercent`) match `ResolveDateRange`'s pattern of using named tuple elements for readability at the call site — keep that consistent.

#### Decision 2: `private static` vs `private` instance method
**Options considered:** instance method vs. `static`.

**Chosen approach:** `private static`, matching `CalculateSeverity` and `CalculateStockCoveragePercent`.

**Rationale:** `ComputePackageMetrics` has no dependency on instance state (`_manufactureClient`, `_catalogSource`, etc.) — it's a pure function of its three parameters, calling only the two existing static helpers. Marking it `static` is free (no behavior change, no test impact) and keeps the class's convention that pure calculation helpers are static while I/O-bound methods are instance methods. This also makes the helper trivially unit-testable in isolation later if ever needed, without needing to construct the service's dependencies.

## Implementation Guidance

### Directory / Module Structure
No new files. Add the method to the existing file, placed after `ResolveDateRange` and before `CalculateSeverity` (or immediately after `CalculateStockCoveragePercent`) — anywhere in the private-helpers block at the bottom of the class (lines 334–370) is acceptable; spec suggests grouping it alongside the other private helpers.

### Interfaces and Contracts
No public interface changes. New private signature only:

```csharp
private static (decimal dailySales, int suggestedQuantity, GiftPackageSeverity severity, decimal stockCoveragePercent)
    ComputePackageMetrics(LogisticsCatalogItem product, decimal salesCoefficient, int daysDiff)
```

Call sites deconstruct directly:
```csharp
var (dailySales, suggestedQuantity, severity, stockCoveragePercent) =
    ComputePackageMetrics(product, salesCoefficient, daysDiff);
```

### Data Flow
Unchanged. `product` (a `LogisticsCatalogItem`), `salesCoefficient`, and `daysDiff` (from `ResolveDateRange`) flow into the new helper exactly as they currently flow inline into the duplicated block; the four returned values feed the same `GiftPackageDto` construction as before, in both `GetAvailableGiftPackagesAsync` (per-item, in a loop) and `GetGiftPackageDetailAsync` (single item, then BOM/ingredient loading continues unchanged after the helper call).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Subtle arithmetic reordering changes rounding/decimal precision | Low | Preserve the exact two-step computation (`totalSalesInPeriod` then `dailySales`) as shown in spec FR-1, or verify the single-expression form is bit-for-bit equivalent for `decimal` arithmetic (it is, since `decimal` division/multiplication associativity here doesn't change results) before collapsing to one line; existing tests will catch any drift. |
| Regression in either call site during manual find-replace | Low | Existing tests in `GiftPackageManufactureServiceTests.cs` cover both public methods; run them post-refactor as the acceptance gate (spec FR-4). |

## Specification Amendments
None. The spec is complete, correctly scoped, and matches the codebase's existing patterns. No architectural changes are needed to it.

## Prerequisites
None. No migrations, config, or infrastructure changes required — implementation can start immediately.
