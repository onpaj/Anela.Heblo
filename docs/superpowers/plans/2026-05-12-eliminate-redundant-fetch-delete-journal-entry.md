# Eliminate Redundant Database Fetch in DeleteJournalEntryHandler — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the journal-entry soft-delete from two database fetches into one by moving persistence orchestration into the handler, removing the redundant `IJournalRepository.DeleteSoftAsync` method, and aligning the delete flow with the sibling `UpdateJournalEntryHandler`.

**Architecture:** The handler currently fetches the entity, then calls `DeleteSoftAsync(int id, …)` which silently re-fetches the same entity inside the repository. We replace that with the existing `UpdateJournalEntryHandler` pattern: handler calls `GetByIdAsync` once, mutates the aggregate via `JournalEntry.SoftDelete`, then calls inherited `UpdateAsync` + `SaveChangesAsync`. The `DeleteSoftAsync` method is deleted from `IJournalRepository` and `JournalRepository`. No new interfaces, no new methods — the base `IRepository<JournalEntry, int>` already exposes everything needed.

**Tech Stack:** .NET 8, EF Core, MediatR, xUnit, Moq, FluentAssertions.

---

## Scope & Out-of-Scope

**In scope:**
- `DeleteJournalEntryHandler` — replace `DeleteSoftAsync` call with handler-direct persistence.
- `IJournalRepository` — remove `DeleteSoftAsync` declaration.
- `JournalRepository` — remove `DeleteSoftAsync` implementation.
- `DeleteJournalEntryHandlerTests` — update mock setups and verifications.

**Out of scope (DO NOT TOUCH):**
- `IMarketingActionRepository.DeleteSoftAsync` and `MarketingActionRepository.DeleteSoftAsync` — parallel anti-pattern, separate ticket.
- `DeleteMarketingActionHandler` and `MarketingActionHandlerSyncTests` — Marketing module is out of scope.
- `JournalRepository.GetByIdAsync` includes (`ProductAssociations`, `TagAssignments.Tag`) — preserved as-is per spec/arch-review decisions.
- External contracts (`DeleteJournalEntryRequest`, `DeleteJournalEntryResponse`), error codes, log message format, HTTP endpoint signature — unchanged.

## Files Touched

| File | Change |
|------|--------|
| `backend/test/Anela.Heblo.Tests/Features/Journal/DeleteJournalEntryHandlerTests.cs` | Update mock surface from `DeleteSoftAsync` → `UpdateAsync` + `SaveChangesAsync`; add soft-delete state assertions on the entity passed to `UpdateAsync`. |
| `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/DeleteJournalEntry/DeleteJournalEntryHandler.cs` | Replace `_journalRepository.DeleteSoftAsync(request.Id, …)` (single line) with three lines: `entry.SoftDelete(…)`, `await _journalRepository.UpdateAsync(entry, ct)`, `await _journalRepository.SaveChangesAsync(ct)`. Remove the obsolete ownership-comment block. |
| `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` | Delete the `DeleteSoftAsync` method declaration (line 7). |
| `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` | Delete the `DeleteSoftAsync` method implementation (lines 27–36). |

---

## Task 1: Update tests to express the new persistence contract (RED)

