# Relocate Journal Repositories to Correct Persistence Folder — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the Journal module's six persistence files out of `Persistence/Catalog/Journal/` to their canonical location `Persistence/Journal/`, update the namespace from `Anela.Heblo.Persistence.Catalog.Journal` to `Anela.Heblo.Persistence.Journal`, and fix the two consumers that import the old namespace. Pure structural cleanup — zero behavior change, zero migrations.

**Architecture:** All six files (two repositories + four EF Core entity configurations) move together as a single atomic refactor commit. `git mv` preserves history. EF Core configuration discovery uses assembly scan (`ApplyConfigurationsFromAssembly` at `ApplicationDbContext.cs:170`), so the namespace move is runtime-safe with no `OnModelCreating` change. Block-scoped namespace style is **preserved verbatim** (no file-scoped conversion) per the spec NFR-4 "surgical changes" rule. `Persistence/Catalog/` itself stays — only the `Journal/` subfolder is removed.

**Tech Stack:** .NET 8, C#, EF Core, MediatR, xUnit + FluentAssertions + Moq, `git mv`, `dotnet build`, `dotnet format`, `dotnet test`.

---

## File Inventory

### Files to move (6 files, all from `backend/src/Anela.Heblo.Persistence/Catalog/Journal/` → `backend/src/Anela.Heblo.Persistence/Journal/`)

| File | Lines | Current namespace declaration |
|------|-------|-------------------------------|
| `JournalRepository.cs` | ~250 | `namespace Anela.Heblo.Persistence.Catalog.Journal` (line 7) |
| `JournalTagRepository.cs` | 21 | `namespace Anela.Heblo.Persistence.Catalog.Journal` (line 5) |
| `JournalEntryConfiguration.cs` | ~60 | `namespace Anela.Heblo.Persistence.Catalog.Journal` (line 6) |
| `JournalEntryProductConfiguration.cs` | ~25 | `namespace Anela.Heblo.Persistence.Catalog.Journal` (line 6) |
| `JournalEntryTagConfiguration.cs` | ~30 | `namespace Anela.Heblo.Persistence.Catalog.Journal` (line 6) |
| `JournalEntryTagAssignmentConfiguration.cs` | 22 | `namespace Anela.Heblo.Persistence.Catalog.Journal` (line 6) |

### Consumer files to update (2 files)

