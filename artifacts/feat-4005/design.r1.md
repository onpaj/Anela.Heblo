# Design: Unit Test Coverage for SubmitDraftReplyFeedbackHandler

## Component Design

**New file:** `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SubmitDraftReplyFeedbackHandlerTests.cs`

A single test class, `SubmitDraftReplyFeedbackHandlerTests`, following the existing `GetDraftReplyFeedbackListHandlerTests` pattern:

- **Fields:** `Mock<IRagInteractionLogRepository>` and `Mock<ICurrentUserService>`, initialized inline (`= new();`).
- **`CreateHandler()`:** private helper constructing `new SubmitDraftReplyFeedbackHandler(_repository.Object, _currentUserService.Object)`.
- **Test methods (one per FR, AAA style):**
  - `Handle_LogNotFound_ReturnsNotFound` (FR-1)
  - `Handle_WrongFeature_ReturnsNotFound` (FR-2)
  - `Handle_OwnershipMismatch_ReturnsForbidden` (FR-3)
  - `Handle_PrecisionScoreAlreadySet_ReturnsAlreadySubmitted` (FR-4)
  - `Handle_StyleScoreAlreadySet_ReturnsAlreadySubmitted` (FR-5)
  - `Handle_Success_WritesScoresAndSaves` (FR-6)
  - `Handle_Success_NullComment_WritesNull` (FR-7)

Each method arranges a `RagInteractionLog` (and, where reached, a `CurrentUser` via `_currentUserService.Setup(...)`), acts by awaiting `CreateHandler().Handle(request, CancellationToken.None)`, and asserts on the response plus `Mock.Verify` calls for `GetCurrentUser`/`SaveChangesAsync` as specified per FR. No production code is touched; the handler under test is exercised only through its two existing interface dependencies.

## Data Schemas

No schema changes — tests exercise existing types as mocks/plain objects:

- **`SubmitDraftReplyFeedbackRequest`**: `LogId: Guid`, `PrecisionScore: int`, `StyleScore: int`, `Comment: string?`
- **`SubmitDraftReplyFeedbackResponse : BaseResponse`**: `Success: bool`, `ErrorCode: ErrorCodes?`, `Params: Dictionary<string,string>?`
- **`RagInteractionLog`** (test fixture, mutated in place by the handler): `Id: Guid`, `Feature: RagFeature`, `UserId: string`, `PrecisionScore: int?`, `StyleScore: int?`, `FeedbackComment: string?`
- **`CurrentUser`** (record, mocked via `ICurrentUserService.GetCurrentUser()`): `Id, Name, Email, IsAuthenticated`
- **Error codes exercised:** `ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound` (2709), `ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted` (2710), `ErrorCodes.Forbidden` (0014)
- **Mocked collaborator members:** `IRagInteractionLogRepository.GetByIdAsync(Guid, CancellationToken) -> Task<RagInteractionLog?>`, `IRagInteractionLogRepository.SaveChangesAsync(CancellationToken) -> Task`, `ICurrentUserService.GetCurrentUser() -> CurrentUser`
