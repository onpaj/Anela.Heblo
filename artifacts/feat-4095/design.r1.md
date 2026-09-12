# Design: Unit tests for `DraftReplyLoggingBehavior`

## Component Design

No production components are introduced or changed. `DraftReplyLoggingBehavior.cs` is untouched. The
only design surface for this ticket is the new test class.

### `DraftReplyLoggingBehaviorTests`
- **Location:** `backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs`
  (new top-level `Smartsupp/Pipeline/` folder, mirroring the existing top-level `KnowledgeBase/Pipeline/`
  that holds the structural precedent `QuestionLoggingBehaviorTests.cs` — not the `Features/Smartsupp/`
  folder where the rest of this module's tests live; per arch review Decision 2, this pre-existing
  dual-layout inconsistency is not this ticket's to fix).
- **Framework:** xUnit (`[Fact]`), Moq, plain `Assert.*` (matching the sibling test, not
  FluentAssertions, per arch review Decision 3).
- **System under test:** `DraftReplyLoggingBehavior`, invoked directly via `Handle(request, next, cancellationToken)` — no MediatR pipeline, no DI container, no database.

**Collaborators — mocked (Moq):**
| Field | Type | Role |
|---|---|---|
| `_repositoryMock` | `Mock<IRagInteractionLogRepository>` | Verify `SaveAsync` is/isn't called; `Setup(...).ThrowsAsync(...)` for the exception path; `Callback` to capture the persisted `RagInteractionLog` |
| `_currentUserServiceMock` | `Mock<ICurrentUserService>` | `GetCurrentUser()` returns a known `CurrentUser` (e.g. `new CurrentUser("user-1", "Test User", null, true)`) |
| `_loggerMock` | `Mock<ILogger<DraftReplyLoggingBehavior>>` | Present only to satisfy the constructor; not asserted against (sibling test does not assert on the logger either) |

**Collaborator — real, not mocked:**
| Field | Type | Role |
|---|---|---|
| `_recorder` | `RagInteractionRecorder` (concrete) | Populated via its real public API (`RecordInteraction(RagFeature.SmartsuppDraftReply, ...)`, optionally `RecordRetrieval(...)`) the same way `GenerateDraftReplyHandler` does in production. Using the concrete type (not `Mock<IRagInteractionRecorder>`) lets `HasInteraction` fall out naturally from whether `RecordInteraction` was called, rather than requiring a mock stub that could drift out of sync with production behavior — this is the exact risk the spec calls out for FR-2/FR-3. |

**Test-double wiring for `next`:** a plain delegate `() => Task.FromResult(response)` stands in for the inner `GenerateDraftReplyHandler`; no real handler is invoked.

**Test cases (6 `[Fact]`s, matching `QuestionLoggingBehaviorTests`'s granularity):**
1. `Handle_WhenResponseUnsuccessful_DoesNotSaveOrSetId` — FR-2: recorder has an interaction recorded, `next` returns `Success = false`; assert `SaveAsync` never called, `result.Id` is `null`, other response fields (e.g. `Answer`) unchanged.
2. `Handle_WhenNoInteractionRecorded_DoesNotSaveOrSetId` — FR-3: recorder left untouched (`HasInteraction == false`), `next` returns `Success = true`; assert `SaveAsync` never called, `result.Id` is `null`.
3. `Handle_WritesLogRow_AndReturnsResponse` — FR-4 (log content): happy path; capture the `RagInteractionLog` passed to `SaveAsync` via `Callback` and assert its `UserId` matches the mocked current user, plus whichever of `Question`/`Answer`/`TopK` were actually recorded in this test's arrange step. Do not assert factory-internal fields (`SystemPrompt`, `RetrievedChunksJson`, `ExpandedQuery`, etc.) not exercised by this behavior's contract.
4. `Handle_WhenLogSaved_SetsResponseIdToLogId` — FR-4 (id stamping): assert `SaveAsync` called exactly once with the same `CancellationToken` passed into `Handle`, and `result.Id` is non-null, non-`Guid.Empty`, and equal to the captured log's `Id`.
5. `Handle_WhenDbWriteFails_StillReturnsResponse` — FR-5: `_repositoryMock.Setup(r => r.SaveAsync(...)).ThrowsAsync(new Exception(...))`; request has a concrete `ConversationId` (e.g. `"conv-1"`, never `null`, since the catch block interpolates it into a log message); `await Handle(...)` directly (not `Assert.ThrowsAsync`) to prove no exception propagates; assert `result.Answer` (or equivalent) unchanged from what `next` produced.
6. `Handle_WhenDbWriteFails_ResponseIdRemainsNull` — FR-5: same arrange as #5; assert `result.Id` is `null` (the throw happens before `response.Id = log.Id` is reached).

No test exercises MediatR pipeline registration/DI wiring for `DraftReplyLoggingBehavior` — out of scope per spec.

## Data Schemas
No new or changed data schemas, database tables, API request/response shapes, or event payloads. This
is a test-only change against existing, unmodified types (`RagInteractionLog`, `GenerateDraftReplyRequest`,
`GenerateDraftReplyResponse`, `CurrentUser`). Field references needed for test construction are already
enumerated in `spec.r1.md`'s Data Model section and are not repeated here.
