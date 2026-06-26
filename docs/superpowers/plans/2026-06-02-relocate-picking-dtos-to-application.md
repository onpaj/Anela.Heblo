# Relocate Picking List Operation DTOs from Domain to Application — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `PrintPickingListRequest`, `PrintPickingListResult`, and `IPickingListSource` from `Anela.Heblo.Domain.Features.Logistics.Picking` to `Anela.Heblo.Application.Features.Logistics.Picking`, restoring Clean Architecture's dependency rule (Application → Domain, not the reverse).

**Architecture:** Pure namespace relocation. Three CLR types and one port interface move outward from the most stable layer (Domain) to the use-case layer (Application). Type kinds, member shapes, default values, and method signatures are preserved verbatim — only the namespace and file locations change. All 11 consumer files (across 4 projects) get a one-line `using` directive flip. No `.csproj`, no DI registration, no migration, no API/UI change.

**Tech Stack:** .NET 8 / C# 12, Clean Architecture (Domain / Application / Adapters), xUnit + Moq + FluentAssertions for tests.

---

## Context for the executing engineer

You have not seen this codebase before. Read these before starting:

- `docs/architecture/development_guidelines.md` — DTO/contract rules, module boundaries.
- `docs/📘 Architecture Documentation – MVP Work.md` — modules, dependency rule.
- `CLAUDE.md` (repo root) — "surgical changes" rule, `dotnet build` + `dotnet format` validation gate.

### Why this matters
The three types in `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` carry use-case concerns (`SendToPrinter`, `ChangeOrderState`, `ExportedFiles`) that have nothing to do with domain invariants. Their presence in Domain inverts the dependency rule. The fix is pure relocation; we change neither behaviour nor public surface.

### Scope (don't get tempted to widen)
- **In scope:** moving the three files, flipping 11 `using` directives, deleting the now-empty Domain folder, format gate.
- **Out of scope:** renaming types, converting `class` to `record`, splitting `PrintPickingListRequest`, moving to `Application/Features/ExpeditionList/Contracts/` (the cleaner long-term home — track as follow-up), editing tests beyond the `using` line.
- **Hard rule from `CLAUDE.md`:** "DTOs are classes, never C# records." Keep them as classes with mutable properties.

### The 11 consumer files (memorise this list)

The arch-review counted these; verified by grep. **Every one of them has exactly one line** `using Anela.Heblo.Domain.Features.Logistics.Picking;` that must flip to `using Anela.Heblo.Application.Features.Logistics.Picking;`.

| # | Project | File | Line |
|---|---|---|---|
| 1 | Anela.Heblo.Application | `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` | 1 |
| 2 | Anela.Heblo.Application | `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IExpeditionListService.cs` | 1 |
| 3 | Anela.Heblo.Application | `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs` | 2 |
| 4 | Anela.Heblo.Application | `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs` | 3 |
| 5 | Anela.Heblo.Adapters.ShoptetApi | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` | 8 |
| 6 | Anela.Heblo.Adapters.ShoptetApi | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` | 16 |
| 7 | Anela.Heblo.Tests | `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs` | 11 |
| 8 | Anela.Heblo.Tests | `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs` | 4 |
| 9 | Anela.Heblo.Tests | `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs` | 4 |
| 10 | Anela.Heblo.Adapters.Shoptet.Tests | `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` | 7 |
| 11 | Anela.Heblo.Adapters.Shoptet.Tests | `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` | 8 |

### Why the TDD red→green dance doesn't fit here

This is a relocation refactor with **no new behaviour**. The "test" that gates correctness is the existing suite: it must continue to pass with only `using`-directive churn. The acceptance signal for each task is **`dotnet build` continues to succeed at the relocation boundary**, and the final task runs the full test suite.

There is exactly one interesting risk: while the three files are being moved (created in Application but not yet deleted from Domain, or vice versa), the build will fail with either "type already defined" or "type not found". We accept that transient breakage *within* Task 2 + Task 3 and only require a green build at the **end** of Task 3. Do not commit a half-moved tree.

