# Encapsulate JournalEntry Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the `JournalEntry` field-update bookkeeping (title/content/entryDate normalisation + modified-audit timestamps + modified-by identity) out of `UpdateJournalEntryHandler` into a single domain method `JournalEntry.Update(...)`, mirroring the existing `SoftDelete` pattern. Behaviour-preserving refactor.

**Architecture:** Adds one `public void Update(...)` method to the rich `JournalEntry` aggregate at `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs`, placed adjacent to `SoftDelete` (~line 153). Replaces the seven-line direct-mutation block at `UpdateJournalEntryHandler.cs:51-59` with a single call. The handler retains ownership of authentication, authorisation, the `"Unknown User"` fallback, repository persistence, and the two existing replace-collection calls (`ReplaceProductAssociations`, `ReplaceTagAssignments`). No schema, contract, DI, or HTTP-surface changes.

**Tech Stack:** .NET 8, EF Core (change tracker picks up entity mutations as today), MediatR (handler unchanged structurally), xUnit + FluentAssertions + Moq for tests.

---

## File Structure

**Modify:**
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs` — add a new `public void Update(string? title, string content, DateTime entryDate, string userId, string username)` method adjacent to `SoftDelete` (around line 153).
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs` — replace the block at lines 51-59 (the `var now = DateTime.UtcNow;` line through `entry.ModifiedByUsername = ...`) with a single `entry.Update(...)` call. The `?? "Unknown User"` fallback for `currentUser.Name` stays in the handler. The `ReplaceProductAssociations` and `ReplaceTagAssignments` calls at lines 61-62 remain unchanged.
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs` — add an `Update` test region alongside the existing `ReplaceProductAssociations_*` / `ReplaceTagAssignments_*` regions.

**Create:**
- `backend/test/Anela.Heblo.Tests/Features/Journal/UpdateJournalEntryHandlerTests.cs` — new xUnit test class modelled on `DeleteJournalEntryHandlerTests.cs`. Covers the four handler-level paths: unauthenticated, empty user id, entry not found, valid request. Provides the regression coverage the spec assumed already existed (it does not).

**No changes to:**
- `UpdateJournalEntryRequest` / `UpdateJournalEntryResponse` (contracts unchanged)
- `JournalEntryMapper`, `JournalEntryDto`
- `IJournalRepository` (interface unchanged)
- FluentValidation rules
- HTTP routes / status codes / error codes
- OpenAPI-generated TypeScript client (no contract change → no regeneration)
- Database schema (no migration)

---

## Task 1: Write failing tests for `JournalEntry.Update`

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs`

Test fixture helper `NewEntry()` already exists in this file (lines 9-17). Reuse it.

- [ ] **Step 1.1: Open `JournalEntryTests.cs` and append a new region at the end of the class (just before the closing `}` on the final line).**

Append these tests after the last `ReplaceTagAssignments_*` test. They reference `JournalEntry.Update`, which does not yet exist — the file will not compile, which is the desired RED state.

