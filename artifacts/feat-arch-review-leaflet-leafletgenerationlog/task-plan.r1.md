# Rename `LeafletGenerationLoggingBehavior` → `LeafletGenerationPersistenceBehavior` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the misnamed `LeafletGenerationLoggingBehavior` MediatR pipeline behavior (and its test fixture + DI registration) to `LeafletGenerationPersistenceBehavior` to reflect its actual responsibility (persisting `LeafletGeneration` and stamping the response with its ID), without changing any runtime behavior.

**Architecture:** Pure identifier rename across three artifacts in the Leaflet vertical slice. The production class lives in `Application/Features/Leaflet/Pipeline/`, is wired through `LeafletModule.AddLeafletModule`, and has a Moq/xUnit test fixture. The class body, MediatR contract, DI lifetime, and test assertions are preserved byte-for-byte; only the type name, file name, and logger generic argument change. Renames use `git mv` so blame/history follows the files. Frozen historical plan docs are intentionally left untouched.

**Tech Stack:** .NET 8, MediatR `IPipelineBehavior<TRequest, TResponse>`, xUnit + Moq for tests, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging, `git mv` for rename-with-history.

---

## File Map

**Renamed (via `git mv`):**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs` → `LeafletGenerationPersistenceBehavior.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs` → `LeafletGenerationPersistenceBehaviorTests.cs`

**Edited (identifier replacement only):**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehavior.cs` — class name, constructor name, `ILogger<T>` generic
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs` — DI registration on lines 24–26
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehaviorTests.cs` — test class name, `Mock<ILogger<T>>` generic, `CreateBehavior` return + constructor

**Intentionally NOT touched:**
- `docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md` — frozen historical plan, references remain as historical record (per spec FR-4 and arch-review Spec Amendment 2).

---

## Working Directory

All steps run from the worktree root:
```
/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-leaflet-leafletgenerationlog
```

---

### Task 1: Baseline — confirm tests pass before the rename

This establishes a green baseline so any post-rename test break is provably caused by the rename, not pre-existing flakiness.

**Files:**
- Read-only verification.

- [ ] **Step 1: Confirm the current production class and test file exist with their old names**

Run:
```bash
ls backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs
ls backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs
```
Expected: both files print without error.

- [ ] **Step 2: Confirm the four expected references and nothing else**

Run:
```bash
rg -l "LeafletGenerationLoggingBehavior"
```
Expected: exactly these four paths print (order may vary):
```
backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs
backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs
backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs
docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md
```
If any **other** file is listed, STOP and report — the plan does not cover that surface and needs amendment before continuing.

- [ ] **Step 3: Build the solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 4: Run the existing fixture's three tests as the baseline**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~LeafletGenerationLoggingBehaviorTests" \
  --no-build
```
Expected: `Passed!  - Failed: 0, Passed: 3, Skipped: 0`. The three test names are:
- `Handle_SavesGenerationRow_AndSetsResponseId`
- `Handle_WhenDbWriteFails_StillReturnsResponse_WithNullId`
- `Handle_ReturnsOriginalResponse`

If any test fails here, STOP — pre-existing failure must be triaged before the rename starts.

---

### Task 2: Rename the production class file with `git mv`

**Files:**
- Rename: `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs` → `LeafletGenerationPersistenceBehavior.cs`

- [ ] **Step 1: Rename the file using `git mv`**

Run:
```bash
git mv backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs \
       backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehavior.cs
```
Expected: no output. `git mv` exits silently on success.

- [ ] **Step 2: Verify the rename registered as a rename (not delete + create)**

Run:
```bash
git status
```
Expected output contains a single rename line (similar wording):
```
renamed: backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs -> backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehavior.cs
```
If Git instead shows a `deleted:` plus an `untracked:` entry, STOP and run `git mv` again — rename detection must succeed so blame follows the file.

- [ ] **Step 3: Confirm the build now FAILS (filename moved, identifiers stale)**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: build still succeeds (C# does not require filename == class name; the type still compiles under its old identifier). If it succeeds, that is the expected state — proceed. If it fails, read the error: it should be solely caused by the file move, not by something unrelated.

Note: the build is expected to stay green at this step. The actual breaking change (the class identifier) happens in Task 3.

---

### Task 3: Update the production class identifier and logger generic

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehavior.cs`

