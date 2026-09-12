# Specification: Unit tests for `DraftReplyLoggingBehavior`

## Summary
`DraftReplyLoggingBehavior` is a MediatR pipeline behavior with 0% line coverage against a 60% threshold. This is a test-coverage / tech-debt task: add a focused unit test suite that exercises all three execution paths (skip-on-failure, happy path, exception-swallow) so the behavior's contract — when it writes a RAG interaction log and when it stamps `response.Id` — is protected by regression tests.

## Background
`DraftReplyLoggingBehavior` sits in the MediatR pipeline for `GenerateDraftReplyRequest` → `GenerateDraftReplyResponse` (Smartsupp draft-reply generation). After the inner handler runs, it decides whether to persist a `RagInteractionLog` row (used for the eval dataset and for later feedback linking) and, if so, stamps `response.Id` with the new log's id so the frontend can associate a sent reply and any feedback back to this draft.

The behavior is currently untested. The brief flags two specific risks of a silent regression:
- If the skip condition (`!response.Success || !_recorder.HasInteraction`) is evaluated incorrectly, log rows are silently never written and the eval dataset goes empty without any visible error.
- If the exception-swallow path around `SaveAsync` regresses and starts rethrowing, every DB write failure would break the draft-reply feature for the operator (the caller's response would fail, not just miss a log).

An almost-identical sibling behavior, `QuestionLoggingBehavior` (KnowledgeBase feature), already has a full test suite at `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/QuestionLoggingBehaviorTests.cs`. That file is the direct structural precedent for this task: same collaborators (`IRagInteractionLogRepository`, a real `RagInteractionRecorder` instead of a mock, `ICurrentUserService`, `ILogger<T>`), same three behaviors under test. The new suite should mirror its conventions rather than invent new ones.

## Functional Requirements

### FR-1: Test project and file placement
Add a new test class `DraftReplyLoggingBehaviorTests` under `backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs`, mirroring the existing `KnowledgeBase/Pipeline/QuestionLoggingBehaviorTests.cs` layout (`test/Anela.Heblo.Tests/<Feature>/Pipeline/<Behavior>Tests.cs`). No new test project or package reference is needed — `Moq`, `xUnit`, and the `RagInteractionRecorder` concrete class are already referenced by the existing sibling test.

**Acceptance criteria:**
- File exists at `backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs`.
- Test class uses xUnit (`[Fact]`) and Moq, consistent with the rest of the test project.
- No production code in `DraftReplyLoggingBehavior.cs` is modified — this is a test-only change (the class is already fully testable via constructor-injected interfaces).

### FR-2: Skip path — generation failed (`response.Success == false`)
When the inner handler's response has `Success = false`, the behavior must not call `_repository.SaveAsync`, must not call `_currentUserService.GetCurrentUser()` (implied by the code short-circuiting before that line, but not required to assert explicitly), and must return the response unmodified with `response.Id` left `null`.

**Acceptance criteria:**
- Arrange: `RagInteractionRecorder.RecordInteraction(...)` is called first so `HasInteraction` is `true` (isolating this test to the `Success` branch of the `||`), then the inner delegate returns a `GenerateDraftReplyResponse` with `Success = false`.
- Assert: `result.Id` is `null`.
- Assert: `_repository.Verify(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()), Times.Never)`.
- Assert: the returned response's other fields (e.g. `Answer`) are unchanged from what the inner delegate produced.

### FR-3: Skip path — no interaction recorded (`_recorder.HasInteraction == false`)
When `response.Success = true` but the recorder was never populated (`RecordInteraction` not called, so `HasInteraction` stays `false` on the real `RagInteractionRecorder`), the behavior must skip logging exactly as in FR-2.

**Acceptance criteria:**
- Arrange: do not call `RecordInteraction` on the recorder; inner delegate returns `Success = true`.
- Assert: `result.Id` is `null`.
- Assert: `_repository.Verify(r => r.SaveAsync(...), Times.Never)`.

### FR-4: Happy path — log persisted and response stamped
When `response.Success = true` and `_recorder.HasInteraction = true`, the behavior must build a `RagInteractionLog` via `RagInteractionLogFactory.Build`, call `_repository.SaveAsync(log, cancellationToken)` exactly once, and set `response.Id = log.Id` before returning.

**Acceptance criteria:**
- Arrange: `_currentUserService` mock returns a `CurrentUser` with a known `Id`; recorder has `RecordInteraction` (and optionally `RecordRetrieval`) called; `_repository.SaveAsync` is set up to capture the passed `RagInteractionLog` via a `Callback`.
- Act: invoke `Handle` with a `GenerateDraftReplyRequest` (a non-null `ConversationId` is required — see FR-5) and an inner delegate returning `Success = true`.
- Assert: `_repository.Verify(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), cancellationToken), Times.Once)` (verify the same `CancellationToken` passed into `Handle` is forwarded).
- Assert: `result.Id` is not null, is not `Guid.Empty`, and equals the captured log's `Id`.
- Assert (grounding the log content, following the sibling test's pattern): captured log's `UserId` matches the mocked current user, and other fields the factory derives from the recorder (e.g. `Question`, `Answer`, `TopK` if `RecordRetrieval` was also called) match what was recorded.

### FR-5: Exception-swallow path
When `_repository.SaveAsync` throws, the exception must be caught inside the behavior, logged via `ILogger<DraftReplyLoggingBehavior>.LogError` (do not over-assert the exact log call shape — sibling test does not assert on the logger mock at all, only that no exception propagates), and the original response returned with `response.Id` remaining `null` (since the throw happens before the `response.Id = log.Id` assignment).

**Acceptance criteria:**
- Arrange: `_repository.Setup(r => r.SaveAsync(...)).ThrowsAsync(new Exception("..."))`; recorder has an interaction recorded; response `Success = true`; request has a non-null `ConversationId` (the catch block's log message interpolates `request.ConversationId`, so a null value should not throw a `NullReferenceException` inside the catch — use a concrete string like `"conv-1"` to keep the test unambiguous).
- Act: call `Handle` — must complete without throwing (`await` the call directly rather than wrapping in `Assert.ThrowsAsync`, to prove no exception propagates).
- Assert: `result` is the same response instance / equal to what the inner delegate returned (e.g. `result.Answer` unchanged).
- Assert: `result.Id` is `null`.

### FR-6: Test isolation / avoid over-coupling to the factory internals
Per the sibling test's own convention, prefer a real `RagInteractionRecorder` instance over a mock (documented there as matching production: the recorder is populated by nested handlers in the same DI scope, and the behavior reads it back). Do not assert against every field `RagInteractionLogFactory.Build` derives — only the fields called out in FR-4 and whatever is needed to prove the happy-path contract, to avoid the test suite becoming a duplicate spec of the factory itself (which is presumably tested separately or is simple enough not to need it here).

**Acceptance criteria:**
- The test class field for the recorder is a concrete `RagInteractionRecorder`, not `Mock<IRagInteractionRecorder>`.
- `IRagInteractionLogRepository` and `ICurrentUserService` remain mocked via Moq, consistent with the sibling suite.

## Non-Functional Requirements

### NFR-1: Performance
N/A — this is a test-coverage task with no runtime/performance implications. The new tests should run in-process with mocks (no real DB, no I/O) and complete in well under a second each, consistent with the rest of the unit test suite.

### NFR-2: Security
N/A — no new production code, no new attack surface. Tests use in-memory mocks only; no real credentials or external services are touched.

## Data Model
No new or changed data model. For test-construction reference only:
- `RagInteractionLog` (domain entity, `Anela.Heblo.Domain.Features.Rag`) — built by `RagInteractionLogFactory.Build(IRagInteractionRecorder, string? userId, long durationMs, DateTimeOffset now)`; has at least `Id` (Guid), plus fields sourced from the recorder (`Question`, `Answer`, `TopK`, `SourceCount`, `UserId`, `DurationMs`, `Feature`, etc. — see `QuestionLoggingBehaviorTests` for the fields already known to be populated).
- `GenerateDraftReplyResponse` (`Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply`) — has `Id` (`Guid?`), `Answer` (string), `Sources` (list), and inherited `Success`/error fields from `BaseResponse`.
- `GenerateDraftReplyRequest` — has `ConversationId` (string, non-null in real usage — set from the controller route) and `Topic` (string?).
- `CurrentUser` — constructed in tests the same way the sibling suite does, e.g. `new CurrentUser("user-1", "Test User", null, true)`.

## API / Interface Design
N/A — no public API, controller, or UI surface changes. This behavior is internal MediatR pipeline plumbing; the tests interact with it directly via its `Handle` method, not through HTTP.

## Dependencies
- Existing test project `Anela.Heblo.Tests` and its current package references (xUnit, Moq) — no new dependencies to add.
- Sibling reference implementation: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/QuestionLoggingBehaviorTests.cs` (structural template).
- Production types under test, unchanged: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Pipeline/DraftReplyLoggingBehavior.cs`, `IRagInteractionLogRepository`, `IRagInteractionRecorder`/`RagInteractionRecorder`, `ICurrentUserService`, `RagInteractionLogFactory`, `GenerateDraftReplyRequest`/`Response`.

## Out of Scope
- Any change to `DraftReplyLoggingBehavior.cs` production logic or its registration in the MediatR pipeline.
- Testing `RagInteractionLogFactory.Build` internals in depth (assume it is either already covered elsewhere or trivial; only assert the fields needed to prove this behavior's contract).
- Testing `RagInteractionRecorder` itself (already exercised indirectly via the sibling suite and used here only as a test fixture).
- Raising the coverage-gate threshold or CI configuration changes — this task only adds the missing tests to clear the existing 60% threshold for this file.
- Integration/E2E tests — this is unit-test-only work using mocked repository/user-service.

## Open Questions
None.

## Status: COMPLETE