### Pre-flight: the file contents you'll be re-creating

These are the **current** Domain files, verbatim. You will re-create them at the Application path with **only** the namespace line and (for `PrintPickingListRequest.cs`) the `Carriers` resolution changed. Everything else must match byte-for-byte aside from those two edits and the trailing newline.

**`backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListRequest.cs`** (23 lines):

```csharp
namespace Anela.Heblo.Domain.Features.Logistics.Picking;

public class PrintPickingListRequest
{
    public const int DefaultSourceStateId = -2; // Vyrizuje se
    //private const string DesiredStateId = "26"; // Bali se
    public const int DefaultDesiredStateId = 26; // Bali se

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public bool ChangeOrderState { get; set; }

    public static IList<Carriers> DefaultCarriers { get; set; } = new List<Carriers>()
    {
        Logistics.Carriers.Zasilkovna,
        Logistics.Carriers.GLS,
        Logistics.Carriers.PPL,
        Logistics.Carriers.Osobak
    };

    public bool SendToPrinter { get; set; }
}
```

Note: `Carriers` is defined in `backend/src/Anela.Heblo.Domain/Features/Logistics/Carriers.cs` under namespace `Anela.Heblo.Domain.Features.Logistics`. In the current Domain location, both `IList<Carriers>` and `Logistics.Carriers.Zasilkovna` resolve implicitly because the file's namespace shares the `Anela.Heblo.Domain.Features.Logistics` prefix. After the move to `Anela.Heblo.Application.Features.Logistics.Picking`, neither resolution works without help. The fix is one `using` directive at the top of the relocated file.

**`backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListResult.cs`** (8 lines):

```csharp
namespace Anela.Heblo.Domain.Features.Logistics.Picking;

public class PrintPickingListResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
    public IList<int> OrderIds { get; set; } = new List<int>();
}
```

No external type references. Namespace flip only.

**`backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/IPickingListSource.cs`** (10 lines, trailing newline):

```csharp
namespace Anela.Heblo.Domain.Features.Logistics.Picking;

public interface IPickingListSource
{
    Task<PrintPickingListResult> CreatePickingList(
        PrintPickingListRequest request,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken = default);
}
```

References `PrintPickingListResult` and `PrintPickingListRequest` from the **same namespace** (currently Domain.Picking, after the move Application.Picking). No external resolution needed; namespace flip is sufficient.

---

## File Structure

### Files created (3)

