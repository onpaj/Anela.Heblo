# JournalEntry Replace Semantics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the raw `.Clear()` + per-item add pattern in `UpdateJournalEntryHandler` with two new tell-don't-ask domain methods on `JournalEntry` — `ReplaceProductAssociations` and `ReplaceTagAssignments` — that perform set-diff replacement, preserve unchanged child rows, and validate input atomically.

**Architecture:** Adds two `public void` methods to the rich `JournalEntry` aggregate using a set-diff algorithm (`HashSet<T>` keyed on `ProductCodePrefix` / `TagId`). Extracts a single private `NormalizeProductCode` helper used by both `AssociateWithProduct` and the new replace method to keep normalization rules DRY. The handler shrinks from ~20 lines of mutation logic to two intent-revealing calls. No schema, contract, or DI changes.

**Tech Stack:** .NET 8, EF Core (change tracker handles child add/remove via composite PKs), xUnit + FluentAssertions for tests.

---

## File Structure

**Modify:**
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs` — add `NormalizeProductCode` private helper, refactor `AssociateWithProduct` to use it, add `ReplaceProductAssociations` and `ReplaceTagAssignments`.
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs` — replace lines 61–80 with two domain method calls.

**Create:**
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs` — xUnit tests for both new domain methods plus `NormalizeProductCode` behavior surfaced via `AssociateWithProduct` to confirm no regression.

**Validation commands (from worktree root):**
- `dotnet build backend/Anela.Heblo.sln`
- `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
- `dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Journal"`

---

## Task 1: Extract `NormalizeProductCode` helper and refactor `AssociateWithProduct`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs:55-68`

