# Relocate BackgroundRefresh DTOs to Application Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move three DTOs (`RefreshTaskDto`, `RefreshTaskStatusDto`, `RefreshTaskExecutionLogDto`) from `Anela.Heblo.API/Controllers/` into `Anela.Heblo.Application/Features/BackgroundJobs/Contracts/` so the API project stops owning DTOs (per `docs/architecture/development_guidelines.md`).

**Architecture:** Pure relocation. Use `git mv` to preserve history, update the namespace to `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` (matching existing siblings like `RecurringJobDto`), and add one `using` directive to `BackgroundRefreshController`. No DTO shape changes; HTTP wire contract and generated TS client must remain functionally identical.

**Tech Stack:** .NET 8 / C# (xUnit tests), NSwag/OpenAPI generation, TypeScript client output under `frontend/src/api/generated/`.

---

## File Structure

**Files moved (3):**
- `backend/src/Anela.Heblo.API/Controllers/RefreshTaskDto.cs` → `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs`
- `backend/src/Anela.Heblo.API/Controllers/RefreshTaskStatusDto.cs` → `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs`
- `backend/src/Anela.Heblo.API/Controllers/RefreshTaskExecutionLogDto.cs` → `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs`

**Files modified (1):**
- `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs` — add one `using` directive (alphabetically ordered with existing imports).

**Files expected to potentially regenerate (1) — likely zero meaningful diff:**
- `frontend/src/api/generated/api-client.ts` — NSwag default `schemaNameGenerator` uses the CLR type name (not namespace); regeneration is precautionary.

**No new files. No tests added** (relocation only; no behavioral change).

---

## Task 1: Pre-flight verification — capture current state

**Files:**
- Read only.

- [ ] **Step 1: Confirm the three source files exist with current namespace**

Run:
```bash
grep -n '^namespace ' backend/src/Anela.Heblo.API/Controllers/RefreshTaskDto.cs \
                     backend/src/Anela.Heblo.API/Controllers/RefreshTaskStatusDto.cs \
                     backend/src/Anela.Heblo.API/Controllers/RefreshTaskExecutionLogDto.cs
```

Expected output (exactly):
```
backend/src/Anela.Heblo.API/Controllers/RefreshTaskDto.cs:1:namespace Anela.Heblo.API.Controllers;
backend/src/Anela.Heblo.API/Controllers/RefreshTaskStatusDto.cs:1:namespace Anela.Heblo.API.Controllers;
backend/src/Anela.Heblo.API/Controllers/RefreshTaskExecutionLogDto.cs:1:namespace Anela.Heblo.API.Controllers;
```

If output differs, stop and re-read the spec / current files before proceeding.

- [ ] **Step 2: Confirm target folder exists with sibling DTOs**

Run:
```bash
ls backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/
```

Expected output (order may vary):
```
RecurringJobDto.cs
UpdateJobCronRequestBody.cs
UpdateJobStatusRequestBody.cs
```

If the folder does not exist, stop and check `docs/architecture/filesystem.md` — but per the architecture review this folder is already present.

- [ ] **Step 3: Confirm target namespace from a sibling**

Run:
```bash
grep -n '^namespace ' backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs
```

Expected output:
```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs:1:namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
```

This is the **target namespace** used in Tasks 2–4.

- [ ] **Step 4: Confirm baseline backend build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln -nologo -v quiet
```

Expected: `Build succeeded.` with `0 Error(s)`.

If the build is broken on the current branch, stop — fix the existing failure before relocating files.

- [ ] **Step 5: Capture baseline TS client checksum**

Run:
```bash
shasum frontend/src/api/generated/api-client.ts > /tmp/api-client.before.sha
cat /tmp/api-client.before.sha
```

Expected: a single SHA-1 line. Keep `/tmp/api-client.before.sha` — Task 8 compares against it.

---

## Task 2: Move `RefreshTaskDto.cs` and update namespace

**Files:**
- Move: `backend/src/Anela.Heblo.API/Controllers/RefreshTaskDto.cs` → `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs`

- [ ] **Step 1: Move the file with `git mv` (preserves rename detection)**

Run:
```bash
git mv \
  backend/src/Anela.Heblo.API/Controllers/RefreshTaskDto.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs
```

Expected: no stdout output, exit code 0.

- [ ] **Step 2: Update the namespace declaration**

Edit `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs`.

Replace:
```csharp
namespace Anela.Heblo.API.Controllers;
```

With:
```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
```

The remainder of the file is unchanged. Full expected final content:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

public class RefreshTaskDto
{
    public required string TaskId { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public required TimeSpan RefreshInterval { get; init; }
    public required bool Enabled { get; init; }
    public int HydrationTier { get; init; }
    public DateTime? NextScheduledRun { get; init; }
    public RefreshTaskExecutionLogDto? LastExecution { get; init; }
}
```

- [ ] **Step 3: Verify git still sees this as a rename (not delete + add)**

Run:
```bash
git status --short
```

Expected line for this file (the `R` prefix indicates a detected rename):
```
R  backend/src/Anela.Heblo.API/Controllers/RefreshTaskDto.cs -> backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs
```

If git reports `D ... A ...` instead, the edit was too large for similarity detection — revert (`git restore --staged --worktree .`) and retry by editing only the one namespace line.

---

## Task 3: Move `RefreshTaskStatusDto.cs` and update namespace

**Files:**
- Move: `backend/src/Anela.Heblo.API/Controllers/RefreshTaskStatusDto.cs` → `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs`

- [ ] **Step 1: Move the file with `git mv`**

Run:
```bash
git mv \
  backend/src/Anela.Heblo.API/Controllers/RefreshTaskStatusDto.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs
```

Expected: no stdout output, exit code 0.

- [ ] **Step 2: Update the namespace declaration**

Edit `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs`.

Replace:
```csharp
namespace Anela.Heblo.API.Controllers;
```

With:
```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
```

Full expected final content:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

public class RefreshTaskStatusDto
{
    public required string TaskId { get; init; }
    public required bool Enabled { get; init; }
    public string? Description { get; init; }
    public required TimeSpan RefreshInterval { get; init; }
    public RefreshTaskExecutionLogDto? LastExecution { get; init; }
}
```

- [ ] **Step 3: Verify git rename detection**

Run:
```bash
git status --short | grep RefreshTaskStatusDto
```

Expected:
```
R  backend/src/Anela.Heblo.API/Controllers/RefreshTaskStatusDto.cs -> backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs
```

---

## Task 4: Move `RefreshTaskExecutionLogDto.cs` and update namespace

**Files:**
- Move: `backend/src/Anela.Heblo.API/Controllers/RefreshTaskExecutionLogDto.cs` → `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs`

- [ ] **Step 1: Move the file with `git mv`**

Run:
```bash
git mv \
  backend/src/Anela.Heblo.API/Controllers/RefreshTaskExecutionLogDto.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs
```

Expected: no stdout output, exit code 0.

- [ ] **Step 2: Update the namespace declaration**

Edit `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs`.

Replace:
```csharp
namespace Anela.Heblo.API.Controllers;
```

With:
```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
```

Full expected final content:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

public class RefreshTaskExecutionLogDto
{
    public required string TaskId { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public required string Status { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan? Duration { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
```

- [ ] **Step 3: Verify git rename detection**

Run:
```bash
git status --short | grep RefreshTaskExecutionLogDto
```

Expected:
```
R  backend/src/Anela.Heblo.API/Controllers/RefreshTaskExecutionLogDto.cs -> backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs
```

---

