# Marketing Module — Consistent DB Save Error Handling for Update and Delete Handlers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `UpdateMarketingActionHandler` and `DeleteMarketingActionHandler` into line with the guarded-save + structured `ErrorCodes.DatabaseError` pattern already used by `CreateMarketingActionHandler`, so that DB save failures after a successful Outlook write surface as a typed error envelope rather than an unhandled HTTP 500.

**Architecture:** Add an inline `try { …repository writes… } catch (Exception) { log + return typed response(ErrorCodes.DatabaseError) }` block in each handler. No compensation for Outlook (out of scope per spec). No shared helper or pipeline behavior — three handlers, three near-identical blocks, justified by per-handler log phrasing and response type differences. No new interfaces, no DI changes, no schema changes.

**Tech Stack:** .NET 8 / C# 12, MediatR, EF Core (`IMarketingActionRepository`), xUnit, Moq, FluentAssertions. Existing `ErrorCodes.DatabaseError` (= 0011, `[HttpStatusCode(HttpStatusCode.InternalServerError)]`) and existing `UpdateMarketingActionResponse(ErrorCodes, Dictionary<string,string>?)` / `DeleteMarketingActionResponse(ErrorCodes, Dictionary<string,string>?)` constructors are reused as-is.

---

## File Structure

**Production code (modify, do not create):**

- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` — wrap the `UpdateAsync` + `SaveChangesAsync` calls (currently lines 112–113) in a try/catch that logs and returns `ErrorCodes.DatabaseError`.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs` — wrap the `DeleteSoftAsync` call (currently lines 75–76) in the same shape.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` — **untouched** (FR-4); this is the reference implementation.

**Tests (modify, do not create new files):**

- `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` — add one DB-failure test.
- `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs` — add one DB-failure test.
- `backend/test/Anela.Heblo.Tests/Application/Marketing/CreateMarketingActionHandlerTests.cs` — **untouched** (FR-4).

No new files, no new directories, no DI changes, no new packages, no schema changes, no Key Vault entries.

---

## Conventions Reused From Existing Code (read before starting)

These are facts verified against the worktree, not assumptions:

- **`ErrorCodes.DatabaseError`** exists at `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:35` with `[HttpStatusCode(HttpStatusCode.InternalServerError)]`. No new enum value is needed.
- **`UpdateMarketingActionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)`** constructor exists at `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/UpdateMarketingActionRequest.cs:41-42`.
- **`DeleteMarketingActionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)`** constructor exists at `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/DeleteMarketingActionRequest.cs:19-20`.
- **`ILogger<UpdateMarketingActionHandler>`** and **`ILogger<DeleteMarketingActionHandler>`** are already injected (constructor params verified in both handlers).
- **`_repository.DeleteSoftAsync`** internally commits — the Delete handler does NOT call `SaveChangesAsync` afterwards. The try/catch wraps the single `DeleteSoftAsync` call.
- **Reference implementation** to mirror: `CreateMarketingActionHandler.cs:87-112`. Its catch block has shape: `_logger.LogError(dbEx, "…{ActionId}…{EventId}…", …); return new XxxResponse(ErrorCodes.DatabaseError);`
- **Test infrastructure** (`Mock<IMarketingActionRepository>`, `Mock<ILogger<T>>`, `Mock<IOutlookCalendarSync>`, `TestOptionsMonitor<MarketingCalendarOptions>`, `AuthenticatedUser`, `BuildExistingAction`, `BuildRequest`, `BuildHandler`) is already wired in both test classes. New tests reuse it.

---

## Commit Discipline

One commit per task. Use Conventional Commits (`fix:` for the production behavior change, `test:` for tests-only commits). Match the repo's existing style (no `Co-Authored-By` trailer — global attribution is disabled per `~/.claude/settings.json` per the user's notes). Run `dotnet build` and `dotnet format` before each commit.

---

## Task 1: Failing test for `UpdateMarketingActionHandler` DB-save failure

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` (add one new `[Fact]` method)

- [ ] **Step 1: Add a `using` if missing**

Verify the file already has these usings (it does, per current state — listed for completeness):

```csharp
using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Marketing.UseCases.UpdateMarketingAction;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
```

No new using directives needed.

- [ ] **Step 2: Add the failing test**

