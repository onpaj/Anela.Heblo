# Design: Unit Tests for CreateJournalTagHandler

## Component Design

**Test class:** `Anela.Heblo.Tests.Features.Journal.CreateJournalTagHandlerTests`
Location: `backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalTagHandlerTests.cs`

Structure mirrors the sibling `CreateJournalEntryHandlerTests` in the same directory: constructor-wired private readonly mock fields, a `CreateJournalTagHandler` instance built from those mocks, and one `[Fact]` per scenario (no shared base/fixture, no `[Theory]`).

**Mocked dependencies (constructor fields):**
- `Mock<IJournalTagRepository>` — stubs/verifies `AddAsync(JournalEntryTag, CancellationToken)` and `SaveChangesAsync(CancellationToken)`.
- `Mock<ICurrentUserService>` — stubs `GetCurrentUser()` to return the `CurrentUser` fixture for each scenario.
- `Mock<ILogger<CreateJournalTagHandler>>` — pass-through only, no assertions.

**Test-case groups:**

1. **Unauthorized-guard group** (FR-1) — two `[Fact]`s:
   - `Handle_WhenUserNotAuthenticated_ShouldReturnUnauthorizedError` — `GetCurrentUser()` returns `IsAuthenticated: false`.
   - `Handle_WhenUserIdIsEmpty_ShouldReturnUnauthorizedError` — `GetCurrentUser()` returns `IsAuthenticated: true`, `Id: null`/`""`.

   Each asserts `result.Success == false`, `result.ErrorCode == ErrorCodes.UnauthorizedJournalAccess`, `result.Params["resource"] == "journal_tag"`, and verifies `AddAsync`/`SaveChangesAsync` were never invoked on the repository mock.

2. **Success/persistence group** (FR-2) — one `[Fact]`:
   - `Handle_WhenValidRequest_ShouldCreateJournalTagSuccessfully` (naming to match sibling convention) — `GetCurrentUser()` returns an authenticated `CurrentUser` with a non-empty `Id`; request `Name = "  Urgent  "`; `AddAsync` mocked to return a persisted `JournalEntryTag` with `Id = 42`.

   Asserts, via `It.Is<JournalEntryTag>(...)` at the `AddAsync` call site, that the entity passed in has `Name == "Urgent"` (trimmed), `CreatedByUserId == currentUser.Id`, and `Color == request.Color`; verifies `SaveChangesAsync` called exactly once; and asserts the returned `CreateJournalTagResponse` has `Success == true` and `Id`/`Name`/`Color` matching the persisted tag.

The handler is invoked directly (`handler.Handle(request, CancellationToken.None)`), bypassing the MediatR pipeline, consistent with the rest of the Journal test suite. No new test infrastructure, base classes, or helpers are introduced.

## Data Schemas

No new schemas. Tests exercise these existing types as-is:

- `Anela.Heblo.Domain.Features.Journal.JournalEntryTag` — `Id: int`, `Name: string`, `Color: string`, `CreatedAt: DateTime`, `CreatedByUserId: string`.
- `Anela.Heblo.Domain.Features.Journal.IJournalTagRepository : IRepository<JournalEntryTag, int>` — `AddAsync(JournalEntryTag, CancellationToken) : Task<JournalEntryTag>`, `SaveChangesAsync(CancellationToken) : Task<int>`.
- `Anela.Heblo.Domain.Features.Users.CurrentUser` — record `(Id: string?, Name: string?, Email: string?, IsAuthenticated: bool)`.
- `Anela.Heblo.Application.Features.Journal.Contracts.CreateJournalTagRequest` — `Name: string`, `Color: string` (default `"#6B7280"`).
- `Anela.Heblo.Application.Features.Journal.Contracts.CreateJournalTagResponse : BaseResponse` — inherited `Success`, `ErrorCode`, `Params`; adds `Id: int`, `Name: string`, `Color: string`.
- `Anela.Heblo.Application.Features.Journal.ErrorCodes.UnauthorizedJournalAccess`.
