# Specification: Unit test coverage for `RetryStockUpOperationHandler`

## Summary
Add a complete unit test suite for `RetryStockUpOperationHandler` (currently zero coverage) that locks in the four-branch behavior of the retry workflow: not-found, already-completed, normal `Reset` of a Failed operation, and `ForceReset` of any other non-Completed (stuck) state. The decisive business rule under test is the choice between `Reset()` and `ForceReset()`; tests must fail if that branch is swapped.

## Background
`RetryStockUpOperationHandler` is the manual-intervention escape hatch for stuck stock-up operations that block warehouse processes. The handler routes operations differently depending on their state: a `Failed` operation uses domain method `Reset()` (which enforces the state guard `state == Failed`), while any other non-Completed state (e.g. `Submitted`, `Pending`) is treated as "stuck" and is forced back to `Pending` via `ForceReset()` plus a warning log.

If the branch condition is wrong (or `Reset`/`ForceReset` are swapped), one of two regressions happens:
- Stuck `Submitted` operations are routed to `Reset()`, which throws — retries silently fail and the operation stays stuck.
- `Failed` operations are routed to `ForceReset()`, bypassing the state guard that `Reset` enforces.

Both `Reset()` and `ForceReset()` converge on the same response (`Status=InProgress`), so without explicit assertions on _which_ method ran, swapping them produces no observable test failure. The test suite must close that gap.

## Functional Requirements

### FR-1: Test — operation not found
The handler returns a failure response when the repository returns no operation for the requested `OperationId`.

**Acceptance criteria:**
- A test named `Handle_WhenOperationNotFound_ReturnsFailure` exists in `backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs`.
- `IStockUpOperationRepository.GetByIdAsync` is mocked to return `null`.
- Asserts `response.Success == false`.
- Asserts `response.Status == StockUpResultStatus.Failed`.
- Asserts `response.ErrorMessage` contains the requested `OperationId` value.
- Asserts `SaveChangesAsync` is never called.

### FR-2: Test — operation already completed
The handler rejects retry attempts on operations already in `Completed` state without mutating the entity or persisting.

**Acceptance criteria:**
- A test named `Handle_WhenOperationIsCompleted_ReturnsAlreadyCompleted` exists.
- Arrange creates a `StockUpOperation`, calls `MarkAsCompleted(...)` to put it in `Completed` state.
- Asserts `response.Success == false`.
- Asserts `response.Status == StockUpResultStatus.AlreadyCompleted`.
- Asserts `response.ErrorMessage` contains the operation's `DocumentNumber`.
- Asserts operation state is unchanged (`StockUpOperationState.Completed`).
- Asserts `SaveChangesAsync` is never called.

### FR-3: Test — Failed operation calls `Reset()`
For a `Failed` operation, the handler invokes the domain method `Reset()` (not `ForceReset()`), persists, and returns `InProgress`.

**Acceptance criteria:**
- A test named `Handle_WhenOperationIsFailed_CallsResetAndReturnsInProgress` exists.
- Arrange creates an operation and calls `MarkAsFailed(timestamp, "some error")`.
- Asserts `response.Success == true`.
- Asserts `response.Status == StockUpResultStatus.InProgress`.
- Asserts `response.ErrorMessage` is `null`.
- Asserts the resulting operation state is `Pending`, `SubmittedAt` is `null`, `CompletedAt` is `null`, and `ErrorMessage` is `null` (the observable Reset post-state).
- Asserts `SaveChangesAsync` is called exactly once.
- Asserts that **no `LogLevel.Warning` entry** is emitted by the handler. This is the test-observable signal that `Reset()` (not `ForceReset()`) was taken: the handler logs a `Warning` only on the ForceReset branch.

### FR-4: Test — Submitted (stuck) operation calls `ForceReset()` with warning
For a `Submitted` operation (the canonical stuck case), the handler invokes `ForceReset()` with a warning log, persists, and returns `InProgress`.

**Acceptance criteria:**
- A test named `Handle_WhenOperationIsSubmitted_CallsForceResetAndReturnsInProgress` exists.
- Arrange creates an operation and calls `MarkAsSubmitted(timestamp)`.
- Asserts `response.Success == true`.
- Asserts `response.Status == StockUpResultStatus.InProgress`.
- Asserts `response.ErrorMessage` is `null`.
- Asserts the resulting operation state is `Pending`, `SubmittedAt` is `null`, `CompletedAt` is `null`.
- Asserts `SaveChangesAsync` is called exactly once.
- Asserts that **exactly one `LogLevel.Warning` entry** is emitted by the handler (the "Force resetting stuck operation" log). This is the test-observable signal that `ForceReset()` was taken.
- Note: the alternative — asserting that `Reset()` is NOT invoked — is not directly possible because the domain method is non-virtual on a concrete entity. The Warning-log assertion is the substitute and is acceptable because it lives one line above the `ForceReset()` call in the handler.

### FR-5: Test — Pending (stuck) operation also calls `ForceReset()`
A `Pending` operation (rare but valid stuck case — e.g. retry hit before the background task picked it up) takes the `ForceReset` branch.