- [ ] **Step 1: Update the class name, constructor name, and `ILogger<T>` generic**

The full updated file content is:

```csharp
using System.Diagnostics;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Leaflet.Pipeline;

public class LeafletGenerationPersistenceBehavior
    : IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>
{
    private readonly ILeafletRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<LeafletGenerationPersistenceBehavior> _logger;

    public LeafletGenerationPersistenceBehavior(
        ILeafletRepository repository,
        ICurrentUserService currentUserService,
        ILogger<LeafletGenerationPersistenceBehavior> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<GenerateLeafletResponse> Handle(
        GenerateLeafletRequest request,
        RequestHandlerDelegate<GenerateLeafletResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        if (!response.Success)
            return response;

        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var generation = new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = request.Topic,
                Audience = request.Audience.ToString(),
                Length = request.Length.ToString(),
                FinalMarkdown = response.Content,
                KbSourceCount = response.KbSourceCount,
                LeafletSourceCount = response.LeafletSourceCount,
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTimeOffset.UtcNow,
                UserId = currentUser.Id,
            };

            await _repository.SaveGenerationAsync(generation, cancellationToken);
            response.Id = generation.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log leaflet generation. Topic: {Topic}", request.Topic);
        }

        return response;
    }
}
```

Use the `Write` tool to overwrite the file at `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehavior.cs` with the content above.

The only changes vs. the prior file content are:
- `class LeafletGenerationLoggingBehavior` → `class LeafletGenerationPersistenceBehavior`
- `ILogger<LeafletGenerationLoggingBehavior> _logger` → `ILogger<LeafletGenerationPersistenceBehavior> _logger`
- `public LeafletGenerationLoggingBehavior(` → `public LeafletGenerationPersistenceBehavior(`
- `ILogger<LeafletGenerationLoggingBehavior> logger` constructor parameter type → `ILogger<LeafletGenerationPersistenceBehavior> logger`

Method body, error handling, stopwatch usage, response mutation, and the catch-and-log block are byte-identical.

- [ ] **Step 2: Verify the build now fails at the consumers (DI + tests)**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: build FAILS with errors in:
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs` — `LeafletGenerationLoggingBehavior` does not exist
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs` — same type-not-found errors

These are expected and will be fixed in Tasks 4 and 5. Do not commit yet.

---

### Task 4: Update DI registration in `LeafletModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs:24-26`

- [ ] **Step 1: Replace the registration identifier**

Use `Edit` to replace this exact block in `LeafletModule.cs`:

Old:
```csharp
        services.AddScoped<
            IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>,
            LeafletGenerationLoggingBehavior>();
```

New:
```csharp
        services.AddScoped<
            IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>,
            LeafletGenerationPersistenceBehavior>();
```

The surrounding `using` directives, namespace, class definition, options binding, other `AddScoped` calls, and comments are unchanged. Lifetime stays `Scoped`. The `IPipelineBehavior<,>` generic argument tuple is unchanged.

- [ ] **Step 2: Confirm the production assembly now builds**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: `Build succeeded` with 0 errors. The test project will still fail to build until Task 5 — that's fine for now.

---

### Task 5: Rename the test file with `git mv` and update its identifiers

**Files:**
- Rename: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs` → `LeafletGenerationPersistenceBehaviorTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehaviorTests.cs`

- [ ] **Step 1: Rename the test file using `git mv`**

Run:
```bash
git mv backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs \
       backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehaviorTests.cs