```csharp
    // ----- Update -----

    [Fact]
    public void Update_AssignsAllFieldsAndAuditTrail()
    {
        // Arrange
        var entry = NewEntry();
        var originalCreatedAt = entry.CreatedAt;
        var originalCreatedByUserId = entry.CreatedByUserId;
        var originalCreatedByUsername = entry.CreatedByUsername;
        var before = DateTime.UtcNow;

        // Act
        entry.Update(
            title: "New Title",
            content: "New content body",
            entryDate: new DateTime(2026, 6, 4, 14, 30, 0, DateTimeKind.Utc),
            userId: "user-42",
            username: "Alice");

        // Assert
        var after = DateTime.UtcNow;
        entry.Title.Should().Be("New Title");
        entry.Content.Should().Be("New content body");
        entry.EntryDate.Should().Be(new DateTime(2026, 6, 4));
        entry.ModifiedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        entry.ModifiedByUserId.Should().Be("user-42");
        entry.ModifiedByUsername.Should().Be("Alice");

        // Creation audit untouched
        entry.CreatedAt.Should().Be(originalCreatedAt);
        entry.CreatedByUserId.Should().Be(originalCreatedByUserId);
        entry.CreatedByUsername.Should().Be(originalCreatedByUsername);
    }

    [Fact]
    public void Update_WithNullTitle_StoresNullWithoutThrowing()
    {
        var entry = NewEntry();

        var act = () => entry.Update(
            title: null,
            content: "content",
            entryDate: DateTime.UtcNow,
            userId: "u",
            username: "n");

        act.Should().NotThrow();
        entry.Title.Should().BeNull();
    }

    [Fact]
    public void Update_TrimsTitleAndContent()
    {
        var entry = NewEntry();

        entry.Update(
            title: "  spaced title  ",
            content: "  spaced content  ",
            entryDate: DateTime.UtcNow,
            userId: "u",
            username: "n");

        entry.Title.Should().Be("spaced title");
        entry.Content.Should().Be("spaced content");
    }

    [Fact]
    public void Update_StripsTimeComponentFromEntryDate()
    {
        var entry = NewEntry();
        var input = new DateTime(2026, 6, 4, 14, 30, 45, DateTimeKind.Utc);

        entry.Update(
            title: "t",
            content: "c",
            entryDate: input,
            userId: "u",
            username: "n");

        entry.EntryDate.Should().Be(new DateTime(2026, 6, 4));
        entry.EntryDate.TimeOfDay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Update_DoesNotTouchDeletionFields()
    {
        var entry = NewEntry();
        // Pre-condition: not deleted
        entry.IsDeleted.Should().BeFalse();
        entry.DeletedAt.Should().BeNull();
        entry.DeletedByUserId.Should().BeNull();
        entry.DeletedByUsername.Should().BeNull();

        entry.Update(
            title: "t",
            content: "c",
            entryDate: DateTime.UtcNow,
            userId: "u",
            username: "n");

        entry.IsDeleted.Should().BeFalse();
        entry.DeletedAt.Should().BeNull();
        entry.DeletedByUserId.Should().BeNull();
        entry.DeletedByUsername.Should().BeNull();
    }

    [Fact]
    public void Update_DoesNotTouchProductAndTagCollections()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("AB-1");
        entry.AssignTag(7);

        entry.Update(
            title: "t",
            content: "c",
            entryDate: DateTime.UtcNow,
            userId: "u",
            username: "n");

        entry.ProductAssociations.Should().ContainSingle()
            .Which.ProductCodePrefix.Should().Be("AB-1");
        entry.TagAssignments.Should().ContainSingle()
            .Which.TagId.Should().Be(7);
    }
```

- [ ] **Step 1.2: Run the tests to verify they fail to compile (true RED).**

Run from the repo root:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -20
```

Expected: build fails with `CS1061` or `CS7036` complaining that `JournalEntry` does not contain a definition for `Update`. This confirms the tests reference the not-yet-existing API.

If the build instead succeeds, the new tests were not actually added to the file — re-check the edit.

---

## Task 2: Implement `JournalEntry.Update` to make Task 1 tests pass

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs:153`

- [ ] **Step 2.1: Insert the `Update` method adjacent to `SoftDelete`.**

Insert this method **immediately above** the existing `SoftDelete` method (which starts at line 153). Placement above (not below) keeps "the canonical update operation" first and "the lifecycle-end operation" after it — mirroring the order other aggregates in the codebase use.

Edit: replace the line containing `public void SoftDelete(string userId, string username)` so the new method appears immediately before it. The exact code to insert:

```csharp
        public void Update(string? title, string content, DateTime entryDate, string userId, string username)
        {
            Title = title?.Trim();
            Content = content.Trim();
            EntryDate = entryDate.Date;
            ModifiedAt = DateTime.UtcNow;
            ModifiedByUserId = userId;
            ModifiedByUsername = username;
        }

        public void SoftDelete(string userId, string username)
```

