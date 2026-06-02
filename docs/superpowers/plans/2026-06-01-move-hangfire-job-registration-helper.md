# Move HangfireJobRegistrationHelper to API Layer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Relocate `HangfireJobRegistrationHelper` from `Anela.Heblo.Application` to `Anela.Heblo.API/Infrastructure/Hangfire/`, restoring the Clean Architecture boundary for the helper itself while preserving exact runtime behavior, public surface, and `git --follow` history.

**Architecture:** Pure structural refactor — single file move (source + test) plus namespace updates. No interface changes, no DI wiring changes, no package additions. Existing tests (8 in `HangfireJobRegistrationHelperTests`) act as the regression safety net. The `Hangfire.Core` `PackageReference` on `Anela.Heblo.Application.csproj` is **retained** because six other Application-layer files still depend on it (out of scope per the architecture review).

**Tech Stack:** .NET 8, C# 12, xUnit, Hangfire 1.8.21, `git mv`.

---

## File Structure

### Files moved (use `git mv`)

| From | To |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs` | `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs` |
| `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs` | `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs` |

### Files edited in place (using directive cleanup)

- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` — remove now-unused `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` (line 1). All other types used by this file resolve from `Anela.Heblo.Domain.*`, `Microsoft.*`, or the file's own namespace.

### Files explicitly NOT edited

- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` — keeps `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` because that namespace still provides the `IHangfireRecurringJobScheduler` interface it implements. The helper resolves via the file's own `Anela.Heblo.API.Infrastructure.Hangfire` namespace.
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — the `Hangfire.Core` `PackageReference` (line 13) **stays**. Six other Application files (`FailedJobsTile`, `DashboardModule`, `GenerateArticleJob`, `GenerateArticleHandler`, `ProductExportDownloadJob`, `PlaudPollingJob`) still use Hangfire types. Removing the package would break them and is explicitly out of scope per `spec.r1.md` Out of Scope and `arch-review.r1.md` Amendment B.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` — already accurate (refers only to the unchanged `IHangfireJobEnqueuer`/`IHangfireRecurringJobScheduler` abstractions).
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/Infrastructure/HangfireTestFixture.cs` — stays at this path; the moved test references it via `using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;` (unchanged).

### Target final state for the moved helper

```csharp
using System.Reflection;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Single entry point for binding a runtime <see cref="Type"/> to
/// <see cref="RecurringJob.AddOrUpdate{TJob}(string, System.Linq.Expressions.Expression{Action{TJob}}, string, RecurringJobOptions)"/>.
/// Used by both startup discovery and runtime CRON updates so that both code paths
/// produce identical Hangfire <c>RecurringJob</c> records.
/// </summary>
public static class HangfireJobRegistrationHelper
{
    // ... body unchanged from current Application-layer version ...
}
```

### Target final state for the moved test class declaration

```csharp
using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using Hangfire;
using Hangfire.Storage;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.Hangfire;

[Collection("Hangfire")]
public class HangfireJobRegistrationHelperTests : IDisposable
{
    // ... body unchanged ...
}
```

---

## Task 1: Establish green baseline

Confirm the solution builds, formats, and the `HangfireJobRegistrationHelperTests` are green **before** any changes. This locks in the regression-safety contract.

**Files:** none modified.

- [ ] **Step 1: Build the solution to confirm a clean starting point**

Run:

```bash
dotnet build backend/Anela.Heblo.sln -nologo -v minimal
```

Expected: `Build succeeded` with `0 Warning(s)` and `0 Error(s)` for the affected projects (`Anela.Heblo.Application`, `Anela.Heblo.API`, `Anela.Heblo.Tests`). If warnings exist that pre-date this refactor, note them — the post-refactor build must not show *new* warnings.

- [ ] **Step 2: Run the target tests to confirm baseline green**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~HangfireJobRegistrationHelperTests" \
  --no-build --nologo -v normal
```

Expected: `Passed!` with 8 tests passed (1×`WithValidInputs`, 1×`CalledTwice`, 1×`WithNullJobType`, 3×`WithMissingJobName`, 3×`WithMissingCron`, 3×`WithMissingTimeZoneId`, 1×`TypeNotImplementingIRecurringJob`, 1×`WithInvalidTimeZoneId` — total 8 distinct `[Fact]`/`[Theory]` rows expanding to ~14 test runs).