| Path | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` | Use-case input DTO carrying workflow switches (`SendToPrinter`, `ChangeOrderState`) and carrier filters. |
| `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs` | Use-case output DTO listing exported file paths and affected order IDs. |
| `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs` | Application-side port producing a picking list; one production implementation in `Adapters.ShoptetApi`. |

### Files deleted (3 + the now-empty folder)

| Path | Notes |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListRequest.cs` | Replaced by the Application copy. |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListResult.cs` | Replaced by the Application copy. |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/IPickingListSource.cs` | Replaced by the Application copy. |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` (folder) | Verified empty after the three deletions — confirmed via `Glob` before the plan was written: it contains exactly these three files and nothing else. |

### Files modified (11 — one-line `using` flip each)

See the table in "Context for the executing engineer" above.

### Files explicitly **not** touched
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Carriers.cs` — stays in Domain (it's a real domain enum).
- All `.csproj` files — project references already in place.
- DI registration call sites — type-based, picks up the new namespace via the `using` flip in `ShoptetApiAdapterServiceCollectionExtensions.cs` (file 6).
- Any other files outside the Picking subsystem.

---

## Task 0: Baseline snapshot (no edits)

Capture the "before" state so you can compare cleanly after the move.

**Files:** none modified.

- [ ] **Step 1: Confirm the three Domain files exist and the folder contains nothing else**

Run:
```bash
ls -1 backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/
```
Expected output (exactly these three lines, no others):
```
IPickingListSource.cs
PrintPickingListRequest.cs
PrintPickingListResult.cs
```

If the listing contains additional files, **stop and re-read the spec's FR-5** — the folder must stay if other files exist. (At the time this plan was written, the folder contained exactly these three.)

- [ ] **Step 2: Confirm the 11 consumer references**

Run:
```bash
grep -rln "Anela.Heblo.Domain.Features.Logistics.Picking" backend/src backend/test
```
Expected: exactly 14 hits — the 3 Domain files themselves (namespace declarations) plus the 11 consumer files listed above. If the count differs, list every file and reconcile with the plan's table before proceeding.

- [ ] **Step 3: Confirm Domain does not currently reference Application**

Run:
```bash
grep -rn "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain/ || echo "CLEAN"
```
Expected: `CLEAN` (no matches). This is the dependency-rule invariant NFR-3 protects.

- [ ] **Step 4: Run the full build to establish a green baseline**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: build succeeds with 0 errors. Note the warning count; the final build must not exceed it.

- [ ] **Step 5: Run the full test suite to establish a green baseline**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --nologo
```
Expected: all tests pass. **Write down the total test count** (e.g. "Passed: N"). The final test run must match this number exactly (FR-6).

- [ ] **Step 6: No commit — this task is verification only**

Nothing to stage.

---

## Task 1: Create `PrintPickingListResult.cs` in Application

Start with the result DTO because it has zero external references — easiest to land cleanly, and `IPickingListSource` depends on it.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs`

- [ ] **Step 1: Create the new file**

Path: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs`

Content (exact, including final newline):
```csharp
namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
    public IList<int> OrderIds { get; set; } = new List<int>();
}
```

This is byte-identical to the Domain version except for the namespace on line 1.

- [ ] **Step 2: Verify the file is at the expected path**

Run:
```bash
ls backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs
```
Expected: the path is printed (no "No such file" error).

- [ ] **Step 3: Do not commit yet**

The solution will not build until Task 4 (we'll have two copies of `PrintPickingListResult` with the same simple name in scope from `ExpeditionListService.cs` etc.). Leave staging empty until Task 6.

---

## Task 2: Create `PrintPickingListRequest.cs` in Application

This one needs the `Carriers` resolution fix called out in the arch review's Risks table.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`

- [ ] **Step 1: Create the new file**

Path: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`

Content (exact, including final newline):
```csharp
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListRequest
{
    public const int DefaultSourceStateId = -2; // Vyrizuje se
    //private const string DesiredStateId = "26"; // Bali se
    public const int DefaultDesiredStateId = 26; // Bali se

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public bool ChangeOrderState { get; set; }

    public static IList<Carriers> DefaultCarriers { get; set; } = new List<Carriers>()
    {
        Carriers.Zasilkovna,
        Carriers.GLS,
        Carriers.PPL,
        Carriers.Osobak
    };

    public bool SendToPrinter { get; set; }
}
```

Two changes versus the Domain original:

1. **Added** `using Anela.Heblo.Domain.Features.Logistics;` at the top — required because the new file's namespace (`Anela.Heblo.Application.Features.Logistics.Picking`) no longer has implicit access to the Domain `Carriers` enum.
2. **Changed** the four `Logistics.Carriers.X` references to bare `Carriers.X`. The `Logistics.` prefix worked in the old location because the enclosing namespace was `Anela.Heblo.Domain.Features.Logistics.Picking`, so unqualified `Logistics` resolved up the hierarchy. In the new file it would resolve to `Anela.Heblo.Application.Features.Logistics` — the wrong namespace, which has no `Carriers` member. With the explicit `using` directive above, the bare names resolve unambiguously to the Domain enum.

All other tokens (constants, defaults, property names, accessors, comments, whitespace) are identical to the Domain version. The **public surface** is preserved verbatim, satisfying FR-1.

- [ ] **Step 2: Verify the file is at the expected path**

Run:
```bash
ls backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs
```
Expected: the path is printed.

- [ ] **Step 3: Do not commit yet**

Build is still broken (duplicate types). Continue to Task 3.

---

## Task 3: Create `IPickingListSource.cs` in Application

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs`

