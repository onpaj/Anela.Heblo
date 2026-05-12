# Remove Unimplemented `FamilyEntries` from `JournalIndicator` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the never-populated `FamilyEntries` property from the `JournalIndicator` domain entity and its mirrored `JournalIndicatorDto`, redefining `TotalEntries` as a passthrough of `DirectEntries`, and lock the current `GetJournalIndicatorsAsync` behavior in place with regression tests.

**Architecture:** Negative-delta, contract-shrinking change inside a single Vertical Slice (Journal). Domain entity, contract DTO, and repository implementation all change in `backend/src/Anela.Heblo.{Domain,Application,Persistence}/.../Journal`. No new files, no DI changes, no migrations, no MediatR handlers. The OpenAPI / TypeScript client regenerates mechanically on `npm run build`.

**Tech Stack:** .NET 8 (xUnit + FluentAssertions + EF Core InMemory for tests), OpenAPI client generation pipeline (TypeScript output is a derived artifact, not edited by hand).

---

## File Map

| Path | Action | Responsibility |
|------|--------|----------------|
| `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` | Modify | Add three `[Fact]`s pinning `GetJournalIndicatorsAsync` behavior; drop dead `using` |
| `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs` | Modify | Drop `FamilyEntries`; `TotalEntries => DirectEntries` |
| `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs` | Modify | Mirror the domain change in the DTO contract |
| `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` | Modify | Collapse orphan blank line at 209–210; no semantic change |
| `frontend/src/api/generated/*` | Auto-regenerated | `familyEntries` disappears from the TS client schema on `npm run build` |

**TDD ordering rationale:** The new tests pass against **current** code (because `FamilyEntries` is always `0`, today's `TotalEntries == DirectEntries + 0 == DirectEntries` and tomorrow's `TotalEntries == DirectEntries` produce the same value). Writing them first locks in the observable behavior before the refactor, so any unexpected drift surfaces immediately.

---

## Pre-flight: Baseline Build

- [ ] **Step 1: Confirm clean working tree on feature branch**

Run:
```bash
git status
git rev-parse --abbrev-ref HEAD
```
Expected: working tree clean; branch is `feat-arch-review-journal-familyentries-on-jou`.

- [ ] **Step 2: Verify backend baseline builds**

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded` with 0 errors. Take note of pre-existing warning counts so post-change warnings can be compared.

- [ ] **Step 3: Verify backend tests baseline is green**

Run:
```bash
cd backend && dotnet test --no-build
```
Expected: all tests pass. If they don't, stop — the baseline is broken and this plan's tests will be unreliable.

- [ ] **Step 4: Verify frontend baseline builds**

Run:
```bash
cd frontend && npm run build
```
Expected: build succeeds; TypeScript client regenerated; no errors. (Confirms the OpenAPI pipeline is functional before this change.)

---

## Task 1: Lock current `GetJournalIndicatorsAsync` behavior with three integration tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

**Why first:** Establishes a green regression net **before** the property change. The tests use the public repository API and assert on the domain `JournalIndicator`. They must pass on current code (where `TotalEntries == DirectEntries + 0`) and continue to pass after the refactor (where `TotalEntries == DirectEntries`).

- [ ] **Step 1: Add the three failing-by-absence test methods**

Append the following three tests inside the `JournalRepositoryIntegrationTests` class (before the `CreateEntryWithFamily` helper at line 197). Use **uppercase** product codes since `AssociateWithProduct` upper-cases its input (`JournalEntry.cs:66`).

```csharp
[Fact]
public async Task GetJournalIndicatorsAsync_WithMultipleDirectEntries_ReturnsCorrectCount()
{
    // Arrange
    var latest = DateTime.Today;
    var middle = DateTime.Today.AddDays(-1);
    var earliest = DateTime.Today.AddDays(-2);

    var e1 = new JournalEntry
    {
        Title = "TON002 entry 1",
        Content = "Content",
        EntryDate = latest,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user"
    };
    e1.AssociateWithProduct("TON002");

    var e2 = new JournalEntry
    {
        Title = "TON002 entry 2",
        Content = "Content",
        EntryDate = middle,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user"
    };
    e2.AssociateWithProduct("TON002");

    var e3 = new JournalEntry
    {
        Title = "TON002 entry 3",
        Content = "Content",
        EntryDate = earliest,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user"
    };
    e3.AssociateWithProduct("TON002");

    await _context.Set<JournalEntry>().AddRangeAsync(e1, e2, e3);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetJournalIndicatorsAsync(new[] { "TON002" });

    // Assert
    result.Should().ContainKey("TON002");
    var indicator = result["TON002"];
    indicator.DirectEntries.Should().Be(3);
    indicator.TotalEntries.Should().Be(indicator.DirectEntries);
    indicator.LastEntryDate.Should().Be(latest);
    indicator.HasRecentEntries.Should().BeTrue();
}