Notes for the implementer:
- Use four-space indentation followed by the existing class-body indent (matches the rest of the file).
- Do **not** add XML doc comments — `SoftDelete` does not have them; match the surrounding style.
- Do **not** add guard clauses (no `ArgumentNullException`, no empty-string checks). The spec is explicit that this is a recorder, not a guard. `content.Trim()` will throw `NullReferenceException` if a caller violates the non-nullable contract; that matches `SoftDelete`'s stance.
- Do **not** touch the `ReplaceProductAssociations` / `ReplaceTagAssignments` / `AssociateWithProduct` / `AssignTag` / `NormalizeProductCode` methods.

- [ ] **Step 2.2: Build to confirm compilation succeeds.**

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.` with zero warnings introduced.

- [ ] **Step 2.3: Run the new domain tests to verify GREEN.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~JournalEntryTests.Update_" --no-build 2>&1 | tail -15
```

(`--no-build` reuses the build from Step 2.2; if the test project hasn't been built yet, drop `--no-build`.)

Expected: 6 tests pass — `Update_AssignsAllFieldsAndAuditTrail`, `Update_WithNullTitle_StoresNullWithoutThrowing`, `Update_TrimsTitleAndContent`, `Update_StripsTimeComponentFromEntryDate`, `Update_DoesNotTouchDeletionFields`, `Update_DoesNotTouchProductAndTagCollections`.

If any test fails, the implementation diverged from the spec — fix the implementation, not the test.

- [ ] **Step 2.4: Run the full `JournalEntryTests` class to confirm no regressions.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~JournalEntryTests" --no-build 2>&1 | tail -10
```

Expected: all pre-existing `AssociateWithProduct_*`, `ReplaceProductAssociations_*`, `ReplaceTagAssignments_*` tests still pass alongside the 6 new ones.

- [ ] **Step 2.5: Commit.**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs \
        backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs
git commit -m "feat: add JournalEntry.Update domain method

Mirrors the existing SoftDelete pattern: the entity owns its own
field assignment, input normalisation (trim, date-strip) and
audit-trail bookkeeping (ModifiedAt/ModifiedByUserId/
ModifiedByUsername). No caller yet — handler refactor follows in the
next commit."
```

---

## Task 3: Write failing handler-level tests for `UpdateJournalEntryHandler`

The spec assumed handler-level tests already existed; they do not. Tests are added **before** the handler refactor so the refactor is verified against locked-in expectations.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Journal/UpdateJournalEntryHandlerTests.cs`

- [ ] **Step 3.1: Create the test file with the four handler-path tests.**

The file mirrors the structure of `DeleteJournalEntryHandlerTests.cs` (same field set, same DI wiring, same `CurrentUser` shape). Each test exercises the **current** handler behaviour, which must remain identical after Task 4.

```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.UpdateJournalEntry;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class UpdateJournalEntryHandlerTests
{
    private readonly Mock<IJournalRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<UpdateJournalEntryHandler>> _loggerMock;
    private readonly UpdateJournalEntryHandler _handler;

    public UpdateJournalEntryHandlerTests()
    {
        _repositoryMock = new Mock<IJournalRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<UpdateJournalEntryHandler>>();
        _handler = new UpdateJournalEntryHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    private static UpdateJournalEntryRequest BuildRequest(int id = 1) => new()
    {
        Id = id,
        Title = "  New Title  ",
        Content = "  Updated body  ",
        EntryDate = new DateTime(2026, 6, 4, 14, 30, 0, DateTimeKind.Utc),
        AssociatedProducts = new List<string> { "AB-1" },
        TagIds = new List<int> { 7 }
    };

    private static JournalEntry BuildExistingEntry(int id = 1) => new()
    {
        Id = id,
        Title = "Old Title",
        Content = "Old body",
        EntryDate = new DateTime(2026, 6, 1),
        CreatedAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
        CreatedByUserId = "creator",
        CreatedByUsername = "Creator",
        ModifiedAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorizedError()
    {
        // Arrange
        var request = BuildRequest();
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: null,
                Name: null,
                Email: null,
                IsAuthenticated: false));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedJournalAccess);
        result.Params.Should().ContainKey("resource");
        result.Params["resource"].Should().Be("journal_entry");

        _repositoryMock.Verify(
            x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ReturnsUnauthorizedError()
    {
        var request = BuildRequest();
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: string.Empty,
                Name: null,
                Email: null,
                IsAuthenticated: true));

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedJournalAccess);
    }

    [Fact]
    public async Task Handle_WhenEntryNotFound_ReturnsNotFoundError()
    {
        var entryId = 999;
        var request = BuildRequest(entryId);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: "user123",
                Name: "Test User",
                Email: "test@example.com",
                IsAuthenticated: true));

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JournalEntry?)null);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.JournalEntryNotFound);
        result.Params.Should().ContainKey("entryId");
        result.Params["entryId"].Should().Be(entryId.ToString());

        _repositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenValidRequest_UpdatesEntryWithTrimmedFieldsAndAuditTrail()
    {
        // Arrange
        var entryId = 1;
        var userId = "user123";
        var request = BuildRequest(entryId);
        var existing = BuildExistingEntry(entryId);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: userId,
                Name: "Test User",
                Email: "test@example.com",
                IsAuthenticated: true));

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var before = DateTime.UtcNow;

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert — response shape
        var after = DateTime.UtcNow;
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Id.Should().Be(entryId);
        result.ModifiedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);

        // Assert — entity mutations (the entity instance is the same one passed into UpdateAsync)
        existing.Title.Should().Be("New Title");
        existing.Content.Should().Be("Updated body");
        existing.EntryDate.Should().Be(new DateTime(2026, 6, 4));
        existing.ModifiedByUserId.Should().Be(userId);
        existing.ModifiedByUsername.Should().Be("Test User");
        existing.ModifiedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);

        // Creation audit untouched
        existing.CreatedByUserId.Should().Be("creator");
        existing.CreatedByUsername.Should().Be("Creator");

        // Collections were replaced
        existing.ProductAssociations.Select(p => p.ProductCodePrefix)
            .Should().BeEquivalentTo(new[] { "AB-1" });
        existing.TagAssignments.Select(t => t.TagId)
            .Should().BeEquivalentTo(new[] { 7 });

        _repositoryMock.Verify(
            x => x.UpdateAsync(existing, It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCurrentUserNameIsNull_FallsBackToUnknownUser()
    {
        var entryId = 1;
        var request = BuildRequest(entryId);
        var existing = BuildExistingEntry(entryId);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: "user123",
                Name: null,
                Email: null,
                IsAuthenticated: true));

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        existing.ModifiedByUsername.Should().Be("Unknown User");
    }
}
```

- [ ] **Step 3.2: Run the new tests — they should all PASS against the current (pre-refactor) handler.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateJournalEntryHandlerTests" 2>&1 | tail -10
```

