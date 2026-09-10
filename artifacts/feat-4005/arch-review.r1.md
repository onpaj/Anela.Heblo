# Architecture Review: Unit Test Coverage for SubmitDraftReplyFeedbackHandler

## Skip Design: true

This is a backend-only unit-test coverage-gap task with zero production code changes and no UI surface. No screens, components, or visual decisions are involved.

## Architectural Fit Assessment

This is the simplest possible fit: adding a missing test class beside an existing, well-understood handler, following a convention already established by a sibling test in the same feature folder (`GetDraftReplyFeedbackListHandlerTests`). No new architectural surface, no new abstractions, no module-boundary questions. `SubmitDraftReplyFeedbackHandler` is a small MediatR handler with two constructor-injected dependencies (`IRagInteractionLogRepository`, `ICurrentUserService`), both already interfaces with mockable methods (`GetByIdAsync`, `SaveChangesAsync`, `GetCurrentUser`) — this is exactly the shape the project's existing handler tests are built for. There is nothing to architect here beyond confirming the test file's location and mock setup match the codebase's own conventions, which the exploration below confirms.

## Proposed Architecture

### Component Overview

```
backend/test/Anela.Heblo.Tests/Features/Smartsupp/
  └── SubmitDraftReplyFeedbackHandlerTests.cs   (NEW)
        ├── Mock<IRagInteractionLogRepository>   -- GetByIdAsync, SaveChangesAsync
        └── Mock<ICurrentUserService>            -- GetCurrentUser

Handler under test (unchanged):
  SubmitDraftReplyFeedbackHandler.Handle(request, ct)
        → _repository.GetByIdAsync
        → _currentUserService.GetCurrentUser (only reached if log found+correct feature)
        → _repository.SaveChangesAsync (only reached on happy path)
```

No new components, no DI registration changes, no new files outside the single test class.

### Key Design Decisions

#### Decision 1: Test class location and naming
**Options considered:** (a) new file `SubmitDraftReplyFeedbackHandlerTests.cs` in `Features/Smartsupp/` alongside `GetDraftReplyFeedbackListHandlerTests.cs`; (b) a nested subfolder per use case (e.g. `Features/Smartsupp/SubmitDraftReplyFeedback/...Tests.cs`).
**Chosen approach:** (a) — flat file directly under `Features/Smartsupp/`, matching every existing Smartsupp handler test in the directory listing (`GetDraftReplyFeedbackListHandlerTests.cs`, `CloseConversationHandlerTests.cs`, `GetConversationHandlerTests.cs`, etc. are all flat, not nested per use case; only `Mappers/` and `WebhookAudit/` get subfolders because they group multiple related types).
**Rationale:** Consistency with 20+ sibling files in the same directory. No test currently mirrors the `UseCases/<Name>/` nesting from `src/` — don't invent a new convention for one file.

#### Decision 2: Mock construction and verification style
**Options considered:** (a) field-initialized `Mock<T>` instances (`private readonly Mock<...> _x = new();`) with a `CreateHandler()` factory method, matching `GetDraftReplyFeedbackListHandlerTests`; (b) constructor-built handler per test method.
**Chosen approach:** (a), copying the `GetDraftReplyFeedbackListHandlerTests` pattern exactly: readonly mock fields + a private `CreateHandler()` helper.
**Rationale:** Spec explicitly cites this file as the pattern to follow ("per the existing pattern used by sibling handler tests"). No reason to deviate.

#### Decision 3: How to assert "SaveChangesAsync never called" / "GetCurrentUser never called"
**Options considered:** (a) `Mock.Verify(x => x.Method(...), Times.Never)`; (b) rely on default Moq behavior (unset method returns default, no explicit verification).
**Chosen approach:** (a) — explicit `Times.Never`/`Times.Once` verification per FR-1 through FR-6's acceptance criteria.
**Rationale:** The spec requires this as an explicit assertion, not an implicit consequence of unconfigured mocks (which would silently pass even if the code were refactored to call the method with a no-op return). This is the whole point of the coverage gap: catching a regression, not just reaching the line.

## Implementation Guidance

### Directory / Module Structure