Append this `[Fact]` inside the `UpdateMarketingActionHandlerTests` class, immediately after `Handle_HonorsRuntimePushEnabledFlip_FalseToTrue` (the current last test):

```csharp
[Fact]
public async Task Handle_ReturnsDatabaseError_AndLogsOutOfSyncWarning_WhenDbSaveFails()
{
    _outlookSync
        .Setup(x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    _repository
        .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .ThrowsAsync(new Exception("DB unavailable"));

    var act = async () => await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

    var result = await act.Should().NotThrowAsync();

    result.Subject.Success.Should().BeFalse();
    result.Subject.ErrorCode.Should().Be(ErrorCodes.DatabaseError);

    _outlookSync.Verify(
        x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
        Times.Once);

    _logger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("may now be out of sync")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}
```

**Why these assertions:**
- `NotThrowAsync` enforces FR-1 acceptance criterion: the exception must not propagate to MediatR.
- `ErrorCode == DatabaseError` enforces the typed-response contract.
- `_outlookSync.UpdateEventAsync … Times.Once` confirms the Outlook write happened *before* the DB write — i.e. this test does exercise the "Outlook succeeded, DB failed" path, not some earlier short-circuit.
- The `_logger.Verify(...)` block is the documented `Mock<ILogger<T>>` pattern for asserting a structured log message; matching on the substring `"may now be out of sync"` enforces FR-3 (greppable phrase).

- [ ] **Step 3: Run the new test to confirm it FAILS**

Run from repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateMarketingActionHandlerTests.Handle_ReturnsDatabaseError_AndLogsOutOfSyncWarning_WhenDbSaveFails"
```

**Expected:** Test FAILS. The thrown `Exception("DB unavailable")` propagates out of `Handle` (because there is no try/catch around `SaveChangesAsync` yet). `NotThrowAsync` reports the unhandled exception.

- [ ] **Step 4: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs
git commit -m "test: add failing test for UpdateMarketingActionHandler DB save failure"
```

---

## Task 2: Implement guarded save in `UpdateMarketingActionHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs:112-113`

- [ ] **Step 1: Replace the unguarded repository calls with a try/catch**

Locate the existing two-line block at lines 112–113:

```csharp
            await _repository.UpdateAsync(action, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
```

Replace **both lines** with the following block (preserve the same 12-space indentation since this is inside the `Handle` method body inside the namespace-then-class wrapping):

```csharp
            try
            {
                await _repository.UpdateAsync(action, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx,
                    "DB save failed after Outlook update for MarketingAction {ActionId}; Outlook event {EventId} may now be out of sync",
                    action.Id,
                    action.OutlookEventId);
                return new UpdateMarketingActionResponse(ErrorCodes.DatabaseError);
            }
```

**Notes on what to preserve:**
- Do not touch the `_logger.LogInformation("MarketingAction {ActionId} updated by user {UserId}", action.Id, currentUser.Id);` call directly below or the success-path `return new UpdateMarketingActionResponse { Id = action.Id, ModifiedAt = action.ModifiedAt };` — they stay outside the try/catch (only the success path reaches them, because the catch returns early).
- The catch type is `Exception` (not `DbUpdateException`) to match the Create handler precedent (FR-4 / arch-review Decision 2). This intentionally also catches `OperationCanceledException`; per arch-review §Risks this is acceptable.
- Log placeholders are `{ActionId}` and `{EventId}` — structured logging, NOT string interpolation (FR-3).
- No sensitive payload fields are logged (NFR-2).
- No Outlook revert is attempted (Out of Scope).

- [ ] **Step 2: Run the previously failing test — it should now PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateMarketingActionHandlerTests.Handle_ReturnsDatabaseError_AndLogsOutOfSyncWarning_WhenDbSaveFails"
```

**Expected:** PASS.

- [ ] **Step 3: Run the full `UpdateMarketingActionHandlerTests` class to confirm no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateMarketingActionHandlerTests"
```

**Expected:** All ~10 tests PASS (the 9 existing + the new one).

- [ ] **Step 4: Build and format**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs
```

**Expected:** Build succeeds with no warnings on the changed file; format produces no diff (or only trivial whitespace fixes).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs
git commit -m "fix: guard DB save in UpdateMarketingActionHandler and return DatabaseError on failure"
```

---

