I have enough context to produce the review. The handler is straightforward, the spec is precise, and the proposed test placement matches existing conventions exactly.

```markdown
# Architecture Review: Unit test coverage for `RetryStockUpOperationHandler`

## Skip Design: true

No UI, no visual components, no API contract changes. Pure test-code addition inside the existing backend test project.

## Architectural Fit Assessment

The proposal is a textbook fit and requires zero structural deviation.

- The handler under test (`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/RetryStockUpOperation/RetryStockUpOperationHandler.cs`) is a vanilla MediatR `IRequestHandler` in the Vertical Slice + Clean Architecture layout. Its only collaborators are `IStockUpOperationRepository` (a domain port) and `ILogger<T>`. Both are trivial to mock.
- The sibling handler `AcceptStockUpOperationHandler` already has an established, mature test fixture (`backend/test/Anela.Heblo.Tests/Features/Catalog/AcceptStockUpOperationHandlerTests.cs`) using xUnit + Moq + plain `Assert.*`. The spec correctly designates this as the style reference. The new file should be a near-twin in shape (constructor-injected mocks, AAA layout, identical logger-verification idiom).
- The domain entity `StockUpOperation` exposes a public constructor and public state-transition methods (`MarkAsSubmitted`, `MarkAsCompleted`, `MarkAsFailed`) that let tests arrange any state in `Pending`, `Submitted`, `Completed`, or `Failed` without reflection or test doubles — the spec leverages this correctly.
- Integration points are limited to the single test file. No solution/project file edits are needed; the test discovery is convention-based.

The only real architectural tension is the spec's chosen proxy for distinguishing `Reset()` vs `ForceReset()` (assert on `LogLevel.Warning`). This is forced by the fact that `Reset` and `ForceReset` are non-virtual methods on a concrete entity, so a Moq spy is impossible without changing production code. The log-level signal is one line above the `ForceReset()` call in the handler, making it a tight and acceptable proxy. We confirm this trade-off below.

## Proposed Architecture

### Component Overview

```
                ┌───────────────────────────────────────────────────────┐
                │ RetryStockUpOperationHandlerTests   (xUnit class)     │
                │                                                       │
                │  ctor: builds Mock<IStockUpOperationRepository>,      │
                │        Mock<ILogger<RetryStockUpOperationHandler>>,   │
                │        and the SUT instance.                          │
                │                                                       │
                │  5 [Fact] methods, one per branch in spec.            │
                └───────────────┬───────────────────────┬───────────────┘
                                │                       │
                       arranges │                       │ asserts on
                                ▼                       ▼
                ┌──────────────────────────┐  ┌──────────────────────────┐
                │ StockUpOperation         │  │ Mock<IStockUpOperation-  │
                │ (real domain entity,     │  │ Repository>              │
                │  state set via public    │  │ - GetByIdAsync           │
                │  Mark* methods)          │  │ - SaveChangesAsync       │
                └──────────────────────────┘  └──────────────────────────┘
                                                        │
                                                        ▼
                                            ┌──────────────────────────┐
                                            │ Mock<ILogger<...>>       │
                                            │ Verify LogLevel.Warning  │
                                            │ Times.Once / Times.Never │
                                            └──────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Use a real `StockUpOperation` instance, not a mock

**Options considered:**
- (A) Mock `StockUpOperation` (would require it to be virtual or extracted to an interface).
- (B) Use a real entity, arrange state via public `MarkAs*` methods.

**Chosen approach:** (B). Match the existing `AcceptStockUpOperationHandlerTests` pattern.

**Rationale:** The entity has no infrastructure dependencies; constructing it is cheap and deterministic. Mocking the entity would require production-code changes (interface extraction or virtualization) explicitly ruled out by the brief. Using the real entity also lets the tests assert on the observable post-state (`State == Pending`, `SubmittedAt == null`, etc.), which is a stronger guarantee than a mock verification of "the method was called."

#### Decision 2: Detect the `Reset` vs `ForceReset` branch via the `Warning` log

**Options considered:**
- (A) Spy on the domain method directly (impossible — `Reset`/`ForceReset` are non-virtual on a concrete class).
- (B) Refactor the handler to take a strategy/policy and mock that (out of scope per brief).
- (C) Assert on the `LogLevel.Warning` Moq verification (`Times.Once` for ForceReset branch, `Times.Never` for Reset branch).

**Chosen approach:** (C).

**Rationale:** The handler emits exactly one `LogLevel.Warning` line ("Force resetting stuck operation …") **only** on the ForceReset branch. This is the only test-observable signal that does not require production-code change. The other log lines in the handler are `LogLevel.Information`, so a `LogLevel.Warning` `Times.Once` / `Times.Never` assertion is a clean, unambiguous proxy. The risk is that a future refactor could remove or re-level the warning log; the spec acknowledges this trade-off and tests for the existing handler exactly as-is.

#### Decision 3: Fixed timestamp constant; no `DateTime.UtcNow`

**Options considered:** Use `DateTime.UtcNow` (matches `AcceptStockUpOperationHandlerTests`) vs use a fixed constant.

**Chosen approach:** Fixed constant `static readonly DateTime FixedNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)` (per NFR-2).

**Rationale:** Determinism. The handler's behavior does not depend on the clock, but the test should not depend on `DateTime.UtcNow` either — this is a project-wide hygiene improvement over the older `AcceptStockUpOperationHandlerTests` pattern and worth seeding here. **Note:** this is a deliberate, *minor* deviation from the neighbour file; flagged here so reviewers don't treat it as an inconsistency.

## Implementation Guidance

### Directory / Module Structure

Single new file, no project file change:

```
backend/test/Anela.Heblo.Tests/Features/Catalog/
└── RetryStockUpOperationHandlerTests.cs   ← NEW (mirrors AcceptStockUpOperationHandlerTests.cs)
```

