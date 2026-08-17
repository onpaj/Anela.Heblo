# Architecture Review: Unit Tests for CreateJournalTagHandler

## Skip Design: true

This is a backend-only, test-only change (adding `CreateJournalTagHandlerTests.cs`). No production code, no API contracts, no UI component, screen, or visual behavior is touched or introduced. There is nothing for a design pass to evaluate.

## Architectural Fit Assessment

This task fits the codebase's existing patterns exactly and requires no architectural decisions of its own — it is closing a coverage gap on an already-correct implementation, not introducing new structure.

- `CreateJournalTagHandler` is a standard MediatR `IRequestHandler<TRequest, TResponse>` in the Journal vertical slice (`Features/Journal/UseCases/CreateJournalTag/`), following the same shape as its sibling `CreateJournalEntryHandler` in `Features/Journal/UseCases/CreateJournalEntry/`: constructor-injected repository + `ICurrentUserService` + `ILogger<T>`, an early-return auth guard, then a create/persist/return flow.
- I verified the handler source (`CreateJournalTagHandler.cs`) against the spec line-by-line — the guard condition (`!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id)`), the `ErrorCodes.UnauthorizedJournalAccess` response with `{"resource": "journal_tag"}`, the `Name.Trim()` normalization, and the `AddAsync`/`SaveChangesAsync` persistence sequence all match the spec's description exactly. No discrepancy found.
- I confirmed the repository contract: `IJournalTagRepository : IRepository<JournalEntryTag, int>`, and the base `IRepository<TEntity, TKey>` (`Anela.Heblo.Xcc/Persistance/IRepository.cs`) declares `Task<TEntity> AddAsync(...)` and `Task<int> SaveChangesAsync(...)`. The spec's mock setup guidance (`AddAsync` returns a `JournalEntryTag`, `SaveChangesAsync` returns `int`) is consistent with this.
- I read the sibling test file `CreateJournalEntryHandlerTests.cs` in full. It uses the exact pattern the spec asks to mirror: xUnit class with `Mock<TRepo>`, `Mock<ICurrentUserService>`, `Mock<ILogger<THandler>>` fields wired up in the constructor, `[Fact]` methods named `Handle_When<Condition>_Should<Outcome>`, FluentAssertions (`.Should().Be(...)`, `.Should().ContainKey(...)`), and Moq `.Verify(..., Times.Never/Once)`. The new test file should follow this exact structural template — same field naming convention, same constructor-based setup, same assertion style.
- Integration points: none beyond the handler itself and its two constructor dependencies (`IJournalTagRepository`, `ICurrentUserService`); `ILogger` is a pass-through mock, not asserted on, consistent with the sibling test.

No conflicts with existing conventions were found. No new abstractions, interfaces, or test infrastructure are needed — the `Anela.Heblo.Tests` project already references xUnit, Moq, and FluentAssertions, and already has three tests set up this way in the same folder.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Tests (test project)
└── Features/Journal/
    ├── CreateJournalEntryHandlerTests.cs   (existing, pattern source)
    └── CreateJournalTagHandlerTests.cs     (NEW — this task)
            │
            │ constructs, with mocked deps:
            ▼
    CreateJournalTagHandler                 (production, Anela.Heblo.Application)
            │                    │
            ▼                    ▼
    IJournalTagRepository   ICurrentUserService     (both mocked via Moq)
    (Mock<IJournalTagRepository>)   (Mock<ICurrentUserService>)