| File | Line | Change |
|------|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs` | 4 | `using Anela.Heblo.Persistence.Catalog.Journal;` → `using Anela.Heblo.Persistence.Journal;` |
| `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` | 3 | `using Anela.Heblo.Persistence.Catalog.Journal;` → `using Anela.Heblo.Persistence.Journal;` |

### Files NOT to touch
- `docs/superpowers/plans/2026-05-12-remove-journal-family-entries.md` — historical plan document, references are textual artifacts of an old change. Spec FR-4 acceptance criterion targets `.cs`, `.csproj`, `.json` only; markdown notes are out of scope.
- `Persistence/Catalog/` parent folder — `Inventory/`, `Stock/`, `ManufactureDifficulty/` legitimately belong under Catalog and remain unchanged.
- `ApplicationDbContext.cs` — uses assembly-scan configuration discovery; needs no code change.

---

## Decomposition Rationale

This is one atomic refactor. The build is **broken** in any state where some files have moved but consumers still reference the old namespace, or vice versa. Therefore the move + namespace edit + consumer update **must land in a single commit**. Task 2 below performs the entire refactor as one commit. Task 1 captures a green baseline; Task 3 performs the final validation grep + build + test sweep.

Within Task 2, individual steps are bite-sized (each `git mv` and each namespace edit is its own step) but they share one final commit, because intermediate states do not compile.

---

## Task 1: Establish Green Baseline

**Files:** none modified — verification only.

**Purpose:** Confirm the current state matches the spec's "before" picture and that Journal-related tests pass on `main` before we touch anything. If anything is already broken, we want to know now, not after the refactor.

- [ ] **Step 1: Verify the six source files exist at the old path**

Run:
```bash
ls backend/src/Anela.Heblo.Persistence/Catalog/Journal/
```

Expected output (exactly these six files):
```
JournalEntryConfiguration.cs
JournalEntryProductConfiguration.cs
JournalEntryTagAssignmentConfiguration.cs
JournalEntryTagConfiguration.cs
JournalRepository.cs
JournalTagRepository.cs
```

If any file is missing or extra files appear, **stop** and reconcile with the spec before proceeding.

- [ ] **Step 2: Verify the target folder does not yet exist**

Run:
```bash
test -d backend/src/Anela.Heblo.Persistence/Journal && echo "EXISTS - investigate" || echo "OK - target absent"
```

Expected: `OK - target absent`.

If `EXISTS - investigate`, stop and check whether someone else already started this work.

- [ ] **Step 3: Capture baseline grep of old namespace references**

Run:
```bash
grep -rn "Anela\.Heblo\.Persistence\.Catalog\.Journal" backend/ --include="*.cs" --include="*.csproj" --include="*.json"
```

Expected output — exactly these eight lines (six file declarations + two consumer `using` directives):
```
backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs:4:using Anela.Heblo.Persistence.Catalog.Journal;
backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs:3:using Anela.Heblo.Persistence.Catalog.Journal;
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryConfiguration.cs:6:namespace Anela.Heblo.Persistence.Catalog.Journal
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryProductConfiguration.cs:6:namespace Anela.Heblo.Persistence.Catalog.Journal
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryTagAssignmentConfiguration.cs:6:namespace Anela.Heblo.Persistence.Catalog.Journal
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryTagConfiguration.cs:6:namespace Anela.Heblo.Persistence.Catalog.Journal
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:7:namespace Anela.Heblo.Persistence.Catalog.Journal
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalTagRepository.cs:5:namespace Anela.Heblo.Persistence.Catalog.Journal
```

If you see more lines, an additional consumer exists that the spec did not list. Add it to your mental checklist for Task 2 (use the same `using` swap pattern).

- [ ] **Step 4: Confirm baseline build is green**

Run from repo root:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with `0 Error(s)`.

If the build is already red on `main` (or your starting branch), **stop** and resolve before refactoring.

- [ ] **Step 5: Confirm baseline Journal tests pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal" --no-build
```

Expected: `Passed!` with all Journal-related tests green.

If any Journal test is already failing on baseline, **stop** — do not start the refactor on top of a red test suite.

---

## Task 2: Atomic Move + Namespace Update + Consumer Fix (single commit)

**Files:**
- Move (via `git mv`): six files from `backend/src/Anela.Heblo.Persistence/Catalog/Journal/` to `backend/src/Anela.Heblo.Persistence/Journal/`
- Modify (namespace edit): all six moved files
- Modify (using directive swap):
  - `backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs:4`
  - `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs:3`
- Delete: empty folder `backend/src/Anela.Heblo.Persistence/Catalog/Journal/`

**Critical constraints:**
- **Keep block-scoped namespace** in every moved file (`namespace X { ... }`). Do **not** convert to file-scoped (`namespace X;`) even if neighboring files use the file-scoped form. Spec NFR-4 and arch-review Decision 2 require this.
- **Do not edit any line** in the moved files other than the single `namespace …` declaration line.
- **Do not run `dotnet format`** on the moved files in this task — if it rewrites block-scoped to file-scoped, you'll violate NFR-4. Format verification happens in Task 3 and must report no changes.

- [ ] **Step 1: Create the new target folder**

Run:
```bash
mkdir -p backend/src/Anela.Heblo.Persistence/Journal
```

(No expected output. Verify it exists with `ls -d backend/src/Anela.Heblo.Persistence/Journal/`.)

- [ ] **Step 2: `git mv` the six files**

Run:
```bash
git mv backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs                      backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs
git mv backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalTagRepository.cs                   backend/src/Anela.Heblo.Persistence/Journal/JournalTagRepository.cs
git mv backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryConfiguration.cs              backend/src/Anela.Heblo.Persistence/Journal/JournalEntryConfiguration.cs
git mv backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryProductConfiguration.cs       backend/src/Anela.Heblo.Persistence/Journal/JournalEntryProductConfiguration.cs
git mv backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryTagConfiguration.cs           backend/src/Anela.Heblo.Persistence/Journal/JournalEntryTagConfiguration.cs
git mv backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryTagAssignmentConfiguration.cs backend/src/Anela.Heblo.Persistence/Journal/JournalEntryTagAssignmentConfiguration.cs
```

