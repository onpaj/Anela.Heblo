# Fix `MarketingAction.AssociateWithProduct` Duplicate Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `MarketingAction.AssociateWithProduct` deduplicate against the same normalized value it persists, so mixed-case duplicate product codes no longer crash on `SaveChanges` with a primary-key violation.

**Architecture:** Single-file domain-layer change inside the `MarketingAction` aggregate root. Compute `productCode.Trim().ToUpperInvariant()` once at the top of the method and use that value for both the `Any()` duplicate-check guard and the `ProductCodePrefix` field of the new `MarketingActionProduct`. No layer boundaries crossed, no contracts changed, no migration. A new xUnit + FluentAssertions test class — colocated with the existing `MarketingActionSyncTests` — covers the fixed behaviour and the empty-input contract.

**Tech Stack:** .NET 8 / C# 12, xUnit, FluentAssertions. No mocks (pure domain logic, no dependencies).

---

## File Structure

**Files to modify:**
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` (lines 71–85) — move the `Trim().ToUpperInvariant()` call out of the `Add` initializer to a local `normalized` variable computed before the duplicate-check guard; compare against `normalized` inside `Any()`.

**Files to create:**
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs` — new xUnit test class colocated with `MarketingActionSyncTests.cs`, following the same conventions (xUnit + FluentAssertions, shared `CreateAction()` factory pattern, no mocks).

**Files explicitly NOT touched:**
- `MarketingActionProduct.cs` — schema and composite key unchanged.
- `CreateMarketingActionHandler.cs`, `UpdateMarketingActionHandler.cs`, `ImportFromOutlookHandler.cs` — application-layer handlers do not need refactoring (out of scope per arch review).
- Any EF Core configuration, migration, or DTO — schema-level fixes are explicitly out of scope.
- Any frontend or controller code — the API contract does not change.
- `LinkToFolder` sibling method — folder-key normalization is out of scope.

---

## Task 1: Document the bug with a failing TDD test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs`

This task establishes the test scaffold and the **single failing test that proves the bug exists** before we touch production code. The test asserts the realistic in-loop scenario from the arch review: a single handler call passes both `"abc"` and `"ABC"` in sequence; today this crashes at `SaveChanges` because the entity adds two rows.

- [ ] **Step 1: Create the test file with the bug-reproducer test only**

Create `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs`:

```csharp
using System;
using System.Linq;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionAssociateWithProductTests
    {
        private static MarketingAction CreateAction()
        {
            return new MarketingAction
            {
                Title = "Test Action",
                StartDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                CreatedByUserId = "user-1",
            };
        }

        [Fact]
        public void AssociateWithProduct_DeduplicatesAcrossCase_WhenCalledTwiceWithMixedCasing()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.AssociateWithProduct("abc");
            action.AssociateWithProduct("ABC");

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            action.ProductAssociations.Single().ProductCodePrefix.Should().Be("ABC");
        }
    }
}
```

- [ ] **Step 2: Run the new test to verify it fails for the expected reason**

Run from the worktree root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~MarketingActionAssociateWithProductTests" \
    --nologo --verbosity minimal
```

Expected: FAIL with a FluentAssertions message similar to
`Expected action.ProductAssociations to contain 1 item(s), but found 2`.

If the test passes here, STOP — the production bug is not what we think it is and the plan needs re-validation against the actual code.

- [ ] **Step 3: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs
git commit -m "test: add failing case-insensitive dedup test for AssociateWithProduct"
```

---

## Task 2: Fix the duplicate-check guard

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs:71-85`

Replace the `AssociateWithProduct` body so a single `normalized` local drives both the duplicate-check `Any()` predicate and the `ProductCodePrefix` value of the new `MarketingActionProduct`. The empty-input guard already exists at lines 73–74 and stays untouched.

- [ ] **Step 1: Apply the fix**

Open `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`. Replace the method body (lines 71–85) so the final shape is:

```csharp
public void AssociateWithProduct(string productCode)
{
    if (string.IsNullOrWhiteSpace(productCode))
        throw new ArgumentException("Product code cannot be empty", nameof(productCode));

    var normalized = productCode.Trim().ToUpperInvariant();

    if (ProductAssociations.Any(pa => pa.ProductCodePrefix == normalized))
        return;

    ProductAssociations.Add(new MarketingActionProduct
    {
        MarketingActionId = Id,
        ProductCodePrefix = normalized,
        CreatedAt = DateTime.UtcNow,
    });
}
```

Diff summary (three changes):
1. Insert `var normalized = productCode.Trim().ToUpperInvariant();` before the `Any()` guard.
2. Change the `Any()` predicate from `pa => pa.ProductCodePrefix == productCode` to `pa => pa.ProductCodePrefix == normalized`.
3. Change `ProductCodePrefix = productCode.Trim().ToUpperInvariant()` inside the `Add(...)` initializer to `ProductCodePrefix = normalized`.

- [ ] **Step 2: Run the failing test again to verify it now passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~MarketingActionAssociateWithProductTests" \
    --nologo --verbosity minimal
```

Expected: 1 passed, 0 failed.

- [ ] **Step 3: Run the full Marketing domain test suite to confirm no regression**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Anela.Heblo.Tests.Domain.Marketing" \
    --nologo --verbosity minimal
```

Expected: all tests pass (existing `MarketingActionSyncTests` cover `MarkOutlookSynced` / `ClearOutlookLink`; the fix does not affect them).

- [ ] **Step 4: Commit the fix**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs
git commit -m "fix(marketing): deduplicate AssociateWithProduct against normalized value"
```

---

