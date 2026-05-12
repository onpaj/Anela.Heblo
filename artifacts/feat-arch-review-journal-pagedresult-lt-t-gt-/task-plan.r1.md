# Relocate `PagedResult<T>` to the Xcc Shared Layer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the generic `PagedResult<T>` pagination DTO out of the Journal feature's domain namespace and into the cross-cutting `Anela.Heblo.Xcc.Persistance` namespace so Marketing (and future modules) stops depending on Journal's domain for a non-Journal type.

**Architecture:** Single-file relocation. Create `PagedResult.cs` alongside the existing `IRepository<TEntity, TKey>` in `Anela.Heblo.Xcc/Persistance/`. Delete the inline declaration from `IJournalRepository.cs`. Repair `using` directives in four consumer files. No behavior change, no wire-format change, no new project references, no shim. Hard cut-over.

**Tech Stack:** .NET 8, C# 12 (file-scoped namespaces), xUnit + FluentAssertions + Moq for tests. `ImplicitUsings` is enabled in the Xcc project so `System.Collections.Generic` is implicit.

---

## File Structure

**Create (1 file):**
- `backend/src/Anela.Heblo.Xcc/Persistance/PagedResult.cs` — the relocated `public class PagedResult<T>`. Lives next to `IRepository.cs` and `IReadOnlyRepository.cs`.

**Modify (5 files):**
- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` — delete the inline `public class PagedResult<T>` at lines 26-32. Existing `using Anela.Heblo.Xcc.Persistance;` (line 1) keeps `PagedResult<JournalEntry>` resolving.
- `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` — remove `using Anela.Heblo.Domain.Features.Journal;` (line 5). The existing `using Anela.Heblo.Xcc.Persistance;` (line 6) keeps `PagedResult<MarketingAction>` resolving.
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` — remove `using Anela.Heblo.Domain.Features.Journal;` (line 1) and add `using Anela.Heblo.Xcc.Persistance;`. The Journal using is currently this file's only resolution path for `PagedResult` and has no other Journal types in scope.
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — add `using Anela.Heblo.Xcc.Persistance;`. Keep the existing `using Anela.Heblo.Domain.Features.Journal;` (line 1) — it is still required for `IJournalRepository`, `JournalEntry`, `JournalQueryCriteria`, `JournalSearchCriteria`, `JournalEntryProduct`, `JournalIndicator`.
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — add `using Anela.Heblo.Xcc.Persistance;`. Keep the existing `using Anela.Heblo.Domain.Features.Journal;` (line 3) — still required for `IJournalRepository`, `JournalEntry`, `JournalSearchCriteria`.

**Important sequencing constraint:** The change must be applied as a single atomic refactor. Between creating the new type and deleting the old one, several files would have an ambiguous `PagedResult` reference. Therefore *no intermediate build/commit* — apply all edits in Task 2, then verify in Task 3, then commit in Task 4.

---

## Task 1: Establish baseline

**Files:** none (verification only)

- [ ] **Step 1: Verify baseline build is green**

Run:
```bash
cd backend && dotnet build
```

Expected: solution builds with zero errors. If the baseline is broken, stop and fix it before proceeding — this refactor must not be conflated with an unrelated repair.

- [ ] **Step 2: Verify baseline test suite is green**

Run:
```bash
cd backend && dotnet test --no-build
```

Expected: all tests pass. Note the total count (e.g., "Passed: N"). Re-confirm the same count after the refactor.

- [ ] **Step 3: Record the current `PagedResult` reference set**

Run (from repo root):
```bash
grep -RnE "PagedResult" backend/src backend/test
```

Expected output (5 referencing files exactly):
```
backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:9
backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:13
backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:26  (declaration)
backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs:14
backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs:38
backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs:107
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:38
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:70
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:79
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:152
backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs:46
backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs:117
```

If any *other* file matches, stop — the spec's blast-radius assumption is wrong and the plan needs updating. Surface the unexpected match and ask before proceeding.

- [ ] **Step 4: Confirm no fully-qualified Journal-namespaced `PagedResult` references exist**

Run:
```bash
grep -RnE "Features\.Journal\.PagedResult" backend/
```

Expected: zero hits. (Risk R1 from arch-review.)

---

## Task 2: Apply the relocation atomically

**Files:**
- Create: `backend/src/Anela.Heblo.Xcc/Persistance/PagedResult.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs`

> Apply all six edits below in sequence without intermediate build attempts. The build will fail mid-sequence (ambiguous reference / type not found) by design. The final build check happens in Task 3.

- [ ] **Step 1: Create the new `PagedResult<T>` file in Xcc.Persistance**

Create `backend/src/Anela.Heblo.Xcc/Persistance/PagedResult.cs` with exactly this content:

```csharp
namespace Anela.Heblo.Xcc.Persistance;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
```