Verify with:
```bash
git status --short
```

Expected: six `R` (rename) entries, each from `Catalog/Journal/…` to `Journal/…`. No other modifications yet.

- [ ] **Step 3: Update namespace in `JournalRepository.cs`**

Find the single line `namespace Anela.Heblo.Persistence.Catalog.Journal` (at line 7) and change to:
```csharp
namespace Anela.Heblo.Persistence.Journal
```

Keep the opening `{` on the next line untouched. Do **not** add `;`. Do **not** change anything else in the file.

Verify:
```bash
grep -n "^namespace " backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs
```
Expected: `7:namespace Anela.Heblo.Persistence.Journal`

- [ ] **Step 4: Update namespace in `JournalTagRepository.cs`**

Change line 5 from `namespace Anela.Heblo.Persistence.Catalog.Journal` to:
```csharp
namespace Anela.Heblo.Persistence.Journal
```

Verify:
```bash
grep -n "^namespace " backend/src/Anela.Heblo.Persistence/Journal/JournalTagRepository.cs
```
Expected: `5:namespace Anela.Heblo.Persistence.Journal`

- [ ] **Step 5: Update namespace in `JournalEntryConfiguration.cs`**

Change line 6 from `namespace Anela.Heblo.Persistence.Catalog.Journal` to:
```csharp
namespace Anela.Heblo.Persistence.Journal
```

Verify:
```bash
grep -n "^namespace " backend/src/Anela.Heblo.Persistence/Journal/JournalEntryConfiguration.cs
```
Expected: `6:namespace Anela.Heblo.Persistence.Journal`

- [ ] **Step 6: Update namespace in `JournalEntryProductConfiguration.cs`**

Change line 6 from `namespace Anela.Heblo.Persistence.Catalog.Journal` to:
```csharp
namespace Anela.Heblo.Persistence.Journal
```

Verify:
```bash
grep -n "^namespace " backend/src/Anela.Heblo.Persistence/Journal/JournalEntryProductConfiguration.cs
```
Expected: `6:namespace Anela.Heblo.Persistence.Journal`

- [ ] **Step 7: Update namespace in `JournalEntryTagConfiguration.cs`**

Change line 6 from `namespace Anela.Heblo.Persistence.Catalog.Journal` to:
```csharp
namespace Anela.Heblo.Persistence.Journal
```

Verify:
```bash
grep -n "^namespace " backend/src/Anela.Heblo.Persistence/Journal/JournalEntryTagConfiguration.cs
```
Expected: `6:namespace Anela.Heblo.Persistence.Journal`

- [ ] **Step 8: Update namespace in `JournalEntryTagAssignmentConfiguration.cs`**

Change line 6 from `namespace Anela.Heblo.Persistence.Catalog.Journal` to:
```csharp
namespace Anela.Heblo.Persistence.Journal
```

Verify:
```bash
grep -n "^namespace " backend/src/Anela.Heblo.Persistence/Journal/JournalEntryTagAssignmentConfiguration.cs
```
Expected: `6:namespace Anela.Heblo.Persistence.Journal`

- [ ] **Step 9: Update consumer `using` in `JournalModule.cs`**

In `backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs`, change line 4 from:
```csharp
using Anela.Heblo.Persistence.Catalog.Journal;
```
to:
```csharp
using Anela.Heblo.Persistence.Journal;
```

Touch no other line in this file. Do not reorder or sort `using` directives.

Verify:
```bash
sed -n '1,6p' backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs
```
Expected line 4: `using Anela.Heblo.Persistence.Journal;`

> Note on coordination with issue #2513: if that PR has already merged on your starting branch, the `AddScoped<IJournalRepository, …>` registrations may have moved to `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` instead. Detect with `grep -n "JournalRepository" backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`. If a match is found there, update the `using` in `PersistenceModule.cs` using the same pattern, and **drop** the `JournalModule.cs` edit if its `using Anela.Heblo.Persistence.Catalog.Journal;` line is also gone. The acceptance criterion is "zero hits of the old namespace string anywhere in `backend/`," not "edit a specific file" — Step 12 below enforces that.