- [ ] **Step 1: Create the new file**

Path: `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs`

Content (exact, including final newline):
```csharp
namespace Anela.Heblo.Application.Features.Logistics.Picking;

public interface IPickingListSource
{
    Task<PrintPickingListResult> CreatePickingList(
        PrintPickingListRequest request,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken = default);
}
```

Byte-identical to the Domain original except for the namespace on line 1. `PrintPickingListRequest` and `PrintPickingListResult` resolve through the same namespace.

- [ ] **Step 2: Verify the file is at the expected path**

Run:
```bash
ls backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs
```
Expected: the path is printed.

- [ ] **Step 3: Do not commit yet**

Move to Task 4 to delete the Domain copies.

---

## Task 4: Delete the three Domain files and the empty folder

**Files:**
- Delete: `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListRequest.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListResult.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/IPickingListSource.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` (the directory)

- [ ] **Step 1: Delete the three Domain files via git rm**

Run (one command, three paths):
```bash
git rm backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListRequest.cs \
       backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListResult.cs \
       backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/IPickingListSource.cs
```
Expected: three "rm 'backend/src/...'" lines.

Using `git rm` (instead of plain `rm`) automatically stages the deletion alongside the new files for Task 7's commit.

- [ ] **Step 2: Verify the folder is empty, then delete it**

Run:
```bash
ls -A backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/ 2>&1
```
Expected: empty output (the listing is empty, or `ls` reports "No such file or directory" if git already cleaned it up). If anything is listed (a hidden file, a stray sibling), stop and investigate — FR-5 says only delete the folder when empty.

If non-empty: list each remaining file in the task notes and surface to the reviewer. Do **not** delete unknown content.

If empty and still present, remove it:
```bash
rmdir backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/
```
Expected: no output (success). `rmdir` refuses to delete non-empty directories, which is the safety net we want.

- [ ] **Step 3: Confirm Domain no longer carries Picking concerns**

Run:
```bash
ls backend/src/Anela.Heblo.Domain/Features/Logistics/
```
Expected: only `Carriers.cs` (and possibly other unrelated files that were present before this change). No `Picking/` subdirectory.

- [ ] **Step 4: Do not commit yet**

The solution still won't build — 11 consumer files still import the old namespace. Move to Task 5.

---

## Task 5: Flip `using` directives in the 4 Application consumer files

Each file has exactly one line to change. Use `Edit` on each file individually so reviewers see one-line diffs.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs:1`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IExpeditionListService.cs:1`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs:2`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs:3`

The same `Edit` payload applies to every file in this task — only the path changes:

- `old_string`: `using Anela.Heblo.Domain.Features.Logistics.Picking;`
- `new_string`: `using Anela.Heblo.Application.Features.Logistics.Picking;`

- [ ] **Step 1: Update `ExpeditionListService.cs` (line 1)**

Edit the file with the substitution above. Expected: one line changed.

- [ ] **Step 2: Update `IExpeditionListService.cs` (line 1)**

Edit the file with the substitution above. Expected: one line changed.

- [ ] **Step 3: Update `RunExpeditionListPrintFixHandler.cs` (line 2)**

Edit the file with the substitution above. Expected: one line changed.

- [ ] **Step 4: Update `PrintPickingListJob.cs` (line 3)**

Edit the file with the substitution above. Expected: one line changed.

- [ ] **Step 5: Spot-check the Application project has no stale references**

Run:
```bash
grep -rn "Anela.Heblo.Domain.Features.Logistics.Picking" backend/src/Anela.Heblo.Application/ || echo "CLEAN"
```
Expected: `CLEAN`.

- [ ] **Step 6: Do not commit yet**

Six more consumer files to go in Tasks 6 and 7.

---

## Task 6: Flip `using` directives in the 2 Adapter consumer files

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs:8`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs:16`

Same substitution:

- `old_string`: `using Anela.Heblo.Domain.Features.Logistics.Picking;`
- `new_string`: `using Anela.Heblo.Application.Features.Logistics.Picking;`