Notes:
- File-scoped namespace, matching the convention of the neighbouring `IRepository.cs` and `IReadOnlyRepository.cs`.
- No `using System.Collections.Generic;` needed — `Anela.Heblo.Xcc.csproj` has `<ImplicitUsings>enable</ImplicitUsings>`.
- `Nullable` is `enable` in the csproj; the type has no nullable references so no annotations are required.
- `public class`, not `record` (project rule: DTOs are classes, never records — OpenAPI generators mishandle record parameter order). Settable properties preserve every existing object-initializer call site.
- Shape is byte-identical to the current Journal-namespaced declaration.

- [ ] **Step 2: Remove the inline `PagedResult<T>` declaration from `IJournalRepository.cs`**

In `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`, delete lines 26-32 (the entire `public class PagedResult<T>` block and the blank line preceding it). The file must end with the closing brace of the interface and the closing brace of the namespace.

Before:
```csharp
        Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default);
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }
}
```

After:
```csharp
        Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default);
    }
}
```

Leave the `using Anela.Heblo.Xcc.Persistance;` at line 1 untouched — it now resolves `PagedResult<JournalEntry>` to the shared type.

- [ ] **Step 3: Remove the Journal using from `IMarketingActionRepository.cs`**

In `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs`, delete the line:

```csharp
using Anela.Heblo.Domain.Features.Journal;
```

(currently line 5). Keep all other usings, including `using Anela.Heblo.Xcc.Persistance;` (line 6) which now resolves `PagedResult<MarketingAction>`.

After the edit, the top of the file should read:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Marketing
```

- [ ] **Step 4: Update `MarketingActionRepository.cs` — remove Journal using, add Xcc.Persistance using**

In `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs`, replace line 1 (`using Anela.Heblo.Domain.Features.Journal;`) with `using Anela.Heblo.Xcc.Persistance;`. The Journal using is currently the file's only resolution path for `PagedResult` and the file does not reference any other Journal type (verified during planning — `MarketingAction`, `MarketingActionQueryCriteria`, `MarketingSyncStatus` all live in `Anela.Heblo.Domain.Features.Marketing`).

After the edit, the top of the file should read:
```csharp
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
```

(`dotnet format` will sort the usings; the alphabetical order above is the expected post-format result.)

- [ ] **Step 5: Add Xcc.Persistance using to `JournalRepository.cs`**

In `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`, add `using Anela.Heblo.Xcc.Persistance;` to the using block. Keep the existing `using Anela.Heblo.Domain.Features.Journal;` — it is still required for `IJournalRepository`, `JournalEntry`, `JournalQueryCriteria`, `JournalSearchCriteria`, `JournalEntryProduct`, and `JournalIndicator`.

After the edit, the top of the file should read:
```csharp
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
```

- [ ] **Step 6: Add Xcc.Persistance using to `SearchJournalEntriesHandlerTests.cs`**

In `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs`, add `using Anela.Heblo.Xcc.Persistance;` to the using block. Keep `using Anela.Heblo.Domain.Features.Journal;` — still required for `IJournalRepository`, `JournalEntry`, `JournalSearchCriteria`.

After the edit, the top of the file should read:
```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.SearchJournalEntries;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Xcc.Persistance;
using FluentAssertions;
using Moq;
using Xunit;
```

No changes to the test bodies. The two `new PagedResult<JournalEntry> { ... }` object initializers at lines 46 and 117 continue to compile because the new type has identical shape.

---

## Task 3: Verify the relocation

**Files:** none (verification only)

- [ ] **Step 1: Build the solution**

Run:
```bash
cd backend && dotnet build
```

Expected: solution builds with zero errors and zero new warnings. If errors appear, the most likely cause is a missed `using` directive — re-check Task 2 Steps 3-6 against the affected files.

- [ ] **Step 2: Run `dotnet format`**

Run:
```bash
cd backend && dotnet format
```

Expected: completes without complaint, or applies only `using`-directive ordering changes in the four files modified in Task 2 Steps 3-6. Re-run `dotnet build` after formatting to confirm the format pass did not regress the build.

- [ ] **Step 3: Confirm no Journal-namespaced `PagedResult` references survive**

Run (from repo root):
```bash
grep -RnE "Features\.Journal\.PagedResult" backend/
```

Expected: zero hits.

- [ ] **Step 4: Confirm Marketing has zero Journal-namespace references**

Run (from repo root):
```bash
grep -RnE "Features\.Journal" backend/src/Anela.Heblo.Domain/Features/Marketing backend/src/Anela.Heblo.Persistence/Marketing
```

Expected: zero hits. This is the architectural acceptance check called out in the arch-review's amendment to FR-3.

- [ ] **Step 5: Confirm only one `PagedResult<T>` declaration remains anywhere**

Run (from repo root):
```bash
grep -RnE "(class|record)\s+PagedResult" backend/
```

Expected output (exactly one line):
```
backend/src/Anela.Heblo.Xcc/Persistance/PagedResult.cs:3:public class PagedResult<T>
```

If any other line appears, a duplicate declaration was missed — locate and remove it.

- [ ] **Step 6: Run the full backend test suite**

Run:
```bash
cd backend && dotnet test --no-build
```

Expected: all tests pass, total count matches the Task 1 Step 2 baseline. Pay particular attention to `SearchJournalEntriesHandlerTests` — its two tests construct `PagedResult<JournalEntry>` directly and exercise the relocated type end-to-end through MediatR. Also note any Marketing or Journal handler tests; they exercise `PagedResult` transitively through repository mocks.

- [ ] **Step 7: Inspect the OpenAPI/TypeScript client diff**

The project regenerates the TypeScript client on build. Check whether the regeneration produced a diff:

```bash
git status frontend/src/api-client 2>/dev/null
git diff --stat frontend/ 2>/dev/null
```

Expected: no diff. `PagedResult<T>` is not currently exposed through any OpenAPI controller response — it transits as a repository return type and is unwrapped into feature-specific response DTOs before reaching controllers. If a non-empty client diff *does* appear:
- Inspect it. If it is purely a `$ref` namespace change with identical JSON shape, accept and commit it alongside the refactor.
- If the diff introduces *renamed* schema entries or changed property names, stop — the wire contract is changing and the spec's NFR-3 (wire compatibility) is violated. Surface this before continuing.

---

## Task 4: Commit

**Files:** all six files from Task 2.

- [ ] **Step 1: Stage the changes**

Run:
```bash
git add \
  backend/src/Anela.Heblo.Xcc/Persistance/PagedResult.cs \
  backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs \
  backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs \
  backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs \
  backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs \
  backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs
```

If Task 3 Step 7 produced a TypeScript client diff and it was determined safe to absorb, also stage `frontend/src/api-client` (or whatever path the regeneration touched). Otherwise do not stage frontend files — there should be none.

- [ ] **Step 2: Verify the staged diff matches expectations**

Run:
```bash
git diff --cached --stat
```

Expected: six files changed, one new file (`PagedResult.cs`), small line-count delta (one ~7-line addition for the new file, one ~7-line deletion in `IJournalRepository.cs`, ±1 line each for the using-directive adjustments).

Then run:
```bash
git diff --cached
```

Sanity-check:
- `PagedResult.cs` contains the exact 7-line type definition from Task 2 Step 1.
- `IJournalRepository.cs` removes the inline class and nothing else.
- The four consumer files show only `using` directive changes — no method bodies, no signatures, no whitespace churn in unrelated regions.

- [ ] **Step 3: Commit**

Run:
```bash
git commit -m "$(cat <<'EOF'
refactor: relocate PagedResult<T> to Xcc.Persistance

Move the generic PagedResult<T> pagination DTO out of the Journal
feature's domain namespace and into Anela.Heblo.Xcc.Persistance,
alongside IRepository<TEntity, TKey>. Removes the cross-module
dependency where Marketing's domain interface imported
Anela.Heblo.Domain.Features.Journal solely to reach PagedResult.

Pure relocation — type shape, property names, serialization, and
runtime behavior are unchanged. No new abstractions, no shim, no
project references added.
EOF
)"
```

(Global git settings disable Co-Authored-By attribution — no trailer needed.)

- [ ] **Step 4: Confirm working tree is clean**

Run:
```bash
git status
```

Expected: `nothing to commit, working tree clean`.

---

## Self-review notes

**Spec coverage:**
- FR-1 (introduce in Xcc) → Task 2 Step 1.
- FR-2 (remove inline declaration) → Task 2 Step 2.
- FR-3 (update consumers) → Task 2 Steps 3-6.
- FR-3 amendment from arch-review (verify Marketing has zero Journal references) → Task 3 Step 4.
- FR-4 (preserve behavior end-to-end) → Task 3 Steps 1-2, 6, 7.
- FR-5 (no new abstractions) → enforced by the exact code block in Task 2 Step 1 and the diff inspection in Task 4 Step 2.
- NFR-1 (no perf impact) → falls out of identical type shape; no separate check needed.
- NFR-3 (wire compatibility) → Task 3 Step 7 (TypeScript client regeneration check).
- NFR-4 (maintainability) → satisfied by Decision 1 (co-location with IRepository).

**Risk coverage:**
- R1 (fully-qualified Journal references) → Task 3 Step 3.
- R2 (OpenAPI client drift) → Task 3 Step 7.
- R3 (Marketing infra file still importing Journal for other reasons) → verified during planning; the file references no other Journal type. Task 2 Step 4 replaces the using outright.
- R5 (reflection-based contract test pinning declaring assembly) → Task 3 Step 6 catches this; mitigation is "update the test as part of the change" if it surfaces.
- R6 (`dotnet format` re-adding removed usings) → Task 3 Step 2 followed by another build; formatters do not re-add removed usings, so this is belt-and-braces only.

**Placeholder scan:** No TBDs, no "implement later", no "handle edge cases" placeholders. Every code change has exact before/after content. Every grep has an expected output.

**Type consistency:** The `PagedResult<T>` shape used in Task 2 Step 1 (`Items`, `TotalCount`, `PageNumber`, `PageSize`, all settable, `Items` defaulted to `new()`) matches the current declaration and every object-initializer call site verified during planning (`MarketingActionRepository.cs:107-113`, `JournalRepository.cs:70-76`, `JournalRepository.cs:152-158`, `SearchJournalEntriesHandlerTests.cs:46-52, 117-123`).