**Acceptance criteria:**
- A test named `Handle_WhenOperationIsPending_CallsForceResetAndReturnsInProgress` exists.
- Arrange creates a fresh `StockUpOperation` (constructor leaves it in `Pending`).
- Asserts the same response shape as FR-4 (`Success == true`, `Status == InProgress`, no `ErrorMessage`).
- Asserts state remains `Pending` after handling.
- Asserts exactly one `LogLevel.Warning` entry is emitted.
- Asserts `SaveChangesAsync` is called exactly once.
- Rationale: parametrising the stuck-state branch over more than one input state catches an off-by-one branch error like `if (state == Submitted)` instead of `if (state == Failed)`.

## Non-Functional Requirements

### NFR-1: Test discipline
- Use the project's established test stack: **xUnit** + **Moq** + plain `Assert.*` assertions. Follow the pattern of `backend/test/Anela.Heblo.Tests/Features/Catalog/AcceptStockUpOperationHandlerTests.cs`.
- Follow the AAA layout (`// Arrange`, `// Act`, `// Assert`) with descriptive `MethodUnderTest_GivenCondition_ExpectsBehavior` naming, matching neighbour test files.
- Use a per-class shared `Mock<IStockUpOperationRepository>`, `Mock<ILogger<RetryStockUpOperationHandler>>`, and handler instance constructed in the test class constructor (mirrors `AcceptStockUpOperationHandlerTests`).
- The new test file must build with no warnings under the existing nullable-reference-types settings.

### NFR-2: Determinism and isolation
- No real database, no `Testcontainers`, no shared mutable state between tests. Pure in-process unit tests.
- Use a fixed timestamp constant (e.g. `new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)`) when arranging operations — do not call `DateTime.UtcNow`.

### NFR-3: Performance
- The full new test class must complete in under 200 ms on a developer workstation. There is no I/O.

### NFR-4: Coverage
- After this work, line coverage of `RetryStockUpOperationHandler.Handle` must be 100% (all four branches plus the entry path).
- The test class itself must contribute to the project's 80% minimum coverage requirement.

## Data Model
No data model changes. Tests rely on the existing domain types:
- `StockUpOperation` (entity, `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs`)
- `StockUpOperationState` (enum: `Pending`, `Submitted`, `Completed`, `Failed`)
- `StockUpResultStatus` (enum, including `Failed`, `AlreadyCompleted`, `InProgress`)
- `IStockUpOperationRepository` (only `GetByIdAsync` and `SaveChangesAsync` are used by the handler)

## API / Interface Design

### Test file
`backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs`

### Class shape
```csharp
public class RetryStockUpOperationHandlerTests
{
    private readonly Mock<IStockUpOperationRepository> _repositoryMock;
    private readonly Mock<ILogger<RetryStockUpOperationHandler>> _loggerMock;
    private readonly RetryStockUpOperationHandler _handler;
    private static readonly DateTime FixedNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public RetryStockUpOperationHandlerTests() { /* construct mocks + handler */ }

    [Fact] public async Task Handle_WhenOperationNotFound_ReturnsFailure() { /* FR-1 */ }
    [Fact] public async Task Handle_WhenOperationIsCompleted_ReturnsAlreadyCompleted() { /* FR-2 */ }
    [Fact] public async Task Handle_WhenOperationIsFailed_CallsResetAndReturnsInProgress() { /* FR-3 */ }
    [Fact] public async Task Handle_WhenOperationIsSubmitted_CallsForceResetAndReturnsInProgress() { /* FR-4 */ }
    [Fact] public async Task Handle_WhenOperationIsPending_CallsForceResetAndReturnsInProgress() { /* FR-5 */ }
}
```

### Logger verification helper
The standard `ILogger` Moq verification idiom (matches existing tests):
```csharp
_loggerMock.Verify(
    x => x.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, t) => true),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Once); // or Times.Never for FR-3
```

## Dependencies
- **Project**: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
- **NuGet packages** (already present in the project): `xunit`, `Moq`, `Microsoft.Extensions.Logging.Abstractions`.
- **Source under test**: no changes to production code expected. If the handler is modified to make `Reset`/`ForceReset` directly verifiable (e.g. by extracting a strategy or moving the choice to a virtual method), that is out of scope here — the log-level assertion is the agreed proxy.
- **Reference test for style**: `backend/test/Anela.Heblo.Tests/Features/Catalog/AcceptStockUpOperationHandlerTests.cs`.

## Out of Scope
- Modifying `RetryStockUpOperationHandler.cs` or any production code.
- Integration tests against a real database, Testcontainers, or a real `IStockUpOperationRepository` implementation.
- Tests for the upstream MediatR pipeline (validation behaviors, controllers, request DTO binding).
- Tests for sibling handlers (`AcceptStockUpOperationHandler`, `GetStockUpOperationsHandler`, etc.) — already covered or out of scope.
- Tests for the domain methods `Reset()` and `ForceReset()` themselves — covered by `StockUpOperationTests` in `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpOperationTests.cs`.
- Verifying exception/error paths from `SaveChangesAsync` (the handler currently does not catch them and the brief lists only four branches).

## Open Questions
None.

## Status: COMPLETE