Single new file:
```
backend/test/Anela.Heblo.Tests/Features/Smartsupp/SubmitDraftReplyFeedbackHandlerTests.cs
```
No production files change. No `.csproj` changes needed — `Anela.Heblo.Tests` already references xUnit, Moq, and FluentAssertions (confirmed via `GetDraftReplyFeedbackListHandlerTests.cs`'s usings).

### Interfaces and Contracts

Types to reference (all verified to exist as named in the spec, no discrepancies found):
- `SubmitDraftReplyFeedbackHandler` — constructor `(IRagInteractionLogRepository repository, ICurrentUserService currentUserService)`
- `SubmitDraftReplyFeedbackRequest` — `LogId: Guid`, `PrecisionScore: int`, `StyleScore: int`, `Comment: string?` (note: `[Range(1,5)]` attributes exist on the request but are a pipeline-validation concern, not exercised by `Handle` — out of scope per spec, correctly)
- `SubmitDraftReplyFeedbackResponse : BaseResponse` — parameterless ctor (success) and `(ErrorCodes, Dictionary<string,string>?)` ctor (failure); inherited `Success`, `ErrorCode`, `Params` members come from `BaseResponse`
- `RagInteractionLog` — settable `Feature`, `UserId`, `PrecisionScore` (`int?`), `StyleScore` (`int?`), `FeedbackComment` (`string?`)
- `RagFeature` enum — use `RagFeature.SmartsuppDraftReply` and any other member (e.g. `RagFeature.KnowledgeBase`) for FR-2's wrong-feature case
- `CurrentUser` — **is a `record`** (`Domain/Features/Users/CurrentUser.cs`, positional: `Id, Name, Email, IsAuthenticated`) — this is a domain type, not a DTO, so the project's "DTOs are classes" rule does not apply here; construct it as `new CurrentUser("user-b", null, null, true)` or with named args
- `ICurrentUserService.GetCurrentUser()` — mock via `_currentUserService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser(...))`
- `IRagInteractionLogRepository.GetByIdAsync(Guid, CancellationToken)` returning `Task<RagInteractionLog?>`, and `SaveChangesAsync(CancellationToken)` returning `Task`
- `ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound` (2709), `ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted` (2710), `ErrorCodes.Forbidden` (0014) — all confirmed present in `Anela.Heblo.Application/Shared/ErrorCodes.cs`

### Data Flow

Standard AAA per test method, all using mocks only — no `DbContext`, no real database:
1. **Arrange**: build a `RagInteractionLog` instance with the fields under test; `Setup` the repository's `GetByIdAsync` to return it (or `null` for FR-1); for FR-3–FR-7, `Setup` `_currentUserService.GetCurrentUser()` to return a matching or mismatched `CurrentUser`.
2. **Act**: `await CreateHandler().Handle(request, CancellationToken.None)` (or `default`, matching sibling style).
3. **Assert**: check `result.Success` / `result.ErrorCode` / `result.Params["logId"]`, then `Mock.Verify` calls on `_repository`/`_currentUserService` as specified per FR, then (for FR-4/FR-6/FR-7) assert on the same `log` instance's mutated/unmutated fields since the handler mutates the entity reference in place rather than returning a new one.

Note for FR-1: since `request.LogId.ToString()` is used as the dictionary value, use the same `Guid` instance in both the request and the assertion to avoid a formatting mismatch (there is none for `Guid.ToString()`, but keep the `Guid` variable shared between arrange and assert for clarity).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test asserts on `BaseResponse` members (`Success`, `ErrorCode`, `Params`) that may have subtly different shapes than expected (e.g. `Params` key casing) | Low | `GetDraftReplyFeedbackListHandlerTests` already exercises `result.Success`; if `BaseResponse.Params` behaves unexpectedly, read `Application/Shared/BaseResponse.cs` before finalizing assertions — not read in this pass, but the constructor signature `(ErrorCodes, Dictionary<string,string>?)` is confirmed on the response class itself |
| Mutating the same `RagInteractionLog` instance across the "before" and "after" assertion in FR-4/FR-6/FR-7 could mask a bug if the handler ever swapped to returning a new entity | Low | Not a concern for this handler (mutates in place, verified by reading the source) — no mitigation needed, just don't defensively clone the log in the test |
| None of these tests touch EF Core or a real `DbContext`, so they don't validate the repository implementation itself | None (by design) | Explicitly out of scope per spec; repository behavior has its own tests elsewhere |

## Specification Amendments

None. The spec is complete, unambiguous, and fully verified against the actual handler source, request/response types, `CurrentUser`, and `ErrorCodes` — every type and member the spec references exists exactly as described. Proceed with implementation as specified.

## Prerequisites

None. No migrations, no config, no new packages — `Anela.Heblo.Tests` already has xUnit/Moq/FluentAssertions wired up and a directly analogous handler test to copy the pattern from.