[Fact]
public async Task GetJournalIndicatorsAsync_WithNoEntries_ReturnsZeroIndicator()
{
    // Arrange — intentionally no entries inserted

    // Act
    var result = await _repository.GetJournalIndicatorsAsync(new[] { "UNUSED999" });

    // Assert
    result.Should().ContainKey("UNUSED999");
    var indicator = result["UNUSED999"];
    indicator.DirectEntries.Should().Be(0);
    indicator.TotalEntries.Should().Be(0);
    indicator.LastEntryDate.Should().BeNull();
    indicator.HasRecentEntries.Should().BeFalse();
}

[Fact]
public async Task GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries()
{
    // Arrange
    var recent = DateTime.Today.AddDays(-5);
    var entry = new JournalEntry
    {
        Title = "Recent CREAM001 entry",
        Content = "Content",
        EntryDate = recent,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "test-user"
    };
    entry.AssociateWithProduct("CREAM001");
    await _context.Set<JournalEntry>().AddAsync(entry);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetJournalIndicatorsAsync(new[] { "CREAM001" });

    // Assert
    result.Should().ContainKey("CREAM001");
    var indicator = result["CREAM001"];
    indicator.DirectEntries.Should().Be(1);
    indicator.HasRecentEntries.Should().BeTrue();
    indicator.LastEntryDate.Should().Be(recent);
}
```

Notes:
- Do **not** test the 30-day boundary exactly — wall-clock coupling would flake. The first test uses `DateTime.Today` (which is ≤ 30 days), the second uses zero entries, the third uses `-5 days`. Boundary cases require an injectable `IClock`, which is out of scope per arch-review.
- Assert `LastEntryDate` against the exact arrange value, not `BeCloseTo(DateTime.Today, ...)` — `EntryDate` is stored verbatim.
- Do **not** assert `TotalEntries` directly against the integer count alone. Use `indicator.TotalEntries.Should().Be(indicator.DirectEntries)` so the test reads the same way before and after the refactor and documents the invariant.

- [ ] **Step 2: Run the new tests — they must pass on current code**

Run:
```bash
cd backend && dotnet test --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetJournalIndicatorsAsync" -v normal
```
Expected: 3 tests pass, 0 fail. (If any fails on current code, stop and investigate — the test or the harness is wrong, not the production code.)

- [ ] **Step 3: Run the full test project to confirm nothing else broke**

Run:
```bash
cd backend && dotnet test
```
Expected: same baseline pass count + 3 new tests = all pass.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
git commit -m "test: pin GetJournalIndicatorsAsync behavior with regression tests"
```

---

## Task 2: Remove `FamilyEntries` from the domain entity `JournalIndicator`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs`

- [ ] **Step 1: Edit the file to its final shape**

Replace the file body with:

```csharp
using System;

namespace Anela.Heblo.Domain.Features.Journal
{
    public class JournalIndicator
    {
        public string ProductCode { get; set; } = null!;
        public int DirectEntries { get; set; }
        public int TotalEntries => DirectEntries;
        public DateTime? LastEntryDate { get; set; }
        public bool HasRecentEntries { get; set; } // Within last 30 days
    }
}
```

