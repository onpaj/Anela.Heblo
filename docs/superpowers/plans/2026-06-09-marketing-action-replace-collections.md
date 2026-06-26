# MarketingAction Encapsulate Collection Replacement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop mutating `MarketingAction`'s EF Core navigation collections from the Application layer by adding domain-owned `ReplaceProductAssociations` and `ReplaceFolderLinks` methods on the aggregate root, then refactor `UpdateMarketingActionHandler` to delegate to them.

**Architecture:** Two new pure-domain methods on `MarketingAction` accept a sequence and a `utcNow` timestamp, normalize/deduplicate inputs, clear the existing tracked collection, and repopulate with new child entities. EF Core change tracking continues to emit identical DELETE+INSERT SQL because the navigation properties remain `virtual ICollection<>`. The handler stops calling `.Clear()`/`AssociateWithProduct`/`LinkToFolder` and instead delegates to the new methods.

**Tech Stack:** .NET 8, C#, Entity Framework Core, xUnit, FluentAssertions, Moq, MediatR.

---

## File Structure

| Action | Path |
|---|---|
| Modify | `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — add `ReplaceProductAssociations` and `ReplaceFolderLinks` methods |
| Modify | `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` — replace lines 95–111 with two delegated calls |
| Create | `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs` — pure-domain unit tests |
| Create | `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs` — pure-domain unit tests |
| Modify | `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` — add tests asserting clearing semantics through the handler |

No new files outside test additions, no namespace changes, no new dependencies.

---

## Conventions Recap

Read once before starting; they apply to every task below.

- **Time abstraction:** None exists in this codebase. The Application handler captures `var now = DateTime.UtcNow;` (line 59 of `UpdateMarketingActionHandler.cs`) and the domain methods accept `DateTime utcNow` parameters — same pattern as `MarketingAction.UpdateDetails` and `MarkOutlookSynced`.
- **Validation:** Per-entry whitespace/empty/null inside the input sequence throws `ArgumentException`, matching the existing `AssociateWithProduct` and `LinkToFolder` single-add methods. A `null` *sequence* is treated as an empty sequence (clears the collection).
- **Dedup key for folder links:** Composite `(folderKey, folderType)` (this differs intentionally from `LinkToFolder`, which dedupes by `folderKey` alone — document this asymmetry in XML doc comments). Out of scope to harmonize the existing method.
- **Dedup key for products:** Normalized (trim + invariant-upper) string.
- **Normalization:** Product codes — `Trim()` then `ToUpperInvariant()`. Folder keys — `Trim()` only (no case folding, matching `LinkToFolder`).
- **Encapsulation surface:** `ProductAssociations` / `FolderLinks` setters remain public (`get; set;`). Out of scope to lock them down.
- **File style:** Block-scoped namespaces, usings inside namespace block, private methods below public ones — match the existing `MarketingAction.cs` layout.
- **Test patterns:** xUnit + FluentAssertions. Use `MarketingActionTestBuilder` from `Anela.Heblo.Tests.Domain.Marketing` to construct entities. Use AAA (Arrange / Act / Assert) — see `MarketingActionAssociateWithProductTests.cs` as the canonical reference.

---

## Task 1: Add `ReplaceProductAssociations` failing test — empty input clears existing

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs`

- [ ] **Step 1: Write the failing test file**