- [ ] **Step 1: Update `ShoptetApiExpeditionListSource.cs` (line 8)**

Edit the file with the substitution above. Expected: one line changed.

This is the only production implementation of `IPickingListSource`. After this edit, the implementation references the Application-side interface, which the spec's Decision 4 verifies will work because `Adapters.ShoptetApi.csproj` already references both `Anela.Heblo.Application.csproj` and `Anela.Heblo.Domain.csproj`.

- [ ] **Step 2: Update `ShoptetApiAdapterServiceCollectionExtensions.cs` (line 16)**

Edit the file with the substitution above. Expected: one line changed.

The DI registration here is type-based (`AddScoped<IPickingListSource, ShoptetApiExpeditionListSource>()`); flipping the `using` re-resolves the interface symbol to the new namespace with no further changes (Decision 4 in the arch review).

- [ ] **Step 3: Spot-check the Adapter project has no stale references**

Run:
```bash
grep -rn "Anela.Heblo.Domain.Features.Logistics.Picking" backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ || echo "CLEAN"
```
Expected: `CLEAN`.

- [ ] **Step 4: Do not commit yet**

Five test files remain in Task 7.

---

## Task 7: Flip `using` directives in the 5 test consumer files

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs:11`
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs:4`
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs:4`
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs:7`
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs:8`

Same substitution as Tasks 5 and 6:

- `old_string`: `using Anela.Heblo.Domain.Features.Logistics.Picking;`
- `new_string`: `using Anela.Heblo.Application.Features.Logistics.Picking;`

- [ ] **Step 1: Update `ShoptetApiExpeditionListSourceTests.cs` (line 11)**

Edit. Expected: one line changed.

- [ ] **Step 2: Update `ExpeditionListServicePrintSinkTests.cs` (line 4)**

Edit. Expected: one line changed.

- [ ] **Step 3: Update `ExpeditionListServiceOrderStateTests.cs` (line 4)**

Edit. Expected: one line changed.

- [ ] **Step 4: Update `PickingListIntegrationTests.cs` (line 7)**

Edit. Expected: one line changed.

- [ ] **Step 5: Update `ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` (line 8)**

Edit. Expected: one line changed.

- [ ] **Step 6: Confirm no test file picked up incidental edits**

Run:
```bash
git diff --stat backend/test/
```
Expected: each of the 5 files listed above shows exactly `+1 -1` (one line added, one removed). If any file shows more changes, revert the file and re-do the edit with surgical precision — FR-6 forbids edits beyond `using` directives.

- [ ] **Step 7: Confirm all 11 consumer flips landed**

Run:
```bash
grep -rln "Anela.Heblo.Domain.Features.Logistics.Picking" backend/src backend/test
```
Expected: **no output**. All old-namespace references in the `backend/` tree are gone.

If grep reports any file, fix it before moving on. Acceptable hits exist only in `docs/superpowers/plans/` (this plan file and historical artefacts) — but the command above is scoped to `backend/`, so we don't expect hits there.

---

## Task 8: Final verification gate

This is the green-build, green-test, dependency-rule, formatting checkpoint. All previous tasks build toward this single moment.

**Files:** no edits unless `dotnet format` reports changes (then commit those edits inside this task).

- [ ] **Step 1: Build the solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: 0 errors. Warning count must not exceed the baseline noted in Task 0 / Step 4. If the build fails, the most likely causes are:

| Failure | Likely cause | Fix |
|---|---|---|
| `CS0246: The type or namespace name 'Carriers' could not be found` in the relocated `PrintPickingListRequest.cs` | The `using Anela.Heblo.Domain.Features.Logistics;` line was omitted in Task 2. | Re-add it. |
| `CS0535: ShoptetApiExpeditionListSource does not implement interface member ...` | A consumer in Task 5/6/7 was missed and still binds to the old (deleted) interface symbol. | Re-run the grep from Task 7 / Step 7 and flip the straggler. |
| `CS0234: The type or namespace name 'Picking' does not exist in 'Anela.Heblo.Domain.Features.Logistics'` | A consumer still imports the old namespace. | Same fix. |
| `CS0101: The namespace 'Anela.Heblo...Picking' already contains a definition for ...` | Task 4 didn't delete a Domain file, so duplicates exist. | Delete the leftover Domain file. |

- [ ] **Step 2: Run the full test suite**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --nologo
```
Expected:
- All tests pass.
- The total test count matches the baseline noted in Task 0 / Step 5.
- No tests were added, removed, or skipped (FR-6).