```
Expected: no output.

- [ ] **Step 2: Verify the second rename also registered as a rename**

Run:
```bash
git status
```
Expected: a second `renamed:` line for the test file, in addition to the production-class rename from Task 2.

- [ ] **Step 3: Update class name, mock generic, and helper method**

Use the `Write` tool to overwrite `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehaviorTests.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.Leaflet.Pipeline;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.Pipeline;

public class LeafletGenerationPersistenceBehaviorTests
{
    private readonly Mock<ILeafletRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private readonly Mock<ILogger<LeafletGenerationPersistenceBehavior>> _logger = new();

    private LeafletGenerationPersistenceBehavior CreateBehavior() =>
        new(_repository.Object, _userService.Object, _logger.Object);

    private static GenerateLeafletRequest MakeRequest() =>
        new()
        {
            Topic = "Vitamin C serum",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Medium,
        };

    private static GenerateLeafletResponse MakeResponse() =>
        new()
        {
            Content = "# Vitamin C serum\n\nGreat for skin.",
            KbSourceCount = 3,
            LeafletSourceCount = 1,
        };

    [Fact]
    public async Task Handle_SavesGenerationRow_AndSetsResponseId()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test", null, true));

        LeafletGeneration? captured = null;
        _repository
            .Setup(r => r.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .Callback<LeafletGeneration, CancellationToken>((g, _) => captured = g)
            .Returns(Task.CompletedTask);

        var behavior = CreateBehavior();
        var response = MakeResponse();
        var result = await behavior.Handle(MakeRequest(), () => Task.FromResult(response), default);

        Assert.NotNull(captured);
        Assert.Equal("Vitamin C serum", captured.Topic);
        Assert.Equal("EndConsumer", captured.Audience);
        Assert.Equal("Medium", captured.Length);
        Assert.Equal("# Vitamin C serum\n\nGreat for skin.", captured.FinalMarkdown);
        Assert.Equal(3, captured.KbSourceCount);
        Assert.Equal(1, captured.LeafletSourceCount);
        Assert.Equal("user-1", captured.UserId);
        Assert.True(captured.DurationMs >= 0);
        Assert.NotEqual(Guid.Empty, captured.Id);
        Assert.Equal(captured.Id, result.Id);
    }

    [Fact]
    public async Task Handle_WhenDbWriteFails_StillReturnsResponse_WithNullId()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test", null, true));
        _repository
            .Setup(r => r.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB down"));

        var behavior = CreateBehavior();
        var response = MakeResponse();
        var result = await behavior.Handle(MakeRequest(), () => Task.FromResult(response), default);

        Assert.Equal("# Vitamin C serum\n\nGreat for skin.", result.Content);
        Assert.Null(result.Id);
    }

    [Fact]
    public async Task Handle_ReturnsOriginalResponse()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test", null, true));
        _repository
            .Setup(r => r.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var behavior = CreateBehavior();
        var response = MakeResponse();
        var result = await behavior.Handle(MakeRequest(), () => Task.FromResult(response), default);

        Assert.Equal(response, result);
        Assert.Equal("# Vitamin C serum\n\nGreat for skin.", result.Content);
    }
}
```

The only changes vs. the prior content are:
- `public class LeafletGenerationLoggingBehaviorTests` → `public class LeafletGenerationPersistenceBehaviorTests`
- `Mock<ILogger<LeafletGenerationLoggingBehavior>>` → `Mock<ILogger<LeafletGenerationPersistenceBehavior>>`
- `private LeafletGenerationLoggingBehavior CreateBehavior()` → `private LeafletGenerationPersistenceBehavior CreateBehavior()`

All three `[Fact]` tests, their AAA bodies, helpers, and assertions are unchanged.

- [ ] **Step 4: Build the whole solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded` with 0 errors and 0 warnings related to this change.

- [ ] **Step 5: Run the renamed test fixture**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~LeafletGenerationPersistenceBehaviorTests" \
  --no-build
