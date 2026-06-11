# Remove embedded SaveChangesAsync from IMarketingActionRepository.DeleteSoftAsync — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove `DeleteSoftAsync` from `IMarketingActionRepository` and inline the soft-delete sequence in `DeleteMarketingActionHandler`, eliminating a hidden mid-handler commit and a redundant second DB load.

**Architecture:** Caller-controlled persistence. The handler — which already loaded the entity for the Outlook lookup — now also owns the mutation: `entity.SoftDelete(...) → repository.UpdateAsync(entity) → repository.SaveChangesAsync()`. This matches the precedent set by `DeleteJournalEntryHandler` (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/DeleteJournalEntry/DeleteJournalEntryHandler.cs:51-53`) and the existing `UpdateMarketingActionHandler` pattern. No new abstractions; the surface area shrinks.

**Tech Stack:** .NET 8, C# 12, MediatR, EF Core, xUnit + Moq + FluentAssertions.

---

## Scope and constraints

- **Single feature module:** Marketing only. Journal already implements the target pattern — do not touch it.
- **No domain change:** `MarketingAction.SoftDelete(string userId, string username)` (file `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs:129`) stays 2-arg. Per arch-review §Spec Amendments, the spec example showing a 3rd `now` argument is a spec typo — do NOT add a third parameter.
- **No new abstractions:** no Unit-of-Work, no service wrapper, no thinned `DeleteSoftAsync(entity)` overload. Inline the three lines.
- **No public contract change:** MediatR request/response for `DeleteMarketingAction` is byte-for-byte unchanged.
- **No schema or migration change.**

## Files touched

| Path | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` | Remove line 11 (`DeleteSoftAsync` declaration). |
| `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` | Remove lines 27–36 (`DeleteSoftAsync` implementation). |
| `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs` | Replace the try-block at lines 75–86 with the inlined sequence. |
| `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs` | Switch all `DeleteSoftAsync` setups/verifications to `UpdateAsync` + `SaveChangesAsync`; add a regression test asserting `GetByIdAsync` is called exactly once. |
| `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs` | Switch the 3 `DeleteSoftAsync` references (lines 286, 305, 330) to `UpdateAsync` + `SaveChangesAsync`. |

## Reference signatures (read these before editing)

- `IRepository<TEntity, TKey>.UpdateAsync` → `Task UpdateAsync(TEntity entity, CancellationToken ct = default)` (`backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs:16`).
- `IRepository<TEntity, TKey>.SaveChangesAsync` → `Task<int> SaveChangesAsync(CancellationToken ct = default)` (`backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs:22`). **Note:** returns `Task<int>`, not `Task`. Mock setups use `.ReturnsAsync(1)` (or any int) — `.Returns(Task.CompletedTask)` will NOT compile.
- `MarketingAction.SoftDelete(string userId, string username)` (`backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs:129`) — 2 args; reads `DateTime.UtcNow` internally.
- `BaseRepository.UpdateAsync` is a no-op state marker (`DbSet.Update(entity); return Task.CompletedTask;`) at `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs:70-74`. Keep the call anyway — matches `UpdateMarketingActionHandler` and `DeleteJournalEntryHandler`.

---

## Task 1: Update DeleteMarketingActionHandlerTests to expect the new persistence sequence

**Goal:** Switch every mock setup and verification on `DeleteSoftAsync` to `UpdateAsync` + `SaveChangesAsync`. Add a regression test asserting the entity is loaded exactly once per delete (FR-4). After this task the tests will FAIL because the handler still calls `DeleteSoftAsync` — that's the intended RED state and proves the assertions are meaningful.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs`

- [ ] **Step 1.1: Replace the default `DeleteSoftAsync` setup in the constructor (lines 49–51)**

Find this block at the top of the class constructor:

```csharp
        _repository.Setup(x => x.DeleteSoftAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
```

Replace with:

```csharp
        _repository.Setup(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
```

- [ ] **Step 1.2: Rewrite `Handle_CallsOutlookDeleteBeforeSoftDelete_WhenPushEnabled` (lines 65–84) to assert ordering against `SaveChangesAsync`**

The "db" callback was anchored on `DeleteSoftAsync` (the commit). After the refactor the commit step is `SaveChangesAsync`. Replace the entire test body with:

```csharp
    [Fact]
    public async Task Handle_CallsOutlookDeleteBeforeSoftDelete_WhenPushEnabled()
    {
        var callOrder = new List<string>();

        _outlookSync
            .Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => callOrder.Add("outlook"))
            .Returns(Task.CompletedTask);

        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => callOrder.Add("db"))
            .ReturnsAsync(1);

        await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        callOrder.Should().ContainInOrder("outlook", "db");
    }
```

- [ ] **Step 1.3: Update `Handle_TreatsOutlook404AsSuccess_AndProceedsWithSoftDelete` (lines 86–98)**

Replace the verification block at lines 96–97 (the `_repository.Verify(x => x.DeleteSoftAsync(...))` call) with:

```csharp
        _repository.Verify(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
```

- [ ] **Step 1.4: Update `Handle_ReturnsForbiddenError_WhenOutlookThrows403` (lines 100–114)**

Replace lines 111–113 (the `_repository.Verify(x => x.DeleteSoftAsync(...), Times.Never)` block) with:

```csharp
        _repository.Verify(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
```

- [ ] **Step 1.5: Update `Handle_ReturnsSyncError_WhenOutlookThrowsNon403Non404` (lines 116–130)**

Replace lines 127–129 (the `_repository.Verify(x => x.DeleteSoftAsync(...), Times.Never)` block) with:

```csharp
        _repository.Verify(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
```

- [ ] **Step 1.6: Update `Handle_SkipsOutlook_WhenActionHasNoEventId` (lines 132–147)**

Replace lines 145–146 (the `_repository.Verify(x => x.DeleteSoftAsync(...), Times.Once)` block) with:

```csharp
        _repository.Verify(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
```

- [ ] **Step 1.7: Update `Handle_SkipsOutlook_WhenPushDisabled` (lines 149–160)**

Replace lines 158–159 (the `_repository.Verify(x => x.DeleteSoftAsync(...), Times.Once)` block) with:

```csharp
        _repository.Verify(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
```

- [ ] **Step 1.8: Update `Handle_ReturnsDatabaseError_WhenDbDeleteFails` (lines 175–198)**

The throw was anchored on `DeleteSoftAsync`. Re-anchor on `SaveChangesAsync` (the commit step that fails). Replace lines 178–181 (the `_repository.Setup(x => x.DeleteSoftAsync(...)).ThrowsAsync(...)` block) with:

```csharp
        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB unavailable"));
```

Leave the response-shape assertions (lines 184–186), the Outlook verify (lines 187–189), and the log-message verify (lines 190–197) unchanged — the "already deleted" log message is preserved by the handler's catch block.

- [ ] **Step 1.9: Add a new regression test asserting `GetByIdAsync` is called exactly once per delete (FR-4)**

Append this test immediately after `Handle_HonorsRuntimePushEnabledFlip_FalseToTrue` (after the closing brace of that test, before the closing brace of the class):

```csharp
    [Fact]
    public async Task Handle_LoadsEntityExactlyOnce_PerDeleteRequest()
    {
        await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        _repository.Verify(
            x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 1.10: Build the test project to verify the file compiles**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: `Build succeeded.` with 0 errors. (The interface still has `DeleteSoftAsync` at this point — Moq does not require all interface methods to be set up, so removing setups is safe.)

- [ ] **Step 1.11: Run only this test file to confirm RED state**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~DeleteMarketingActionHandlerTests"
```

Expected: tests FAIL with messages like `Expected invocation on the mock once, but was 0 times` for `UpdateAsync` / `SaveChangesAsync`. This proves the new assertions actually check the new behaviour. Do not proceed to Task 2 until you see failures of this shape — passing tests at this stage indicate the assertions are not wired correctly.

- [ ] **Step 1.12: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs
git commit -m "test(marketing): retarget DeleteMarketingActionHandler tests to UpdateAsync + SaveChangesAsync"
```

---

## Task 2: Update MarketingActionHandlerSyncTests to expect the new persistence sequence

**Goal:** Fix the 3 `DeleteSoftAsync` references in the sync-tests file so they don't break compilation once we remove `DeleteSoftAsync` from the interface (arch-review §Spec Amendments item 4 — the spec missed this file). This task is a parallel RED step to Task 1.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs`

- [ ] **Step 2.1: Update the setup at line 285–287 in `DeleteHandler_CallsDeleteEvent_WhenOutlookEventIdExists`**

Find this block:

```csharp
            _repositoryMock
                .Setup(x => x.DeleteSoftAsync(42, AuthenticatedUser.Id!, AuthenticatedUser.Name!, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
```

Replace with:

```csharp
            _repositoryMock
                .Setup(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _repositoryMock
                .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
```

- [ ] **Step 2.2: Update the verification at line 304–306 in the same test**

Find this block:

```csharp
            _repositoryMock.Verify(
                x => x.DeleteSoftAsync(42, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
```

Replace with:

```csharp
            _repositoryMock.Verify(
                x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _repositoryMock.Verify(
                x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
                Times.Once);
```

- [ ] **Step 2.3: Update the verification at line 329–331 in `DeleteHandler_ReturnsError_WhenOutlookThrowsNonNotFound`**

Find this block:

```csharp
            _repositoryMock.Verify(
                x => x.DeleteSoftAsync(42, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
```

Replace with:

```csharp
            _repositoryMock.Verify(
                x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _repositoryMock.Verify(
                x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
                Times.Never);
```

- [ ] **Step 2.4: Build the test project**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 2.5: Run the sync tests to confirm RED state**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~MarketingActionHandlerSyncTests.DeleteHandler"
```

Expected: `DeleteHandler_CallsDeleteEvent_WhenOutlookEventIdExists` FAILS with `Expected invocation on the mock once, but was 0 times` for `UpdateAsync` / `SaveChangesAsync`. `DeleteHandler_ReturnsError_WhenOutlookThrowsNonNotFound` should still PASS (it verifies `Times.Never`, which is satisfied regardless of which DB-write method exists).

- [ ] **Step 2.6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs
git commit -m "test(marketing): retarget DeleteHandler sync tests to UpdateAsync + SaveChangesAsync"
```

---

## Task 3: Inline the soft-delete sequence in DeleteMarketingActionHandler (GREEN)

**Goal:** Replace the `_repository.DeleteSoftAsync(...)` call with the three-line sequence (`SoftDelete` → `UpdateAsync` → `SaveChangesAsync`). The existing try/catch around the DB step wraps both new calls (arch-review Decision 3). All other handler logic — auth check, `GetByIdAsync`, not-found branch, Outlook block, success log, response shape — stays byte-for-byte unchanged.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs`

- [ ] **Step 3.1: Replace the try/catch block at lines 75–86**

Find this exact block:

```csharp
            try
            {
                await _repository.DeleteSoftAsync(
                    request.Id, currentUser.Id, currentUser.Name ?? "Unknown User", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "DB soft-delete failed after Outlook delete for MarketingAction {ActionId}; Outlook event {EventId} already deleted — DB row still present",
                    request.Id, action.OutlookEventId);
                return new DeleteMarketingActionResponse(ErrorCodes.DatabaseError);
            }
```

Replace with:

```csharp
            action.SoftDelete(currentUser.Id, currentUser.Name ?? "Unknown User");

            try
            {
                await _repository.UpdateAsync(action, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "DB soft-delete failed after Outlook delete for MarketingAction {ActionId}; Outlook event {EventId} already deleted — DB row still present",
                    request.Id, action.OutlookEventId);
                return new DeleteMarketingActionResponse(ErrorCodes.DatabaseError);
            }
```

Notes:
- `MarketingAction.SoftDelete` is a 2-arg method that captures `DateTime.UtcNow` internally. Do NOT pass a third `now` argument — the spec example showing `, now` is a typo per arch-review §Spec Amendments item 1.
- The mutation (`SoftDelete`) is intentionally OUTSIDE the try. A pure in-memory mutation cannot throw the kind of exception this `catch` is designed to handle, and keeping it outside makes the catch's log message ("DB soft-delete failed") truthful.
- `UpdateAsync` on an already-tracked entity is a no-op state marker (`DbSet.Update`). Keep the call for pattern parity with `UpdateMarketingActionHandler` and `DeleteJournalEntryHandler`.

- [ ] **Step 3.2: Build the solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with 0 errors. (`DeleteSoftAsync` still exists on the interface — no callers reference it now, but the interface declaration is harmless.)

- [ ] **Step 3.3: Run the full Marketing test suite to confirm GREEN**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Marketing"
```

Expected: all Marketing tests PASS, including the tests retargeted in Tasks 1 and 2 and the new `Handle_LoadsEntityExactlyOnce_PerDeleteRequest` regression test.

- [ ] **Step 3.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs
git commit -m "refactor(marketing): inline soft-delete sequence in DeleteMarketingActionHandler"
```

---

## Task 4: Remove DeleteSoftAsync from the repository surface

**Goal:** Delete the now-orphaned `DeleteSoftAsync` from the interface and the implementation. After this task, `grep -rn DeleteSoftAsync backend/` over the Marketing scope must return zero hits in production and test code.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs`

- [ ] **Step 4.1: Remove the interface declaration**

In `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs`, delete line 11 and the blank line that follows it. Before:

```csharp
    public interface IMarketingActionRepository : IRepository<MarketingAction, int>
    {
        Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default);

        Task<PagedResult<MarketingAction>> GetPagedAsync(
```

After:

```csharp
    public interface IMarketingActionRepository : IRepository<MarketingAction, int>
    {
        Task<PagedResult<MarketingAction>> GetPagedAsync(
```

- [ ] **Step 4.2: Remove the implementation**

In `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs`, delete lines 27–36 (the entire `DeleteSoftAsync` method body) and the blank line that follows. Before (lines 26–37):

```csharp

        public async Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default)
        {
            var entity = await GetByIdAsync(id, cancellationToken);
            if (entity != null)
            {
                entity.SoftDelete(userId, username);
                await UpdateAsync(entity, cancellationToken);
                await SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<PagedResult<MarketingAction>> GetPagedAsync(
```

After:

```csharp

        public async Task<PagedResult<MarketingAction>> GetPagedAsync(
```

- [ ] **Step 4.3: Grep-verify no remaining references in the backend source/test tree**

Run:
```bash
grep -rn "DeleteSoftAsync" backend/src backend/test
```

Expected: zero output. If anything matches, the refactor has missed a caller — stop and report rather than guess; the spec FR-1 and §Backwards compatibility both require zero remaining references.

(Documentation hits under `docs/superpowers/plans/` are pre-existing historical plans and are acceptable — the grep is scoped to `backend/` to ignore them.)

- [ ] **Step 4.4: Build the solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with 0 errors. The compiler is the final guarantee that no caller was missed; any external implementer of `IMarketingActionRepository` (e.g. a hand-rolled fake) would surface here.

- [ ] **Step 4.5: Run the full Marketing test suite again**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Marketing"
```

Expected: all Marketing tests PASS.

- [ ] **Step 4.6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs \
        backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs
git commit -m "refactor(marketing): remove orphaned DeleteSoftAsync from IMarketingActionRepository"
```

---

## Task 5: Final validation

**Goal:** Run the full validation gate listed in `CLAUDE.md` to confirm the refactor is shippable.

- [ ] **Step 5.1: Format**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code 0, no diagnostics. If diagnostics appear, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) to apply fixes, then commit them with message `chore: dotnet format`.

