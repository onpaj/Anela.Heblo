# Architecture Review: Catalog/StockUpOperationResult unit test coverage

## Skip Design: true

This is a pure test-authoring task against an existing, unmodified production class (`StockUpOperationResult.cs`). There is no UI, no API surface, and no visual component involved — Skip Design is set per the standing rule for backend-only, non-behavioral changes.

## Architectural Fit Assessment

This is a coverage-gap remediation, not a feature. It fits cleanly into the existing test project without requiring any new infrastructure:

- `StockUpOperationResult` is a self-contained result/value object in `Anela.Heblo.Application/Features/Catalog/Services/`, with no DI dependencies, no I/O, and a private parameterless constructor that forces construction through its own static factories.
- The test project `backend/test/Anela.Heblo.Tests` already has a matching directory `Features/Catalog/Services/` containing sibling test files for other classes in the same production folder (`MarginCalculationServiceTests.cs`, `ProductWeightRecalculationServiceTests.cs`, `ProductCatalogQueryServiceTests.cs`, `SalesCostCalculationServiceTests.cs`), all namespaced `Anela.Heblo.Tests.Features.Catalog.Services`. The new test class fits this directory/namespace with zero structural changes.
- Confirmed stack from `docs/architecture/testing-strategy.md` and the sibling test file: **xUnit** (`[Fact]`/`[Theory]`), **FluentAssertions** (`.Should()`), **Moq** for mocking (not needed here — no dependencies to mock). No new packages, no new test project.

## Proposed Architecture

### Component Overview

```
backend/test/Anela.Heblo.Tests/
└── Features/Catalog/Services/
    ├── MarginCalculationServiceTests.cs        (existing, sibling pattern)
    ├── ProductWeightRecalculationServiceTests.cs (existing)
    └── StockUpOperationResultTests.cs           <-- NEW (this task)
```

No components are added or wired together; this is a leaf addition to the existing test tree. The class under test has exactly one production dependency: `StockUpOperation` (`Anela.Heblo.Domain.Features.Catalog.Stock`), which is constructed directly (no mock) since it is a plain entity with a public constructor.

### Key Design Decisions

#### Decision 1: How to reach all six `StockUpResultStatus` values given the private constructor
**Options considered:**
1. Reflection / `Activator.CreateInstance` with non-public constructor + property injection to bypass the factories entirely.
2. Use only the seven public static factory methods, since together they produce all six enum values (`Success`, `AlreadyCompleted`, `AlreadyInShoptet`, `InProgress`, `PreviouslyFailed`, and `Failed` — the latter reachable via `SubmitFailed`, `VerificationFailed`, or `VerificationError`).

**Chosen approach:** Option 2 — factory methods only, no reflection.

**Rationale:** The spec's "Open Question" flags this as needing a decision; the answer is unambiguous once you enumerate the factories against the enum: every value of `StockUpResultStatus` is produced by at least one factory. Reflection would test an implementation detail (the private constructor exists for encapsulation, e.g. to prevent inconsistent states) and adds no verification value the factory methods don't already provide — it would also break if the constructor signature ever changes, for no benefit. Testing through the public factory API is strictly better: it exercises real call paths and doubles as the factory-method coverage required by FR-2–FR-9. Resolve the spec's open question this way; no implementer decision needed.

#### Decision 2: Building `StockUpOperation` test instances
**Options considered:**
1. Add a test builder/helper class.
2. Construct `StockUpOperation` directly via its public constructor per test.

**Chosen approach:** Option 2 — direct construction, no builder.

**Rationale:** `StockUpOperation`'s public constructor (`documentNumber, productCode, amount, sourceType, sourceId`) has no invalid-value traps for this task (avoid empty strings for `documentNumber`/`productCode` and a non-zero `amount`, since the constructor validates these) and needs only two additional method calls to set the two fields the factories consume:
- `PreviouslyFailed` reads `operation.ErrorMessage` → call `operation.MarkAsFailed(timestamp, "some error")` to populate it (there is no other way to set `ErrorMessage`; it has a private setter).
- `InProgress` reads `operation.State` → the default post-construction state is `StockUpOperationState.Pending`, which is already a "known value" satisfying FR-5; no extra call needed unless the test wants to show a different state (optional, not required).

A dedicated builder/helper is unwarranted for a single test file exercising one type with a two-argument construction path; it would add indirection without reducing duplication meaningfully. No existing test helper for `StockUpOperation` was found in the repo (checked `Domain/Catalog` test tree) — none needs to be introduced for this scope.

## Implementation Guidance

### Directory / Module Structure

