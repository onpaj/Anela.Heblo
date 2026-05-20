# Relocate OrgChartService HTTP Adapter to Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the concrete `OrgChartService` HTTP-client adapter from `Features/OrgChart/Services/` to `Features/OrgChart/Infrastructure/` (with matching namespace update) so the OrgChart module obeys the documented Services-vs-Infrastructure boundary used by every other feature module.

**Architecture:** Pure source-tree refactor of one file plus one using-directive addition. The `IOrgChartService` interface (the abstraction the use-case handler depends on) stays in `Services/`; only the concrete adapter moves into a new `Infrastructure/` folder. No behavior, no API, no DI registration line, and no tests change. Git history is preserved via `git mv`.

**Tech Stack:** .NET 8, C#, MediatR, Microsoft.Extensions.Http, Microsoft.Extensions.Options, xUnit (existing test suite — no new tests).

---

## File Structure

**Modified files (3 total — exactly matches spec FR-5 / arch-review):**

| Action  | Path | Responsibility |
|---------|------|----------------|
| Move (delete) | `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` | Concrete HTTP adapter — old location |
| Move (create) | `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs` | Concrete HTTP adapter — new location (byte-identical body, only namespace differs) |
| Edit | `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` | DI registration — gain one `using` for the new namespace |

**Unchanged files (do NOT touch):**

- `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/IOrgChartService.cs` — interface stays in `Services/` (consumer owns the abstraction).
- `backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs` — already `using ...Services;` for `IOrgChartService`.
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — calls extension method by name, namespace-agnostic.
- `backend/src/Anela.Heblo.Application/Features/OrgChart/Contracts/*` — DTOs untouched.
- `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartOptions.cs` — config class untouched.

---

## Task 1: Move OrgChartService.cs to Infrastructure via `git mv`

**Files:**
- Move: `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` → `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs`

- [ ] **Step 1: Pre-flight — confirm starting state**

Run from the worktree root:

```bash
ls backend/src/Anela.Heblo.Application/Features/OrgChart/
```

Expected output (no `Infrastructure/` yet):

```
Contracts
OrgChartModule.cs
OrgChartOptions.cs
Services
UseCases
```

And confirm the source file exists:

```bash
ls backend/src/Anela.Heblo.Application/Features/OrgChart/Services/
```

Expected output includes both:

```
IOrgChartService.cs
OrgChartService.cs
```

- [ ] **Step 2: Perform the move with `git mv` (auto-creates `Infrastructure/`)**

```bash
git mv \
  backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs \
  backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
```

`git mv` creates the destination directory automatically. No `mkdir` step is needed.

- [ ] **Step 3: Verify the move and that git detected a rename (not a delete+add)**

```bash
git status
```

Expected output contains exactly one `renamed:` line for this file (and nothing else under `Changes to be committed`):

```
On branch feat-arch-review-orgchart-http-adapter-orgcha
Changes to be committed:
  (use "git restore --staged <file>..." to unstage)
	renamed:    backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs -> backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
```

If git shows `deleted:` + `new file:` instead of `renamed:` — **stop**. That means rename detection failed and `git log --follow` will not work. Run `git diff --staged --find-renames=40` to inspect, then redo with `git mv` from a clean state. Default threshold (50%) should detect the rename since contents are byte-identical at this point.

- [ ] **Step 4: Verify directory layout post-move**

```bash
ls backend/src/Anela.Heblo.Application/Features/OrgChart/
```

Expected (now includes `Infrastructure/`):

```
Contracts
Infrastructure
OrgChartModule.cs
OrgChartOptions.cs
Services
UseCases
```

```bash
ls backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/
ls backend/src/Anela.Heblo.Application/Features/OrgChart/Services/
```

Expected:

```
# Infrastructure/
OrgChartService.cs

# Services/
IOrgChartService.cs
```

Do **not** commit yet — the file still has the old namespace and the build is broken.

---

## Task 2: Update the namespace in the moved file

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs` (line 6 only)

- [ ] **Step 1: Inspect line 6 of the moved file**

```bash
sed -n '1,8p' backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
```

Expected output (note line 6 still says `.Services`):

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.OrgChart.Services;

/// <summary>
```

- [ ] **Step 2: Replace the namespace line in-place**

Edit the file so the namespace declaration at line 6 reads:

```csharp
namespace Anela.Heblo.Application.Features.OrgChart.Infrastructure;
```

