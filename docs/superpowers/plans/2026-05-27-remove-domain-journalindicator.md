# Remove duplicate `JournalIndicator` from Domain layer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate `Anela.Heblo.Domain.Features.Journal.JournalIndicator` (a query projection masquerading as a domain entity) by replacing it with a minimal Domain primitive `JournalIndicatorSnapshot`, and drop the no-op `TotalEntries => DirectEntries` property from `JournalIndicatorDto`. Pure refactor — no behavior change, no schema change, no API shape change.

**Architecture:** Domain gets a small `readonly record struct JournalIndicatorSnapshot(int DirectEntries, DateTime? LastEntryDate, bool HasRecentEntries)` (no `ProductCode` — that's the dictionary key). `IJournalRepository.GetJournalIndicatorsAsync` returns `Dictionary<string, JournalIndicatorSnapshot>` instead of `Dictionary<string, JournalIndicator>`. The Persistence implementation builds snapshots in one shot (no mutation) by first aggregating into a local accumulator, then materialising the immutable record struct. `JournalIndicatorDto` stays as the Application-layer DTO (kept per spec) but loses the redundant `TotalEntries` property.

**Tech Stack:** .NET 8, C# (Clean Architecture monorepo), EF Core, xUnit + FluentAssertions + Moq for tests. No frontend, no OpenAPI/TS-client changes (the DTO has zero consumers outside this repo's own declaration and is not exposed via HTTP — verified by grep).

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs` | **Create** | New Domain primitive: `readonly record struct` holding `DirectEntries`, `LastEntryDate`, `HasRecentEntries`. No `ProductCode` (dictionary key). |
| `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` | **Modify** | Change return type of `GetJournalIndicatorsAsync` from `Dictionary<string, JournalIndicator>` to `Dictionary<string, JournalIndicatorSnapshot>`. |
| `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` | **Modify** | Rewrite `GetJournalIndicatorsAsync` body to build immutable snapshots in one shot (no post-construction mutation). |
| `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` | **Modify** | Drop three `TotalEntries` assertions (lines 247, 264, 294). Tests continue to pin observable behavior via `DirectEntries`, `LastEntryDate`, `HasRecentEntries`. |
| `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs` | **Delete** | Was a read-model masquerading as a domain entity. No remaining references after the steps above. |
| `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs` | **Modify** | Remove `public int TotalEntries => DirectEntries;` line. `ProductCode`, `DirectEntries`, `LastEntryDate`, `HasRecentEntries` remain. |

No new folders. No DI changes. No `Module.cs` changes. No appsettings / Key Vault / migration changes. No frontend changes.

---

## Task 1: Add `JournalIndicatorSnapshot` Domain primitive

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs`

This task introduces the new Domain type but does not yet wire it anywhere. The solution must still build after this step because no consumer is changed yet.

- [ ] **Step 1: Create the new file**

Create `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs` with this exact content:

```csharp
using System;

namespace Anela.Heblo.Domain.Features.Journal
{
    /// <summary>
    /// Repository-level read-model projection of per-product journal counts and last-entry metadata.
    /// Returned by <see cref="IJournalRepository.GetJournalIndicatorsAsync"/> keyed by product code.
    /// Not a domain entity — has no identity, behavior, or lifecycle.
    /// </summary>
    public readonly record struct JournalIndicatorSnapshot(
        int DirectEntries,
        DateTime? LastEntryDate,
        bool HasRecentEntries);
}
```

Notes:
- `readonly record struct` — small (≤16-byte payload), value semantics, no heap allocation per element.
- No `ProductCode` field — it is the dictionary key in `Dictionary<string, JournalIndicatorSnapshot>` and must not be duplicated.
- The XML comment explicitly disclaims domain-entity status to discourage future maintainers from adding behavior and re-creating the smell this refactor removes.

- [ ] **Step 2: Verify the solution still builds**

Run from repo root:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeds with no new warnings. No other file references `JournalIndicatorSnapshot` yet, so the existing `JournalIndicator` is still in use and unchanged.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs
git commit -m "refactor: add JournalIndicatorSnapshot domain primitive"
```

---

## Task 2: Update `IJournalRepository` interface signature

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:19-21`

After this step the solution will **not** compile — the implementation in `JournalRepository.cs` still returns `Dictionary<string, JournalIndicator>`. That is expected. Tasks 3 and 4 close the gap.

- [ ] **Step 1: Change the return type**

In `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`, locate this method (currently at lines 19–21):

```csharp
        Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default);
```

Replace it with:

```csharp
        Task<Dictionary<string, JournalIndicatorSnapshot>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default);
```

Leave all other methods unchanged. Do not touch `using` directives — both types live in the same namespace.

- [ ] **Step 2: Verify the build now fails as expected**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build **fails** with errors pointing at `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — the implementation no longer matches the interface. Errors will mention `JournalIndicator` vs `JournalIndicatorSnapshot`. This confirms the contract change reached the consumer.

Do not commit yet — the next task fixes the implementation in the same logical step.

---

## Task 3: Update `JournalRepository` implementation

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:165-209`

The current implementation mutates `JournalIndicator` instances after construction:

```csharp
result[productCode] = new JournalIndicator { ProductCode = productCode };
// ...later...
result[da.ProductCode].DirectEntries = da.Count;
result[da.ProductCode].LastEntryDate = da.LastEntryDate;
// ...later...
indicator.HasRecentEntries = ...
```

A `readonly record struct` cannot be mutated. The fix is to compute every field into a local accumulator first, then materialise each snapshot in a single `new(...)` expression at the end.

- [ ] **Step 1: Replace the method body**

Open `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`. Locate `GetJournalIndicatorsAsync` (currently lines 165–209). Replace the entire method with this exact body:

```csharp
        public async Task<Dictionary<string, JournalIndicatorSnapshot>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default)
        {
            var productCodeList = productCodes.ToList();

            // Aggregate direct associations into a per-product accumulator.
            var directAssociations = await Context.Set<JournalEntryProduct>()
                .Where(jep => productCodeList.Contains(jep.ProductCodePrefix))
                .Join(Context.Set<JournalEntry>().Where(je => !je.IsDeleted),
                    jep => jep.JournalEntryId,
                    je => je.Id,
                    (jep, je) => new { ProductCode = jep.ProductCodePrefix, je.EntryDate, je.CreatedAt })
                .GroupBy(x => x.ProductCode)
                .Select(g => new
                {
                    ProductCode = g.Key,
                    Count = g.Count(),
                    LastEntryDate = g.Max(x => x.EntryDate)
                })
                .ToListAsync(cancellationToken);

            var aggregatesByProduct = directAssociations.ToDictionary(x => x.ProductCode);

            var thirtyDaysAgo = DateTime.Today.AddDays(-30);
            var result = new Dictionary<string, JournalIndicatorSnapshot>(productCodeList.Count);

            foreach (var productCode in productCodeList)
            {
                if (aggregatesByProduct.TryGetValue(productCode, out var aggregate))
                {
                    var hasRecentEntries = aggregate.LastEntryDate >= thirtyDaysAgo;
                    result[productCode] = new JournalIndicatorSnapshot(
                        DirectEntries: aggregate.Count,
                        LastEntryDate: aggregate.LastEntryDate,
                        HasRecentEntries: hasRecentEntries);
                }
                else
                {
                    result[productCode] = new JournalIndicatorSnapshot(
                        DirectEntries: 0,
                        LastEntryDate: null,
                        HasRecentEntries: false);
                }
            }

            return result;
        }
```

Notes on what changed semantically (must be **nothing**):
- Empty/missing product codes still appear in the result with `DirectEntries = 0`, `LastEntryDate = null`, `HasRecentEntries = false` — same as before.
- `HasRecentEntries` is true when `LastEntryDate >= today - 30 days`. The original guarded `LastEntryDate.HasValue` before comparing; since the new code only enters the "has aggregate" branch when there is at least one matching row (and `LastEntryDate` is `DateTime`, not `DateTime?`, inside the projection), the guard is unnecessary in that branch. The else branch sets `false` explicitly.
- The aggregation EF query is byte-for-byte the same — no behavior risk from query rewriting.

Leave the `using` block unchanged — `Anela.Heblo.Domain.Features.Journal` is already imported (line 1).

- [ ] **Step 2: Verify the build now succeeds**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeds. The integration tests in `JournalRepositoryIntegrationTests.cs` still compile because they only access `DirectEntries`, `LastEntryDate`, `HasRecentEntries` (and `TotalEntries`, which still exists on the *Domain* `JournalIndicator` — wait, no: the tests now receive `JournalIndicatorSnapshot`, which does **not** have `TotalEntries`). Build will likely fail with three errors at the test file referencing `TotalEntries`. Task 4 fixes this.

If the only remaining compile errors are about `TotalEntries` on the snapshot in the test file, proceed to Task 4 — that is the expected state.

- [ ] **Step 3: Commit (combined interface + implementation change)**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs \
        backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "refactor: return JournalIndicatorSnapshot from IJournalRepository"
```

Note: tests are still broken at this point. The commit captures a coherent contract-and-implementation change; the test fix follows in Task 4.

---

## Task 4: Update integration tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs:247,264,294`

Three test assertions reference `indicator.TotalEntries`. These must be removed (not rewritten) — `TotalEntries` was always identical to `DirectEntries`, so the existing `DirectEntries` assertion already pins the same behavior. The test type `JournalIndicatorSnapshot` does not have a `TotalEntries` member; keeping the assertions would not compile and would re-create the no-op the refactor removes.

The local variable `var indicator = result["TON002"];` (and equivalents) still works without change — `var` infers `JournalIndicatorSnapshot`, and all three asserted properties (`DirectEntries`, `LastEntryDate`, `HasRecentEntries`) exist on the snapshot.

- [ ] **Step 1: Remove `TotalEntries` assertion at line 247**

In `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`, find this block inside `GetJournalIndicatorsAsync_WithMultipleDirectEntries_ReturnsCorrectCount` (currently around lines 244–249):

```csharp
        result.Should().ContainKey("TON002");
        var indicator = result["TON002"];
        indicator.DirectEntries.Should().Be(3);
        indicator.TotalEntries.Should().Be(indicator.DirectEntries);
        indicator.LastEntryDate.Should().Be(latest);
        indicator.HasRecentEntries.Should().BeTrue();
```

Replace it with (the only change is dropping the `TotalEntries` line):

```csharp
        result.Should().ContainKey("TON002");
        var indicator = result["TON002"];
        indicator.DirectEntries.Should().Be(3);
        indicator.LastEntryDate.Should().Be(latest);
        indicator.HasRecentEntries.Should().BeTrue();
```

- [ ] **Step 2: Remove `TotalEntries` assertion at line 264**

In the same file, find this block inside `GetJournalIndicatorsAsync_WithNoEntries_ReturnsZeroIndicator` (currently around lines 261–266):

```csharp
        result.Should().ContainKey("UNUSED999");
        var indicator = result["UNUSED999"];
        indicator.DirectEntries.Should().Be(0);
        indicator.TotalEntries.Should().Be(0);
        indicator.LastEntryDate.Should().BeNull();
        indicator.HasRecentEntries.Should().BeFalse();
```

Replace it with:

```csharp
        result.Should().ContainKey("UNUSED999");
        var indicator = result["UNUSED999"];
        indicator.DirectEntries.Should().Be(0);
        indicator.LastEntryDate.Should().BeNull();
        indicator.HasRecentEntries.Should().BeFalse();
```

- [ ] **Step 3: Remove `TotalEntries` assertion at line 294**

In the same file, find this block inside `GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries` (currently around lines 291–296):

```csharp
        result.Should().ContainKey("CREAM001");
        var indicator = result["CREAM001"];
        indicator.DirectEntries.Should().Be(1);
        indicator.TotalEntries.Should().Be(indicator.DirectEntries);
        indicator.HasRecentEntries.Should().BeTrue();
        indicator.LastEntryDate.Should().Be(recent);
```

Replace it with:

```csharp
        result.Should().ContainKey("CREAM001");
        var indicator = result["CREAM001"];
        indicator.DirectEntries.Should().Be(1);
        indicator.HasRecentEntries.Should().BeTrue();
        indicator.LastEntryDate.Should().Be(recent);
```

- [ ] **Step 4: Run the affected tests**

```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~JournalRepositoryIntegrationTests"
```

Expected: All tests in `JournalRepositoryIntegrationTests` pass — including the three `GetJournalIndicatorsAsync_*` tests. If anything fails, the most likely cause is a typo in the persistence rewrite (Task 3, Step 1) — diff against the spec body and re-verify the conditional logic around `aggregatesByProduct.TryGetValue`.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
git commit -m "test: drop TotalEntries assertions from journal indicator tests"
```

---

## Task 5: Delete the obsolete `JournalIndicator.cs`

**Files:**
- Delete: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs`

At this point nothing in the solution references the old `JournalIndicator` class. Grep first to confirm before deleting — if anything still references it, that means an earlier task missed a spot.

- [ ] **Step 1: Verify no remaining references in source**

Run:

```bash
grep -rn "JournalIndicator\b" backend/src backend/test --include="*.cs"
```

Expected output: matches only on the **file path** `Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs` itself (the type's own declaration). No matches in any other `.cs` file. If you see any other match in `backend/src` or `backend/test`, stop and fix it before deleting.

Notes on what the grep should NOT flag:
- `JournalIndicatorSnapshot` — different identifier, contains `JournalIndicator` as a prefix. The `\b` word-boundary in the pattern prevents the false positive on `Snapshot`. If your shell strips the backslash, use the explicit literal form: `grep -rn "JournalIndicator[^A-Za-z]" backend/src backend/test --include="*.cs"`.
- `JournalIndicatorDto` — also has the prefix; same word-boundary protection applies. If the grep flags it, ensure the regex has `\b` and re-run.

- [ ] **Step 2: Delete the file**

```bash
git rm backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs
```

- [ ] **Step 3: Verify the solution still builds**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeds. No errors.

- [ ] **Step 4: Run the affected tests again**

```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~JournalRepositoryIntegrationTests"
```

Expected: All `JournalRepositoryIntegrationTests` pass.

- [ ] **Step 5: Commit**

```bash
git commit -m "refactor: delete obsolete Domain JournalIndicator type"
```

(The `git rm` from Step 2 already staged the deletion.)

---

## Task 6: Remove the no-op `TotalEntries` from `JournalIndicatorDto`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs:9`

`JournalIndicatorDto` has zero consumers in the codebase today (verified by grep — only its own declaration matches). Per spec FR-3 the DTO is preserved but loses the no-op property. (The arch-review's §1 amendment recommended deleting the DTO entirely; the spec — which is the authoritative input and marked COMPLETE — keeps it. This plan follows the spec.)

- [ ] **Step 1: Remove the `TotalEntries` line**

Open `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs` and locate this content (currently lines 5–12):

```csharp
    public class JournalIndicatorDto
    {
        public string ProductCode { get; set; } = null!;
        public int DirectEntries { get; set; }
        public int TotalEntries => DirectEntries;
        public DateTime? LastEntryDate { get; set; }
        public bool HasRecentEntries { get; set; } // Within last 30 days
    }
```

Replace it with (one line removed):

```csharp
    public class JournalIndicatorDto
    {
        public string ProductCode { get; set; } = null!;
        public int DirectEntries { get; set; }
        public DateTime? LastEntryDate { get; set; }
        public bool HasRecentEntries { get; set; } // Within last 30 days
    }
```

The class stays a `class` (not a `record`) per the project rule: DTOs are classes — the OpenAPI generator mishandles record parameter order.

- [ ] **Step 2: Verify the build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs
git commit -m "refactor: drop no-op TotalEntries property from JournalIndicatorDto"
```

---

## Task 7: Final verification

No code edits in this task — only the project's standard pre-completion validation plus the spec's negative-grep checks.

- [ ] **Step 1: Confirm zero residual references to `TotalEntries`/`totalEntries`**

Run:

```bash
grep -rn "TotalEntries\|totalEntries" backend/src frontend/src --include="*.cs" --include="*.ts" --include="*.tsx"
```

Expected output: zero matches. (The TypeScript OpenAPI client is auto-generated on backend build; since the DTO is not exposed by any controller, no `totalEntries` field should ever have appeared in `frontend/src` — verify it stays absent.)

- [ ] **Step 2: Confirm the Domain layer no longer owns a `JournalIndicator` type**

Run:

```bash
grep -rn "class JournalIndicator\b\|record.*JournalIndicator\b" backend/src/Anela.Heblo.Domain --include="*.cs"
```

Expected: zero matches. (`JournalIndicatorSnapshot` is filtered out by the `\b` word boundary.)

- [ ] **Step 3: Run `dotnet format`**

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: completes without errors. If it changes any file, review and stage the changes.

- [ ] **Step 4: Final full build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeds with no new warnings.

- [ ] **Step 5: Run the full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln
```

Expected: All tests pass. The change is purely a type-level refactor — no test should regress.

- [ ] **Step 6: Frontend sanity check**

Even though no frontend changes are expected (the DTO is not exposed), confirm the OpenAPI-generated client did not change unexpectedly:

```bash
cd frontend && npm run build && npm run lint
```

Expected: Both pass. The generated TypeScript client should have **no diff** versus `main` for journal-related types (because the DTO was never in the OpenAPI document to begin with).

- [ ] **Step 7: Commit any formatting fixups (if any)**

If `dotnet format` modified files in Step 3, commit those now:

```bash
git status
# If there are changes:
git add -A
git commit -m "chore: dotnet format"
```

If no files changed, skip this step.

---

## Self-Review Notes (internal — do not include in execution)

**Spec coverage check:**
- FR-1 (delete Domain `JournalIndicator`) → Task 5.
- FR-2 (update `IJournalRepository` to use Domain-only return type) → Tasks 1, 2, 3.
- FR-3 (remove `TotalEntries` from DTO + downstream cleanup) → Task 6 (DTO); Task 4 (test assertions); Task 7 Step 1 (negative grep). Per arch-review §2, the FR-3 acceptance criteria about regenerated TS client and frontend updates are vacuously true (no controller exposes the DTO) and are verified by the negative grep in Task 7 Step 1 plus the frontend build in Step 6.
- FR-4 (update tests) → Task 4.
- NFR-1 (performance) — preserved: dictionary build is still O(n) over the same EF aggregation; only the inner loop body differs.
- NFR-2 (security) — no surface change.
- NFR-3 (maintainability) — Domain no longer owns a read-model; one source of truth for the DTO shape.
- NFR-4 (backward compat) — single PR, no API consumers affected since the DTO was never exposed.

**Placeholder scan:** none — every code block contains the exact content to write.

**Type consistency check:**
- `JournalIndicatorSnapshot` — same name everywhere (Task 1 declaration, Task 2 interface, Task 3 implementation, Task 4 test inference).
- Field names `DirectEntries`, `LastEntryDate`, `HasRecentEntries` — identical between snapshot and DTO; identical to the existing test assertions.
- Method signature `GetJournalIndicatorsAsync(IEnumerable<string> productCodes, CancellationToken cancellationToken = default)` — unchanged across interface and implementation.
