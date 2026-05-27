# Relocate BackgroundJobs Request Body DTOs to Application Layer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `UpdateJobStatusRequestBody` and `UpdateJobCronRequestBody` from `RecurringJobsController.cs` into the `BackgroundJobs` application module's `Contracts/` folder, with zero behavioural or wire-format change.

**Architecture:** Mechanical relocation. Two new files are created in `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/`, the two in-controller class definitions are deleted, and the now-orphan `using System.ComponentModel.DataAnnotations;` directive at the top of the controller is removed. The controller already imports the target `Contracts` namespace, so post-move type resolution is automatic.

**Tech Stack:** .NET 8, C#, ASP.NET Core MVC, xUnit + FluentAssertions + Moq, NSwag (TypeScript client regeneration on build).

---

## Background & Why TDD-Lite Here

The two DTOs are pure data shapes with no behaviour. The behaviour they participate in (HTTP request binding, controller dispatch, MediatR mapping, validation) is already covered by `RecurringJobsControllerTests.cs`. Those existing tests are the regression net for this move — they must continue to pass **without any edit**. There is no new code path to drive via a new RED→GREEN test; the discipline here is to make small, verifiable changes and run the existing test suite + build + OpenAPI diff at each gate.

---

## File Structure

**Files to create (2):**

| Path | Responsibility |
|------|----------------|
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs` | Request body DTO for `PUT /api/RecurringJobs/{jobName}/status` |
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs` | Request body DTO for `PUT /api/RecurringJobs/{jobName}/cron` |

**Files to modify (1):**

| Path | Change |
|------|--------|
| `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` | Delete lines 125–146 (both DTO definitions + their XML doc comments). Delete line 1 (`using System.ComponentModel.DataAnnotations;`) — it is orphan after the move. |

**Files explicitly NOT touched:**