## Task 3: Failing test for `DeleteMarketingActionHandler` DB-soft-delete failure

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs` (add one new `[Fact]` method)

- [ ] **Step 1: Verify required usings are already present**

The file already imports `System.Net`, `FluentAssertions`, `Moq`, `Microsoft.Extensions.Logging`, and the handler/contract namespaces. No additions needed.

- [ ] **Step 2: Add the failing test**

Append this `[Fact]` inside `DeleteMarketingActionHandlerTests`, immediately after `Handle_HonorsRuntimePushEnabledFlip_FalseToTrue`:

```csharp
[Fact]
public async Task Handle_ReturnsDatabaseError_AndLogsAlreadyDeletedWarning_WhenSoftDeleteFails()
{
    _outlookSync
        .Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    _repository
        .Setup(x => x.DeleteSoftAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new Exception("DB unavailable"));

    var act = async () => await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

    var result = await act.Should().NotThrowAsync();

    result.Subject.Success.Should().BeFalse();
    result.Subject.ErrorCode.Should().Be(ErrorCodes.DatabaseError);

    _outlookSync.Verify(
        x => x.DeleteEventAsync("event-abc", It.IsAny<CancellationToken>()),
        Times.Once);

    _logger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("already deleted")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}
```

**Notes:**
- The `BuildExistingAction` helper sets `OutlookEventId = "event-abc"` by default, so `_outlookSync.DeleteEventAsync("event-abc", …)` is the expected call.
- The log substring `"already deleted"` matches the message defined in Task 4. The Delete handler also logs `"already deleted (404)"` at `LogInformation` level for the 404 happy path (lines 62–64 of the handler), but this test forces a `LogError` (different level + different code path), and `_logger.Verify(... LogLevel.Error ...)` narrows the match to the new DB-failure error log only — there is no collision.

- [ ] **Step 3: Run the new test to confirm it FAILS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DeleteMarketingActionHandlerTests.Handle_ReturnsDatabaseError_AndLogsAlreadyDeletedWarning_WhenSoftDeleteFails"
```

**Expected:** FAIL — the `Exception("DB unavailable")` thrown by `DeleteSoftAsync` propagates.

- [ ] **Step 4: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs
git commit -m "test: add failing test for DeleteMarketingActionHandler soft-delete failure"
```

---

## Task 4: Implement guarded soft-delete in `DeleteMarketingActionHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs:75-76`

- [ ] **Step 1: Replace the unguarded `DeleteSoftAsync` call with a try/catch**

Locate the existing two-line block at lines 75–76 (this is one logical statement broken across two lines):

```csharp
            await _repository.DeleteSoftAsync(
                request.Id, currentUser.Id, currentUser.Name ?? "Unknown User", cancellationToken);
```

Replace **both lines** with:

```csharp
            try
            {
                await _repository.DeleteSoftAsync(
                    request.Id, currentUser.Id, currentUser.Name ?? "Unknown User", cancellationToken);
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx,
                    "DB soft-delete failed after Outlook delete; Outlook event {EventId} already deleted — DB row {ActionId} still present",
                    action.OutlookEventId,
                    request.Id);
                return new DeleteMarketingActionResponse(ErrorCodes.DatabaseError);
            }
```

**Notes:**
- This wraps **only** the `DeleteSoftAsync` call. There is no separate `SaveChangesAsync` in this handler — `DeleteSoftAsync` commits internally (arch-review Decision 3).
- `action.OutlookEventId` is in scope (the handler reads `action` from `GetByIdAsync` at line 47). When `PushEnabled == false` or `OutlookEventId` was null, no Outlook write happened — the `{EventId}` placeholder simply renders `null`/empty, and the message still emits, satisfying NFR-3 (greppability is preserved without a special branch).
- The log message uses the more specific phrase `"already deleted — DB row {ActionId} still present"` (arch-review §Specification Amendments §3) to avoid collision with the *successful 404* log on lines 62–64 (`"already deleted (404); proceeding with soft-delete"`). The substring `"already deleted"` remains grep-friendly per FR-3, but the surrounding context disambiguates intent.
- The subsequent `_logger.LogInformation("MarketingAction {ActionId} deleted by user {UserId}", request.Id, currentUser.Id);` (line 78) and the success-path `return new DeleteMarketingActionResponse { Id = request.Id };` (line 80) stay untouched outside the try/catch — only the success path reaches them.
- No Outlook recreate is attempted (Out of Scope).

- [ ] **Step 2: Run the previously failing test — it should now PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DeleteMarketingActionHandlerTests.Handle_ReturnsDatabaseError_AndLogsAlreadyDeletedWarning_WhenSoftDeleteFails"
```