Create the test file with a single failing test that proves the method exists and clears existing associations when given an empty input.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionReplaceProductAssociationsTests
    {
        private static readonly DateTime UtcNow = new(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc);

        private static MarketingAction CreateAction() =>
            new MarketingActionTestBuilder()
                .WithId(1)
                .WithTitle("Test Action")
                .WithStartDate(UtcNow)
                .WithCreatedAt(UtcNow)
                .WithModifiedAt(UtcNow)
                .WithCreatedBy("user-1")
                .Build();

        [Fact]
        public void ReplaceProductAssociations_ClearsExisting_WhenInputIsEmpty()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("ABC");
            action.AssociateWithProduct("XYZ");
            action.ProductAssociations.Should().HaveCount(2);

            // Act
            action.ReplaceProductAssociations(Enumerable.Empty<string>(), UtcNow);

            // Assert
            action.ProductAssociations.Should().BeEmpty();
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionReplaceProductAssociationsTests" --no-restore`

Expected: COMPILATION FAILURE — `MarketingAction` does not contain a definition for `ReplaceProductAssociations`.

- [ ] **Step 3: Add the minimal `ReplaceProductAssociations` method to `MarketingAction`**

File: `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`

Insert the following method immediately after `LinkToFolder` (after line 127, before `SoftDelete`):

```csharp
        /// <summary>
        /// Atomically replaces the full set of product associations.
        /// A <c>null</c> sequence is treated as empty (clears all associations).
        /// Each entry is trimmed and upper-cased (invariant) before dedup.
        /// Throws <see cref="ArgumentException"/> if any entry is null, empty, or whitespace.
        /// </summary>
        public void ReplaceProductAssociations(IEnumerable<string>? productCodes, DateTime utcNow)
        {
            var normalized = new List<string>();
            if (productCodes != null)
            {
                foreach (var raw in productCodes)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        throw new ArgumentException("Product code cannot be empty", nameof(productCodes));

                    var code = raw.Trim().ToUpperInvariant();
                    if (!normalized.Contains(code))
                        normalized.Add(code);
                }
            }

            ProductAssociations.Clear();
            foreach (var code in normalized)
            {
                ProductAssociations.Add(new MarketingActionProduct
                {
                    MarketingActionId = Id,
                    ProductCodePrefix = code,
                    CreatedAt = utcNow,
                });
            }
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionReplaceProductAssociationsTests" --no-restore`

Expected: PASS — 1 test passed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs \
        backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs
git commit -m "feat(marketing): add ReplaceProductAssociations to MarketingAction"
```

---

## Task 2: `ReplaceProductAssociations` — null input clears existing

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs`

- [ ] **Step 1: Add the failing test**

Append inside the existing class:

```csharp
        [Fact]
        public void ReplaceProductAssociations_ClearsExisting_WhenInputIsNull()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("ABC");

            // Act
            action.ReplaceProductAssociations(null, UtcNow);

            // Assert
            action.ProductAssociations.Should().BeEmpty();
        }
```

- [ ] **Step 2: Run test**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName=Anela.Heblo.Tests.Domain.Marketing.MarketingActionReplaceProductAssociationsTests.ReplaceProductAssociations_ClearsExisting_WhenInputIsNull" --no-restore`

Expected: PASS (the implementation from Task 1 already handles `null`).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs
git commit -m "test(marketing): cover null input on ReplaceProductAssociations"
```

---

## Task 3: `ReplaceProductAssociations` — normalization + dedup

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs`

- [ ] **Step 1: Add the failing test**

```csharp
        [Fact]
        public void ReplaceProductAssociations_NormalizesAndDeduplicates_AcrossCaseAndWhitespace()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.ReplaceProductAssociations(new[] { "abc", "ABC", " abc " }, UtcNow);

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            action.ProductAssociations.Single().ProductCodePrefix.Should().Be("ABC");
        }
```

- [ ] **Step 2: Run test**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName=Anela.Heblo.Tests.Domain.Marketing.MarketingActionReplaceProductAssociationsTests.ReplaceProductAssociations_NormalizesAndDeduplicates_AcrossCaseAndWhitespace" --no-restore`

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs
git commit -m "test(marketing): cover normalize+dedup on ReplaceProductAssociations"
```

---

## Task 4: `ReplaceProductAssociations` — throws on per-entry null/empty/whitespace

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs`

- [ ] **Step 1: Add the failing test**

```csharp
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ReplaceProductAssociations_Throws_WhenSequenceContainsNullEmptyOrWhitespace(string? badEntry)
        {
            // Arrange
            var action = CreateAction();

            // Act
            Action act = () =>
                action.ReplaceProductAssociations(new[] { "GOOD", badEntry! }, UtcNow);

            // Assert
            act.Should().Throw<ArgumentException>()
                .Which.ParamName.Should().Be("productCodes");
        }
```

- [ ] **Step 2: Run test**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReplaceProductAssociations_Throws_WhenSequenceContainsNullEmptyOrWhitespace" --no-restore`

Expected: PASS (validation already implemented in Task 1).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs
git commit -m "test(marketing): assert ReplaceProductAssociations throws on invalid entries"
```

---

## Task 5: `ReplaceProductAssociations` — delta scenario (kept + added + removed)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs`

- [ ] **Step 1: Add the failing test**

```csharp
        [Fact]
        public void ReplaceProductAssociations_ReplacesEntireSet_OnDeltaInput()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("KEEP");
            action.AssociateWithProduct("REMOVE");

            // Act — KEEP stays (re-supplied), REMOVE goes away, ADD is new
            action.ReplaceProductAssociations(new[] { "KEEP", "ADD" }, UtcNow);

            // Assert
            action.ProductAssociations
                .Select(p => p.ProductCodePrefix)
                .Should().BeEquivalentTo(new[] { "KEEP", "ADD" });
        }

        [Fact]
        public void ReplaceProductAssociations_UsesProvidedUtcNowOnAllNewRows()
        {
            // Arrange
            var action = CreateAction();
            var moment = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);

            // Act
            action.ReplaceProductAssociations(new[] { "A", "B" }, moment);

            // Assert
            action.ProductAssociations.Should().OnlyContain(p => p.CreatedAt == moment);
        }

        [Fact]
        public void ReplaceProductAssociations_SetsMarketingActionIdOnNewRows()
        {
            // Arrange
            var action = CreateAction();
            action.Id = 99;

            // Act
            action.ReplaceProductAssociations(new[] { "A" }, UtcNow);

            // Assert
            action.ProductAssociations.Single().MarketingActionId.Should().Be(99);
        }
```

- [ ] **Step 2: Run all `ReplaceProductAssociations` tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionReplaceProductAssociationsTests" --no-restore`

Expected: PASS — all 7 tests (1 from Task 1 + 1 from Task 2 + 1 from Task 3 + 3 InlineData from Task 4 + 3 from Task 5 = 9 facts including theory rows).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs
git commit -m "test(marketing): cover delta/utcNow/MarketingActionId on ReplaceProductAssociations"
```

---

## Task 6: Add `ReplaceFolderLinks` failing test — empty input clears existing

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs`

- [ ] **Step 1: Write the failing test file**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionReplaceFolderLinksTests
    {
        private static readonly DateTime UtcNow = new(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc);

        private static MarketingAction CreateAction() =>
            new MarketingActionTestBuilder()
                .WithId(1)
                .WithTitle("Test Action")
                .WithStartDate(UtcNow)
                .WithCreatedAt(UtcNow)
                .WithModifiedAt(UtcNow)
                .WithCreatedBy("user-1")
                .Build();

        [Fact]
        public void ReplaceFolderLinks_ClearsExisting_WhenInputIsEmpty()
        {
            // Arrange
            var action = CreateAction();
            action.LinkToFolder("key-1", MarketingFolderType.General);
            action.FolderLinks.Should().HaveCount(1);

            // Act
            action.ReplaceFolderLinks(
                Enumerable.Empty<(string folderKey, MarketingFolderType folderType)>(),
                UtcNow);

            // Assert
            action.FolderLinks.Should().BeEmpty();
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionReplaceFolderLinksTests" --no-restore`

Expected: COMPILATION FAILURE — `MarketingAction` has no `ReplaceFolderLinks`.

- [ ] **Step 3: Add the minimal `ReplaceFolderLinks` method to `MarketingAction`**

File: `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`

Insert immediately after `ReplaceProductAssociations`:

```csharp
        /// <summary>
        /// Atomically replaces the full set of folder links.
        /// A <c>null</c> sequence is treated as empty (clears all links).
        /// <paramref name="folderKey"/> is trimmed before dedup.
        /// Deduplicates by the composite key (<c>folderKey</c>, <c>folderType</c>) —
        /// this is stricter than <see cref="LinkToFolder"/>, which dedupes by
        /// <c>folderKey</c> alone. The asymmetry is intentional; new code should
        /// use this method when replacing the full set.
        /// Throws <see cref="ArgumentException"/> if any entry's
        /// <paramref name="folderKey"/> is null, empty, or whitespace.
        /// </summary>
        public void ReplaceFolderLinks(
            IEnumerable<(string folderKey, MarketingFolderType folderType)>? links,
            DateTime utcNow)
        {
            var normalized = new List<(string folderKey, MarketingFolderType folderType)>();
            if (links != null)
            {
                foreach (var (rawKey, type) in links)
                {
                    if (string.IsNullOrWhiteSpace(rawKey))
                        throw new ArgumentException("Folder key cannot be empty", nameof(links));

                    var key = rawKey.Trim();
                    if (!normalized.Any(n => n.folderKey == key && n.folderType == type))
                        normalized.Add((key, type));
                }
            }

            FolderLinks.Clear();
            foreach (var (key, type) in normalized)
            {
                FolderLinks.Add(new MarketingActionFolderLink
                {
                    MarketingActionId = Id,
                    FolderKey = key,
                    FolderType = type,
                    CreatedAt = utcNow,
                });
            }
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionReplaceFolderLinksTests" --no-restore`

Expected: PASS — 1 test passed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs \
        backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs
git commit -m "feat(marketing): add ReplaceFolderLinks to MarketingAction"
```

---

## Task 7: `ReplaceFolderLinks` — null input clears existing

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs`

- [ ] **Step 1: Add the failing test**

```csharp
        [Fact]
        public void ReplaceFolderLinks_ClearsExisting_WhenInputIsNull()
        {
            // Arrange
            var action = CreateAction();
            action.LinkToFolder("key-1", MarketingFolderType.General);

            // Act
            action.ReplaceFolderLinks(null, UtcNow);

            // Assert
            action.FolderLinks.Should().BeEmpty();
        }
```

- [ ] **Step 2: Run test**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName=Anela.Heblo.Tests.Domain.Marketing.MarketingActionReplaceFolderLinksTests.ReplaceFolderLinks_ClearsExisting_WhenInputIsNull" --no-restore`

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs
git commit -m "test(marketing): cover null input on ReplaceFolderLinks"
```

---

## Task 8: `ReplaceFolderLinks` — whitespace trim + composite-key dedup

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs`

- [ ] **Step 1: Add the failing test**

```csharp
        [Fact]
        public void ReplaceFolderLinks_TrimsWhitespaceFromFolderKey()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.ReplaceFolderLinks(
                new[] { ("  key-1  ", MarketingFolderType.General) },
                UtcNow);

            // Assert
            action.FolderLinks.Single().FolderKey.Should().Be("key-1");
        }

        [Fact]
        public void ReplaceFolderLinks_DeduplicatesByCompositeKey_WhenSameKeyAndType()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.ReplaceFolderLinks(
                new[]
                {
                    ("key-1", MarketingFolderType.General),
                    ("key-1", MarketingFolderType.General),
                    (" key-1 ", MarketingFolderType.General),
                },
                UtcNow);

            // Assert
            action.FolderLinks.Should().HaveCount(1);
        }

        [Fact]
        public void ReplaceFolderLinks_KeepsBothEntries_WhenSameKeyButDifferentType()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.ReplaceFolderLinks(
                new[]
                {
                    ("key-1", MarketingFolderType.General),
                    ("key-1", MarketingFolderType.Project),
                },
                UtcNow);

            // Assert
            action.FolderLinks.Should().HaveCount(2);
            action.FolderLinks.Select(f => f.FolderType)
                .Should().BeEquivalentTo(new[]
                {
                    MarketingFolderType.General,
                    MarketingFolderType.Project,
                });
        }
```

- [ ] **Step 2: Run tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionReplaceFolderLinksTests" --no-restore`

Expected: PASS — all current tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs
git commit -m "test(marketing): cover trim+composite-key dedup on ReplaceFolderLinks"
```

---

## Task 9: `ReplaceFolderLinks` — throws on per-entry whitespace `folderKey`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs`

- [ ] **Step 1: Add the failing test**

```csharp
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ReplaceFolderLinks_Throws_WhenAnyFolderKeyIsNullEmptyOrWhitespace(string? badKey)
        {
            // Arrange
            var action = CreateAction();

            // Act
            Action act = () =>
                action.ReplaceFolderLinks(
                    new[]
                    {
                        ("good", MarketingFolderType.General),
                        (badKey!, MarketingFolderType.General),
                    },
                    UtcNow);

            // Assert
            act.Should().Throw<ArgumentException>()
                .Which.ParamName.Should().Be("links");
        }
```

- [ ] **Step 2: Run test**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReplaceFolderLinks_Throws_WhenAnyFolderKeyIsNullEmptyOrWhitespace" --no-restore`

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs
git commit -m "test(marketing): assert ReplaceFolderLinks throws on invalid folder keys"
```

---

## Task 10: `ReplaceFolderLinks` — delta + utcNow + MarketingActionId propagation

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs`

- [ ] **Step 1: Add the failing tests**

```csharp
        [Fact]
        public void ReplaceFolderLinks_ReplacesEntireSet_OnDeltaInput()
        {
            // Arrange
            var action = CreateAction();
            action.LinkToFolder("KEEP", MarketingFolderType.General);
            action.LinkToFolder("REMOVE", MarketingFolderType.General);

            // Act
            action.ReplaceFolderLinks(
                new[]
                {
                    ("KEEP", MarketingFolderType.General),
                    ("ADD", MarketingFolderType.Project),
                },
                UtcNow);

            // Assert
            action.FolderLinks
                .Select(f => (f.FolderKey, f.FolderType))
                .Should().BeEquivalentTo(new[]
                {
                    ("KEEP", MarketingFolderType.General),
                    ("ADD", MarketingFolderType.Project),
                });
        }

        [Fact]
        public void ReplaceFolderLinks_UsesProvidedUtcNowOnAllNewRows()
        {
            // Arrange
            var action = CreateAction();
            var moment = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);

            // Act
            action.ReplaceFolderLinks(
                new[]
                {
                    ("a", MarketingFolderType.General),
                    ("b", MarketingFolderType.Project),
                },
                moment);

            // Assert
            action.FolderLinks.Should().OnlyContain(f => f.CreatedAt == moment);
        }

        [Fact]
        public void ReplaceFolderLinks_SetsMarketingActionIdOnNewRows()
        {
            // Arrange
            var action = CreateAction();
            action.Id = 99;

            // Act
            action.ReplaceFolderLinks(
                new[] { ("a", MarketingFolderType.General) },
                UtcNow);

            // Assert
            action.FolderLinks.Single().MarketingActionId.Should().Be(99);
        }
```

- [ ] **Step 2: Run all `ReplaceFolderLinks` tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingActionReplaceFolderLinksTests" --no-restore`

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs
git commit -m "test(marketing): cover delta/utcNow/MarketingActionId on ReplaceFolderLinks"
```

---

## Task 11: Handler — failing test asserts `Clear` semantics when request lists are null

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs`

This test will pass with the current implementation too (the existing handler also clears on null), but we use it to *lock in* the contract before the refactor in Task 13.

- [ ] **Step 1: Add a helper to the test class and the new test**

Add the following method and test inside `UpdateMarketingActionHandlerTests`. Place the helper above the existing static helpers (right after the private fields at line 22), and the test below the existing `Handle_UpdatesProductsAndFolderLinks_WhenProvided` test (after line 205).

Helper (place near other private helpers):

```csharp
    private static MarketingAction BuildExistingActionWithCollections()
    {
        var action = BuildExistingAction();
        action.AssociateWithProduct("OLD-PROD");
        action.LinkToFolder("old-key", MarketingFolderType.General);
        return action;
    }
```

New test:

```csharp
    [Fact]
    public async Task Handle_ClearsCollections_WhenRequestListsAreNull()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingActionWithCollections());

        var request = BuildRequest();
        request.AssociatedProducts = null;
        request.FolderLinks = null;

        var result = await BuildHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repository.Verify(x => x.UpdateAsync(
            It.Is<MarketingAction>(a =>
                a.ProductAssociations.Count == 0 &&
                a.FolderLinks.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run test to verify it currently passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName=Anela.Heblo.Tests.Application.Marketing.UpdateMarketingActionHandlerTests.Handle_ClearsCollections_WhenRequestListsAreNull" --no-restore`

Expected: PASS (current handler already clears via direct `.Clear()` — this test locks in the contract before refactor).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs
git commit -m "test(marketing): lock UpdateMarketingAction clear-on-null contract"
```

---

## Task 12: Handler — failing test asserts delta semantics (mixed kept/added/removed)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs`

- [ ] **Step 1: Add the new test below the previous one**

```csharp
    [Fact]
    public async Task Handle_ReplacesCollections_OnDeltaInput()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingActionWithCollections());

        var request = BuildRequest();
        request.AssociatedProducts = new List<string> { "OLD-PROD", "NEW-PROD" };
        request.FolderLinks = new List<MarketingFolderLinkRequest>
        {
            new() { FolderKey = "old-key", FolderType = MarketingFolderType.General },
            new() { FolderKey = "new-key", FolderType = MarketingFolderType.Project },
        };

        var result = await BuildHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repository.Verify(x => x.UpdateAsync(
            It.Is<MarketingAction>(a =>
                a.ProductAssociations.Count == 2 &&
                a.ProductAssociations.Any(p => p.ProductCodePrefix == "OLD-PROD") &&
                a.ProductAssociations.Any(p => p.ProductCodePrefix == "NEW-PROD") &&
                a.FolderLinks.Count == 2 &&
                a.FolderLinks.Any(f => f.FolderKey == "old-key" && f.FolderType == MarketingFolderType.General) &&
                a.FolderLinks.Any(f => f.FolderKey == "new-key" && f.FolderType == MarketingFolderType.Project)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run test to verify it currently passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName=Anela.Heblo.Tests.Application.Marketing.UpdateMarketingActionHandlerTests.Handle_ReplacesCollections_OnDeltaInput" --no-restore`

Expected: PASS (current handler produces the same final state).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs
git commit -m "test(marketing): lock UpdateMarketingAction delta-replace contract"
```

---

## Task 13: Refactor `UpdateMarketingActionHandler` to delegate to new domain methods

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`

- [ ] **Step 1: Replace lines 95–111 with two delegated calls**

In `UpdateMarketingActionHandler.cs`, the current block at lines 95–111 is:

```csharp
            action.ProductAssociations.Clear();
            if (request.AssociatedProducts?.Any() == true)
            {
                foreach (var product in request.AssociatedProducts.Distinct())
                {
                    action.AssociateWithProduct(product);
                }
            }

            action.FolderLinks.Clear();
            if (request.FolderLinks?.Any() == true)
            {
                foreach (var link in request.FolderLinks)
                {
                    action.LinkToFolder(link.FolderKey.Trim(), link.FolderType);
                }
            }
```

Replace it with:

```csharp
            action.ReplaceProductAssociations(request.AssociatedProducts, now);
            action.ReplaceFolderLinks(
                request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)),
                now);
```

(The `using System.Linq;` directive at line 3 already covers `.Select`.)

- [ ] **Step 2: Run the entire `UpdateMarketingActionHandlerTests` suite to verify no behavioral regression**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateMarketingActionHandlerTests" --no-restore`

Expected: PASS — all existing tests + the two new lock-in tests pass.

- [ ] **Step 3: Verify no remaining direct collection mutations against `MarketingAction` in the Application layer**

Run: `cd backend && grep -rn "ProductAssociations\.Clear\|FolderLinks\.Clear\|ProductAssociations\.Add\|FolderLinks\.Add\|ProductAssociations\.Remove\|FolderLinks\.Remove" src/Anela.Heblo.Application/ || echo "No direct mutations remain"`

Expected output: `No direct mutations remain` (the grep returns no matches and the `||` branch prints the success message).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs
git commit -m "refactor(marketing): delegate collection replacement to MarketingAction domain methods"
```

---

## Task 14: Full validation gates

**Files:** none modified

This is the final hard gate per `CLAUDE.md` — `dotnet build`, `dotnet format`, and the full test suite.

- [ ] **Step 1: Format the modified files**

Run: `cd backend && dotnet format --include src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceProductAssociationsTests.cs test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionReplaceFolderLinksTests.cs test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs`

Expected: completes without errors. If `dotnet format` made changes, inspect them with `git diff` — they should be whitespace only.

- [ ] **Step 2: Build the solution**

Run: `cd backend && dotnet build --no-restore`

Expected: `Build succeeded.` with 0 errors and 0 warnings introduced by the changes (pre-existing warnings unaffected).

- [ ] **Step 3: Run the full Marketing test surface**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Marketing" --no-restore`

Expected: All Marketing tests pass — at minimum the existing `MarketingActionAssociateWithProductTests`, `MarketingActionConstructorTests`, `MarketingActionSyncTests`, `MarketingActionUpdateDetailsTests`, `UpdateMarketingActionHandlerTests`, and the two new `MarketingActionReplaceProductAssociationsTests` + `MarketingActionReplaceFolderLinksTests` suites.

- [ ] **Step 4: Run the full backend test suite as a final gate**

Run: `cd backend && dotnet test --no-restore`

Expected: PASS — entire test suite green, no regressions outside Marketing.

- [ ] **Step 5: Commit any formatting changes (if any)**

```bash
git status
# If `dotnet format` produced uncommitted changes:
git add -u
git commit -m "chore(marketing): apply dotnet format"
```

If `git status` is clean, skip this step.

---

## Self-Review

**Spec coverage:**

| Spec requirement | Implemented in |
|---|---|
| FR-1 `ReplaceProductAssociations` exists with given signature | Task 1 (impl), Tasks 1–5 (tests) |
| FR-1 normalize trim+upper, dedup | Task 3 |
| FR-1 null sequence → empty | Task 2 |
| FR-1 empty input leaves collection empty | Task 1 |
| FR-1 `utcNow` propagates to `CreatedAt` | Task 5 |
| FR-1 unit test coverage (empty, null, dup, mixed-case, whitespace, delta) | Tasks 1, 2, 3, 5 |
| FR-2 `ReplaceFolderLinks` exists with given signature | Task 6 |
| FR-2 normalize trim on `folderKey` | Task 8 |
| FR-2 composite-key dedup | Task 8 |
| FR-2 null sequence → empty | Task 7 |
| FR-2 same-key-different-type keeps both | Task 8 |
| FR-2 throws on null/empty/whitespace key | Task 9 |
| FR-2 unit test coverage (empty, null, dup, distinct-type, whitespace, delta) | Tasks 6, 7, 8, 9, 10 |
| FR-3 Handler stops touching collections directly | Task 13 |
| FR-3 Handler delegates to new methods with `now` | Task 13 |
| FR-3 Existing handler tests still pass | Task 13 step 2 |
| FR-3 New handler test for clear-all | Task 11 |
| FR-3 New handler test for delta composition | Task 12 |
| FR-4 `AssociatedProducts = null` clears | Task 11 |
| FR-4 `FolderLinks = null` clears | Task 11 |
| FR-4 Existing tests unchanged | Task 13 step 2 (no test edits to existing) |
| NFR-1 No new round trips | Architecture unchanged (Clear+Add on tracked collection) |
| NFR-2 Validation enforced in domain | Tasks 1, 6 |
| NFR-3 Zero direct mutation in Application | Task 13 step 3 (grep gate) |
| NFR-4 Tests use POCO construction only | Tasks 1–10 all use `MarketingActionTestBuilder` |
| Arch-review amendment 5: XML docs documenting null⇒empty, normalization, dedup, asymmetry | Tasks 1, 6 (doc comments included) |

All spec requirements covered.

**Placeholder scan:** No `TBD`, `TODO`, `implement later`, `appropriate error handling`, `similar to Task N`, or stub-style instructions remain. Each step contains exact code or exact commands.

**Type consistency:** Method names `ReplaceProductAssociations` / `ReplaceFolderLinks`, signatures `(IEnumerable<string>?, DateTime)` / `(IEnumerable<(string folderKey, MarketingFolderType folderType)>?, DateTime)`, and `ParamName` strings (`"productCodes"`, `"links"`) match consistently across the implementation steps (Tasks 1, 6) and test steps (Tasks 4, 9). Property names `ProductAssociations`, `FolderLinks`, `ProductCodePrefix`, `FolderKey`, `FolderType`, `CreatedAt`, `MarketingActionId` match the actual entity definitions verified by reading the source files.