Replace exactly this one line. The four `using` directives (lines 1–4), the blank line (5), and everything from line 7 onward must remain byte-identical to the original. Do NOT reorder usings, do NOT remove comments, do NOT touch the body of the class. The using `Anela.Heblo.Application.Features.OrgChart.Contracts;` (line 2) must be preserved — the class still depends on `OrgChartResponse`.

- [ ] **Step 3: Verify the diff is exactly one line**

```bash
git diff --staged --no-color backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
```

Note: after `git mv` followed by an edit, the file shows as `renamed:` plus modified content. To see just the content diff vs the original location, use:

```bash
git diff HEAD --no-color -- backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
```

Expected: exactly one removed line and one added line in the diff hunks (ignoring rename header):

```
-namespace Anela.Heblo.Application.Features.OrgChart.Services;
+namespace Anela.Heblo.Application.Features.OrgChart.Infrastructure;
```

If the diff shows more than this one-for-one change, revert the file (`git restore --source=HEAD --staged --worktree <path>` then redo `git mv` and re-edit only that one line).

- [ ] **Step 4: Verify there is no second `namespace` declaration**

```bash
grep -n '^namespace ' backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
```

Expected: exactly one line:

```
6:namespace Anela.Heblo.Application.Features.OrgChart.Infrastructure;
```

If grep finds zero or more than one match, fix the file before continuing.

- [ ] **Step 5: Verify the `Contracts` using directive is still present (spec FR-2 acceptance)**

```bash
grep -n 'using Anela.Heblo.Application.Features.OrgChart.Contracts;' \
  backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
```

Expected: one match on line 2.

```
2:using Anela.Heblo.Application.Features.OrgChart.Contracts;
```

Do **not** build yet — `OrgChartModule.cs` still can't resolve the concrete class.

---

## Task 3: Add the `Infrastructure` using directive to `OrgChartModule.cs`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` (insert one using directive)

- [ ] **Step 1: Read the current state of `OrgChartModule.cs`**

```bash
cat backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs
```

Current top-of-file (lines 1–3):

```csharp
using Anela.Heblo.Application.Features.OrgChart.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

- [ ] **Step 2: Insert the new using directive**

Add `using Anela.Heblo.Application.Features.OrgChart.Infrastructure;` immediately after the existing `using Anela.Heblo.Application.Features.OrgChart.Services;` line. The top of the file must read exactly:

```csharp
using Anela.Heblo.Application.Features.OrgChart.Services;
using Anela.Heblo.Application.Features.OrgChart.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

Note: the `Services` line stays — it is needed for `IOrgChartService` (interface lives in `Services`). The new `Infrastructure` line resolves the concrete `OrgChartService`.

Do **not** touch anything else in the file. In particular, line 21 must remain exactly:

```csharp
services.AddHttpClient<IOrgChartService, OrgChartService>();
```

- [ ] **Step 3: Verify the diff is exactly one added line**

```bash
git diff --no-color backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs
```

Expected hunk:

```
@@ -1,3 +1,4 @@
 using Anela.Heblo.Application.Features.OrgChart.Services;
+using Anela.Heblo.Application.Features.OrgChart.Infrastructure;
 using Microsoft.Extensions.Configuration;
 using Microsoft.Extensions.DependencyInjection;
```

If the diff shows anything more than this single added line, revert (`git checkout -- backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs`) and redo Step 2.

---

## Task 4: Build verification (`dotnet build`)

**Files:** (none directly — verification only)

- [ ] **Step 1: Restore + build the backend solution**

```bash
cd backend && dotnet build
```

Expected: `Build succeeded.` with **0 Error(s)**. Any non-zero error count attributable to OrgChart symbols (e.g. `CS0246: The type or namespace name 'OrgChartService' could not be found`) means the namespace update or the using directive is wrong — recheck Tasks 2 and 3.

Existing pre-refactor warnings unrelated to OrgChart may persist — that is acceptable provided no **new** warnings reference OrgChart. To isolate, search the build output:

```bash
cd backend && dotnet build 2>&1 | grep -iE 'orgchart|OrgChart' || echo "no OrgChart-related build messages"
```

Expected output: `no OrgChart-related build messages`.

- [ ] **Step 2: Return to worktree root for the rest of the verification**

```bash
cd ..
pwd
```

Expected: the worktree root (path ending in `feat-arch-review-orgchart-http-adapter-orgcha`).

---

## Task 5: Format verification (`dotnet format`)

**Files:** (none directly — verification only)

- [ ] **Step 1: Run `dotnet format` in verify-only mode against the moved file**