**Expected:** PASS.

- [ ] **Step 3: Run the full `DeleteMarketingActionHandlerTests` class to confirm no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DeleteMarketingActionHandlerTests"
```

**Expected:** All ~9 tests PASS.

- [ ] **Step 4: Build and format**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs
```

**Expected:** Build succeeds; format diff is empty or trivial.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs
git commit -m "fix: guard soft-delete in DeleteMarketingActionHandler and return DatabaseError on failure"
```

---

## Task 5: Cross-handler grep verification (FR-3)

This task verifies the greppable-phrase requirement across all three handlers. It changes nothing; it only confirms FR-3's acceptance criterion holds.

- [ ] **Step 1: Grep for the "out of sync" phrase**

```bash
grep -rn "out of sync" backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/
```

**Expected:** At least one hit, in `UpdateMarketingActionHandler.cs`.

- [ ] **Step 2: Grep for the "already deleted" phrase**

```bash
grep -rn "already deleted" backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/
```

**Expected:** At least two hits, in `DeleteMarketingActionHandler.cs` — one in the existing 404 happy-path log (`Outlook event {EventId} already deleted (404)`) and one in the new DB-failure log (`Outlook event {EventId} already deleted — DB row {ActionId} still present`).

- [ ] **Step 3: Grep for the Create handler's reference phrase**

```bash
grep -rn "compensating Outlook event" backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/
```

**Expected:** One hit in `CreateMarketingActionHandler.cs` (unchanged from before this work). Confirms FR-4 — the reference implementation is intact.

- [ ] **Step 4: Confirm no string interpolation snuck into the new log messages (FR-3 forbids it)**

```bash
grep -nE '_logger\.LogError\([^,]*,\s*\$"' backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs
```

**Expected:** No matches. (If any line shows `LogError(... , $"…"`, that's a violation of FR-3 — fix by replacing with a non-interpolated template and structured arguments.)

- [ ] **Step 5: No commit needed**

This task produces no code changes. If a grep failure surfaces a bug, fix it under the relevant task above and re-run greps.

---

## Task 6: Full Marketing test suite + final build/format sweep

This is the final gate before declaring the change complete.

- [ ] **Step 1: Run all Marketing module tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Application.Marketing"
```

**Expected:** All Marketing handler tests pass — Create (~10), Update (~10), Delete (~9). The new DB-failure tests for Update and Delete are present and green. The existing `CreateMarketingActionHandler` tests are unchanged and green (FR-4).

- [ ] **Step 2: Build the full Application project**

```bash
dotnet build backend/Anela.Heblo.sln
```

**Expected:** Solution builds with no new warnings on the touched files.

- [ ] **Step 3: Format the application project (sweep)**

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes
```

**Expected:** Exit code 0 (no changes needed). If non-zero, run without `--verify-no-changes`, commit any whitespace fixes as `chore: format`.

- [ ] **Step 4: Run the full backend test suite to confirm no cross-module regressions**

```bash
dotnet test backend/Anela.Heblo.sln
```

**Expected:** Existing pass-rate maintained. Anything failing in unrelated modules pre-existed and is out of scope here (do not "fix" unrelated test failures in this change — log them as a separate issue if you find any).

- [ ] **Step 5: No commit needed unless `dotnet format` produced diffs in step 3**

If step 3 produced formatting changes only, commit:

```bash
git add -p
git commit -m "chore: dotnet format sweep after marketing handler error-handling fix"
```

Otherwise skip this step.

---

## Spec Coverage Self-Check

Mapping every spec requirement to the task that implements it:

| Spec Requirement | Implemented by |
|---|---|
| FR-1 — guard `UpdateAsync` + `SaveChangesAsync` in Update handler | Task 2 |
| FR-1 acceptance: forced DB-throw returns `DatabaseError`, no exception escapes | Task 1 (test) + Task 2 (impl) |
| FR-1 acceptance: error-level log contains `{ActionId}` + `{EventId}` | Task 1 (`_logger.Verify` with `LogLevel.Error` + substring) |
| FR-1 acceptance: existing happy-path Update tests still pass | Task 2 Step 3 |
| FR-1 acceptance: API integration returns structured envelope, not 500 | **Amended per arch-review §Specification Amendments §2** — no Marketing `WebApplicationFactory` harness exists; unit tests (Task 1/2) are the hard gate. Acceptable per arch-review. |
| FR-2 — guard `DeleteSoftAsync` in Delete handler | Task 4 |
| FR-2 acceptance: forced throw returns typed `DatabaseError`, no exception escapes | Task 3 + Task 4 |
| FR-2 acceptance: log contains `{ActionId}` + `{EventId}` | Task 3 (`_logger.Verify`) |
| FR-2 acceptance: existing happy-path Delete tests still pass | Task 4 Step 3 |
| FR-3 — consistent log format with structured placeholders, greppable phrases | Tasks 2/4 (log messages) + Task 5 (grep verification) |
| FR-3 acceptance: grep "out of sync" / "already deleted" returns hits per handler | Task 5 Steps 1–2 |
| FR-3 acceptance: structured placeholders, NOT string interpolation | Task 5 Step 4 |
| FR-4 — preserve Create handler behavior | Files list explicitly excludes Create handler; Task 6 Step 1 runs Create tests; Task 5 Step 3 verifies Create reference phrase still present. |
| NFR-1 — performance: no measurable impact | Inline try/catch only; success path unchanged. No work needed. |
| NFR-2 — security: no sensitive payload logged | Tasks 2/4 log only `{ActionId}` and `{EventId}` (no title, description, customer data). |
| NFR-3 — observability: operator can locate row + Outlook event from log | Tasks 2/4 log both IDs. |
| NFR-4 — consistency / maintainability across all three handlers | Tasks 2/4 mirror the Create handler's shape; Task 5 grep verification confirms symmetry. |
| Out of Scope items | Honored: no Outlook revert in Task 2, no Outlook recreate in Task 4, no outbox/2PC, no retry, no save-order reordering, no shared helper. |

No gaps.

---

## Placeholder Self-Check

No "TBD", "TODO", "implement later", "similar to Task N", "add appropriate error handling", or "write tests for the above" anywhere in this plan. Every code-modifying step shows the exact code. Every command shows the expected output.

## Type / Signature Consistency Self-Check

- `UpdateMarketingActionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)` — used in Task 2 as `new UpdateMarketingActionResponse(ErrorCodes.DatabaseError)` (one arg, second defaults). Matches the constructor signature at `UpdateMarketingActionRequest.cs:41`. ✅
- `DeleteMarketingActionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)` — used in Task 4 as `new DeleteMarketingActionResponse(ErrorCodes.DatabaseError)`. Matches `DeleteMarketingActionRequest.cs:19`. ✅
- `ErrorCodes.DatabaseError` — confirmed at `ErrorCodes.cs:35`. ✅
- `_repository.DeleteSoftAsync(int id, string userId, string username, CancellationToken)` — Task 4's catch wraps exactly this signature (verified against `DeleteMarketingActionHandler.cs:75-76`). ✅
- `_logger.Verify(... It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("…")) ...)` — the standard `Mock<ILogger<T>>` pattern (arch-review §Risks). The Create handler's existing test `Handle_CompensatesOutlookEvent_WhenDbSaveFails` (lines 108–125 of `CreateMarketingActionHandlerTests.cs`) does NOT itself assert on log content, so this pattern is being introduced here; per arch-review §Specification Amendments §1 it is the recommended approach for the FR-1/FR-2 log assertions. ✅
- `action.Id` (Task 2 log) and `request.Id` (Task 4 log) — both resolve to `int`. In Task 2, `action` was loaded via `GetByIdAsync` at line 52, so `action.Id` is the persisted ID. In Task 4, `request.Id` and `action.Id` are equal (the request ID is what was used to load the action), but `request.Id` is used because the catch wraps a call that takes `request.Id` directly — symmetric with the call shape. ✅
- `action.OutlookEventId` — `string?`. In Task 2's log it may be the existing event ID or the freshly-assigned one from `MarkOutlookSynced` (arch-review §Risks notes this is the desired behavior). In Task 4's log it is whatever was on `action` at load time. Both render correctly through structured logging when null (renders as empty / `(null)`). ✅