Expected: 5 tests pass.

**Important:** These tests pass against today's handler because today's handler produces exactly the behaviour they assert. That is the point — they lock the current behaviour in place so the Task 4 refactor can be verified to preserve it. If a test fails here, the test is wrong (not the handler) and must be fixed before proceeding.

- [ ] **Step 3.3: Commit.**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/UpdateJournalEntryHandlerTests.cs
git commit -m "test: add UpdateJournalEntryHandler unit tests

Covers the four handler paths (unauthenticated, empty user id, entry
not found, valid request) plus the null-Name fallback to
\"Unknown User\". Locks current behaviour in place before the upcoming
domain-method refactor."
```

---

## Task 4: Refactor `UpdateJournalEntryHandler` to call `entry.Update(...)`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs:51-59`

- [ ] **Step 4.1: Replace the seven-line direct-mutation block with a single `entry.Update(...)` call.**

The current block (lines 51-59) reads:

```csharp
            var now = DateTime.UtcNow;

            // Update basic fields
            entry.Title = request.Title?.Trim();
            entry.Content = request.Content.Trim();
            entry.EntryDate = request.EntryDate.Date;
            entry.ModifiedAt = now;
            entry.ModifiedByUserId = currentUser.Id;
            entry.ModifiedByUsername = currentUser.Name ?? "Unknown User";
```