```bash
cd backend && dotnet format --include \
  src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs \
  src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs \
  --verify-no-changes
```

Expected: exit code 0 with no output, or a message like `Format complete in <duration>. No files formatted.`

If `dotnet format` reports drift (exit code 2), re-run **without** `--verify-no-changes` to apply fixes, then re-stage and re-verify. Common drift is whitespace at end of the inserted `using` line — applying `dotnet format` resolves it.

- [ ] **Step 2: Re-stage any files `dotnet format` touched**

```bash
cd ..
git add -u backend/src/Anela.Heblo.Application/Features/OrgChart/
git status
```

Expected: still only the same `renamed:` + `modified:` files staged — nothing else.

---

## Task 6: Test verification (full backend suite)

**Files:** (none directly — verification only)

- [ ] **Step 1: Run the entire backend test suite**

```bash
cd backend && dotnet test --no-build
```

Expected: `Passed: <N>, Failed: 0, Skipped: <M>`.

The spec confirms **zero existing tests reference `OrgChartService` or `IOrgChartService`** directly, so this run validates that no indirect breakage occurred (DI scanning, ApplicationModule wiring, etc.). If any test fails:

1. Check whether the failure mentions OrgChart symbols.
2. If yes → recheck Tasks 2 and 3 (namespace/using directive).
3. If no → the failure is pre-existing and unrelated; capture the test name and report it but do not let it block this refactor. Cross-check by running the same command on `main` to confirm the baseline.

- [ ] **Step 2: Return to worktree root**

```bash
cd ..
```

---

## Task 7: History continuity check (arch-review amendment #1)

**Files:** (none directly — verification only)

- [ ] **Step 1: Verify `git log --follow` works on the relocated file**

```bash
git log --follow --oneline \
  backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs | head -20
```

Expected: more than one commit, including historical commits from the file's life at `Features/OrgChart/Services/OrgChartService.cs`. If the output contains only one or two recent commits (i.e. only this refactor's commit when it lands), git did not detect the rename — go back to Task 1 Step 3 and diagnose.

- [ ] **Step 2: Spot-check rename detection on the staged change**

```bash
git diff --staged --find-renames=50 --stat
```

Expected: the line for `OrgChartService.cs` shows `100% similarity` rename detection (or `99%` accounting for the one namespace line change):

```
.../Services/OrgChartService.cs => .../Infrastructure/OrgChartService.cs   (99%)
```

If it shows less than ~95%, contents drifted beyond the namespace line — diff against `HEAD` and revert any unintended edits.

---

## Task 8: Stale reference grep (arch-review amendment #2)

**Files:** (none directly — verification only)

- [ ] **Step 1: Grep the entire repository for the old fully-qualified concrete-class reference**

```bash
git grep -nE 'Anela\.Heblo\.Application\.Features\.OrgChart\.Services\.OrgChartService\b' || \
  echo "no stale fully-qualified references"
```

Expected: `no stale fully-qualified references`. The only legitimate hit would be on the moved file itself if the namespace edit was botched — none should remain anywhere.

- [ ] **Step 2: Grep for any remaining references to `OrgChart.Services` and confirm each one points only to `IOrgChartService`**

```bash
git grep -nE 'OrgChart\.Services' -- \
  ':!docs/superpowers/plans/' \
  ':!artifacts/'
```

Expected matches (and nothing else):

- `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/IOrgChartService.cs` — interface itself.
- `backend/src/Anela.Heblo.Application/Features/OrgChart/UseCases/GetOrganizationStructure/GetOrganizationStructureHandler.cs` — `using Anela.Heblo.Application.Features.OrgChart.Services;` to resolve the interface.
- `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` — the same `using` (now alongside the new `Infrastructure` using).

If any other file references `OrgChart.Services` (e.g. a stale doc or comment that points at the old concrete class location), update it. Cross-reference the arch-review's note: there should be zero hits in `docs/` for the old path — verify:

```bash
git grep -nE 'OrgChart/Services/OrgChartService|OrgChart\.Services\.OrgChartService' -- docs/ || \
  echo "no stale doc references"
```

Expected: `no stale doc references`.

- [ ] **Step 3: Confirm `GetOrganizationStructureHandler.cs` and `ApplicationModule.cs` were not modified (spec FR-5)**

```bash
git diff --staged --name-only
```

Expected: exactly these three paths, in any order:

```
backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs
backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs
```

The third path appears because of the rename — that's expected. If any other file appears in `--name-only`, revert it (`git restore --staged <path>` then `git checkout -- <path>`).

