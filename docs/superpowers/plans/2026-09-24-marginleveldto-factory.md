# Extract MarginLevelDto Construction Duplication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 12 identical inline `new MarginLevelDto { Percentage = ..., Amount = ..., CostLevel = ..., CostTotal = ... }` constructions in `GetProductMarginsHandler.cs` and `GetCatalogDetailHandler.cs` with calls to a single new `MarginLevelDto.FromDomain(MarginLevel)` static factory. Pure refactor, no behavior change.

**Architecture:** Add one static factory method to the existing `MarginLevelDto` class (matching the codebase's existing `PurchaseOrderHistoryDto.FromDomain` precedent), then mechanically substitute all 12 call sites across the two handlers. Verified via `spec.r1.md` / `arch-review.r1.md`: the factory parameter type is `Anela.Heblo.Domain.Features.Catalog.MarginLevel` (not `MarginData` as the originating issue's snippet said).

**Tech Stack:** .NET 8, C#, xUnit, FluentAssertions, Moq.

---

### task: add-marginleveldto-factory

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/MarginLevelDtoTests.cs` (new file)

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/MarginLevelDtoTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class MarginLevelDtoTests
{
    [Fact]
    public void FromDomain_CopiesAllFourFieldsVerbatim()
    {
        // Arrange
        var level = new MarginLevel(percentage: 12.34m, amount: 56.78m, costTotal: 90.12m, costLevel: 3.45m);

        // Act
        var dto = MarginLevelDto.FromDomain(level);

        // Assert
        dto.Percentage.Should().Be(12.34m);
        dto.Amount.Should().Be(56.78m);
        dto.CostTotal.Should().Be(90.12m);
        dto.CostLevel.Should().Be(3.45m);
    }

    [Fact]
    public void FromDomain_ZeroLevel_ProducesZeroDto()
    {
        // Arrange
        var level = MarginLevel.Zero;

        // Act
        var dto = MarginLevelDto.FromDomain(level);

        // Assert
        dto.Percentage.Should().Be(0m);
        dto.Amount.Should().Be(0m);
        dto.CostTotal.Should().Be(0m);
        dto.CostLevel.Should().Be(0m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarginLevelDtoTests"`
Expected: FAIL to compile — `MarginLevelDto` does not contain a definition for `FromDomain`.

- [ ] **Step 3: Add the factory method**

In `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs`, add the `using` and the method so the file reads:

```csharp
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Represents margin data for a specific margin level (M0, M1, or M2)
/// </summary>
public class MarginLevelDto
{
    /// <summary>
    /// Margin percentage at this level
    /// </summary>
    [JsonPropertyName("percentage")]
    public decimal Percentage { get; set; }

    /// <summary>
    /// Absolute margin amount at this level (in currency)
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Cost specific to this level only (incremental cost)
    /// </summary>
    [JsonPropertyName("costLevel")]
    public decimal CostLevel { get; set; }

    /// <summary>
    /// Cumulative cost up to and including this level
    /// </summary>
    [JsonPropertyName("costTotal")]
    public decimal CostTotal { get; set; }

    /// <summary>
    /// Maps a domain margin-level value to its DTO. Centralizes the field-by-field copy
    /// used at every M0-M3 call site across the Catalog module's margin handlers.
    /// </summary>
    public static MarginLevelDto FromDomain(MarginLevel level) => new()
    {
        Percentage = level.Percentage,
        Amount = level.Amount,
        CostLevel = level.CostLevel,
        CostTotal = level.CostTotal
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarginLevelDtoTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs backend/test/Anela.Heblo.Tests/Features/Catalog/MarginLevelDtoTests.cs
git commit -m "feat(catalog): add MarginLevelDto.FromDomain mapping factory"
```

---

### task: replace-inline-construction-in-get-product-margins-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs:209-270`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` (existing — run, do not modify unless it fails)

**Depends on:** `add-marginleveldto-factory` (needs `MarginLevelDto.FromDomain` to exist).