## Task 3: Add complete behavioural coverage for the fixed method

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs`

Expand the test class to cover every acceptance criterion from FR-1, FR-2, and FR-3 in `spec.r1.md`. Each test is small, behaviour-named, and uses Arrange-Act-Assert.

- [ ] **Step 1: Append the remaining tests to the test class**

Open `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs` and add the following methods inside the existing `MarketingActionAssociateWithProductTests` class (after `AssociateWithProduct_DeduplicatesAcrossCase_WhenCalledTwiceWithMixedCasing`, before the closing brace):

```csharp
        [Fact]
        public void AssociateWithProduct_NoOps_WhenLowercaseDuplicateOfExistingUppercase()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("ABC");
            var snapshotBefore = action.ProductAssociations.Single();

            // Act
            action.AssociateWithProduct("abc");

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            action.ProductAssociations.Single().Should().BeSameAs(snapshotBefore);
        }

        [Fact]
        public void AssociateWithProduct_NoOps_WhenWhitespacePaddedLowercaseDuplicateOfExistingUppercase()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("ABC");
            var snapshotBefore = action.ProductAssociations.Single();

            // Act
            action.AssociateWithProduct(" abc ");

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            action.ProductAssociations.Single().Should().BeSameAs(snapshotBefore);
        }

        [Fact]
        public void AssociateWithProduct_AddsNormalizedRow_WhenInputIsNewCode()
        {
            // Arrange
            var action = CreateAction();
            action.Id = 42;

            // Act
            action.AssociateWithProduct("xyz");

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            var added = action.ProductAssociations.Single();
            added.ProductCodePrefix.Should().Be("XYZ");
            added.MarketingActionId.Should().Be(42);
            added.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void AssociateWithProduct_NormalizesPaddedMixedCaseInput_WhenNoExistingAssociation()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.AssociateWithProduct("  aBc  ");

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            action.ProductAssociations.Single().ProductCodePrefix.Should().Be("ABC");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void AssociateWithProduct_Throws_WhenInputIsNullEmptyOrWhitespace(string? input)
        {
            // Arrange
            var action = CreateAction();

            // Act
            Action act = () => action.AssociateWithProduct(input!);

            // Assert
            act.Should()
                .Throw<ArgumentException>()
                .Which.ParamName.Should().Be("productCode");
            action.ProductAssociations.Should().BeEmpty();
        }
```

The final file has six test methods total (the original bug reproducer plus five appended here).

- [ ] **Step 2: Run the new test class to confirm everything passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~MarketingActionAssociateWithProductTests" \
    --nologo --verbosity minimal
```

Expected: 8 passed (1 `[Fact]` from Task 1 + 4 new `[Fact]`s + 3 cases of the `[Theory]`), 0 failed.

- [ ] **Step 3: Commit the expanded coverage**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs
git commit -m "test(marketing): cover empty input + add cases for AssociateWithProduct"
```

---

## Task 4: Validation gates

**Files:** None modified.

Run the project-wide validation gates required by `CLAUDE.md` before declaring the task done. These confirm the fix integrates cleanly with the rest of the backend (build, format, all relevant tests).

- [ ] **Step 1: Format C# files touched by the change**

```bash
dotnet format backend/Anela.Heblo.sln --include \
    backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs \
    backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs
```

Expected: no errors. If `dotnet format` rewrites either file, re-run Task 3 step 2 to confirm tests still pass, then `git add` the formatted files and amend the most recent commit with `git commit --amend --no-edit`.

- [ ] **Step 2: Build the whole backend**

```bash
dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal
```

Expected: `Build succeeded. 0 Error(s)`. Warnings count must not increase compared to the baseline before this change.

- [ ] **Step 3: Run the full Marketing application + domain test scope**

The arch review identifies `CreateMarketingActionHandler` and `UpdateMarketingActionHandler` as the real callers. Run both their tests and the domain tests to confirm the behavioural change (silent no-op vs. previous `DbUpdateException`) does not break any existing handler test.

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Marketing" \
    --nologo --verbosity minimal
```

Expected: all tests pass. If any test relied on duplicate codes raising an exception, treat that as evidence of a pre-existing test contract and surface it for review **before** modifying that test — do not silently rewrite assertions to fit the new behaviour.

- [ ] **Step 4: Run the full backend test suite as the final gate**

```bash
dotnet test backend/Anela.Heblo.sln --nologo --verbosity minimal
```

Expected: all tests pass. This catches any indirect coupling (Architecture tests, reflection-based tests, etc.) that might be affected.

- [ ] **Step 5: Confirm the working tree is clean and commits are in order**

```bash
git status
git log --oneline -5
```

Expected: `git status` reports a clean tree; `git log` shows the three commits from Tasks 1–3 in chronological order at the top of the branch (plus the Task 4 amend if formatting required one).

---

## Out-of-Scope Follow-ups (DO NOT implement in this PR)

Captured here so a future reviewer can see they were considered and deliberately deferred. Each is a separate, smaller PR if and when needed.

1. **Switch handler-side `request.AssociatedProducts.Distinct()` to `Distinct(StringComparer.OrdinalIgnoreCase)`** in `CreateMarketingActionHandler` and `UpdateMarketingActionHandler`. Pure perf/clarity — correctness is now fully owned by the domain entity.
2. **Apply the same normalize-then-compare fix to `MarketingAction.LinkToFolder`** if folder-key duplication ever surfaces. Today the spec is explicit about scope.
3. **Refactor `UpdateMarketingActionHandler`'s `Clear()` + re-add pattern** to a diff-based merge if EF Core change-tracking or audit-trail behaviour becomes a concern.
4. **Add a case-insensitive DB collation or EF value converter** for `ProductCodePrefix` if a future change ever bypasses the aggregate. Larger schema-surface change; not justified today.