## Task 5: Update `BackgroundRefreshController` to import the new namespace

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs` (lines 1–5).

After Task 4 the project no longer compiles — `BackgroundRefreshController` references three symbols whose namespace just changed. This task fixes that.

- [ ] **Step 1: Verify the build is currently broken (proof the change is necessary)**

Run:
```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -nologo -v quiet 2>&1 | grep -E "error CS|RefreshTask"
```

Expected: errors referencing `RefreshTaskDto`, `RefreshTaskStatusDto`, and/or `RefreshTaskExecutionLogDto` (CS0246 "type or namespace … could not be found" or similar). This confirms the controller is the only consumer needing an `using` update.

- [ ] **Step 2: Add the new `using` directive (alphabetically ordered)**

Edit `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs`.

Replace this block at the top of the file:
```csharp
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
```

With:
```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
```

No other line in the file changes. The namespace `Anela.Heblo.API.Controllers` on line 5 (controller's own namespace) stays as-is — it is the controller's namespace, not a reference to a moved DTO.

- [ ] **Step 3: Verify the build now succeeds**

Run:
```bash
dotnet build backend/Anela.Heblo.sln -nologo -v quiet
```

Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`.

If new warnings appear, investigate before continuing — NFR-1 requires zero new warnings.

---

## Task 6: Apply scoped `dotnet format` and verify zero changes

**Files:**
- The four touched files only.

Per the architecture review, run `dotnet format` scoped to the four touched files (avoid rewriting unrelated files).

- [ ] **Step 1: Run `dotnet format` scoped to the touched files**

Run:
```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs \
            backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs \
            backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs \
            backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs
```

Expected: no stdout indicating fixes applied (or only trivial whitespace normalization). Exit code 0.

- [ ] **Step 2: Verify formatter would now make no changes**

Run:
```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs \
            backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs \
            backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs \
            backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs \
  --verify-no-changes
```

Expected: exit code 0, no output. If non-zero, re-run Step 1 and recheck.

---

## Task 7: Run backend tests

**Files:**
- All test projects under `backend/test/` (none expected to require modification per arch review — repo-wide grep showed no test referencing these three symbols).