Replace it with:

```csharp
            entry.Update(
                request.Title,
                request.Content,
                request.EntryDate,
                currentUser.Id,
                currentUser.Name ?? "Unknown User");
```

Implementation notes:
- The local `var now = DateTime.UtcNow;` is no longer needed — `JournalEntry.Update` calls `DateTime.UtcNow` internally. Grep the rest of the handler method for `now` before removing; in the current source `now` is only referenced inside the block being replaced.
- The `?? "Unknown User"` fallback stays in the handler — this is application-layer policy about how identity is presented, not a domain rule (per arch-review Decision 4).
- The comment `// Update basic fields` is removed along with the block; the new method call is self-documenting.
- **Do not touch** the two lines that follow (lines 61-62 in the original):

  ```csharp
              entry.ReplaceProductAssociations(request.AssociatedProducts);
              entry.ReplaceTagAssignments(request.TagIds);
  ```

  These are separate aggregate operations and remain in the handler (per arch-review "One omission to be aware of" + Specification Amendment 1).
- **Do not touch** the surrounding handler logic: the `currentUser` resolution, the `IsAuthenticated`/empty-id guard, the `GetByIdAsync` call and null check, the `UpdateAsync`/`SaveChangesAsync` calls, the `_logger.LogInformation` call, the response construction.

- [ ] **Step 4.2: Build the solution to confirm the change compiles.**

```bash
dotnet build backend/Anela.Heblo.sln 2>&1 | tail -10
```

Expected: `Build succeeded.` with zero new warnings.

- [ ] **Step 4.3: Run the handler tests to confirm behaviour is preserved.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateJournalEntryHandlerTests" --no-build 2>&1 | tail -10
```

Expected: all 5 tests from Task 3 still pass — the refactor changed *where* the mutations live, not *what* they do.

If `Handle_WhenValidRequest_UpdatesEntryWithTrimmedFieldsAndAuditTrail` fails on the trim or date-strip assertion, the new `Update` method is missing the normalisation step — go back to Task 2 and fix it (the test is correct).

- [ ] **Step 4.4: Run the full Journal test suite to confirm no collateral regressions.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Journal" --no-build 2>&1 | tail -15
```

Expected: all Journal tests pass (`JournalEntryTests`, `CreateJournalEntryHandlerTests`, `DeleteJournalEntryHandlerTests`, `GetJournalEntryHandlerTests`, `SearchJournalEntriesHandlerTests`, `JournalEntryMapperTests`, `UpdateJournalEntryHandlerTests`, plus the `JournalRepositoryIntegrationTests` if its prerequisites are present).

- [ ] **Step 4.5: Run `dotnet format` on the touched files.**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs \
            backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs \
            backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs \
            backend/test/Anela.Heblo.Tests/Features/Journal/UpdateJournalEntryHandlerTests.cs
```

Expected: no changes, or only whitespace adjustments. If substantial changes are produced, review them before committing.

- [ ] **Step 4.6: Commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs
git commit -m "refactor: delegate JournalEntry update to domain method

Replaces seven lines of direct field mutation in
UpdateJournalEntryHandler with a single entry.Update(...) call.
Title/content trimming, entry-date stripping, and the modified-audit
trail now live with the entity that owns the data — same pattern as
SoftDelete. ReplaceProductAssociations and ReplaceTagAssignments are
intentionally left as separate handler calls.

Pre-existing follow-up (out of scope, flag for separate PR):
DeleteJournalEntryHandler passes a possibly-null currentUser.Name
into SoftDelete without the \"Unknown User\" fallback that the
Update path uses — two paths, two policies. Align in a follow-up."
```

