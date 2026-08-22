# Specification: Unit Tests for CreateJournalTagHandler

## Summary
`CreateJournalTagHandler` (Journal module) currently has 24.3% line coverage against a 60% threshold, with the authorization guard and the success/persistence path both untested. This is a narrow test-coverage task: add a focused unit test suite that exercises the unauthenticated-rejection branch and the authenticated-success branch, mirroring the existing sibling test suite for `CreateJournalEntryHandler`. No production code changes are required or in scope.

## Background
`CreateJournalTagHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalTag/CreateJournalTagHandler.cs`) has two logical branches:

1. If `_currentUserService.GetCurrentUser()` returns a user that is not authenticated, or whose `Id` is null/empty, the handler short-circuits and returns a `CreateJournalTagResponse` built with `ErrorCodes.UnauthorizedJournalAccess` and a `Params` dictionary containing `{ "resource": "journal_tag" }`. The repository is never touched in this branch.
2. Otherwise, the handler builds a new `JournalEntryTag` (trimming `request.Name`, copying `request.Color`, stamping `CreatedAt = DateTime.UtcNow` and `CreatedByUserId = currentUser.Id`), persists it via `_tagRepository.AddAsync` followed by `_tagRepository.SaveChangesAsync`, logs an informational message, and returns a `CreateJournalTagResponse` populated with the persisted tag's `Id`, `Name`, and `Color`.

Neither branch is currently covered by an automated test, which is a regression risk: journal tags are user-scoped, and a silent removal of the auth guard would let unauthenticated requests create tags attributed to an empty/blank `CreatedByUserId`, corrupting ownership data. Likewise, a regression in `Name.Trim()` would allow visually-duplicate tags (e.g. `"Urgent"` vs `"Urgent "`) to be created as distinct records.

An analogous handler in the same module, `CreateJournalEntryHandler`, already has a test suite (`backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalEntryHandlerTests.cs`) that follows the same Moq + FluentAssertions + xUnit pattern this task should reuse for consistency.

## Functional Requirements

### FR-1: Unauthenticated request is rejected without persistence
When `Handle` is invoked with a `CreateJournalTagRequest` and `ICurrentUserService.GetCurrentUser()` returns a `CurrentUser` where `IsAuthenticated == false` (or, as a second scenario, `IsAuthenticated == true` but `Id` is `null`/empty), the handler must return a failure response and must not attempt to persist anything.

**Acceptance criteria:**
- A test arranges `ICurrentUserService.GetCurrentUser()` to return a non-authenticated `CurrentUser` (e.g. `IsAuthenticated: false`) and asserts:
  - `result.Success` is `false`.
  - `result.ErrorCode` equals `ErrorCodes.UnauthorizedJournalAccess`.
  - `result.Params` contains key `"resource"` with value `"journal_tag"`.
- The same test (or a companion test) verifies `IJournalTagRepository.AddAsync(...)` was never invoked (`Times.Never`), and `SaveChangesAsync` was never invoked.
- A second test covers the `Id`-missing-but-authenticated-flag-true edge case (`IsAuthenticated: true`, `Id: null` or `Id: ""`) and asserts the same failure response/behavior, to lock in the `string.IsNullOrEmpty(currentUser.Id)` half of the guard condition.

### FR-2: Authenticated request creates and persists a trimmed, correctly-attributed tag
When `Handle` is invoked with a `CreateJournalTagRequest` and `ICurrentUserService.GetCurrentUser()` returns an authenticated `CurrentUser` with a non-empty `Id`, the handler must persist a new tag via the repository and return a success response reflecting the persisted entity.