---

## Task 9: Final commit

**Files:** all three staged files from prior tasks.

- [ ] **Step 1: Final `git status` sanity check**

```bash
git status
```

Expected exactly:

```
On branch feat-arch-review-orgchart-http-adapter-orgcha
Changes to be committed:
	renamed:    backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs -> backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
	modified:   backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
	modified:   backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs
```

(Some git versions consolidate the `renamed:` + `modified:` lines for the moved file into a single line. Either form is acceptable as long as the rename was detected.)

If you see any `Untracked files:` for the OrgChart folder, or any unstaged modifications, resolve them before committing.

- [ ] **Step 2: Commit**

```bash
git commit -m "refactor(orgchart): relocate HTTP adapter to Infrastructure folder

Move OrgChartService concrete class from Features/OrgChart/Services/
to Features/OrgChart/Infrastructure/ to match the Services-vs-Infrastructure
boundary documented in docs/architecture/filesystem.md and used by every
other feature module. IOrgChartService interface stays in Services/ as
the abstraction the use-case handler depends on. Module gains one using
directive to resolve the relocated concrete class for the existing
AddHttpClient<IOrgChartService, OrgChartService>() registration.

No behavior change, no API change, no test change."
```

- [ ] **Step 3: Verify the commit landed and rename survived**

```bash
git show --stat HEAD
```

Expected: the commit summary lists the rename (with `99%` or `100%` similarity), the modified `OrgChartService.cs` at the new path, and the one-line `OrgChartModule.cs` change. Nothing else.

```bash
git log --follow --oneline \
  backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs | head -5
```

Expected: this commit at the top, followed by historical commits from before the move. If `--follow` produces only this commit, rename detection failed when the commit was created — `git commit --amend` after re-running `git mv` from a clean staging area can recover, but if the commit has already been pushed, prefer leaving it and noting it as a known git artifact.

---

## Acceptance Criteria Mapping (cross-check vs spec FRs)

| Spec FR | Covered by |
|---------|-----------|
| FR-1 (file moved to Infrastructure with `git mv`) | Task 1 Steps 2–4 |
| FR-2 (namespace updated to `...Infrastructure`, `Contracts` using preserved) | Task 2 Steps 2–5 |
| FR-3 (`IOrgChartService` stays in `Services/`) | Task 8 Step 3 (file does not appear in diff) |
| FR-4 (both usings present in `OrgChartModule.cs`, registration line unchanged) | Task 3 Steps 2–3 |
| FR-5 (no other source files modified) | Task 8 Step 3 |
| FR-6 (build green, format clean, behavior unchanged, tests pass) | Tasks 4, 5, 6 |
| NFR-3 (matches convention of other feature modules) | Achieved by FR-1; verified visually in Task 1 Step 4 |
| NFR-4 (git history preserved) | Task 7 Steps 1–2; final check in Task 9 Step 3 |
| Arch-review amendment #1 (`git log --follow` verification) | Task 7 Step 1; reconfirmed Task 9 Step 3 |
| Arch-review amendment #2 (stale reference grep) | Task 8 Steps 1–2 |
| Arch-review amendment #3 (follow-up tracker) | Out of scope per spec — not a code task; raise as a separate ticket post-merge |

---

## Notes for the Executing Engineer

- **Surgical changes only.** The diff for this PR is exactly: one rename, one line changed in the moved file (the namespace), one line added to `OrgChartModule.cs`. If your diff shows anything else (reordered usings, reformatted braces, "while I was in there" cleanups, refactored exception handling), revert and start over. The value of this PR is the byte-identical move — preserving it lets `git blame` and `git log --follow` keep working on every line of `OrgChartService.cs`.

- **Do not refactor logic.** Retry/timeout/Polly/JSON-options improvements were explicitly listed as out-of-scope in the spec. Note them as follow-ups if you spot something, do not mix them in.

- **Do not change the interface location.** Moving `IOrgChartService` into `Infrastructure/` would force the use-case handler to `using ...Infrastructure;`, defeating the boundary. The arch-review's Decision 1 explains why (consumer owns the abstraction / dependency inversion).

- **Do not relocate other features' adapters.** The arch-review identified `MeetingTasks/Services/GraphPlannerService.cs`, `ExpeditionList/Services/FileSystemPrintQueueSink.cs`, etc. as similarly misfiled. Those are out of scope. File separate tickets after merge.

- **Frontend / OpenAPI client / migrations / config.** None of these need changes. There is no contract change, no DB change, no env change.