- [ ] **Step 1: Run the existing test suite first to capture the current-passing baseline**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"`
Expected: PASS (all existing tests green before this task's edit).

- [ ] **Step 2: Replace the M0-M3 averages block**

In `GetProductMarginsHandler.cs`, inside `MapToMarginDto`, replace:

```csharp
                // Use pre-calculated averages from margin history
                M0 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M0.Percentage,
                    Amount = marginHistory.Averages.M0.Amount,
                    CostLevel = marginHistory.Averages.M0.CostLevel,
                    CostTotal = marginHistory.Averages.M0.CostTotal
                },
                M1 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M1.Percentage,
                    Amount = marginHistory.Averages.M1.Amount,
                    CostLevel = marginHistory.Averages.M1.CostLevel,
                    CostTotal = marginHistory.Averages.M1.CostTotal
                },
                M2 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M2.Percentage,
                    Amount = marginHistory.Averages.M2.Amount,
                    CostLevel = marginHistory.Averages.M2.CostLevel,
                    CostTotal = marginHistory.Averages.M2.CostTotal
                },
                M3 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M3.Percentage,
                    Amount = marginHistory.Averages.M3.Amount,
                    CostLevel = marginHistory.Averages.M3.CostLevel,
                    CostTotal = marginHistory.Averages.M3.CostTotal
                },
```

with:

```csharp
                // Use pre-calculated averages from margin history
                M0 = MarginLevelDto.FromDomain(marginHistory.Averages.M0),
                M1 = MarginLevelDto.FromDomain(marginHistory.Averages.M1),
                M2 = MarginLevelDto.FromDomain(marginHistory.Averages.M2),
                M3 = MarginLevelDto.FromDomain(marginHistory.Averages.M3),
```

- [ ] **Step 3: Replace the MonthlyHistory projection block**

In the same method, replace:

```csharp
                // Monthly history for charts (filtered to last 13 months)
                MonthlyHistory = filteredMonthlyData.Select(m => new MonthlyMarginDto
                {
                    Month = m.Key,
                    M0 = new MarginLevelDto
                    {
                        Percentage = m.Value.M0.Percentage,
                        Amount = m.Value.M0.Amount,
                        CostLevel = m.Value.M0.CostLevel,
                        CostTotal = m.Value.M0.CostTotal
                    },
                    M1 = new MarginLevelDto
                    {
                        Percentage = m.Value.M1.Percentage,
                        Amount = m.Value.M1.Amount,
                        CostLevel = m.Value.M1.CostLevel,
                        CostTotal = m.Value.M1.CostTotal
                    },
                    M2 = new MarginLevelDto
                    {
                        Percentage = m.Value.M2.Percentage,
                        Amount = m.Value.M2.Amount,
                        CostLevel = m.Value.M2.CostLevel,
                        CostTotal = m.Value.M2.CostTotal
                    },
                    M3 = new MarginLevelDto
                    {
                        Percentage = m.Value.M3.Percentage,
                        Amount = m.Value.M3.Amount,
                        CostLevel = m.Value.M3.CostLevel,
                        CostTotal = m.Value.M3.CostTotal
                    }
                }).ToList()
```

with:

```csharp
                // Monthly history for charts (filtered to last 13 months)
                MonthlyHistory = filteredMonthlyData.Select(m => new MonthlyMarginDto
                {
                    Month = m.Key,
                    M0 = MarginLevelDto.FromDomain(m.Value.M0),
                    M1 = MarginLevelDto.FromDomain(m.Value.M1),
                    M2 = MarginLevelDto.FromDomain(m.Value.M2),
                    M3 = MarginLevelDto.FromDomain(m.Value.M3)
                }).ToList()
```

- [ ] **Step 4: Confirm no other `new MarginLevelDto` remains in this file**

Run: `grep -n "new MarginLevelDto" backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`
Expected: no output (0 matches).

- [ ] **Step 5: Run tests to verify they still pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"`
Expected: PASS — identical result set to Step 1's baseline.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs
git commit -m "refactor(catalog): use MarginLevelDto.FromDomain in GetProductMarginsHandler"
```

---

### task: replace-inline-construction-in-get-catalog-detail-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs:211-245`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs` (existing — run, do not modify unless it fails)
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs` (existing — run, do not modify unless it fails)

**Depends on:** `add-marginleveldto-factory` (needs `MarginLevelDto.FromDomain` to exist). Independent of `replace-inline-construction-in-get-product-margins-handler` (different file) — can run in parallel with it once the factory task is done.

- [ ] **Step 1: Run the existing test suites first to capture the current-passing baseline**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetCatalogDetailHandlerTests|FullyQualifiedName~GetCatalogDetailHandlerFullHistoryTests"`
Expected: PASS (all existing tests green before this task's edit).

- [ ] **Step 2: Replace the M0-M3 block in `GetMarginHistoryFromMargins`**

In `GetCatalogDetailHandler.cs`, inside `GetMarginHistoryFromMargins`, replace:

```csharp
                // M0 - Material + Manufacturing costs
                M0 = new MarginLevelDto
                {
                    Percentage = m.Value.M0.Percentage,
                    Amount = m.Value.M0.Amount,
                    CostLevel = m.Value.M0.CostLevel,
                    CostTotal = m.Value.M0.CostTotal
                },

                // M1 - M0 + Manufacturing costs (if different)
                M1 = new MarginLevelDto
                {
                    Percentage = m.Value.M1.Percentage,
                    Amount = m.Value.M1.Amount,
                    CostLevel = m.Value.M1.CostLevel,
                    CostTotal = m.Value.M1.CostTotal
                },

                // M2 - M1 + Sales costs
                M2 = new MarginLevelDto
                {
                    Percentage = m.Value.M2.Percentage,
                    Amount = m.Value.M2.Amount,
                    CostLevel = m.Value.M2.CostLevel,
                    CostTotal = m.Value.M2.CostTotal
                },

                // M3 - M2 + Overhead (final margin level)
                M3 = new MarginLevelDto
                {
                    Percentage = m.Value.M3.Percentage,
                    Amount = m.Value.M3.Amount,
                    CostLevel = m.Value.M3.CostLevel,
                    CostTotal = m.Value.M3.CostTotal
                }
```

with:

```csharp
                // M0 - Material + Manufacturing costs
                M0 = MarginLevelDto.FromDomain(m.Value.M0),

                // M1 - M0 + Manufacturing costs (if different)
                M1 = MarginLevelDto.FromDomain(m.Value.M1),

                // M2 - M1 + Sales costs
                M2 = MarginLevelDto.FromDomain(m.Value.M2),

                // M3 - M2 + Overhead (final margin level)
                M3 = MarginLevelDto.FromDomain(m.Value.M3)
```

- [ ] **Step 3: Confirm no other `new MarginLevelDto` remains in this file**

Run: `grep -n "new MarginLevelDto" backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`
Expected: no output (0 matches).

- [ ] **Step 4: Run tests to verify they still pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetCatalogDetailHandlerTests|FullyQualifiedName~GetCatalogDetailHandlerFullHistoryTests"`
Expected: PASS — identical result set to Step 1's baseline.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs
git commit -m "refactor(catalog): use MarginLevelDto.FromDomain in GetCatalogDetailHandler"
```

---

### task: full-verification

**Files:** none (verification-only task).

**Depends on:** `add-marginleveldto-factory`, `replace-inline-construction-in-get-product-margins-handler`, `replace-inline-construction-in-get-catalog-detail-handler` (all three must be complete).

- [ ] **Step 1: Confirm zero remaining occurrences of the duplicated pattern project-wide**

Run: `grep -rn "new MarginLevelDto" backend/src/`
Expected: no output (0 matches) — every one of the original 12 inline constructions is gone.

- [ ] **Step 2: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln` (or the solution file used by this repo's CI)
Expected: Build succeeds, 0 errors, 0 new warnings.

- [ ] **Step 3: Format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: no formatting diffs reported. If it reports diffs, run `dotnet format backend/Anela.Heblo.sln` and re-run this check, then include the formatting fix in this task's commit.

- [ ] **Step 4: Full backend test run**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS, 0 failures — same pass count as `main` before this change plus the 2 new `MarginLevelDtoTests` cases.

- [ ] **Step 5: Confirm no frontend/OpenAPI client changes were triggered**

Run: `git status --short frontend/`
Expected: no output — `MarginLevelDto`'s public shape didn't change, so no generated TypeScript client diff should appear from a build. (No `npm run build` is required by this task; the DTO's public members and `[JsonPropertyName]` attributes are untouched, so there is nothing for the generator to pick up.)

- [ ] **Step 6: Commit (if Step 3 produced a formatting fix; otherwise skip — nothing to commit)**

```bash
git add -A
git commit -m "chore(catalog): dotnet format after MarginLevelDto factory refactor"
```