- [ ] **Step 10: Update consumer `using` in `JournalRepositoryIntegrationTests.cs`**

In `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`, change line 3 from:
```csharp
using Anela.Heblo.Persistence.Catalog.Journal;
```
to:
```csharp
using Anela.Heblo.Persistence.Journal;
```

Touch no other line in this file. Do not reorder `using` directives.

Verify:
```bash
sed -n '1,5p' backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
```
Expected line 3: `using Anela.Heblo.Persistence.Journal;`

- [ ] **Step 11: Remove the now-empty source folder**

The `git mv` operations already removed the files from the index. Verify the directory is empty and delete it:
```bash
ls backend/src/Anela.Heblo.Persistence/Catalog/Journal/ 2>/dev/null
```

Expected output: empty (nothing printed) or `No such file or directory`. If anything is listed, **stop** and investigate — a file was missed in Step 2.

Then remove the empty directory (git will not track empty directories, so this is a filesystem-only cleanup):
```bash
rmdir backend/src/Anela.Heblo.Persistence/Catalog/Journal/ 2>/dev/null || true
```

(The `|| true` handles the case where the directory was already removed because `git mv` cleaned it up.)

Verify the directory is gone:
```bash
test -d backend/src/Anela.Heblo.Persistence/Catalog/Journal && echo "FAIL: still exists" || echo "OK: removed"
```
Expected: `OK: removed`

Also verify the Catalog parent still exists with its three legitimate siblings:
```bash
ls backend/src/Anela.Heblo.Persistence/Catalog/
```
Expected:
```
Inventory
ManufactureDifficulty
Stock
```

- [ ] **Step 12: Repo-wide grep for the old namespace (must be zero in `.cs`/`.csproj`/`.json`)**

Run:
```bash
grep -rn "Anela\.Heblo\.Persistence\.Catalog\.Journal" backend/ --include="*.cs" --include="*.csproj" --include="*.json"
```

Expected: **no output** (exit code 1). This is the FR-4 acceptance criterion.

If any line is printed, fix it before continuing. The fix is the same `using` swap pattern as Steps 9–10.

- [ ] **Step 13: Build the backend**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with `0 Error(s)`. Warning count must not increase compared to the Task 1 Step 4 baseline.

If the build fails, the most likely cause is a missed consumer. Re-run the grep from Step 12 and patch any survivors.

- [ ] **Step 14: Run Journal-targeted tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal" --no-build
```

Expected: `Passed!` with the same pass count as the Task 1 Step 5 baseline. No tests should be skipped or excluded that were not skipped at baseline.

If any Journal test fails, the most likely causes are:
- A consumer namespace was missed (re-check Step 12).
- The block-scoped namespace's closing `}` was accidentally deleted while editing the opening line.

- [ ] **Step 15: Stage and inspect the diff**

Run:
```bash
git add -A
git status --short
```

Expected `git status --short` output:
```
R  backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryConfiguration.cs              -> backend/src/Anela.Heblo.Persistence/Journal/JournalEntryConfiguration.cs
R  backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryProductConfiguration.cs       -> backend/src/Anela.Heblo.Persistence/Journal/JournalEntryProductConfiguration.cs
R  backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryTagAssignmentConfiguration.cs -> backend/src/Anela.Heblo.Persistence/Journal/JournalEntryTagAssignmentConfiguration.cs
R  backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryTagConfiguration.cs           -> backend/src/Anela.Heblo.Persistence/Journal/JournalEntryTagConfiguration.cs
R  backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs                      -> backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs
R  backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalTagRepository.cs                   -> backend/src/Anela.Heblo.Persistence/Journal/JournalTagRepository.cs
M  backend/src/Anela.Heblo.Application/Features/Journal/JournalModule.cs
M  backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
```

Confirm with:
```bash
git diff --staged --stat
```
The six renamed files should each show a 1-line change (the `namespace` edit). The two `M` files should each show a 1-line change (the `using` swap).

If any file shows more than ~2 lines changed, **stop** — you've accidentally formatted or edited unrelated lines. Revert the offending file and redo just the targeted edit.

- [ ] **Step 16: Commit**

Run:
```bash
git commit -m "$(cat <<'EOF'
refactor: move Journal repositories from Persistence/Catalog/Journal to Persistence/Journal

