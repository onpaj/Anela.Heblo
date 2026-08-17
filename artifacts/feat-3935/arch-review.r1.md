# Architecture Review: Test Coverage for DeleteManufactureDifficultyHandler

## Skip Design: true
Test-only change to a backend MediatR handler. No new or changed UI, no new endpoints, no schema changes, no visual components.

## Architectural Fit Assessment
This is a pure test-addition task inside the existing Vertical Slice `UseCases/DeleteManufactureDifficulty` folder in `Anela.Heblo.Application/Features/Catalog`. It has one integration point — the sibling test project `backend/test/Anela.Heblo.Tests/Features/Catalog/` — and follows a pattern already established by two sibling files in the same directory: `CreateManufactureDifficultyHandlerTests.cs` and `UpdateManufactureDifficultyHandlerTests.cs`. Both use xUnit + Moq + FluentAssertions and mock `IManufactureDifficultyRepository` / `ICatalogRepository` directly, exactly matching `docs/architecture/testing-strategy.md`'s prescribed stack ("xUnit for all .NET tests", "Moq for dependency mocking", "FluentAssertions for readable test assertions"). No architectural deviation is needed; this task is additive and isolated.

## Proposed Architecture

### Component Overview
```
backend/test/Anela.Heblo.Tests/Features/Catalog/
└── DeleteManufactureDifficultyHandlerTests.cs   (NEW)
        │
        ├── mocks: Mock<IManufactureDifficultyRepository>
        ├── mocks: Mock<ICatalogRepository>
        ├── mocks: Mock<ILogger<DeleteManufactureDifficultyHandler>>
        └── SUT:   DeleteManufactureDifficultyHandler   (UNCHANGED — production code, not touched)
```
No new components, no new files outside the single new test class. `DeleteManufactureDifficultyHandler.cs`, `DeleteManufactureDifficultyRequest.cs`, and `DeleteManufactureDifficultyResponse.cs` are read-only references for the test author.

### Key Design Decisions

#### Decision 1: Test file location and naming
**Options considered:** (a) new file `DeleteManufactureDifficultyHandlerTests.cs` next to the two existing sibling test classes; (b) a shared `ManufactureDifficultyHandlerTests.cs` covering Create/Update/Delete together.
**Chosen approach:** (a) — one file per handler, matching the existing 1:1 file-per-handler convention already present (`CreateManufactureDifficultyHandlerTests.cs`, `UpdateManufactureDifficultyHandlerTests.cs`).
**Rationale:** Consistency with established convention in the same directory; smaller, focused files are easier to review and keep in context, per `docs/development/setup.md`/project style already visible in the folder.

#### Decision 2: Mocking approach for sequencing (FR-2)
**Options considered:** (a) `MockSequence` to assert `DeleteAsync` is invoked strictly before `RefreshManufactureDifficultySettingsData`; (b) rely only on `Times.Once` verification on both calls without ordering; (c) a `Mock.Setup(...).Callback(...)` that records call order into a `List<string>` and asserts on the list.
**Chosen approach:** (a) `MockSequence`, matching Moq idioms already used in this codebase (`Mock<T>` + `Setup`/`Verify` throughout the sibling tests) and directly testable via `InSequence(sequence)` on both mock setups.
**Rationale:** `MockSequence` is the built-in Moq primitive for this exact requirement (FR-2's "cache refresh call happens only after the delete call") and needs no extra bookkeeping code, keeping the test as close as possible in style to the sibling `UpdateManufactureDifficultyHandlerTests.cs`, which already exercises verify-only assertions without inventing custom sequencing scaffolding.

#### Decision 3: Exception-path assertions (FR-3)
**Options considered:** (a) throw a generic `Exception("boom")` from the mocked repository call and assert `response.Message` contains `"boom"`; (b) throw a custom/typed exception and assert on its type.
**Chosen approach:** (a) — matches the handler's actual `catch (Exception ex)` (untyped) and its message-interpolation behavior (`$"Error deleting manufacture difficulty: {ex.Message}"`), so a plain `Exception` with a distinctive message is sufficient and keeps the test decoupled from any exception type that might change later.
**Rationale:** Testing against the interpolated message content (not exception type) matches what the handler actually contractually guarantees to callers (`response.Message`), and is what FR-3's acceptance criteria specify.

## Implementation Guidance

### Directory / Module Structure
- **Create:** `backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs`
- **No modifications** to any file under `backend/src/`.

### Interfaces and Contracts
No new interfaces. Tests construct the handler directly:
```csharp
new DeleteManufactureDifficultyHandler(
    _repositoryMock.Object,
    _catalogRepositoryMock.Object,
    _loggerMock.Object);
```
Constructor signature (verified against `DeleteManufactureDifficultyHandler.cs`): `(IManufactureDifficultyRepository repository, ICatalogRepository catalogRepository, ILogger<DeleteManufactureDifficultyHandler> logger)` — three dependencies, no `IMapper` or `TimeProvider` (unlike `UpdateManufactureDifficultyHandler`, which the sibling test mocks additionally — do not copy those two extra mocks into the new test).

Relevant mocked members (verified against `IManufactureDifficultyRepository.cs` and `ICatalogRepository.cs`):
- `Task<ManufactureDifficultySetting?> GetByIdAsync(int id, CancellationToken ct = default)`
- `Task DeleteAsync(int id, CancellationToken ct = default)`
- `Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct)`

### Data Flow
1. `GetByIdAsync(request.Id)` → `null` ⇒ early return, `Success = false` (FR-1).
2. `GetByIdAsync(request.Id)` → entity ⇒ `DeleteAsync(request.Id)` ⇒ `RefreshManufactureDifficultySettingsData(existing.ProductCode)` ⇒ `Success = true` (FR-2). Note `existing.ProductCode`, not any field off `request`, is the value that must reach the cache-refresh call — this is the crux of the original coverage gap and must be asserted explicitly with `Verify(r => r.RefreshManufactureDifficultySettingsData(existing.ProductCode, ...), Times.Once)`.
3. Any of the above repository calls throwing ⇒ caught by the handler's `catch (Exception ex)` ⇒ `Success = false`, message includes `ex.Message`, no exception escapes `Handle` (FR-3).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Test asserts on `ErrorCode`/`Params` (copying the `Update` sibling's pattern) instead of `Message`, since `DeleteManufactureDifficultyResponse` doesn't use `ErrorCode` | Medium | Spec's Data Model section calls this out explicitly; implementer must assert `response.Message`, not `response.ErrorCode` |
| Over-mocking (adding unused `IMapper`/`TimeProvider` mocks copied from `UpdateManufactureDifficultyHandlerTests.cs`) causes constructor mismatch/compile failure | Low | Constructor signature is spelled out above — 3 dependencies only |
| Exception-path test for `RefreshManufactureDifficultySettingsData` throwing doesn't also verify `DeleteAsync` **was** called (to prove the throw happened after, not instead of, delete) | Low | Task plan should include an explicit `Verify(..., Times.Once)` on `DeleteAsync` in that specific test case |

## Specification Amendments
None — the spec (`spec.r1.md`) is implementable as written; this review only adds implementation-level detail (constructor shape, exact mocked members, `MockSequence` recommendation) that a developer needs.

## Prerequisites
None. No migrations, no config, no new packages — `Anela.Heblo.Tests` already references xUnit, Moq, and FluentAssertions (confirmed via sibling test files' `using` statements).