---

## Task 5: Final verification

- [ ] **Step 5.1: Run the full backend test suite.**

```bash
dotnet test backend/Anela.Heblo.sln --no-build 2>&1 | tail -10
```

Expected: `Passed:` count equal to the pre-change count plus the 11 new tests (6 domain + 5 handler). `Failed: 0`.

- [ ] **Step 5.2: Confirm no contract change reached the OpenAPI surface.**

```bash
git diff --stat origin/main -- 'backend/src/Anela.Heblo.API/**' 'frontend/src/api/**'
```

Expected: zero changes under `Anela.Heblo.API` and `frontend/src/api`. The refactor must not regenerate the TypeScript client.

If files appear in the diff, something leaked into the API surface — investigate before merging.

- [ ] **Step 5.3: Review the final diff for surgical scope.**

```bash
git diff origin/main -- backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs \
                       backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs \
                       backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs \
                       backend/test/Anela.Heblo.Tests/Features/Journal/UpdateJournalEntryHandlerTests.cs
```

Expected scope (visually):
- `JournalEntry.cs` — a single new ~8-line `Update` method inserted above `SoftDelete`. No other lines moved or changed.
- `UpdateJournalEntryHandler.cs` — the block at lines 51-59 replaced by a 6-line `entry.Update(...)` call. Imports unchanged. No other lines moved.
- `JournalEntryTests.cs` — 6 new tests appended in a new `// ----- Update -----` region. No edits to existing tests.
- `UpdateJournalEntryHandlerTests.cs` — new file, ~180 lines.

If anything else changed (other handlers, validators, mappers, configuration, CSS), revert it — the spec is explicit about surgical scope.

- [ ] **Step 5.4: Confirm the PR-description follow-up is captured.**

The arch-review flagged a pre-existing nullability inconsistency in `DeleteJournalEntryHandler.cs:51` (passes `currentUser.Name` directly into `SoftDelete(string username)` without an `"Unknown User"` fallback). This refactor intentionally does **not** fix it. The follow-up note is already included in the Task 4 commit body — confirm it is there before opening the PR.

No further action required if the commit body matches Step 4.6.

---

## Self-Review Notes

**Spec coverage check:**
- FR-1 (Add `Update` method) → Task 2 implements it; Task 1 covers all stated behaviours (happy path, null title, trim, date-strip).
- FR-2 (Replace direct assignments in handler) → Task 4 does exactly this; the `"Unknown User"` fallback stays in the handler.
- FR-3 (Behaviour-preserving) → Task 3 locks current behaviour into tests before the refactor, Task 4 verifies they still pass after.
- NFR-1/2 (Performance, Security) → no change introduced; nothing to verify beyond passing tests.
- NFR-3 (Maintainability) → achieved by the move itself.
- NFR-4 (≥80% coverage on touched files) → Task 1 adds 6 domain tests, Task 3 adds 5 handler tests. The handler had effectively 0% before; it now has all four handler paths plus the null-Name fallback covered.

**Specification Amendments from arch-review:**
- Amendment 1 (collections stay in handler) → explicitly stated in Task 4 Step 4.1 and tested in Task 1 (`Update_DoesNotTouchProductAndTagCollections`).
- Amendment 2 (no existing handler tests → add them) → Task 3 is the implementation.
- Amendment 3 (Delete nullability follow-up) → flagged in Task 4 commit body and Step 5.4.

**Placeholder scan:** none — every step has either exact code or exact command + expected output.

**Type/name consistency:** `Update(string? title, string content, DateTime entryDate, string userId, string username)` used consistently across Task 1 tests, Task 2 implementation, and Task 4 call site. `"Unknown User"` literal consistent between handler call site (Task 4) and handler test assertion (Task 3 `Handle_WhenCurrentUserNameIsNull_FallsBackToUnknownUser`).