Relocate the Journal module's six persistence files (two repositories and four EF Core
entity configurations) from Persistence/Catalog/Journal/ to Persistence/Journal/, where
they belong per docs/architecture/filesystem.md. The Journal module has no structural
relationship to Catalog; the previous location was a scaffolding artifact.

Namespace changes from Anela.Heblo.Persistence.Catalog.Journal to
Anela.Heblo.Persistence.Journal. Both consumers (JournalModule DI registration and
JournalRepositoryIntegrationTests) updated to import the new namespace.

EF Core configuration discovery uses ApplyConfigurationsFromAssembly (assembly scan),
so the move is runtime-safe with no ApplicationDbContext change and no migration.
Block-scoped namespace style preserved per surgical-changes rule.

Related: #2513
EOF
)"
```

Verify the commit landed cleanly:
```bash
git log -1 --stat
```

Expected: one commit; the eight files listed; no other files. Each renamed file shows `1 +/- 1` (one line added, one removed — the namespace declaration). Each consumer shows `1 +/- 1` (one line added, one removed — the `using`).

---

## Task 3: Final Verification Sweep

**Files:** none modified — verification only. If any of these steps reveal a problem, fix it inline and add a separate commit only if a code change is required (e.g., `dotnet format` insists on a change that does NOT touch the block-scoped namespace style).

- [ ] **Step 1: Repo-wide grep across all text types**

Spec FR-4 specifies `.cs`, `.csproj`, `.json` "or any other text files." Cast the net wider than Task 2 Step 12, this time including markdown but excluding historical plan documents and build artifacts:

```bash
grep -rn "Anela\.Heblo\.Persistence\.Catalog\.Journal" backend/ frontend/ docs/ \
  --include="*.cs" --include="*.csproj" --include="*.json" --include="*.props" --include="*.targets" \
  --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules
```

Expected: **no output**. (Markdown plan documents under `docs/superpowers/plans/` are deliberately excluded by the absence of `--include="*.md"`; they are historical artifacts, not live references.)

If output appears, identify whether it is a live reference (code, config) or a historical document. Patch live references with the same `using` swap; leave historical documents alone.

- [ ] **Step 2: Confirm no `Persistence.Catalog.Journal` folder remains**

```bash
test -d backend/src/Anela.Heblo.Persistence/Catalog/Journal && echo "FAIL" || echo "OK"
```

Expected: `OK`

```bash
ls backend/src/Anela.Heblo.Persistence/Catalog/
```

Expected (unchanged from Task 2 Step 11):
```
Inventory
ManufactureDifficulty
Stock
```

- [ ] **Step 3: Confirm the new folder has all six files**

```bash
ls backend/src/Anela.Heblo.Persistence/Journal/
```

Expected:
```
JournalEntryConfiguration.cs
JournalEntryProductConfiguration.cs
JournalEntryTagAssignmentConfiguration.cs
JournalEntryTagConfiguration.cs
JournalRepository.cs
JournalTagRepository.cs
```

- [ ] **Step 4: Verify git history is preserved for each moved file**

For each of the six files, confirm `git log --follow` walks back through the rename:

```bash
for f in JournalRepository.cs JournalTagRepository.cs JournalEntryConfiguration.cs JournalEntryProductConfiguration.cs JournalEntryTagConfiguration.cs JournalEntryTagAssignmentConfiguration.cs; do
  echo "--- $f ---"
  git log --follow --oneline -3 backend/src/Anela.Heblo.Persistence/Journal/$f
done
```

Expected: for each file, more than just the single refactor commit appears (the original creation commit + any intermediate edits are visible). If only the refactor commit appears, rename detection failed — investigate, but note that GitHub web UI may still show this as a rename via its own similarity heuristic, and `git log --follow` is the authoritative source.

- [ ] **Step 5: Run `dotnet format` in verify-only mode**

Spec NFR-4 / FR-5 require no formatting violations and no incidental reformatting.

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes --verbosity diagnostic
```

Expected exit code: `0` (no formatting changes needed).