What changed: deleted line `public int FamilyEntries { get; set; }`; rewrote `TotalEntries` from `DirectEntries + FamilyEntries` to `DirectEntries`. Nothing else moves.

- [ ] **Step 2: Verify no stragglers**

Run:
```bash
cd backend && grep -rn "FamilyEntries" src/Anela.Heblo.Domain/
```
Expected: no output. (FR-1 acceptance criterion.)

- [ ] **Step 3: Build the solution**

Run:
```bash
cd backend && dotnet build
```
Expected: 0 errors. (If the Application project fails to compile because `JournalIndicatorDto` still references `FamilyEntries` on the domain side — it doesn't; they are independent declarations — proceed to Task 3 regardless. The DTO mirror is updated next.)

- [ ] **Step 4: Run tests — Task 1's tests must still pass**

Run:
```bash
cd backend && dotnet test --filter "FullyQualifiedName~JournalRepositoryIntegrationTests.GetJournalIndicatorsAsync"
```
Expected: 3/3 pass. (The assertion `TotalEntries == DirectEntries` continues to hold by construction now.)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs
git commit -m "refactor: drop FamilyEntries from JournalIndicator domain entity"
```

---

## Task 3: Remove `FamilyEntries` from the contract DTO `JournalIndicatorDto`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs`

- [ ] **Step 1: Edit the DTO to mirror the domain entity**

Replace the file body with:

```csharp
using System;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class JournalIndicatorDto
    {
        public string ProductCode { get; set; } = null!;
        public int DirectEntries { get; set; }
        public int TotalEntries => DirectEntries;
        public DateTime? LastEntryDate { get; set; }
        public bool HasRecentEntries { get; set; } // Within last 30 days
    }
}
```

Confirm: still a `class` (not a `record`) — per project rule, DTOs must be classes so the OpenAPI generator orders parameters correctly.

- [ ] **Step 2: Verify no stragglers in Application project**

Run:
```bash
cd backend && grep -rn "FamilyEntries" src/Anela.Heblo.Application/
```
Expected: no output. (FR-2 acceptance criterion.)

- [ ] **Step 3: Build the solution**

Run:
```bash
cd backend && dotnet build
```
Expected: 0 errors, warning count unchanged from baseline.

- [ ] **Step 4: Run the full backend test suite**

Run:
```bash
cd backend && dotnet test
```
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs
git commit -m "refactor: drop FamilyEntries from JournalIndicatorDto contract"
```

---

## Task 4: Clean up `JournalRepository.cs` orphan whitespace

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`

**Why:** Lines 208–211 currently have a double blank line where a never-implemented family-entries query was probably intended. Collapse it. **Do not change** the grouped `directAssociations` query, the `LastEntryDate` assignment loop, or the `HasRecentEntries` 30-day computation.

- [ ] **Step 1: Inspect current lines 204–215**

Run:
```bash
cd backend && sed -n '204,215p' src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
```
Expected:
```
            foreach (var da in directAssociations)
            {
                result[da.ProductCode].DirectEntries = da.Count;
                result[da.ProductCode].LastEntryDate = da.LastEntryDate;
            }


            // Calculate recent entries (within last 30 days)
            var thirtyDaysAgo = DateTime.Today.AddDays(-30);
            foreach (var indicator in result.Values)
            {
                indicator.HasRecentEntries = indicator.LastEntryDate.HasValue &&
```

Note the **two** consecutive blank lines between line 208 (`}`) and line 211 (`// Calculate recent entries...`).

- [ ] **Step 2: Collapse the double blank line into a single blank**

Use Edit to replace:

```csharp
            foreach (var da in directAssociations)
            {
                result[da.ProductCode].DirectEntries = da.Count;
                result[da.ProductCode].LastEntryDate = da.LastEntryDate;
            }


            // Calculate recent entries (within last 30 days)
```

with:

```csharp
            foreach (var da in directAssociations)
            {
                result[da.ProductCode].DirectEntries = da.Count;
                result[da.ProductCode].LastEntryDate = da.LastEntryDate;
            }

            // Calculate recent entries (within last 30 days)
```

Only the blank line removal. No other edits in this file.

- [ ] **Step 3: Run `dotnet format` and audit the diff**

Run:
```bash
cd backend && dotnet format
git diff --stat
```
Expected: changes confined to files this plan touches. If `dotnet format` reformatted unrelated files, revert those hunks:
```bash
git checkout -- <unrelated-file>
```
The spec is explicit about surgical edits; do not import unrelated reformatting under this commit.

- [ ] **Step 4: Build and test**

Run:
```bash
cd backend && dotnet build && dotnet test
```
Expected: 0 build errors; all tests pass (including Task 1's three new tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
git commit -m "chore: collapse orphan blank line in GetJournalIndicatorsAsync"
```

---

## Task 5: Drop dead `using` in the integration test file

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

**Why:** The test file imports `Anela.Heblo.Application.Features.Journal.Contracts` at line 1 but never references any type from that namespace — the new Task 1 tests target the domain `JournalIndicator`, not the DTO. Per arch-review §Specification Amendments #2, drop this dead `using` since the file is already being edited.

- [ ] **Step 1: Confirm `JournalIndicatorDto` is unused in this file**

Run:
```bash
cd backend && grep -n "JournalIndicatorDto" test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
```
Expected: no output. (If anything is returned, abort this step — a later edit accidentally introduced a DTO reference.)

- [ ] **Step 2: Remove the dead import**

Edit `JournalRepositoryIntegrationTests.cs` line 1 — delete the single line:

```csharp
using Anela.Heblo.Application.Features.Journal.Contracts;
```

Resulting top-of-file:

```csharp
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Catalog.Journal;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

- [ ] **Step 3: Build and run tests**

Run:
```bash
cd backend && dotnet build && dotnet test --filter "FullyQualifiedName~JournalRepositoryIntegrationTests"
```
Expected: build succeeds; all 8 tests in the class pass (5 pre-existing + 3 new).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs
git commit -m "chore: drop unused Contracts using in JournalRepositoryIntegrationTests"
```

---

## Task 6: Regenerate the OpenAPI TypeScript client and validate the frontend

**Files:**
- Auto-modified: `frontend/src/api/generated/*` (derived artifacts; do not hand-edit)

**Why:** The OpenAPI schema now omits `familyEntries` from `JournalIndicatorDto`. `npm run build` regenerates the TypeScript client. Any out-of-tree consumer reading `familyEntries` will fail TypeScript compilation here — this is the safety net for the "undiscovered consumer" risk from the arch-review.

- [ ] **Step 1: Build the frontend (regenerates the TS client)**

Run:
```bash
cd frontend && npm run build
```
Expected: build succeeds. If TypeScript compilation fails with errors mentioning `familyEntries`, an undiscovered consumer exists — stop and report. (It must then either be updated to drop `familyEntries` or this plan must be aborted in favor of arch-review path 1 — implementing the property.)

- [ ] **Step 2: Lint the frontend**

Run:
```bash
cd frontend && npm run lint
```
Expected: 0 errors, 0 new warnings. If new warnings show up in generated files, do **not** suppress them by hand-editing generated code; investigate whether the OpenAPI generator's output template needs an update — but only if relevant to this change.

- [ ] **Step 3: Inspect the generated client diff**

Run:
```bash
git diff frontend/src/api/generated/
```
Expected: removal of `familyEntries` field (and possibly `family_entries` if the generator snake-cases) from any `JournalIndicatorDto` model class/interface. No other unrelated changes. If unrelated regeneration drift shows up, it likely reflects unrelated schema or generator-config changes — verify against `main` and revert anything not caused by this property removal.

- [ ] **Step 4: Commit the regenerated client**

```bash
git add frontend/src/api/generated/
git commit -m "chore: regenerate TS client after removing familyEntries"
```

(If the diff is empty — e.g. no client model contained `familyEntries` because the generator inlined it differently — skip the commit and note this in the final summary.)

---

## Task 7: Full validation gates

**Files:** none modified — this task only runs checks.

- [ ] **Step 1: Backend build**

Run:
```bash
cd backend && dotnet build
```
Expected: 0 errors, warning count ≤ baseline.

- [ ] **Step 2: Backend format check**

Run:
```bash
cd backend && dotnet format --verify-no-changes
```
Expected: exit code 0. If it complains, run `dotnet format` and commit the formatting fix as a separate `chore:` commit.

- [ ] **Step 3: Backend tests**

Run:
```bash
cd backend && dotnet test
```
Expected: all tests pass, including the three new `GetJournalIndicatorsAsync_*` tests.

- [ ] **Step 4: Frontend build**

Run:
```bash
cd frontend && npm run build
```
Expected: success.

- [ ] **Step 5: Frontend lint**

Run:
```bash
cd frontend && npm run lint
```
Expected: 0 errors.

- [ ] **Step 6: Final repo-wide `FamilyEntries` sweep**

Run:
```bash
grep -rn "FamilyEntries\|familyEntries" \
  backend/src backend/test frontend/src \
  --include="*.cs" --include="*.ts" --include="*.tsx"
```
Expected: only matches in test method **names** containing the English phrase "FamilyEntries" (e.g. `GetEntriesByProductAsync_WithProductCode_ShouldFindFamilyEntries` at line 61, `GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries` at line 171). These are descriptive names for prefix-matching behavior in `GetEntriesByProductAsync` and are **out of scope** — leave them alone. No `FamilyEntries` property reference anywhere.

- [ ] **Step 7: E2E suite — not required**

No UI surface or new API endpoint exists for this change. The nightly E2E run will exercise any indirect side effects on its next pass. Skip `./scripts/run-playwright-tests.sh` for this PR.

---

## Out of Scope (deferred follow-ups)

These items are explicitly out of scope of this plan but should be filed as separate arch-review items per the spec:

1. **Full dead-code removal** of `JournalIndicator`, `JournalIndicatorDto`, and `GetJournalIndicatorsAsync`. They have no consumer today; this plan only addresses the misleading-property smell per the brief. A follow-up review can decide whether the entire indicator surface should also disappear.
2. **Reintroducing a populated `FamilyEntries`** behind a real consumer. The prefix-matching pattern in `GetEntriesByProductAsync` at `JournalRepository.cs:169` makes this mechanically cheap when needed.
3. **Injectable `IClock` for `HasRecentEntries` boundary testing.** Today's tests deliberately avoid the 30-day boundary because it is wall-clock-coupled. Refactoring to an injected clock would unlock precise boundary coverage but is unrelated to this YAGNI cleanup.

---

## Coverage check (spec → plan)

| Spec requirement | Implemented by |
|------------------|----------------|
| FR-1 — remove `FamilyEntries` from domain, `TotalEntries => DirectEntries` | Task 2 |
| FR-2 — same removal in `JournalIndicatorDto`, regenerated TS client | Task 3 + Task 6 |
| FR-3 — clean up orphan whitespace in `GetJournalIndicatorsAsync`, no semantic change | Task 4 |
| FR-4 — add three integration tests; AAA + FluentAssertions; no exact `LastEntryDate` boundary | Task 1 |
| Arch-review §Amendments #2 — drop dead `using ...Contracts;` | Task 5 |
| NFR-1 — backward compatibility checked via grep + TS client regen | Pre-flight + Task 6 |
| NFR-2 — performance unchanged (no new query) | Task 4 (no semantic change) |
| NFR-3 — maintainability (no speculative property) | Tasks 2 + 3 |
| NFR-4 — `dotnet build`, `dotnet format`, `npm run build`, `npm run lint` all green | Task 7 |