**Acceptance criteria:**
- A test arranges an authenticated `CurrentUser` (non-empty `Id`) and a `CreateJournalTagRequest.Name` containing leading/trailing whitespace (e.g. `"  Urgent  "`).
- `IJournalTagRepository.AddAsync` is mocked to capture the `JournalEntryTag` argument and return a tag instance with a populated `Id` (e.g. `Id = 42`), the same `Color`, and the trimmed `Name`.
- The test asserts, on the `JournalEntryTag` instance passed into `AddAsync`:
  - `Name` equals the trimmed value (`"Urgent"`), not the raw input.
  - `CreatedByUserId` equals `currentUser.Id`.
  - `Color` equals `request.Color`.
- The test asserts `IJournalTagRepository.SaveChangesAsync` was called exactly once.
- The test asserts the returned `CreateJournalTagResponse`:
  - `Success` is `true` (no `ErrorCode` set).
  - `Id` equals the persisted tag's `Id` (from the mocked `AddAsync` return value).
  - `Name` and `Color` equal the persisted tag's `Name`/`Color`.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a unit test addition with mocked dependencies; no I/O, no timing constraints. Full suite runtime impact should be negligible (sub-second for these two-to-three test cases).

### NFR-2: Security
No production security surface is changed. The tests exist specifically to guard the existing authorization check (`IsAuthenticated` / non-empty `Id`) against regression; they must not weaken, bypass, or change that check's behavior.

## Data Model
No schema or entity changes. Tests operate against existing types:
- `Anela.Heblo.Domain.Features.Journal.JournalEntryTag` (`Id: int`, `Name: string`, `Color: string`, `CreatedAt: DateTime`, `CreatedByUserId: string`)
- `Anela.Heblo.Domain.Features.Users.CurrentUser` (record: `Id: string?`, `Name: string?`, `Email: string?`, `IsAuthenticated: bool`)
- `Anela.Heblo.Application.Features.Journal.Contracts.CreateJournalTagRequest` (`Name: string`, `Color: string`, default `"#6B7280"`)
- `Anela.Heblo.Application.Features.Journal.Contracts.CreateJournalTagResponse` (inherits `BaseResponse`: `Success`, `ErrorCode`, `Params`; adds `Id`, `Name`, `Color`)

## API / Interface Design
No public API or interface changes. Tests target the MediatR handler directly:

- `CreateJournalTagHandler.Handle(CreateJournalTagRequest, CancellationToken)` — invoked directly (bypassing MediatR pipeline), with constructor dependencies replaced by mocks:
  - `Mock<IJournalTagRepository>` — stub/verify `AddAsync(JournalEntryTag, CancellationToken)` and `SaveChangesAsync(CancellationToken)`.
  - `Mock<ICurrentUserService>` — stub `GetCurrentUser()`.
  - `Mock<ILogger<CreateJournalTagHandler>>` — no assertions needed; passed through to satisfy the constructor.

New test file location (following the existing module convention): `backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalTagHandlerTests.cs`, using xUnit `[Fact]` methods, Moq for mocking, and FluentAssertions for assertions — matching `CreateJournalEntryHandlerTests.cs` in the same directory.

## Dependencies
- Existing test project `Anela.Heblo.Tests` and its already-referenced packages: xUnit, Moq, FluentAssertions. No new package references required.
- No dependency on external services, database, or network — the repository is fully mocked.

## Out of Scope
- Any change to `CreateJournalTagHandler.cs` or any other production source file.
- Testing `CreateJournalTagRequest` validation attributes (`[Required]`, `[MaxLength]`) — these are ASP.NET model-binding concerns, not handler logic, and are not exercised by calling `Handle` directly.
- Integration tests exercising the real `IJournalTagRepository` / database (a separate `JournalRepositoryIntegrationTests.cs` already exists for that layer).
- Coverage of other Journal Tag use cases (e.g. update/delete/list tags) — this task is scoped to `CreateJournalTagHandler` only, per the coverage-gap ticket.
- Raising the file's line coverage to any specific numeric target beyond what the two described test cases naturally achieve — the two cases cover both branches of `Handle`, which is the stated goal.

## Open Questions
None.

## Status: COMPLETE