| Path | Reason |
|------|--------|
| `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTests.cs` | Already imports `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` at line 2. The four unqualified references (`UpdateJobStatusRequestBody` on lines 115, 153, 183, 221) resolve through that using directive post-move. |
| `frontend/src/api/hooks/useRecurringJobs.ts` | Consumes generated client types by name. NSwag schema names are namespace-independent. |
| `frontend/src/api/generated/api-client.ts` | Auto-regenerated on `npm run build`. We **verify** it does not change (it's a checkpoint, not an edit target). |
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs` | Sibling DTO; out of scope. |
| Any file under `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/` | MediatR Request/Response/Handler triples are unchanged. |

---

## Task 1: Create `UpdateJobStatusRequestBody.cs` in Contracts folder

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs`

- [ ] **Step 1: Verify the target folder exists and has the sibling DTO**

Run:
```bash
ls backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/
```
Expected output includes: `RecurringJobDto.cs`. If the folder doesn't exist, stop — the spec assumes it does.

- [ ] **Step 2: Create the new DTO file**

Create `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs` with this exact content (XML doc comments preserved verbatim from the source on lines 125–134 of `RecurringJobsController.cs`):

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

/// <summary>
/// Request body for updating recurring job status
/// </summary>
public class UpdateJobStatusRequestBody
{
    /// <summary>
    /// Whether the job should be enabled
    /// </summary>
    public bool IsEnabled { get; set; }
}
```

**Critical invariants:**
- Type name: `UpdateJobStatusRequestBody` — exact.
- Property name: `IsEnabled` — exact.
- `public class` — not `record`, not `sealed`, no `init`/`required` keywords. (Project rule: DTOs are classes, never records, because NSwag mishandles record parameter order.)
- No constructor declared.
- File ends with newline.

- [ ] **Step 3: Verify build still succeeds (duplicate type expected)**

At this point both the new file AND the in-controller definition exist, so we will see a CS0101 duplicate-type compile error. That is the expected fail state — confirm it.

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: BUILD FAILED with error `CS0101: The namespace ... already contains a definition for 'UpdateJobStatusRequestBody'` **OR** `CS0436` (type conflicts between namespaces).

If the build succeeds, the new file content is wrong — re-read Step 2. If it fails with any other error, stop and investigate before continuing.

Note: We do not commit yet — the duplicate is resolved in Task 3.

---

## Task 2: Create `UpdateJobCronRequestBody.cs` in Contracts folder

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs`

- [ ] **Step 1: Create the new DTO file**

Create `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs` with this exact content (XML doc comments preserved verbatim from lines 136–146 of `RecurringJobsController.cs`):

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

/// <summary>
/// Request body for updating recurring job CRON expression
/// </summary>
public class UpdateJobCronRequestBody
{
    /// <summary>
    /// The new CRON expression (e.g. "0 3 * * *")
    /// </summary>
    [Required]
    public string CronExpression { get; set; } = string.Empty;
}
```

**Critical invariants:**
- Type name: `UpdateJobCronRequestBody` — exact.
- Property name: `CronExpression` — exact.
- `[Required]` attribute — preserved (uses `System.ComponentModel.DataAnnotations`).
- Default value `= string.Empty` — preserved.
- `public class` — not `record`, not `sealed`, no `init`/`required` keywords.
- The `using System.ComponentModel.DataAnnotations;` directive must be at the top of the new file (not in the controller anymore — the controller's copy is removed in Task 3).
- File ends with newline.

- [ ] **Step 2: Verify build still fails with duplicate-type errors for both DTOs**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: BUILD FAILED with `CS0101`/`CS0436` errors mentioning **both** `UpdateJobStatusRequestBody` and `UpdateJobCronRequestBody`. The Application project itself should compile cleanly; the error originates from the API project, which references Application and now sees the new types alongside the in-controller copies.

If only one duplicate error appears, the second file is missing a type or in the wrong namespace — re-read Step 1.

No commit yet — Task 3 removes the duplicates.

---

## Task 3: Delete in-controller DTOs and orphan `using` directive

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs`

- [ ] **Step 1: Confirm `System.ComponentModel.DataAnnotations` has no other consumer in the file**

Run:
```bash
grep -nE '\[Required\]|\[Range\]|\[StringLength\]|\[MaxLength\]|\[MinLength\]|\[RegularExpression\]|\[EmailAddress\]|\[Phone\]|\[Url\]|\[Compare\]|\[CreditCard\]|\[DataType\]|\[EnumDataType\]|\[FileExtensions\]|\[MaxLength\]|\[MinLength\]|\[ValidationAttribute\]' backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs
```
Expected: only the line `[Required]` inside the `UpdateJobCronRequestBody` class (line 144). After we delete that class, no usage of `System.ComponentModel.DataAnnotations` remains.

If the grep shows additional attribute usage outside the DTO classes, STOP — do not remove the `using` in Step 2. Update the plan to keep the using and document why.

- [ ] **Step 2: Delete the two in-controller DTO definitions (lines 125–146 of the original file)**

Open `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` and delete exactly these blocks:

The first block to delete (the original lines 125–134):
```csharp
/// <summary>
/// Request body for updating recurring job status
/// </summary>
public class UpdateJobStatusRequestBody
{
    /// <summary>
    /// Whether the job should be enabled
    /// </summary>
    public bool IsEnabled { get; set; }
}
```

The second block to delete (the original lines 136–146):
```csharp
/// <summary>
/// Request body for updating recurring job CRON expression
/// </summary>
public class UpdateJobCronRequestBody
{
    /// <summary>
    /// The new CRON expression (e.g. "0 3 * * *")
    /// </summary>
    [Required]
    public string CronExpression { get; set; } = string.Empty;
}
```

Also remove the blank line between them and the trailing newline that separates them from the closing brace of the `RecurringJobsController` class. The file must end at line 123 (the closing `}` of the controller class) followed by a single trailing newline.

- [ ] **Step 3: Delete the orphan `using System.ComponentModel.DataAnnotations;` directive (line 1)**

Remove the first line of the file:
```csharp
using System.ComponentModel.DataAnnotations;
```

The remaining `using` directives must keep their existing order:
```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJobsList;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobStatus;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;
```

Do not reorder them. Do not add or remove other usings. The `using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;` line MUST remain — it is now the resolution path for both relocated DTOs.

- [ ] **Step 4: Verify the controller file shape matches expectation**

Run:
```bash
wc -l backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs
```
Expected: 122 lines (original 146 minus the 22 deleted DTO lines minus the 1 deleted using minus 1 blank-line gap that separated the DTOs).

Run:
```bash
grep -nE 'UpdateJobStatusRequestBody|UpdateJobCronRequestBody|System\.ComponentModel\.DataAnnotations' backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs
```
Expected: exactly two matches, both inside `[FromBody]` parameter declarations — no class definitions, no `using` directive. Specifically:
- One occurrence of `[FromBody] UpdateJobStatusRequestBody request` (inside `UpdateJobStatus` method).
- One occurrence of `[FromBody] UpdateJobCronRequestBody request` (inside `UpdateJobCron` method).
- Zero occurrences of `System.ComponentModel.DataAnnotations`.

If the grep shows class definitions or the using directive, Step 2 or Step 3 was incomplete.

- [ ] **Step 5: Build the solution — must now compile cleanly**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

If errors persist:
- `CS0101` on either DTO type → an in-controller definition was not fully deleted (re-do Step 2).
- `CS0246` "could not be found" on either DTO type → the `using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;` was accidentally removed (restore it).
- `CS0246` on `Required` → the `[Required]` attribute is still in some file that no longer imports `System.ComponentModel.DataAnnotations` (verify Task 2's new file kept its `using`).

---

## Task 4: Verify behaviour parity (tests + format + OpenAPI diff)

**Files:** No source changes. This task is verification-only and produces the artifacts we commit in Task 5.

- [ ] **Step 1: Run the BackgroundJobs controller tests (existing, unmodified)**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs.RecurringJobsControllerTests" --no-build
```
Expected: All tests pass (zero failures, zero skipped). These tests reference `UpdateJobStatusRequestBody` unqualified on lines 115, 153, 183, 221 and resolve them via `using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;` (line 2 of the test file).

If any test fails with a type-resolution error, the `Contracts` using in the test file is missing or the new DTO is in the wrong namespace. Investigate before continuing.

- [ ] **Step 2: Run the full backend test suite to confirm no other test is affected**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --no-build
```
Expected: Same pass count as before the change. Any new failure means an unintended consumer (e.g. another test that referenced `Anela.Heblo.API.Controllers.UpdateJobStatusRequestBody` by FQN) — investigate and resolve before continuing. Per the architecture review, the only consumer is `RecurringJobsControllerTests.cs` and it uses unqualified names, so zero new failures is the expected outcome.

- [ ] **Step 3: Run `dotnet format` scoped to the touched files only**

Per NFR-3 (surgical change scope), do not let `dotnet format` rewrite unrelated files. Run:
```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs \
  --include backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs \
  --include backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs \
  --verify-no-changes
```
Expected: `Format complete` with exit code 0 and no files modified.

If exit code is non-zero, run the same command without `--verify-no-changes` to apply the fixes, then re-run with `--verify-no-changes` to confirm clean. Inspect the resulting diff with `git diff` to make sure formatter did not change anything beyond whitespace/newlines on the three target files.

- [ ] **Step 4: Regenerate the OpenAPI TypeScript client and diff it**

The TypeScript client regenerates on frontend build. Run:
```bash
cd frontend && npm run build && cd ..
```
Expected: Build succeeds.

Then check that the generated client did NOT change shape:
```bash
git diff frontend/src/api/generated/api-client.ts
```
Expected: **zero changes**, OR changes that are purely cosmetic (whitespace, comment ordering). Specifically verify these tokens still appear with identical signatures:
- `class UpdateJobStatusRequestBody` and its `isEnabled` property of type `boolean`.
- `class UpdateJobCronRequestBody` and its `cronExpression` property of type `string`.
- `interface IUpdateJobStatusRequestBody` / `interface IUpdateJobCronRequestBody` (NSwag emits both).
- Methods `recurringJobs_UpdateJobStatus` and `recurringJobs_UpdateJobCron` with identical request body type references.

```bash
grep -nE 'class UpdateJobStatusRequestBody|class UpdateJobCronRequestBody|recurringJobs_UpdateJobStatus|recurringJobs_UpdateJobCron' frontend/src/api/generated/api-client.ts
```
Expected: exactly 4 matches (one per token).

If the type names changed (e.g. NSwag prepended a namespace prefix like `BackgroundJobsContractsUpdateJobStatusRequestBody`), this is a **CRITICAL** blocker per the architecture review. Revert and consult the architecture review's Risks table before proceeding — do not attempt to patch the frontend.

- [ ] **Step 5: Run the frontend lint to confirm the unchanged client still compiles in TypeScript**

Run:
```bash
cd frontend && npm run lint && cd ..
```
Expected: zero lint errors. If errors appear in `useRecurringJobs.ts` or files that consume it, the NSwag output changed shape and Step 4 should have caught it — go back and re-verify.

---

## Task 5: Commit the change

**Files:** All three touched files staged together.

- [ ] **Step 1: Stage exactly the three touched files**

Run:
```bash
git add \
  backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs
```

- [ ] **Step 2: Verify the staged diff is surgical (only these three files)**

Run:
```bash
git status
git diff --cached --stat
```
Expected:
- `git status` shows only the three target files as staged, and no other modified files (the generated `api-client.ts` should not be in the diff — Task 4 Step 4 confirmed zero changes).
- `git diff --cached --stat` shows two new files (the DTOs) and one modified file (the controller) with roughly `-24` lines and `+0` lines on the controller (22 DTO lines + 1 using line + 1 blank gap).

If `api-client.ts` shows changes in `git status` despite Task 4 Step 4 reporting none, re-diff and revert it before continuing — committing a regenerated client with no controller-side changes is fine only when the regeneration is truly identical.

- [ ] **Step 3: Commit with a conventional-commit message**

Run:
```bash
git commit -m "$(cat <<'EOF'
refactor: move BackgroundJobs request body DTOs to Application layer

Relocates UpdateJobStatusRequestBody and UpdateJobCronRequestBody from
RecurringJobsController.cs into the BackgroundJobs module's Contracts/
folder. Enforces the rule that the API project does not own DTOs.

Wire format, OpenAPI schema names, and frontend generated client are
unchanged. Existing RecurringJobsControllerTests pass without edits.
EOF
)"
```
Expected: Commit succeeds. Any pre-commit hook (build, format, test) should pass — Task 4 already verified each.

If a hook fails, fix the underlying issue and create a NEW commit (do not amend per the global git policy).

- [ ] **Step 4: Final post-commit sanity check**

Run:
```bash
git log -1 --stat
git status
```
Expected:
- `git log -1 --stat` shows the commit with exactly 3 files changed: one modification, two additions.
- `git status` shows a clean working tree.

---

## Self-Review Checklist

This was checked against the spec before finalising:

**Spec coverage:**
- FR-1 (relocate `UpdateJobStatusRequestBody`) → Task 1 + Task 3 Step 2.
- FR-2 (relocate `UpdateJobCronRequestBody` preserving `[Required]`) → Task 2 + Task 3 Step 2.
- FR-3 (controller cleanup, remove orphan using) → Task 3 Steps 1, 3, 4.
- FR-4 (consumer compatibility — test file unchanged) → Task 4 Step 1 verifies, file is explicitly in the "do not touch" list.
- FR-5 (validation: build, format, test, OpenAPI client identical) → Task 4 Steps 2, 3, 4, 5.
- NFR-1 (behavioural parity) → Task 4 Step 4 OpenAPI diff is the strongest check; Task 4 Step 1 covers controller behaviour.
- NFR-2 (architectural conformance — DTOs are `public class`, live in `Contracts/`, no record conversion) → Task 1 + Task 2 invariants explicitly enforce this.
- NFR-3 (surgical change scope — only three files touched) → Task 4 Step 3 uses `--include` scoping, Task 5 Step 2 verifies the staged diff.

**Placeholder scan:** no TBDs, no "implement later", no "similar to Task N", no abstract handwaving — all code blocks are complete and copy-pasteable.

**Type consistency:** type names (`UpdateJobStatusRequestBody`, `UpdateJobCronRequestBody`), property names (`IsEnabled`, `CronExpression`), namespace (`Anela.Heblo.Application.Features.BackgroundJobs.Contracts`), and the `[Required]` attribute are spelt identically across every task that mentions them. The architecture review's grep tokens for the OpenAPI diff match the names used in the new DTOs.

No gaps. No fixes needed.