- **New file:** `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/StockUpOperationResultTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Features.Catalog.Services` (matches sibling files in the same directory)
- **No `.csproj` changes** — the file lands inside the existing `Anela.Heblo.Tests` project and is picked up automatically (no explicit `<Compile>` globs to edit; the project uses default SDK-style globbing, confirmed by the presence of the four sibling files already compiling in that folder).

### Interfaces and Contracts

No new interfaces or contracts. Test surface is entirely the existing public API of `StockUpOperationResult`:
```csharp
StockUpOperationResult.Success(StockUpOperation operation)
StockUpOperationResult.AlreadyCompleted(StockUpOperation operation)
StockUpOperationResult.PreviouslyFailed(StockUpOperation operation)
StockUpOperationResult.InProgress(StockUpOperation? operation)
StockUpOperationResult.AlreadyInShoptet(StockUpOperation operation)
StockUpOperationResult.SubmitFailed(StockUpOperation operation, Exception ex)
StockUpOperationResult.VerificationFailed(StockUpOperation operation)
StockUpOperationResult.VerificationError(StockUpOperation operation, Exception ex)
```
plus the `IsSuccess`, `Status`, `Message`, `Operation`, `Exception` properties read back on the result.

Recommended test shape (xUnit + FluentAssertions, matching sibling conventions):
- One `[Fact]` per factory method (FR-2 through FR-9), asserting `Status`, `Message`, `Operation` (reference equality via `.Should().BeSameAs(operation)`), `Exception`, and `IsSuccess` in a single test body each — this mirrors the "one test per factory method" guidance in the brief and is more readable than a single mega-theory for methods with differing signatures (some take an `Exception`, `InProgress` takes a nullable operand).
- One additional `[Theory]`/`[InlineData]`-free or table-driven test dedicated to `IsSuccess`, built by calling the relevant factory for each of the six statuses and asserting the boolean — this satisfies FR-1's ask for a single self-contained place that pins "the current set of success statuses," even though the per-factory tests already cover the same assertions individually. Keep this as a distinct test (not a refactor of the per-factory tests) so the intent ("this is the list of success statuses, on purpose") reads clearly to a future maintainer.
- Cover both branches of `InProgress` per FR-5: non-null operation with a known `State`, and `InProgress(null)` asserting `Operation` is `null` and the literal message `"Operation already in progress (state: )"`.

### Data Flow

N/A beyond in-memory construction → factory call → assertion. No async, no I/O, no DI container involved. Each test is fully self-contained (arrange operation/exception → act via factory → assert on returned `StockUpOperationResult`).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Reflection-based test bypassing the private constructor, testing an implementation detail instead of real usage | Low | Explicitly resolved in this review (Decision 1) — use only the public factory methods; do not use `Activator.CreateInstance` or reflection. |
| Test asserts derived/re-computed expected strings (e.g. re-implementing the interpolation) instead of literal pinned strings, weakening regression value | Low | Spec already flags this (FR-5); assert literal expected strings, not re-derived logic, especially for the `InProgress(null)` case. |
| Constructing `StockUpOperation` with invalid arguments (empty `documentNumber`/`productCode`, zero `amount`) throws `ValidationException` and breaks unrelated tests | Low | Use simple non-empty test values, e.g. `"DOC-1"`, `"PROD-1"`, `amount: 1`; not a design concern, just an implementation note. |
| Adding this file to the wrong namespace/folder breaks the "mirrors production namespace" convention used elsewhere in the test project | Low | Directory (`Features/Catalog/Services/`) and namespace (`Anela.Heblo.Tests.Features.Catalog.Services`) are already established by four sibling files; simply follow the pattern. |

## Specification Amendments

- **Resolve Open Question 1** (private constructor / `IsSuccess` coverage strategy): confirmed — use only the seven public static factories to reach all six `StockUpResultStatus` values; no reflection, no test-internal factory. FR-1 is satisfied by a dedicated `IsSuccess`-focused test built from factory calls, in addition to the `IsSuccess` assertions embedded in each per-factory test (FR-2–FR-9).
- **Resolve Open Question 2** (test framework/assertions): confirmed by inspecting `MarginCalculationServiceTests.cs` — **xUnit** + **FluentAssertions**, in `backend/test/Anela.Heblo.Tests`. No Moq needed for this specific test class (no dependencies to mock).
- **Minor addition to spec:** note that `StockUpOperation.ErrorMessage` can only be populated via `MarkAsFailed(DateTime timestamp, string errorMessage)` (private setter), so the `PreviouslyFailed` test (FR-4) must call `MarkAsFailed` on the constructed operation before invoking `StockUpOperationResult.PreviouslyFailed(operation)`.

## Prerequisites

None. No migrations, no config, no new packages, no new test project. Implementation can start immediately by adding the single new test file.