Namespace: `Anela.Heblo.Tests.Features.Catalog` (matches sibling files).

### Interfaces and Contracts

No new interfaces. The test depends only on these existing types:

| Symbol | Location | Use in test |
|---|---|---|
| `RetryStockUpOperationHandler` | `Application/Features/Catalog/UseCases/RetryStockUpOperation/` | SUT, constructed once per test instance |
| `RetryStockUpOperationRequest` | same | Built per test with target `OperationId` |
| `RetryStockUpOperationResponse` | same | Asserted: `Success`, `Status`, `ErrorMessage` |
| `StockUpOperation` | `Domain/Features/Catalog/Stock/` | Real entity; state set via `MarkAsSubmitted` / `MarkAsCompleted` / `MarkAsFailed` |
| `StockUpOperationState` | same | Enum: `Pending`, `Submitted`, `Completed`, `Failed` |
| `StockUpResultStatus` | `Application/Features/Catalog/Services/StockUpOperationResult.cs` | Enum: `Failed`, `AlreadyCompleted`, `InProgress` |
| `IStockUpOperationRepository` | `Domain/Features/Catalog/Stock/` | Mocked; only `GetByIdAsync` and `SaveChangesAsync` are exercised |

`StockUpOperation` constructor signature confirmed (verified in code):
`(string documentNumber, string productCode, int amount, StockUpSourceType sourceType, int sourceId)`.

### Data Flow

Per-test flow (representative — FR-4, Submitted → ForceReset):

```
Arrange:
  op = new StockUpOperation("DOC-001", "PROD-001", 10, StockUpSourceType.TransportBox, 1)
  op.MarkAsSubmitted(FixedNow)
  _repositoryMock.GetByIdAsync(1, *) ⇒ op
  request = new RetryStockUpOperationRequest { OperationId = 1 }

Act:
  response = await _handler.Handle(request, CancellationToken.None)

Assert (observable contract):
  response.Success == true
  response.Status == StockUpResultStatus.InProgress
  response.ErrorMessage is null
  op.State == StockUpOperationState.Pending
  op.SubmittedAt is null
  op.CompletedAt is null
  _repositoryMock.Verify SaveChangesAsync — Times.Once
  _loggerMock.Verify  LogLevel.Warning — Times.Once   ← proves ForceReset branch
```

For FR-3 (Failed → Reset), the only flow difference is `MarkAsFailed(FixedNow, "API timeout")` in Arrange and `LogLevel.Warning — Times.Never` in Assert.

For FR-1/FR-2 (not-found, already-completed): no state mutation, no `SaveChangesAsync` call, error message contents asserted via `Assert.Contains`.

For FR-5 (Pending → ForceReset): no Mark* call in Arrange (constructor leaves the operation in `Pending`); rest mirrors FR-4.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| The `LogLevel.Warning` proxy could rot if the handler stops emitting the warning. | Medium | Add a one-line comment in the FR-3 and FR-4 test methods explicitly tying the `Warning` count assertion to "this is how we detect the `Reset` vs `ForceReset` branch." A future maintainer who removes or downgrades the log line will see the assertion break and the comment will explain why it matters. |
| Future refactor extracts the branch into a strategy/policy and the log line disappears. | Low | Same comment serves as a notice; consider in a follow-up making the branch directly observable (out of scope per brief). |
| Assertion on operation post-state (e.g. `SubmittedAt == null`) effectively re-tests domain behavior already covered by `StockUpOperationTests`. | Low | Accept duplication — these assertions in the handler tests guard against an accidental `op.Reset()` removal in the handler; they are guarding the handler's *use* of the domain, not the domain itself. |
| `Pending` Submitted timestamp deletions: spec implies `SubmittedAt` is `null` after Pending→ForceReset, but `Pending` operations never had a `SubmittedAt`. | Trivial | The assertion still passes (`null == null`); no change needed, but document the redundancy in a code comment if it's confusing on review. |
| `StockUpResultStatus.InProgress` location (Application/Features/Catalog/Services/) — easy to miss for `using` directives. | Trivial | Include `using Anela.Heblo.Application.Features.Catalog.Services;` in the test file (same as sibling). |

## Specification Amendments

The spec is implementation-ready. Two minor clarifications worth folding back:

1. **FR-5 assertion list:** Add `SaveChangesAsync` is called exactly once (the FR-5 acceptance criteria say "asserts the same response shape as FR-4" but do not explicitly list the `SaveChangesAsync` verification; FR-4 has it but the reuse-by-reference is slightly ambiguous). Trivial editorial fix.
2. **FR-3 / FR-4 log assertion intent comment:** The spec specifies the assertion but does not require a code comment explaining *why* `Warning` count = branch selection. Strongly recommend the implementation include a one-line comment at each of those Verify calls so the proxy is self-documenting (per the Medium risk above). Optional, but cheap.

No other changes. The spec correctly states **Status: COMPLETE** and the open-questions section is appropriately empty.

## Prerequisites

None. Everything required exists today:

- Test project `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` already references xUnit, Moq, and `Microsoft.Extensions.Logging.Abstractions` (verified via the existing `AcceptStockUpOperationHandlerTests.cs`).
- All production types (`RetryStockUpOperationHandler`, `StockUpOperation`, `IStockUpOperationRepository`, `StockUpOperationState`, `StockUpResultStatus`, `RetryStockUpOperationRequest`, `RetryStockUpOperationResponse`) are in place.
- No migrations, config, infrastructure, or feature flags needed.

Implementation can begin immediately; the deliverable is a single new file with five `[Fact]` methods, expected to build clean with nullable enabled and execute in well under 200 ms.
```