**Why this comes first:** Following the global TDD rule. The new tests assert behavior the current handler does not exhibit, so they will fail. That failing state is the signal Task 2 is needed.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/DeleteJournalEntryHandlerTests.cs`

### Step 1.1: Replace the `Handle_WhenUserNotAuthenticated_ShouldReturnUnauthorizedError` verifications

- [ ] **Edit the test file.** Replace the last verification line of `Handle_WhenUserNotAuthenticated_ShouldReturnUnauthorizedError` (currently line 60) with `UpdateAsync` and `SaveChangesAsync` verifications:

Locate this block:

```csharp
        // Verify no repository calls were made
        _repositoryMock.Verify(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.DeleteSoftAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Replace with:

```csharp
        // Verify no repository calls were made
        _repositoryMock.Verify(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

### Step 1.2: Replace the `Handle_WhenEntryDoesNotExist_ShouldReturnNotFoundError` verifications

- [ ] **Edit the test file.** Replace the trailing verifications of `Handle_WhenEntryDoesNotExist_ShouldReturnNotFoundError` (currently lines 122–123):

Locate this block:

```csharp
        // Verify repository calls
        _repositoryMock.Verify(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.DeleteSoftAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Replace with:

```csharp
        // Verify repository calls
        _repositoryMock.Verify(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

### Step 1.3: Rewrite the success-path mock setup and verifications

- [ ] **Edit the test file.** In `Handle_WhenValidRequest_ShouldDeleteEntrySuccessfully`, replace the `DeleteSoftAsync` setup and verifications with the new persistence path. The setup must also configure `UpdateAsync` and `SaveChangesAsync`. Locate the block starting at the `_repositoryMock.Setup(x => x.DeleteSoftAsync(...))` line through the closing `}` of the test method:

Locate this block:

```csharp
        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        _repositoryMock
            .Setup(x => x.DeleteSoftAsync(entryId, userId, "Test User", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Id.Should().Be(entryId);
        result.Message.Should().Be("Journal entry deleted successfully");

        // Verify repository calls
        _repositoryMock.Verify(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.DeleteSoftAsync(entryId, userId, "Test User", It.IsAny<CancellationToken>()), Times.Once);
    }
```

Replace with:

```csharp
        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Id.Should().Be(entryId);
        result.Message.Should().Be("Journal entry deleted successfully");

        // Verify the entity passed to UpdateAsync reflects the soft-delete state
        _repositoryMock.Verify(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(
            x => x.UpdateAsync(
                It.Is<JournalEntry>(e =>
                    e.Id == entryId &&
                    e.IsDeleted &&
                    e.DeletedByUserId == userId &&
                    e.DeletedByUsername == "Test User"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

### Step 1.4: Rewrite the logging-test mock setup

- [ ] **Edit the test file.** In `Handle_WhenDeletionSuccessful_ShouldLogInformation`, replace the `DeleteSoftAsync` setup with `UpdateAsync` + `SaveChangesAsync` setups. The logging assertion itself does not change.

Locate this block:

```csharp
        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        _repositoryMock
            .Setup(x => x.DeleteSoftAsync(entryId, userId, "Test User", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
```

Replace with:

```csharp
        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
```

### Step 1.5: Run the test class — expect failures

- [ ] **Run the tests.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DeleteJournalEntryHandlerTests" \
  --nologo --verbosity normal
```

Expected: At least the two success-path tests (`Handle_WhenValidRequest_ShouldDeleteEntrySuccessfully` and `Handle_WhenDeletionSuccessful_ShouldLogInformation`) FAIL with a Moq verification error similar to:

```
Moq.MockException: Expected invocation on the mock once, but was 0 times: x => x.UpdateAsync(It.Is<JournalEntry>(e => ...), It.IsAny<CancellationToken>())
```

The two negative tests (`Handle_WhenUserNotAuthenticated_…`, `Handle_WhenEntryDoesNotExist_…`) MAY still pass because their `Times.Never` assertions are not violated by the current code path — that is acceptable, the RED signal we need is the success-path failure.

### Step 1.6: Commit the failing tests

- [ ] **Commit.**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/DeleteJournalEntryHandlerTests.cs
git commit -m "test: assert single-fetch persistence contract for delete journal entry handler"
```

---

## Task 2: Refactor the handler to call `UpdateAsync` + `SaveChangesAsync` directly (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/DeleteJournalEntry/DeleteJournalEntryHandler.cs`

### Step 2.1: Replace the `DeleteSoftAsync` call with the handler-direct sequence

- [ ] **Edit the handler.** Locate this block in `DeleteJournalEntryHandler.cs` (currently lines 48–51):

```csharp
            // Check if user owns the entry (for now, allow all authenticated users to delete)
            // In production, you might want to restrict this to the original author

            await _journalRepository.DeleteSoftAsync(request.Id, currentUser.Id, currentUser.Name, cancellationToken);
```

Replace with:

```csharp
            // Check if user owns the entry (for now, allow all authenticated users to delete)
            // In production, you might want to restrict this to the original author

            entry.SoftDelete(currentUser.Id, currentUser.Name);
            await _journalRepository.UpdateAsync(entry, cancellationToken);
            await _journalRepository.SaveChangesAsync(cancellationToken);
```

Rationale notes (do not include as code comments — these are for the implementer's understanding):
- `entry` is the instance returned by `GetByIdAsync` on line 39; it is tracked by EF and mutating it is sufficient to persist on `SaveChangesAsync`.
- `UpdateAsync` is retained for symmetry with `UpdateJournalEntryHandler` (see `UpdateJournalEntryHandler.cs:82-83`), even though EF would persist the change without it.
- `JournalEntry.SoftDelete` (`backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs:82-91`) sets `IsDeleted`, `DeletedAt`, `DeletedByUserId`, `DeletedByUsername`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`.

### Step 2.2: Build the solution

- [ ] **Run the build.**

```bash
dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal
```

Expected: `Build succeeded.` with 0 errors. (`DeleteSoftAsync` still exists on the interface at this point, so no compile errors elsewhere.)

### Step 2.3: Run the test class — expect green

- [ ] **Run the tests.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DeleteJournalEntryHandlerTests" \
  --nologo --verbosity normal
```

Expected: All 5 tests in `DeleteJournalEntryHandlerTests` PASS.

### Step 2.4: Commit the handler change

- [ ] **Commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/UseCases/DeleteJournalEntry/DeleteJournalEntryHandler.cs
git commit -m "refactor: inline soft-delete persistence in delete journal entry handler"
```

---

## Task 3: Remove the now-dead `DeleteSoftAsync` from the repository surface

**Why separate from Task 2:** Splitting these commits keeps the diff legible. The handler change is a behavioral change (single fetch); the repository surface trimming is a pure removal. Reviewing them independently is easier.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`

### Step 3.1: Remove the interface declaration

- [ ] **Edit `IJournalRepository.cs`.** Locate this block (currently lines 6–8):

```csharp
    public interface IJournalRepository : IRepository<JournalEntry, int>
    {
        Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default);

        Task<PagedResult<JournalEntry>> GetEntriesAsync(
```

Replace with:

```csharp
    public interface IJournalRepository : IRepository<JournalEntry, int>
    {
        Task<PagedResult<JournalEntry>> GetEntriesAsync(
```

(Net effect: delete the `DeleteSoftAsync` line plus the blank line that follows it.)

### Step 3.2: Remove the implementation

- [ ] **Edit `JournalRepository.cs`.** Locate this block (currently lines 26–37):

```csharp
        }

        public async Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default)
        {
            var entry = await GetByIdAsync(id, cancellationToken);
            if (entry != null)
            {
                entry.SoftDelete(userId, username);
                await UpdateAsync(entry, cancellationToken);
                await SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<PagedResult<JournalEntry>> GetEntriesAsync(
```

Replace with:

```csharp
        }

        public async Task<PagedResult<JournalEntry>> GetEntriesAsync(
```

(Net effect: delete the entire `DeleteSoftAsync` method including its leading blank line.)

### Step 3.3: Verify no remaining references inside Journal scope

- [ ] **Search for residual references.**

```bash
grep -rn "DeleteSoftAsync" \
  backend/src/Anela.Heblo.Domain/Features/Journal \
  backend/src/Anela.Heblo.Persistence/Catalog/Journal \
  backend/src/Anela.Heblo.Application/Features/Journal \
  backend/test/Anela.Heblo.Tests/Features/Journal
```

Expected: **No output.** Exit code 1 (grep "no matches").

If any match appears in those four paths, stop and fix before proceeding. (Marketing matches in `backend/src/Anela.Heblo.Domain/Features/Marketing/…`, `backend/src/Anela.Heblo.Persistence/Marketing/…`, `backend/src/Anela.Heblo.Application/Features/Marketing/…`, and `backend/test/Anela.Heblo.Tests/Features/Marketing/…` are expected and out of scope — do not touch them.)

### Step 3.4: Build the solution

- [ ] **Run the build.**

```bash
dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal
```

Expected: `Build succeeded.` with 0 errors. The compiler will surface any unexpected caller of `IJournalRepository.DeleteSoftAsync` outside the inspected paths — if it does, stop and report.

### Step 3.5: Run the full Journal test suite

- [ ] **Run all Journal feature tests.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Journal" \
  --nologo --verbosity normal
```

Expected: All Journal tests PASS (including `Create`, `Get`, `Search`, `Update`, `Delete` handler tests).

### Step 3.6: Run `dotnet format`

- [ ] **Format the touched files.**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include \
    backend/src/Anela.Heblo.Application/Features/Journal/UseCases/DeleteJournalEntry/DeleteJournalEntryHandler.cs \
    backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs \
    backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs \
    backend/test/Anela.Heblo.Tests/Features/Journal/DeleteJournalEntryHandlerTests.cs \
  --verbosity minimal
```

Expected: No errors. If `dotnet format` made changes, include them in the next commit.

### Step 3.7: Commit the repository surface trim

- [ ] **Commit.**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs \
        backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "refactor: remove redundant DeleteSoftAsync from IJournalRepository"
```

---

## Task 4: Final validation

### Step 4.1: Run the full backend test suite

- [ ] **Run all backend tests** (this also catches any unintended impact on Marketing or other modules).

```bash
dotnet test backend/Anela.Heblo.sln --nologo --verbosity minimal
```

Expected: All tests PASS. Marketing tests must remain green — they are not part of this change.

### Step 4.2: Final build + format gate

- [ ] **Build and format the whole solution.**

```bash
dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal && \
dotnet format backend/Anela.Heblo.sln --verify-no-changes --verbosity minimal
```

Expected: `Build succeeded.` and `dotnet format` exits with code 0 (no formatting drift in touched files).

### Step 4.3: Sanity sweep — verify single fetch contract

- [ ] **Eyeball the final handler.** Open `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/DeleteJournalEntry/DeleteJournalEntryHandler.cs` and confirm:
  - Exactly one `_journalRepository.GetByIdAsync(...)` call.
  - Followed by `entry.SoftDelete(...)`, `_journalRepository.UpdateAsync(entry, ...)`, `_journalRepository.SaveChangesAsync(...)` in that order.
  - The `_logger.LogInformation` call still reads `"Journal entry {EntryId} deleted by user {UserId}"` with `request.Id` and `currentUser.Id` as arguments.
  - The `DeleteJournalEntryResponse { Id = request.Id }` return statement is preserved.

- [ ] **Eyeball the final repository.** Open `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` and confirm:
  - The `GetByIdAsync` override (with `Include(ProductAssociations)` and `Include(TagAssignments).ThenInclude(Tag)`) is preserved unchanged.
  - No `DeleteSoftAsync` method anywhere in the file.
  - All other methods (`GetEntriesAsync`, `SearchEntriesAsync`, `GetEntriesByProductAsync`, `GetJournalIndicatorsAsync`) are unchanged.

- [ ] **Eyeball the final interface.** Open `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` and confirm:
  - The interface inherits from `IRepository<JournalEntry, int>`.
  - No `DeleteSoftAsync` declaration.
  - The four other methods (`GetEntriesAsync`, `SearchEntriesAsync`, `GetEntriesByProductAsync`, `GetJournalIndicatorsAsync`) are present.

### Step 4.4: No follow-up commit needed unless format drift introduced changes

- [ ] If `dotnet format --verify-no-changes` exited 0, no extra commit is required. If it reported drift (it should not at this point), re-run `dotnet format` without `--verify-no-changes`, then:

```bash
git add -u
git commit -m "chore: apply dotnet format"
```

---

## Acceptance Criteria Cross-Check

| Spec ID | Acceptance | Plan task |
|---------|------------|-----------|
| FR-1 | Repository's `GetByIdAsync` invoked exactly once per successful delete. | Task 1 Step 1.3 (Moq `Times.Once` verification on `GetByIdAsync`); Task 2 makes it pass. |
| FR-2 | Externally observable handler behavior unchanged (error codes, log message, response shape). | Task 1 leaves negative-path assertions intact; logging test (`Handle_WhenDeletionSuccessful_ShouldLogInformation`) preserved; success response asserts `Id` and `Message`. |
| FR-3 | `DeleteSoftAsync` removed from `IJournalRepository` and `JournalRepository`. | Task 3 Steps 3.1–3.2; verified by Step 3.3 grep. |
| FR-4 | Tests updated to verify new persistence path; positive test asserts soft-delete state on entity passed to `UpdateAsync`; negative tests verify `Times.Never`. | Task 1 Steps 1.1–1.4. |
| FR-5 | No production code outside Journal feature references `IJournalRepository.DeleteSoftAsync`; solution builds. | Task 3 Step 3.4 (build), Step 3.3 (grep), Task 4 Step 4.1 (full test run). |
| NFR-1 | One entity-loading SELECT per request; no new N+1; includes preserved. | Task 2 Step 2.1 keeps `GetByIdAsync` includes untouched; only the `DeleteSoftAsync` second fetch is removed. |
| NFR-2 | Unauthenticated rejected before DB; soft-delete audit trail preserved. | Task 1 Step 1.1 verifies `Times.Never` on DB calls when unauthenticated; Task 1 Step 1.3 asserts `DeletedByUserId`/`DeletedByUsername` on the entity. |
| NFR-3 | Handler's local `entry` is the mutated/persisted instance; dead null branch removed. | Task 2 Step 2.1 — `entry` is mutated via `SoftDelete` and passed to `UpdateAsync`; Task 3 Step 3.2 removes the null branch. |
| NFR-4 | Test doubles no longer need to mock `DeleteSoftAsync`. | Task 1 Steps 1.1–1.4 replace mocks. |

## Risks

| Risk | Mitigation in plan |
|------|--------------------|
| Hidden caller of `DeleteSoftAsync` outside the surveyed paths. | Task 3 Step 3.4 build forces compile-time discovery; Step 3.3 grep is the secondary check. |
| Hand-rolled fakes implementing `IJournalRepository` in other test files. | The compiler will flag any class implementing `IJournalRepository` that previously overrode `DeleteSoftAsync` — Task 3 Step 3.4 build catches this. Investigation confirmed only `Mock<IJournalRepository>` is used in test files (`SearchJournalEntriesHandlerTests`, `GetJournalEntryHandlerTests`, `CreateJournalEntryHandlerTests`, `DeleteJournalEntryHandlerTests` — none have non-Moq fakes). |
| EF change-tracker side-effects from `UpdateAsync` marking all properties Modified. | No regression: matches existing `UpdateJournalEntryHandler` and the prior `DeleteSoftAsync` behavior. |
| `dotnet format` drift unrelated to this change. | `--include` flag in Task 3 Step 3.6 scopes formatting to the four touched files only. |

## Notes for the Implementer

- **Do not touch Marketing.** The Marketing module has the same anti-pattern. The spec and architecture review explicitly exclude it. Resist the urge to fix both in one PR — Marketing's handler uses `currentUser.Name ?? "Unknown User"` and has its own test class with different mock setups. A separate ticket should be opened for Marketing after this change merges.
- **Do not slim the `GetByIdAsync` includes.** Whether the eager-loaded `ProductAssociations` and `TagAssignments.Tag` are needed for the delete path is a known follow-up question. Scope discipline: this change removes a fetch, not a join.
- **The placeholder ownership comment stays.** The handler currently has a `// Check if user owns the entry (for now, allow all authenticated users to delete)` comment. The spec marks ownership enforcement as out of scope; leave the comment as-is per the "surgical changes" project rule. (Task 2 Step 2.1 inserts the new code below it, preserving the comment.)
- **Conventional commits.** Three commits: `test: …`, `refactor: …` (handler), `refactor: …` (interface/impl). This matches the project's git history style.