- [ ] **Step 3: Confirm formatter is clean**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes --severity warn
```

Expected: exit code 0 with no diff. If pre-existing format drift is reported, document it; the refactor must not introduce *new* drift.

- [ ] **Step 4: Snapshot current `using Hangfire` count in Application (for FR-2 acceptance evidence)**

Run:

```bash
grep -rln "using Hangfire" backend/src/Anela.Heblo.Application/ | sort
```

Expected (pre-refactor, 7 files):

```
backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs
backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs
backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs
backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs
backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs
```

Record this list — after the refactor the same command must return exactly six files (helper removed; others untouched).

---

## Task 2: Move the source file with `git mv` and update its namespace

This task physically relocates the file with history preserved, then changes only its namespace. The class body is byte-for-byte identical.

**Files:**
- Move: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs` → `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs`
- Modify: the moved file (namespace only).

- [ ] **Step 1: Verify the destination folder exists**

Run:

```bash
ls backend/src/Anela.Heblo.API/Infrastructure/Hangfire/
```

Expected: lists sibling files including `HangfireJobEnqueuer.cs`, `HangfireRecurringJobScheduler.cs`, `RecurringJobDiscoveryService.cs`. The folder exists; no `mkdir` is needed.

- [ ] **Step 2: Move the file with `git mv` (preserves history per NFR-4)**

Run:

```bash
git mv \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs \
  backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs
```

Expected: no output (success). Then verify:

```bash
git status
```

Expected output includes:

```
        renamed:    backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs -> backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs
```

A `renamed:` (not `deleted: ... new file: ...`) line is the proof that history is preserved.

- [ ] **Step 3: Update the namespace in the moved file**

Edit `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs`. Change line 5 only:

From:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;
```

To:

```csharp
namespace Anela.Heblo.API.Infrastructure.Hangfire;
```

No other line in the file changes. The `using` directives (`System.Reflection`, `Anela.Heblo.Domain.Features.BackgroundJobs`, `Hangfire`), class signature, both methods, all attributes, and reflection lookups stay identical.

- [ ] **Step 4: Confirm history follows through the rename**

Run:

```bash
git log --follow --oneline -3 -- backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs
```

Expected: shows at least the original creation commit (`9d8eeb81 feat: Consolidate Hangfire RecurringJob Registration (#1911)`) plus any later edits — proving the rename was tracked.

- [ ] **Step 5: Sanity-build the API project to confirm the helper compiles in its new home**

Run:

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -nologo -v minimal
```

Expected: `Build succeeded` with `0 Error(s)`. The two callers (`RecurringJobDiscoveryService`, `HangfireRecurringJobScheduler`) live in the same `Anela.Heblo.API.Infrastructure.Hangfire` namespace as the moved helper, so they resolve it without any new `using` directive.

**Do not commit yet** — the test project does not yet compile (test file still references the old namespace). Move on to Task 3.

---

## Task 3: Move the test file and update its namespace + using directive

The test class moves to mirror the production folder structure. Its `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` directive must be replaced with `using Anela.Heblo.API.Infrastructure.Hangfire;` so it can resolve the helper at the new location.

**Files:**
- Move: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs` → `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs`
- Modify: the moved test file (namespace + one using directive).

- [ ] **Step 1: Create the destination test folder if missing**

Run:

```bash
mkdir -p backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire
```

Expected: no output (folder created or already exists; idempotent).

- [ ] **Step 2: Verify the test build is currently broken (red proof that Task 2 needs a follow-through)**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -nologo -v minimal
```

Expected: `Build FAILED` with error CS0234 or CS0246, e.g.:

```
HangfireJobRegistrationHelperTests.cs(...): error CS0234: The type or namespace name 'HangfireJobRegistrationHelper' does not exist in the namespace 'Anela.Heblo.Application.Features.BackgroundJobs.Services'
```

This confirms the test file is the only remaining stale reference. **This is expected mid-refactor — it gets fixed in the next steps.**

- [ ] **Step 3: Move the test file with `git mv`**

Run:

```bash
git mv \
  backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs \
  backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs
```

Expected: no output. Then `git status` shows another `renamed:` entry.

- [ ] **Step 4: Update the using directive in the moved test file**

Edit `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs`. Change line 1:

From:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
```

To:

```csharp
using Anela.Heblo.API.Infrastructure.Hangfire;
```

Leave the remaining usings (`Anela.Heblo.Domain.Features.BackgroundJobs`, `Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure`, `Hangfire`, `Hangfire.Storage`, `Xunit`) untouched. The `HangfireTestFixture` and `[Collection("Hangfire")]` resolution still works because the fixture stays at `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/Infrastructure/HangfireTestFixture.cs` with namespace `Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure`.

- [ ] **Step 5: Update the test class namespace**

In the same file, change line 8 (after the using-block change above the declaration moves to where the namespace lives, around line 8):

From:

```csharp
namespace Anela.Heblo.Tests.Features.BackgroundJobs;
```

To:

```csharp
namespace Anela.Heblo.Tests.Infrastructure.Hangfire;
```

The class body — including the two private nested types `HelperTestRecurringJob` and `NotARecurringJob`, the `Dispose()` method, and all `[Fact]`/`[Theory]` methods — is unchanged.

- [ ] **Step 6: Build the test project to confirm it compiles again**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -nologo -v minimal
```

Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 7: Run only the moved tests to confirm runtime equivalence**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~HangfireJobRegistrationHelperTests" \
  --no-build --nologo -v normal
```

Expected: `Passed!` with the same test count as the baseline in Task 1 Step 2. In particular, these two tests prove the reflection path still works after the namespace change:

- `RegisterOrUpdate_WithValidInputs_RegistersJobInHangfireStorage` — exercises `RegisterOrUpdateGeneric<TJob>` via reflection and asserts the job lands in `JobStorage.Current` with the right cron, time zone, and async `Task` return type.
- `RegisterOrUpdate_WithInvalidTimeZoneId_ThrowsUnwrappedTimeZoneNotFoundException` — exercises the `TargetInvocationException` unwrapping path.

If either fails, the reflection lookup (`typeof(HangfireJobRegistrationHelper).GetMethod(nameof(RegisterOrUpdateGeneric), ...)`) is broken — re-read the moved helper file and confirm `RegisterOrUpdateGeneric` is still a `private static` method on the same `HangfireJobRegistrationHelper` type.

---

## Task 4: Remove the now-unused using directive in `RecurringJobDiscoveryService`

Of the two API call sites, only `RecurringJobDiscoveryService.cs` imports the legacy `Anela.Heblo.Application.Features.BackgroundJobs.Services` namespace solely for the helper — the file uses no other type from that namespace. Removing the now-unused directive keeps the file tidy and is required by FR-3 ("No `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` … remains anywhere in the solution referring to this helper").

`HangfireRecurringJobScheduler.cs` keeps the directive because it implements `IHangfireRecurringJobScheduler` from the same namespace.

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs:1`

- [ ] **Step 1: Confirm the file does not depend on any other type from that namespace**

Run:

```bash
grep -E "(IHangfireJobEnqueuer|IHangfireRecurringJobScheduler|HangfireJobRegistrationHelper)" \
  backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs
```

Expected: matches only `HangfireJobRegistrationHelper.RegisterOrUpdate(...)` at one location (around line 79). The other two interfaces are not used in this file — so removing the using is safe.

- [ ] **Step 2: Delete the legacy using directive**

Edit `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`. Delete line 1:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
```

The remaining usings (`Anela.Heblo.Domain.Features.BackgroundJobs`, `Anela.Heblo.Xcc`, `Microsoft.Extensions.Options`) stay. The file's namespace `Anela.Heblo.API.Infrastructure.Hangfire` (line 6) already resolves `HangfireJobRegistrationHelper` at its new location.

- [ ] **Step 3: Confirm `HangfireRecurringJobScheduler.cs` is intentionally untouched**

Run:

```bash
grep -n "using Anela.Heblo.Application.Features.BackgroundJobs.Services" \
  backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs
```

Expected: matches line 1. This is correct — the file implements `IHangfireRecurringJobScheduler` from that namespace and must keep the using.

- [ ] **Step 4: Re-build the API project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -nologo -v minimal
```

Expected: `Build succeeded` with `0 Error(s)` and no *new* warnings versus the Task 1 baseline.

---

## Task 5: Repo-wide stale-reference audit and full validation

Catch any hidden consumer of the old namespace (Razor views, configuration files, comments, or reflective string lookups) before declaring done. Then run the full suite.

**Files:** none modified.

- [ ] **Step 1: Search the entire repo for any stale reference to the old helper path**

Run:

```bash
grep -rn "Anela.Heblo.Application.Features.BackgroundJobs.Services.HangfireJobRegistrationHelper" .
```

Expected: zero matches in source/test directories. Matches in `docs/`, `artifacts/`, or this plan file itself are allowed (and expected — they describe the history of the refactor).

- [ ] **Step 2: Verify FR-2 acceptance — Application no longer carries the helper's Hangfire dependency**

Run:

```bash
grep -rln "using Hangfire" backend/src/Anela.Heblo.Application/ | sort
```

Expected (post-refactor, **6 files** — helper removed, six pre-existing consumers retained):

```
backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs
backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs
backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs
backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs
backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs
```

Diff against the Task 1 Step 4 snapshot: exactly **one** entry removed — `HangfireJobRegistrationHelper.cs`. No other Application files affected.

- [ ] **Step 3: Confirm `Anela.Heblo.Application.csproj` still references `Hangfire.Core` (intentional per Amendment A)**

Run:

```bash
grep -n "Hangfire.Core" backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: one line `<PackageReference Include="Hangfire.Core" Version="1.8.21" />`. **Do not remove.** The six retained consumers above still need it. Removal is tracked separately (Amendment B / spec Out of Scope).

- [ ] **Step 4: Full-solution build**

Run:

```bash
dotnet build backend/Anela.Heblo.sln -nologo -v minimal
```

Expected: `Build succeeded` with `0 Error(s)` and no new warnings versus baseline (NFR-2).

- [ ] **Step 5: Run the full test suite**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --nologo
```

Expected: all tests pass. In particular, both Hangfire-collection test classes (`HangfireJobRegistrationHelperTests` at its new path, plus any other `[Collection("Hangfire")]` consumer) green. The shared `HangfireTestFixture` continues to provide the in-memory `JobStorage`.

- [ ] **Step 6: Confirm no formatting drift introduced**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes --severity warn
```

Expected: exit code 0. If it reports diffs, run `dotnet format backend/Anela.Heblo.sln` to apply, then re-run `--verify-no-changes` to confirm.

- [ ] **Step 7: Verify rename history is intact (NFR-4 acceptance)**

Run:

```bash
git log --follow --oneline -3 -- backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs
git log --follow --oneline -3 -- backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs
```

Expected: each command lists the pre-rename commit history of the moved file, proving `git log --follow` continues to work.

---

## Task 6: Commit

Single atomic commit — the refactor is meaningful only as a complete unit. Intermediate states (after Task 2, before Task 3) do not build, so partial commits are inappropriate.

**Files:** none modified in this task (only staging + commit).

- [ ] **Step 1: Review the diff one last time**

Run:

```bash
git status
git diff --stat HEAD
```

Expected `git status` shows exactly:
- `renamed:` for the helper source file (Application → API path).
- `renamed:` for the test file (Features/BackgroundJobs → Infrastructure/Hangfire path).
- `modified:` for `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` (one line removed).
- The two renamed files also show small content changes (namespace lines only).

No other files in the diff. If anything else appears, investigate before committing.

- [ ] **Step 2: Stage the four affected files explicitly**

Run:

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs \
  backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs \
  backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs \
  backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs \
  backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireJobRegistrationHelperTests.cs
```

(Both the old and new paths for the renamed files are listed so `git add` records the rename correctly even if `git mv` was simulated by a manual move + delete in some environments.)

Expected: no output.

- [ ] **Step 3: Commit with a conventional-commit message**

Run:

```bash
git commit -m "$(cat <<'EOF'
refactor: Move HangfireJobRegistrationHelper from Application to API layer

Relocates HangfireJobRegistrationHelper.cs from
Anela.Heblo.Application/Features/BackgroundJobs/Services/ to
Anela.Heblo.API/Infrastructure/Hangfire/ so the Application layer no
longer takes a compile-time dependency on the Hangfire static API
through this helper. Public surface, method signatures, reflection
dispatch, and runtime behavior are byte-for-byte unchanged.

- Source helper moved with git mv; namespace updated to
  Anela.Heblo.API.Infrastructure.Hangfire (matches sibling adapters).
- Tests moved to backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/
  mirroring the new production path; using directive updated.
- RecurringJobDiscoveryService.cs: stale using directive removed.
- HangfireRecurringJobScheduler.cs: using retained — that namespace
  still provides IHangfireRecurringJobScheduler.

Hangfire.Core PackageReference on Anela.Heblo.Application.csproj is
retained because FailedJobsTile, DashboardModule (JobStorage.Current),
GenerateArticleJob, GenerateArticleHandler, ProductExportDownloadJob,
and PlaudPollingJob still depend on it. Removing those references is
tracked separately (spec Out of Scope; arch-review Amendment B).
EOF
)"
```

Expected: commit succeeds; no pre-commit hook errors. If a hook reports format/lint issues, fix and create a **new** commit (do not `--amend`).

- [ ] **Step 4: Final post-commit sanity check**

Run:

```bash
git log --oneline -1
git log --follow --oneline -3 -- backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs
```

Expected:
- Top commit is the refactor commit.
- `git log --follow` still surfaces the original creation commit of the helper.

---

## Spec Coverage Self-Review

| Spec / Arch-Review item | Task(s) addressing it |
|---|---|
| FR-1 (relocate helper file, preserve surface, namespace = `Anela.Heblo.API.Infrastructure.Hangfire`) | Task 2 Steps 2–3 |
| FR-2 amended (helper-specific Hangfire dependency removed from Application; `Hangfire.Core` package retained for other callers) | Task 5 Steps 2–3 |
| FR-3 (all call sites compile against new namespace; no `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` referring to the helper remains) | Task 4 (`RecurringJobDiscoveryService` cleanup) + Task 5 Step 1 (repo-wide audit). Note: `HangfireRecurringJobScheduler.cs` retains the using legitimately for `IHangfireRecurringJobScheduler`. |
| FR-4 amended (tests relocated under `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/`, namespace and using updated) | Task 3 |
| FR-5 (runtime behavior preserved) | Task 3 Step 7 (reflection-path tests green); Task 5 Step 5 (full suite green) |
| NFR-1 (Clean Architecture boundary restored for the helper) | Task 5 Step 2 |
| NFR-2 (build, format, tests succeed; no new warnings) | Task 5 Steps 4–6 |
| NFR-3 (no backwards-compat shim needed) | N/A — confirmed in Task 5 Step 1 (no external consumers found) |
| NFR-4 (history preserved via `git mv`) | Task 2 Steps 2 & 4; Task 3 Step 3; Task 5 Step 7 |
| Arch-review Decision 1 (namespace `Anela.Heblo.API.Infrastructure.Hangfire`) | Task 2 Step 3 |
| Arch-review Decision 2 (keep `public static`, no accessibility change) | Task 2 Step 3 — only namespace line changed; class declaration untouched |
| Arch-review Decision 3 (mirror test folder structure) | Task 3 Steps 3 & 5 |
| Arch-review Risk: reflection path | Task 3 Step 7 explicitly verifies both reflection-exercising tests |
| Arch-review Risk: hidden stale reference | Task 5 Step 1 |
| Arch-review Risk: `git mv` not used | Task 2 Step 4 + Task 5 Step 7 verify `git log --follow` |
| Arch-review Risk: PR reviewer expects `Hangfire.Core` removed | Commit message in Task 6 Step 3 explicitly documents the retention and points to the follow-up. |

No spec / arch-review item is left without a covering task.