- [ ] **Step 1: Run full backend test suite**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --no-build -nologo -v quiet
```

Expected: all tests pass (`Passed!` summary, `Failed: 0`).

If a test fails with a missing-namespace error on `Anela.Heblo.API.Controllers.RefreshTask*`, that test is a hidden consumer the arch review missed. Update its `using` to `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` and re-run. (Spec NFR-2 explicitly permits this kind of mechanical `using` update.)

If any test fails for another reason, stop — that is unexpected for a pure namespace move and must be investigated.

---

## Task 8: Regenerate frontend TypeScript client and diff

**Files:**
- `frontend/src/api/generated/api-client.ts` (potentially regenerated; expected: no meaningful change).

Per the architecture review, NSwag's default `schemaNameGenerator` is type-name-based — the generated TS client should be unchanged. We still regenerate and diff per the precedent set by `2026-05-27-relocate-backgroundjobs-request-body-dtos.md` and per spec FR-5.

- [ ] **Step 1: Regenerate the TypeScript API client**

Per `docs/development/api-client-generation.md`, the canonical command is the frontend build (which invokes the OpenAPI codegen task) or an explicit codegen target. Run the project's documented regeneration command. If unsure, use:

```bash
cd frontend && npm run build && cd ..
```

This regenerates `frontend/src/api/generated/api-client.ts` as part of the build pipeline and also fulfills the FE build check in Task 9.

Expected: build succeeds with no TypeScript errors.

If the project documents a more specific regen-only command in `docs/development/api-client-generation.md`, prefer that — but the build path above is sufficient.

- [ ] **Step 2: Compute new checksum and diff**

Run:
```bash
shasum frontend/src/api/generated/api-client.ts > /tmp/api-client.after.sha
diff /tmp/api-client.before.sha /tmp/api-client.after.sha || true
```

Two possible outcomes:

**Outcome A — checksums match (expected):**
Output of `diff` is empty. The generated client is byte-identical. No frontend file is staged in this change. Move on to Task 9.

**Outcome B — checksums differ:**
Output of `diff` shows the two SHAs. Run:
```bash
git diff -- frontend/src/api/generated/api-client.ts | head -200
```

Inspect the diff:
- If it consists only of `$ref` path updates, schema component reordering, or whitespace differences with no behavioral change to method signatures or class shapes, the regenerated file may be committed.
- If method signatures, exported class shapes, or DTO field types change, **stop**: this contradicts the architecture review's assumption and indicates the move altered the public surface. Investigate before continuing — the spec mandates no contract change (FR-4, NFR-3).

- [ ] **Step 3: Stage the regenerated client only if it changed**

If Outcome A: do nothing.

If Outcome B (and the diff is benign): run
```bash
git add frontend/src/api/generated/api-client.ts
```

---

## Task 9: Frontend build and lint

**Files:**
- Read only (build / lint validation).

Frontend build was already executed in Task 8 Step 1 (it triggers codegen). This task verifies lint and confirms no other frontend file changed.

- [ ] **Step 1: Run frontend lint**

Run:
```bash
cd frontend && npm run lint && cd ..
```

Expected: exit code 0, no lint errors.

- [ ] **Step 2: Confirm no unintended frontend file changes**

Run:
```bash
git status --short frontend/
```

Expected:
- If TS client was unchanged (Outcome A in Task 8): no output (clean working tree under `frontend/`).
- If TS client was regenerated (Outcome B): only `frontend/src/api/generated/api-client.ts` appears, staged from Task 8 Step 3.

Any other modified file under `frontend/` is unexpected and must be investigated — the spec scopes the change strictly to BE relocation plus (optional) regenerated client.

---

## Task 10: Final verification — repo-wide grep for stale references

**Files:**
- Read only.

Per the architecture review's specification amendment, anchor the grep to the three symbol names (not the broader `Anela.Heblo.API.Controllers` namespace, which has 77 unrelated matches).

- [ ] **Step 1: Grep for stale fully-qualified references to the moved DTOs**

Run:
```bash
git grep -nE 'Anela\.Heblo\.API\.Controllers\.RefreshTask(Dto|StatusDto|ExecutionLogDto)' \
  -- '*.cs' '*.ts' '*.tsx' '*.json'
```

Expected: no output (exit code 1 from `grep` when no matches is normal here — `git grep` exits non-zero with no matches).

If any hit appears, update that file's `using` / import to `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` (C#) or the appropriate path (TS — but TS imports DTOs from the unqualified generated module, so no TS hit is expected).

- [ ] **Step 2: Confirm no `*Dto.cs` files remain under the API project**

Run:
```bash
find backend/src/Anela.Heblo.API -name '*Dto.cs' -not -path '*/bin/*' -not -path '*/obj/*'
```

Expected: no output. (Per spec NFR-4, the API project must define zero DTOs after the change.)

If hits appear, those are other DTOs that violate the same architectural rule — **do not** address them in this plan (out of scope per spec). Note them for a follow-up brief and continue.

- [ ] **Step 3: Confirm the three moved files declare the new namespace**

Run:
```bash
grep -n '^namespace ' \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs
```

Expected (exactly):
```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs:1:namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs:1:namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs:1:namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
```

- [ ] **Step 4: Confirm `git log --follow` works on each moved file (history preserved)**

Run:
```bash
git log --follow --oneline -n 3 \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs | head -3
```

Expected: at least one commit shown from before the rename. Repeat for the other two files if desired.

If `git log --follow` shows only the impending move commit (after Task 11), rename detection failed — review Task 2/3/4 git status output and reset.

---

## Task 11: Commit the change

**Files:**
- All staged: three renames, namespace updates, one controller `using` update, optional regenerated TS client.

- [ ] **Step 1: Stage the four touched backend files (renames and modifications)**

Run:
```bash
git add \
  backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs
```

(The deletions of the old paths are already staged by `git mv` in Tasks 2–4.)

If Task 8 Step 3 staged `frontend/src/api/generated/api-client.ts`, it is also already in the index — leave it.

- [ ] **Step 2: Verify staged diff shows renames + minimal edits**

Run:
```bash
git status --short
git diff --staged --stat
```

Expected `git status --short` (one rename line per DTO, one modify line for the controller):
```
R  backend/src/Anela.Heblo.API/Controllers/RefreshTaskDto.cs -> backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs
R  backend/src/Anela.Heblo.API/Controllers/RefreshTaskStatusDto.cs -> backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs
R  backend/src/Anela.Heblo.API/Controllers/RefreshTaskExecutionLogDto.cs -> backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs
M  backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs
```

(Plus `M  frontend/src/api/generated/api-client.ts` only if Task 8 Outcome B applied.)

If `git status` shows `D` and `A` instead of `R`, rename detection failed — revert (`git restore --staged --worktree .`) and retry Tasks 2–4 in tight edits.

- [ ] **Step 3: Commit with a conventional-commit message**

Run:
```bash
git commit -m "$(cat <<'EOF'
refactor: relocate BackgroundRefresh DTOs to Application contracts

Move RefreshTaskDto, RefreshTaskStatusDto, and RefreshTaskExecutionLogDto
from Anela.Heblo.API/Controllers/ to
Anela.Heblo.Application/Features/BackgroundJobs/Contracts/, restoring
compliance with the rule that the API project never owns DTOs
(docs/architecture/development_guidelines.md). Update
BackgroundRefreshController's using directive accordingly. No behavior,
wire contract, or generated TypeScript client shape changes.
EOF
)"
```

Expected: commit succeeds, hooks pass.

If a pre-commit hook fails, do **not** `--amend` — fix the underlying issue, re-stage, and create a new commit.

- [ ] **Step 4: Final sanity check**

Run:
```bash
git log --oneline -n 1
git diff HEAD~1 --stat
```

Expected: the new commit at HEAD; `--stat` shows the four backend files (and the TS client only if Outcome B applied).

---

## Self-Review

**1. Spec coverage:**

| Spec requirement | Implemented by |
|---|---|
| FR-1 Relocate DTO source files (3 files, `git mv`) | Tasks 2, 3, 4 (Step 1 each) |
| FR-1 history preserved (`git log --follow`) | Task 10 Step 4 |
| FR-2 Update namespaces | Tasks 2, 3, 4 (Step 2 each); verified Task 10 Step 3 |
| FR-3 Update consumers (`BackgroundRefreshController` + any others) | Task 5 (controller); Task 7 (test consumers — none expected, repaired mechanically if any); Task 10 Step 1 (final grep) |
| FR-4 DTO contract shape preserved | Tasks 2–4 explicit full file content; Task 8 diff check |
| FR-5 Regenerate API clients if needed | Task 8 (regeneration + diff + conditional commit) |
| NFR-1 Build & format | Task 5 Step 3 (build, zero warnings); Task 6 (scoped format) |
| NFR-2 Tests pass without changes beyond `using` updates | Task 7 |
| NFR-3 HTTP wire contract unchanged | Tasks 2–4 verbatim DTO content; Task 8 TS diff confirms no shape change |
| NFR-4 API project defines zero DTOs after change | Task 10 Step 2 |

All spec requirements have a corresponding task step.

**2. Placeholder scan:** No "TBD", no "implement later", no "add error handling", no "similar to Task N". Every code block contains the actual content.

**3. Type consistency:** Target namespace `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` is identical across Tasks 2, 3, 4, 5, 10. DTO names (`RefreshTaskDto`, `RefreshTaskStatusDto`, `RefreshTaskExecutionLogDto`) are spelled consistently. File paths are identical everywhere they appear.

**4. Architecture review amendments applied:**
- Grep anchored to the three symbol names (Task 10 Step 1) — not the broader namespace.
- `dotnet format` scoped to the four touched files (Task 6).
- TS client regeneration expected to be a no-op; commit only if diff is benign (Task 8).

Plan complete.