This task is a pure non-behavioral refactor. It introduces the helper that Task 2 depends on and verifies the existing `AssociateWithProduct` contract is unchanged.

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Journal;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class JournalEntryTests
{
    private static JournalEntry NewEntry() => new JournalEntry
    {
        Id = 1,
        Content = "test",
        EntryDate = DateTime.UtcNow.Date,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        CreatedByUserId = "user-1"
    };

    // ----- AssociateWithProduct: existing-behavior regression coverage -----

    [Fact]
    public void AssociateWithProduct_NormalizesCodeToTrimmedUpper()
    {
        var entry = NewEntry();

        entry.AssociateWithProduct("  ab-1  ");

        entry.ProductAssociations.Should().ContainSingle()
            .Which.ProductCodePrefix.Should().Be("AB-1");
    }

    [Fact]
    public void AssociateWithProduct_ThrowsOnWhitespaceCode()
    {
        var entry = NewEntry();

        var act = () => entry.AssociateWithProduct("   ");

        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run tests to verify the regression test passes against the current code**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~JournalEntryTests"`

Expected: Both tests **PASS** (`AssociateWithProduct` already trims/uppers and throws on whitespace).

Note: The current `AssociateWithProduct` compares the raw input against existing `ProductCodePrefix` values before normalizing. The "NormalizesCodeToTrimmedUpper" test passes today only because nothing matches on first add; we will fix the order-of-operations bug as part of the helper extraction in step 3.

- [ ] **Step 3: Extract `NormalizeProductCode` and refactor `AssociateWithProduct`**

Edit `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs` — replace the existing `AssociateWithProduct` (lines 55–68) with:

```csharp
        // Domain methods
        public void AssociateWithProduct(string productCode)
        {
            var normalized = NormalizeProductCode(productCode);

            if (ProductAssociations.Any(pa => pa.ProductCodePrefix == normalized))
                return; // Already associated

            ProductAssociations.Add(new JournalEntryProduct
            {
                JournalEntryId = Id,
                ProductCodePrefix = normalized
            });
        }

        private static string NormalizeProductCode(string? productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                throw new ArgumentException("Product code cannot be empty", nameof(productCode));

            return productCode.Trim().ToUpperInvariant();
        }
```

- [ ] **Step 4: Run tests to verify they still pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~JournalEntryTests"`

Expected: PASS.

- [ ] **Step 5: Run the full journal test suite to catch any regression in handler-level tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal"`

Expected: All existing journal tests PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs \
        backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs
git commit -m "refactor: extract NormalizeProductCode helper on JournalEntry"
```

---

## Task 2: Add `ReplaceProductAssociations` to `JournalEntry`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs`

Adds the first replace method. Validates the whole input set before mutating (so invalid input leaves the entity untouched). Set-diffs against existing associations keyed on `ProductCodePrefix` so unchanged rows keep their identity and `CreatedAt`.

- [ ] **Step 1: Append failing tests for `ReplaceProductAssociations` to `JournalEntryTests.cs`**

Append inside the `JournalEntryTests` class (after the existing tests):

```csharp
    // ----- ReplaceProductAssociations -----

    [Fact]
    public void ReplaceProductAssociations_WithNull_ClearsAll()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("A");
        entry.AssociateWithProduct("B");

        entry.ReplaceProductAssociations(null);

        entry.ProductAssociations.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceProductAssociations_WithEmpty_ClearsAll()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("A");

        entry.ReplaceProductAssociations(Array.Empty<string>());

        entry.ProductAssociations.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceProductAssociations_WithSuperset_AddsMissingCodes()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("A");

        entry.ReplaceProductAssociations(new[] { "A", "B", "C" });

        entry.ProductAssociations.Select(p => p.ProductCodePrefix)
            .Should().BeEquivalentTo(new[] { "A", "B", "C" });
    }

    [Fact]
    public void ReplaceProductAssociations_WithDisjointSet_RemovesOldAndAddsNew()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("A");
        entry.AssociateWithProduct("B");

        entry.ReplaceProductAssociations(new[] { "C", "D" });

        entry.ProductAssociations.Select(p => p.ProductCodePrefix)
            .Should().BeEquivalentTo(new[] { "C", "D" });
    }

    [Fact]
    public void ReplaceProductAssociations_WithOverlap_PreservesExistingInstance()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("X");
        entry.AssociateWithProduct("Y");
        var originalX = entry.ProductAssociations.Single(p => p.ProductCodePrefix == "X");

        entry.ReplaceProductAssociations(new[] { "X" });

        entry.ProductAssociations.Should().ContainSingle()
            .Which.Should().BeSameAs(originalX);
    }

    [Fact]
    public void ReplaceProductAssociations_WithDifferentCaseOverlap_PreservesExistingInstance()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("X");
        entry.AssociateWithProduct("Y");
        var originalX = entry.ProductAssociations.Single(p => p.ProductCodePrefix == "X");

        entry.ReplaceProductAssociations(new[] { "x" });

        entry.ProductAssociations.Should().ContainSingle()
            .Which.Should().BeSameAs(originalX);
    }

    [Fact]
    public void ReplaceProductAssociations_DedupesCaseAndWhitespaceInsensitively()
    {
        var entry = NewEntry();

        entry.ReplaceProductAssociations(new[] { "AB-1", "ab-1", "  AB-1  " });

        entry.ProductAssociations.Should().ContainSingle()
            .Which.ProductCodePrefix.Should().Be("AB-1");
    }

    [Fact]
    public void ReplaceProductAssociations_WithWhitespaceItem_ThrowsAndLeavesStateUnchanged()
    {
        var entry = NewEntry();
        entry.AssociateWithProduct("A");
        var snapshot = entry.ProductAssociations.ToList();

        var act = () => entry.ReplaceProductAssociations(new[] { "B", "   ", "C" });

        act.Should().Throw<ArgumentException>();
        entry.ProductAssociations.Should().BeEquivalentTo(snapshot);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReplaceProductAssociations"`

Expected: Compilation FAIL — `ReplaceProductAssociations` does not exist on `JournalEntry`.

- [ ] **Step 3: Implement `ReplaceProductAssociations` on `JournalEntry`**

Edit `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs` — add after `AssociateWithProduct` and before `AssignTag`:

```csharp
        public void ReplaceProductAssociations(IEnumerable<string>? productCodes)
        {
            // Validate-then-mutate: normalize the entire incoming set first.
            // Any whitespace-only entry throws before any mutation occurs.
            var targetCodes = new HashSet<string>(StringComparer.Ordinal);
            if (productCodes != null)
            {
                foreach (var raw in productCodes)
                {
                    targetCodes.Add(NormalizeProductCode(raw));
                }
            }

            var toRemove = ProductAssociations
                .Where(pa => !targetCodes.Contains(pa.ProductCodePrefix))
                .ToList();
            foreach (var association in toRemove)
            {
                ProductAssociations.Remove(association);
            }

            var existingCodes = new HashSet<string>(
                ProductAssociations.Select(pa => pa.ProductCodePrefix),
                StringComparer.Ordinal);
            foreach (var code in targetCodes)
            {
                if (existingCodes.Contains(code))
                    continue;

                ProductAssociations.Add(new JournalEntryProduct
                {
                    JournalEntryId = Id,
                    ProductCodePrefix = code
                });
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReplaceProductAssociations"`

Expected: All 8 `ReplaceProductAssociations_*` tests PASS.

- [ ] **Step 5: Re-run the broader journal suite to check for regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal"`

Expected: All journal tests PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs \
        backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs
git commit -m "feat(journal): add ReplaceProductAssociations domain method"
```

---

## Task 3: Add `ReplaceTagAssignments` to `JournalEntry`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs`

Same algorithm as Task 2, keyed on `TagId`. No validation beyond deduplication (parity with existing `AssignTag`).

- [ ] **Step 1: Append failing tests for `ReplaceTagAssignments`**

Append inside `JournalEntryTests`:

```csharp
    // ----- ReplaceTagAssignments -----

    [Fact]
    public void ReplaceTagAssignments_WithNull_ClearsAll()
    {
        var entry = NewEntry();
        entry.AssignTag(1);
        entry.AssignTag(2);

        entry.ReplaceTagAssignments(null);

        entry.TagAssignments.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceTagAssignments_WithEmpty_ClearsAll()
    {
        var entry = NewEntry();
        entry.AssignTag(1);

        entry.ReplaceTagAssignments(Array.Empty<int>());

        entry.TagAssignments.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceTagAssignments_WithSuperset_AddsMissingIds()
    {
        var entry = NewEntry();
        entry.AssignTag(1);

        entry.ReplaceTagAssignments(new[] { 1, 2, 3 });

        entry.TagAssignments.Select(t => t.TagId)
            .Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void ReplaceTagAssignments_WithDisjointSet_RemovesOldAndAddsNew()
    {
        var entry = NewEntry();
        entry.AssignTag(1);
        entry.AssignTag(2);

        entry.ReplaceTagAssignments(new[] { 3, 4 });

        entry.TagAssignments.Select(t => t.TagId)
            .Should().BeEquivalentTo(new[] { 3, 4 });
    }

    [Fact]
    public void ReplaceTagAssignments_WithOverlap_PreservesExistingInstance()
    {
        var entry = NewEntry();
        entry.AssignTag(1);
        entry.AssignTag(2);
        var originalOne = entry.TagAssignments.Single(t => t.TagId == 1);

        entry.ReplaceTagAssignments(new[] { 1 });

        entry.TagAssignments.Should().ContainSingle()
            .Which.Should().BeSameAs(originalOne);
    }

    [Fact]
    public void ReplaceTagAssignments_DedupesDuplicateIds()
    {
        var entry = NewEntry();

        entry.ReplaceTagAssignments(new[] { 1, 1, 2 });

        entry.TagAssignments.Select(t => t.TagId)
            .Should().BeEquivalentTo(new[] { 1, 2 });
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReplaceTagAssignments"`

Expected: Compilation FAIL — `ReplaceTagAssignments` does not exist on `JournalEntry`.

- [ ] **Step 3: Implement `ReplaceTagAssignments` on `JournalEntry`**

Edit `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs` — add after `AssignTag`:

```csharp
        public void ReplaceTagAssignments(IEnumerable<int>? tagIds)
        {
            var targetIds = tagIds != null
                ? new HashSet<int>(tagIds)
                : new HashSet<int>();

            var toRemove = TagAssignments
                .Where(ta => !targetIds.Contains(ta.TagId))
                .ToList();
            foreach (var assignment in toRemove)
            {
                TagAssignments.Remove(assignment);
            }

            var existingIds = new HashSet<int>(TagAssignments.Select(ta => ta.TagId));
            foreach (var tagId in targetIds)
            {
                if (existingIds.Contains(tagId))
                    continue;

                TagAssignments.Add(new JournalEntryTagAssignment
                {
                    JournalEntryId = Id,
                    TagId = tagId
                });
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReplaceTagAssignments"`

Expected: All 6 `ReplaceTagAssignments_*` tests PASS.

- [ ] **Step 5: Re-run full journal suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal"`

Expected: All journal tests PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs \
        backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs
git commit -m "feat(journal): add ReplaceTagAssignments domain method"
```

---

## Task 4: Wire the new domain methods into `UpdateJournalEntryHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs:61-80`

Replaces the `.Clear()` + foreach blocks with two domain-method calls.

- [ ] **Step 1: Replace lines 61–80 with two domain calls**

Edit `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs`.

Find this block (lines 61–80):

```csharp
            // Update product associations (both products and families)
            entry.ProductAssociations.Clear();
            if (request.AssociatedProducts?.Any() == true)
            {
                foreach (var productIdentifier in request.AssociatedProducts.Distinct())
                {
                    // Try as full product code first, then as prefix
                    entry.AssociateWithProduct(productIdentifier);
                }
            }

            // Update tag assignments
            entry.TagAssignments.Clear();
            if (request.TagIds?.Any() == true)
            {
                foreach (var tagId in request.TagIds.Distinct())
                {
                    entry.AssignTag(tagId);
                }
            }
```

Replace with:

```csharp
            entry.ReplaceProductAssociations(request.AssociatedProducts);
            entry.ReplaceTagAssignments(request.TagIds);
```

- [ ] **Step 2: Verify the handler builds**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

Expected: Build succeeds.

- [ ] **Step 3: Verify no `.Clear()` reference remains on these collections in the handler**

Run via Grep tool on `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs` for the pattern `(ProductAssociations|TagAssignments)\.Clear`.

Expected: No matches.

- [ ] **Step 4: Run the full journal test suite to confirm handler behavior is preserved**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Journal"`

Expected: All journal tests PASS (including `JournalRepositoryIntegrationTests` and any existing `UpdateJournalEntryHandler` tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs
git commit -m "refactor(journal): use Replace* domain methods in UpdateJournalEntryHandler"
```

---

## Task 5: Final validation gates

**Files:** None modified — verification only.

- [ ] **Step 1: Run full solution build**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: Build succeeds with no errors or new warnings.

- [ ] **Step 2: Run `dotnet format` verification**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`

Expected: Exit code 0, no formatting violations.

If violations are reported, run `dotnet format backend/Anela.Heblo.sln` to fix them, then re-run with `--verify-no-changes` and commit the formatting fixes:

```bash
git add -A
git commit -m "style: dotnet format"
```

- [ ] **Step 3: Run the full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`

Expected: All tests PASS.

- [ ] **Step 4: Confirm no unexpected files changed**

Run: `git status` and `git diff --stat main...HEAD`

Expected: Only the three files in scope (`JournalEntry.cs`, `UpdateJournalEntryHandler.cs`, `JournalEntryTests.cs`) appear in the diff. No migrations, no contract changes, no DI changes.

---

## Spec Coverage Check

| Spec requirement | Implemented in |
|---|---|
| FR-1 `ReplaceProductAssociations` signature + behavior (null/empty clears, dedupe, normalize, whitespace throws, instance preservation) | Task 2 |
| FR-2 `ReplaceTagAssignments` signature + behavior (null/empty clears, dedupe, instance preservation, no tag-existence validation) | Task 3 |
| FR-3 Handler uses the new methods, no `.Clear()` references remain | Task 4 |
| FR-4 Unit-test coverage for both domain methods (all acceptance bullets) | Tasks 2 and 3 |
| FR-5 Existing tests still pass; no new test infrastructure | Tasks 1–5 (existing tests are run at each stage) |
| NFR-1 O(n+m) set-diff via `HashSet<T>` | Tasks 2 and 3 |
| NFR-2 No auth/security change | (no edits to handler auth path) |
| NFR-3 No public API/schema/contract change | (no edits to contracts, EF configurations, migrations) |
| NFR-4 `dotnet build` and `dotnet format` clean | Task 5 |
| Arch-review Decision 2 — single `NormalizeProductCode` helper | Task 1 |
| Arch-review Decision 3 — validate-then-mutate (state unchanged on invalid input) | Task 2, test `ReplaceProductAssociations_WithWhitespaceItem_ThrowsAndLeavesStateUnchanged` |
| Arch-review amendment 5 — case-insensitive overlap preserves instance | Task 2, test `ReplaceProductAssociations_WithDifferentCaseOverlap_PreservesExistingInstance` |