If `dotnet format` reports needed changes:
- If they are inside the six moved files and would convert block-scoped to file-scoped namespace, **do not apply them**. The block-scoped style is a deliberate decision (arch-review Decision 2); the inconsistency with neighboring files is acknowledged and out of scope. Note the discrepancy in the PR description but ship as-is.
- If they are anywhere else (whitespace, unused `using` reordering in `JournalModule.cs` or the integration test), the formatter is right and the prior edit was sloppy. Apply the fix (`dotnet format`) and add a separate small commit `chore: dotnet format` so review history is clear.

- [ ] **Step 6: Run the full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: `Passed!` with the same total count and no new failures or skips relative to baseline.

If integration tests requiring a real database are skipped in your environment (e.g., no PostgreSQL container running), confirm the same skip pattern was present in the Task 1 baseline. Do not mark the task complete if Journal-specific integration tests are failing.

- [ ] **Step 7: Confirm full backend build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.`, `0 Error(s)`, warning count equal to baseline.

- [ ] **Step 8: Spec acceptance criteria checklist**

Walk the spec and tick off each acceptance criterion against the verification results above:

- [ ] FR-1: Both repository files at new path ✓ (Task 3 Step 3)
- [ ] FR-1: Both repository files removed from old path ✓ (Task 3 Step 2)
- [ ] FR-1: Empty `Persistence/Catalog/Journal/` removed ✓ (Task 3 Step 2)
- [ ] FR-1: `Persistence/Catalog/` left intact with `Inventory/`, `Stock/`, `ManufactureDifficulty/` ✓ (Task 3 Step 2)
- [ ] FR-1: Git history preserved via `git mv` ✓ (Task 3 Step 4)
- [ ] FR-2: Both repositories declare `namespace Anela.Heblo.Persistence.Journal` ✓ (Task 2 Steps 3–4 verification greps)
- [ ] FR-2: No file declares the old namespace ✓ (Task 3 Step 1)
- [ ] FR-3: All EF configuration files moved alongside repositories ✓ (Task 3 Step 3)
- [ ] FR-3: All Journal persistence files under `Persistence/Journal/` with new namespace ✓ (Task 3 Steps 1, 3)
- [ ] FR-4: Repo-wide search confirms zero occurrences of old namespace string ✓ (Task 3 Step 1)
- [ ] FR-4: All dependent files compile ✓ (Task 3 Step 7)
- [ ] FR-4: Integration test consumer (`JournalRepositoryIntegrationTests.cs`) updated — added per arch-review amendment ✓ (Task 2 Step 10)
- [ ] FR-5: `dotnet build` succeeds with zero new warnings/errors ✓ (Task 3 Step 7)
- [ ] FR-5: `dotnet format` reports no formatting violations ✓ (Task 3 Step 5)
- [ ] FR-5: All existing Journal-related tests pass ✓ (Task 3 Step 6)
- [ ] FR-5: No EF Core migrations added or modified ✓ (Task 2 Step 15 stat shows only the eight expected files)
- [ ] NFR-1, NFR-2, NFR-3: No runtime, security, or contract impact (structural change only) ✓
- [ ] NFR-4: Surgical changes — only `namespace` and `using` lines edited; block-scoped style preserved ✓ (Task 2 Step 15 stat shows 1 +/- 1 per file)

If any item cannot be ticked, do not close the task — fix it.

---

## Out of Scope (do not do)

- Do **not** move DI registrations between modules. That is issue #2513.
- Do **not** convert block-scoped namespaces to file-scoped, even if neighbors use the latter.
- Do **not** reformat, reorder, or sort `using` directives in `JournalModule.cs` or `JournalRepositoryIntegrationTests.cs`.
- Do **not** edit any line in the moved files other than the `namespace` declaration.
- Do **not** touch `Persistence/Catalog/Inventory/`, `Persistence/Catalog/Stock/`, or `Persistence/Catalog/ManufactureDifficulty/`.
- Do **not** add, modify, or remove EF Core migrations.
- Do **not** edit `ApplicationDbContext.cs` — configuration discovery is by assembly scan, no code change is needed.
- Do **not** audit or refactor other modules with similar issues — scope is Journal only.
- Do **not** update historical plan documents under `docs/superpowers/plans/` even if they reference the old namespace.