- [ ] **Step 5.2: Full build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with 0 errors and 0 warnings (or no NEW warnings relative to the pre-task baseline).

- [ ] **Step 5.3: Full backend test suite**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
```

Expected: all tests PASS. Pay particular attention to any handler or repository test under the `Marketing` namespace — if any other test referenced `DeleteSoftAsync` and Task 4's grep missed it, the build would already have failed; this run is the behavioural cross-check.

- [ ] **Step 5.4: (Optional, only if Step 5.1 produced format fixes) Commit the format-only changes**

```bash
git add -A
git commit -m "chore: dotnet format"
```

If Step 5.1 reported no changes, skip this step.

---

## Spec coverage map

| Spec requirement | Task |
|------------------|------|
| FR-1 — Remove `DeleteSoftAsync` from `IMarketingActionRepository` and `MarketingActionRepository`; no orphaned callers | Task 4 (Steps 4.1, 4.2); verified by Step 4.3 grep + Step 4.4 build |
| FR-2 — Handler loads entity once; performs `SoftDelete → UpdateAsync → SaveChangesAsync`; preserves auth, not-found, error handling, response shape, logging | Task 3 (Step 3.1); regression-guarded by Task 1 Step 1.9 (entity loaded exactly once) and Task 1 Step 1.2 (Outlook→SaveChangesAsync ordering) |
| FR-3 — `MarketingAction.SoftDelete(userId, username)` signature and behaviour unchanged | No code change to `MarketingAction.cs`; explicit non-goal note in Task 3 Step 3.1 |
| FR-4 — Tests updated; entity-loaded-once test added; repository-level `DeleteSoftAsync` tests removed (none exist — direct repository tests were never written for it); build clean; format clean; Marketing tests pass | Tasks 1, 2 (test updates + new regression test); Task 5 (validation gate) |
| NFR-1 — One entity load per delete instead of two | Achieved by Task 3 Step 3.1 (handler reuses the already-loaded entity); regression-guarded by Task 1 Step 1.9 |
| NFR-2 — Soft-delete path matches established Marketing pattern | Task 3 Step 3.1 produces identical shape to `UpdateMarketingActionHandler` and `DeleteJournalEntryHandler` |
| NFR-3 — API surface unchanged; DB state unchanged; no callers outside `DeleteMarketingActionHandler` | Verified by Task 4 Step 4.3 grep + Step 4.4 build; MediatR request/response classes are not touched |
| NFR-4 — Audit fields populated identically; authorization unchanged | `MarketingAction.SoftDelete` not modified; auth check at handler lines 40–45 not touched |
| Arch-review §Spec Amendments item 1 (drop `now` arg from `SoftDelete` call) | Task 3 Step 3.1 notes call MUST stay 2-arg |
| Arch-review §Spec Amendments item 4 (`MarketingActionHandlerSyncTests.cs` must be updated) | Task 2 covers all 3 references |
| Arch-review Decision 3 (wrap both `UpdateAsync` + `SaveChangesAsync` in the existing `try/catch`) | Task 3 Step 3.1 wraps both |
| Arch-review Decision 4 (keep `UpdateAsync` call even though entity is tracked) | Task 3 Step 3.1 keeps the call with explanatory note |