```

No new components. The test file is a pure consumer of the existing handler and its two interface dependencies, invoked directly (`handler.Handle(request, CancellationToken.None)`), bypassing the MediatR pipeline — matching how the sibling test and the rest of the Journal test suite operate.

### Key Design Decisions

#### Decision 1: Test structure — mirror `CreateJournalEntryHandlerTests` exactly

**Options considered:**
- (a) Copy the structural pattern of `CreateJournalEntryHandlerTests.cs` (constructor-wired mocks as private readonly fields, one `[Fact]` per scenario).
- (b) Use a shared test base class or fixture to reduce boilerplate across Journal handler tests.
- (c) Use `[Theory]`/`InlineData` to parametrize the two unauthorized-guard scenarios into one test method.

**Chosen approach:** (a), with a light touch of (c) only if it doesn't reduce clarity — but given the spec explicitly separates "not authenticated" and "authenticated but empty Id" into two named test cases with a shared assertion shape, two `[Fact]` methods (not a `[Theory]`) is preferable here, matching how `CreateJournalEntryHandlerTests` handles its own two analogous unauthorized cases (`Handle_WhenUserNotAuthenticated_ShouldReturnUnauthorizedError` and `Handle_WhenUserIdIsEmpty_ShouldReturnUnauthorizedError` — two separate `[Fact]`s, not a `[Theory]`).

**Rationale:** Consistency with the immediate sibling test in the same directory outweighs marginal DRYness. A shared base/fixture (option b) is out of scope — no other Journal test currently uses one, and introducing one here would be a scope-creeping architectural change the spec explicitly rules out ("No production code changes... Out of Scope").

#### Decision 2: Assertion depth on the success path

**Options considered:**
- (a) Assert only on the returned `CreateJournalTagResponse` (`Success`, `Id`, `Name`, `Color`).
- (b) Assert on both the returned response AND the `JournalEntryTag` instance captured via `It.Is<JournalEntryTag>(...)` at the `AddAsync` call site (verifying `Name` is trimmed, `CreatedByUserId` is set correctly, `Color` passed through) before it ever reaches the mocked return value.

**Chosen approach:** (b) — matches `CreateJournalEntryHandlerTests.Handle_WhenValidRequest_ShouldCreateJournalEntrySuccessfully`, which verifies the `AddAsync` argument via `It.Is<JournalEntry>(e => e.Title == ... && e.CreatedByUserId == ...)`.

**Rationale:** The spec's stated risk (FR-2, "Why it matters" in the brief) is specifically that `Name.Trim()` or `CreatedByUserId` assignment could regress silently. Asserting only on the final response would not catch a bug where the trim/assignment happens correctly in the response construction but not in the entity actually persisted (or vice versa) — asserting on the `AddAsync` argument closes that gap and is what the sibling test already does for the equivalent risk.

## Implementation Guidance

### Directory / Module Structure

No new directories. Single new file:

```
backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalTagHandlerTests.cs
```

Namespace: `Anela.Heblo.Tests.Features.Journal` (matches all sibling files in this directory — confirmed via `CreateJournalEntryHandlerTests.cs` line 11).

### Interfaces and Contracts

No new interfaces or contracts. Test targets these existing types directly (all verified against source):

- `CreateJournalTagHandler` — constructor: `(IJournalTagRepository, ICurrentUserService, ILogger<CreateJournalTagHandler>)`.
- `IJournalTagRepository` (`Anela.Heblo.Domain.Features.Journal`) — mock `AddAsync(JournalEntryTag, CancellationToken) : Task<JournalEntryTag>` and `SaveChangesAsync(CancellationToken) : Task<int>` (inherited from `IRepository<TEntity, TKey>`).
- `ICurrentUserService.GetCurrentUser() : CurrentUser` — mock return value.
- `CurrentUser` (`Anela.Heblo.Domain.Features.Users`) — positional record `(Id, Name, Email, IsAuthenticated)`, same as used in the sibling test.
- `CreateJournalTagRequest` — `Name` (string, required), `Color` (string, default `"#6B7280"`).
- `CreateJournalTagResponse : BaseResponse` — `Id` (int), `Name` (string), `Color` (string), plus inherited `Success`, `ErrorCode`, `Params`.
- `JournalEntryTag` (`Anela.Heblo.Domain.Features.Journal`) — `Id`, `Name`, `Color`, `CreatedAt`, `CreatedByUserId`.
- `ErrorCodes.UnauthorizedJournalAccess`.

Use `SaveChangesAsync(...).ReturnsAsync(1)` in the mock setup (return type is `Task<int>`), matching the sibling test's `_repositoryMock.Setup(x => x.SaveChangesAsync(...)).ReturnsAsync(1);`.

### Data Flow

Test-only; no production data flow changes. Per test case:

1. **Unauthenticated / missing-Id cases:** `Mock<ICurrentUserService>.Setup(GetCurrentUser).Returns(CurrentUser with IsAuthenticated:false or Id:null/empty)` → `handler.Handle(request, CancellationToken.None)` → assert `result.Success == false`, `result.ErrorCode == ErrorCodes.UnauthorizedJournalAccess`, `result.Params["resource"] == "journal_tag"` → verify `_tagRepositoryMock.Verify(AddAsync(...), Times.Never)` and `SaveChangesAsync(...), Times.Never`.
2. **Success case:** `Mock<ICurrentUserService>.Setup(GetCurrentUser).Returns(authenticated CurrentUser)`, `Mock<IJournalTagRepository>.Setup(AddAsync(...)).ReturnsAsync(persisted tag with Id=42)`, request `Name = "  Urgent  "` → `handler.Handle(...)` → verify `AddAsync` was called with `It.Is<JournalEntryTag>(t => t.Name == "Urgent" && t.CreatedByUserId == currentUser.Id && t.Color == request.Color)` → verify `SaveChangesAsync(...), Times.Once` → assert `result.Success == true`, `result.Id == 42`, `result.Name`/`result.Color` match the persisted tag.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test drifts from sibling pattern, creating inconsistency in the Journal test folder | Low | Follow `CreateJournalEntryHandlerTests.cs` structure field-for-field (same mock field names, same constructor wiring, same `[Fact]` naming convention). |
| Assertion only checks the final response and misses a `Name.Trim()`/`CreatedByUserId` regression at the persistence boundary | Medium | Assert on the `JournalEntryTag` argument captured at the `AddAsync` call site via `It.Is<...>(...)`, not just on the returned response (see Decision 2). |
| `BaseResponse.Success` semantics assumed rather than verified (i.e., that `Success == false` follows automatically from a non-null `ErrorCode`) | Low | Not a concern for this task — `BaseResponse`'s `Success` computation is exercised identically by the passing sibling test (`CreateJournalEntryHandlerTests`), so its behavior is already implicitly trusted/covered elsewhere; no new verification needed here. |

## Specification Amendments

None. The spec is accurate, fully grounded in the actual handler and repository code (I independently verified every signature and behavior it describes), and complete. No architectural changes are needed to implement it.

## Prerequisites

None. No migrations, config, or infrastructure changes are required — this is a pure test addition against existing, unchanged production code, using packages (xUnit, Moq, FluentAssertions) already referenced by the `Anela.Heblo.Tests` project.