```
Expected: `Passed!  - Failed: 0, Passed: 3, Skipped: 0`. Same three test names as in Task 1, Step 4 (they're not renamed):
- `Handle_SavesGenerationRow_AndSetsResponseId`
- `Handle_WhenDbWriteFails_StillReturnsResponse_WithNullId`
- `Handle_ReturnsOriginalResponse`

- [ ] **Step 6: Confirm no test refers to the old fixture name (e.g., leftover via filter)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~LeafletGenerationLoggingBehaviorTests" \
  --no-build
```
Expected: `Passed!  - Failed: 0, Passed: 0, Skipped: 0` (no matching tests found — i.e., the old fixture name truly is gone).

---

### Task 6: Full-repo grep assertion (FR-4)

**Files:**
- Read-only verification.

- [ ] **Step 1: Confirm zero matches under `backend/` and `frontend/`**

Run:
```bash
rg "LeafletGenerationLoggingBehavior" backend/src/ backend/test/ frontend/
```
Expected: no output, exit code 1 (rg's "no matches" exit code). If the shell hides exit codes, you can verify with:
```bash
rg "LeafletGenerationLoggingBehavior" backend/src/ backend/test/ frontend/; echo "exit=$?"
```
Expected: `exit=1` (no matches).

If `frontend/` does not exist as a directory in the worktree, the command above will print a warning for that path but still return 1 overall — that is acceptable. The contract is that **no matching content** is found.

- [ ] **Step 2: Confirm the historical plan reference is intentionally still present**

Run:
```bash
rg -l "LeafletGenerationLoggingBehavior" docs/superpowers/plans/
```
Expected output (exactly one file):
```
docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md
```
This is the frozen historical plan and is intentionally preserved per arch-review Spec Amendment 2. Do not modify it.

- [ ] **Step 3: Final whole-repo sanity check excluding the historical plan**

Run:
```bash
rg "LeafletGenerationLoggingBehavior" \
  --glob '!docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md'
```
Expected: no output. If anything else matches, STOP and update this plan to cover the new surface before continuing.

---

### Task 7: Full validation — build, format, full test pass

**Files:**
- Read-only validation.

- [ ] **Step 1: Solution-wide build (no warnings policy)**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Error(s)`. Warning count must be the same as the pre-rename baseline. If new warnings appear (e.g., nullable, unused using directives in the renamed files), STOP and resolve before committing.

- [ ] **Step 2: `dotnet format` — apply, then verify the resulting diff is rename-scoped only**

Run:
```bash
dotnet format backend/Anela.Heblo.sln
```
Expected: command exits 0.

Then:
```bash
git status
git diff --stat
```
Expected: the only modified files are the three renamed/edited files from Tasks 2–5 (`LeafletGenerationPersistenceBehavior.cs`, `LeafletModule.cs`, `LeafletGenerationPersistenceBehaviorTests.cs`). If `dotnet format` touched any *other* file, STOP — that is collateral damage and must be reverted (`git checkout -- <unrelated-file>`) before continuing.

- [ ] **Step 3: Run the full test suite to confirm no other tests broke**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --no-build
```
Expected: all tests pass. Note the failure count is `0`. If any test outside the renamed fixture starts failing, it is almost certainly a pre-existing flake (compare with the Task 1 baseline) — but treat any new failure as a blocker until proven otherwise.

- [ ] **Step 4: One final grep to confirm the rename is complete**

Run:
```bash
rg "LeafletGenerationLoggingBehavior" backend/src/ backend/test/
rg "LeafletGenerationPersistenceBehavior" backend/src/ backend/test/
```
Expected:
- First command: no output (exit 1).
- Second command: matches in exactly two files — `LeafletGenerationPersistenceBehavior.cs` (production) and `LeafletGenerationPersistenceBehaviorTests.cs` (test), plus `LeafletModule.cs` (DI). Three files total.

---

### Task 8: Commit

**Files:**
- All staged changes from Tasks 2–7.

- [ ] **Step 1: Review the staged diff**

Run:
```bash
git status
git diff --staged --stat
```
Expected: two `renamed:` entries plus modifications inside the renamed files and `LeafletModule.cs`. Confirm:
- `LeafletGenerationLoggingBehavior.cs` → `LeafletGenerationPersistenceBehavior.cs` (rename detected)
- `LeafletGenerationLoggingBehaviorTests.cs` → `LeafletGenerationPersistenceBehaviorTests.cs` (rename detected)
- `LeafletModule.cs` (modified)

If Git shows delete+add instead of rename for either file, STOP — re-do the rename using `git mv` so the history follows the file.

- [ ] **Step 2: Stage anything not already staged**

`git mv` stages renames automatically, but if `dotnet format` produced edits, ensure they are staged too:

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehavior.cs
git add backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs
git add backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehaviorTests.cs
```

- [ ] **Step 3: Commit**

Run:
```bash
git commit -m "refactor(leaflet): rename LeafletGenerationLoggingBehavior to LeafletGenerationPersistenceBehavior

The MediatR pipeline behavior persists the LeafletGeneration record and
stamps the response with its ID — its previous 'Logging' name actively
misled readers. Pure rename: file, class, ILogger<T> generic, DI
registration, and test fixture identifiers updated. Method body,
DI lifetime, exception swallow-and-log, response mutation, and all
three existing test assertions are unchanged."
```
Expected: commit succeeds. Pre-commit hooks (if any) pass. If a hook fails, fix the underlying issue and create a new commit (do not amend).

- [ ] **Step 4: Verify the commit recorded the renames**

Run:
```bash
git show --stat HEAD
```
Expected: the diff stat lists both file renames as `path/old.cs => path/new.cs` (rename detection succeeded), plus the `LeafletModule.cs` modification.

---

## Out-of-Scope Reminders (do NOT do)

These were considered and explicitly excluded by the spec and arch review. Do not opportunistically include them:

1. Do **not** split timing/observability into a second `LeafletGenerationLoggingBehavior` — pipeline ordering and a second registration are out of scope.
2. Do **not** move `response.Id = generation.Id` into `GenerateLeafletHandler` — that changes the handler contract.
3. Do **not** tighten the `catch (Exception ex)` block — existing tests assert the swallow behavior.
4. Do **not** make `GenerateLeafletResponse.Id` `init`-only or rename `LeafletGeneration.Id` semantics.
5. Do **not** rename the analogous `QuestionLoggingBehavior` in the KnowledgeBase slice (tracked as a separate follow-up per arch-review Spec Amendment 1).
6. Do **not** edit `docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md` — it is a frozen historical record.

---

## Acceptance Checklist (matches spec FR/NFR)

- [x] FR-1: Production class renamed (file, class, constructor, logger generic). — Tasks 2–3.
- [x] FR-2: DI registration updated to `LeafletGenerationPersistenceBehavior`, scope remains `Scoped`. — Task 4.
- [x] FR-3: Test file renamed, test class renamed, mock generic updated, all three tests pass unchanged. — Task 5.
- [x] FR-4: `rg "LeafletGenerationLoggingBehavior"` under `backend/src/`, `backend/test/`, `frontend/` returns zero; historical plan doc intentionally preserved. — Task 6.
- [x] NFR-1: Behavior parity — `Handle` body, ordering, exception handling unchanged. — Task 3, Step 1 (byte-identical method body).
- [x] NFR-2: `dotnet build` succeeds; `dotnet format` diff is rename-scoped only. — Task 7.
- [x] NFR-3: All three tests pass; full test suite has no new failures. — Tasks 5 and 7.
- [x] NFR-4: Surgical change — no adjacent refactor; only identifier replacements. — Out-of-scope reminders above; Tasks 3/5 use exact replacement content.
- [x] Renames use `git mv` so blame/history follow. — Tasks 2, 5 (verified in steps that check `git status` shows `renamed:`).