If a test fails, the failure is almost certainly a misapplied `using` flip (e.g. an edit that altered more than the namespace). Diff the changed test file with `git diff backend/test/...` and confirm exactly one line changed before debugging deeper.

- [ ] **Step 3: Verify the dependency rule (NFR-3)**

Run:
```bash
grep -rn "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain/ || echo "CLEAN"
```
Expected: `CLEAN`. If a match exists, Domain has been polluted with an Application reference — revert and investigate.

Also verify the `.csproj` for Domain is unchanged:
```bash
git diff backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```
Expected: empty diff (no project-reference additions).

- [ ] **Step 4: Confirm the Domain `Picking` folder is gone**

Run:
```bash
ls backend/src/Anela.Heblo.Domain/Features/Logistics/ 2>&1
```
Expected: lists only `Carriers.cs` (plus any other pre-existing siblings). No `Picking/` entry.

- [ ] **Step 5: Format the changed files only**

Per the arch review's Risks table, we must not let `dotnet format` rewrite unrelated files. Scope it to the moved files plus the modified consumers.

Run:
```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Logistics/Picking/ \
            backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs \
            backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IExpeditionListService.cs \
            backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs \
            backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs \
            backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs \
            backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs \
            backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs \
            backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs \
            backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs \
            backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs \
            backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs
```
Expected: completes with no error. If `dotnet format` reports it cannot find the solution at `backend/Anela.Heblo.sln`, look for the actual `.sln` path under `backend/` (`ls backend/*.sln`) and adjust accordingly.

Then inspect the resulting diff:
```bash
git diff --stat
```
Expected: only the 14 files in scope (3 new in Application, 0 deletions visible here since `git rm` already staged them, 11 modified consumers). If `dotnet format` produced edits to files outside this list, undo them with `git checkout -- <path>` — they are out of scope (NFR-5).

- [ ] **Step 6: Re-run the build after formatting**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: 0 errors, warning count not increased. (Formatting changes shouldn't affect compilation, but the rule is "verify, don't assume.")

- [ ] **Step 7: No commit yet — Task 9 handles staging and message.**

---

## Task 9: Commit

**Files:** all staged changes from Tasks 1–8.

- [ ] **Step 1: Stage every new and modified file**

The Domain deletions were already staged in Task 4 via `git rm`. Stage the new Application files and the 11 modified consumers:

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs \
        backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs \
        backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IExpeditionListService.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs \
        backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs \
        backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs
```

Avoid `git add -A` / `git add .` per the global git-workflow rule — we don't want incidental edits sneaking in.

- [ ] **Step 2: Inspect the staged diff**

Run:
```bash
git diff --cached --stat
```
Expected pattern (counts may vary slightly if `dotnet format` touched whitespace):

```
 backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs                                |  2 +-
 backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs                            |  2 +-
 backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs                          |  2 +-
 backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs                                   |  2 +-
 backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IExpeditionListService.cs                                  |  2 +-
 backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs |  2 +-
 backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs                                            |  9 +++++++++
 backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs                                       | 25 ++++++++++++++++++++++++
 backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs                                        |  8 ++++++++
 backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/IPickingListSource.cs                                                 |  9 -------
 backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListRequest.cs                                            | 23 ----------------------
 backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListResult.cs                                             |  8 --------
 backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs                 |  2 +-
 backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs                                      |  2 +-
 backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs                                       |  2 +-
 backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs                                   |  2 +-
 backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs                                  |  2 +-
```

17 entries total: 3 created, 3 deleted, 11 modified (each `+1 -1`). If any consumer file shows more than `2 +-`, revert it (`git checkout HEAD -- <path>`) and re-do its `using` edit surgically.

- [ ] **Step 3: Create the commit**

Run:
```bash
git commit -m "$(cat <<'EOF'
refactor: relocate picking list operation DTOs from Domain to Application

PrintPickingListRequest, PrintPickingListResult, and IPickingListSource
carry use-case workflow concerns (SendToPrinter, ChangeOrderState,
ExportedFiles) and a port that depends on them. They belonged in the
Application layer, not Domain. Moves the three types to
Anela.Heblo.Application.Features.Logistics.Picking and updates the 11
consumers across Application, Adapters.ShoptetApi, and the two test
projects. No behavioural change; tests assert the relocation preserves
the public surface.

Follow-up: per development_guidelines.md "Consumer owns the contract"
the long-term home is Application/Features/ExpeditionList/Contracts/.
Tracked separately to keep this PR surgical.
EOF
)"
```
Expected: commit succeeds (single commit, conventional `refactor:` type).

If a pre-commit hook fails, fix the underlying issue and create a NEW commit — never `--amend` past a failed hook (per the global git-workflow rule).

- [ ] **Step 4: Verify the working tree is clean**

Run:
```bash
git status
```
Expected: "nothing to commit, working tree clean". If anything remains, that's an unstaged stray edit — investigate before declaring done.

- [ ] **Step 5: Summary check — match against the spec's acceptance criteria**

Mentally walk the spec one more time:

- FR-1 ✓ `PrintPickingListRequest.cs` at new path, namespace flipped, public surface preserved.
- FR-2 ✓ `PrintPickingListResult.cs` at new path, namespace flipped, public surface preserved.
- FR-3 ✓ `IPickingListSource.cs` at new path, namespace flipped, signature unchanged.
- FR-4 ✓ All 11 consumer files now import from `Anela.Heblo.Application.Features.Logistics.Picking`; backend-tree grep confirms zero old references.
- FR-5 ✓ Domain `Picking/` folder removed.
- FR-6 ✓ Build green, test count matches baseline, no test logic edited.
- NFR-3 ✓ `grep -r "Anela.Heblo.Application" backend/src/Anela.Heblo.Domain/` returns CLEAN; `Anela.Heblo.Domain.csproj` unchanged.
- NFR-5 ✓ `dotnet format` ran, no unrelated files in the diff.

Done.

---

## Self-review notes (recorded during plan authorship)

1. **Spec coverage:** FR-1 → Task 2, FR-2 → Task 1, FR-3 → Task 3, FR-4 → Tasks 5/6/7 + verification in Task 7 Step 7, FR-5 → Task 4, FR-6 → Task 8 Step 2 + Task 9 Step 5. NFR-1/NFR-2/NFR-4 are non-actionable (no perf, no security, no compat surface). NFR-3 → Task 8 Step 3. NFR-5 → Task 8 Step 5. All five spec amendments from the arch review are encoded: the corrected 11-file consumer list (Tasks 5–7 table), the `Carriers` resolution fix (Task 2), the dependency-rule grep (Task 8 Step 3), the empty-folder check (Task 4 Step 2), and the follow-up note (Task 9 Step 3 commit body).
2. **Placeholder scan:** every code block contains a complete, exact payload. Every command has expected output. No TODOs.
3. **Type/identifier consistency:** namespaces match in every task — `Anela.Heblo.Application.Features.Logistics.Picking` in Tasks 1/2/3 and as the `new_string` in Tasks 5/6/7. The Domain `Carriers` namespace `Anela.Heblo.Domain.Features.Logistics` appears identically in Task 2's `using` directive. The interface signature `Task<PrintPickingListResult> CreatePickingList(PrintPickingListRequest request, Func<IList<string>, Task>? onBatchFilesReady, CancellationToken cancellationToken = default);` matches the Domain original verbatim.
