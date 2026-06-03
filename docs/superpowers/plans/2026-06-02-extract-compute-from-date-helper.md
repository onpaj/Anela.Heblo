# Extract `ComputeFromDate` helper in `GetCatalogDetailHandler` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate four duplicated "full history vs. N months back" date-window blocks in `GetCatalogDetailHandler` by extracting a single private `ComputeFromDate(int monthsBack)` helper, and pair the floor date constant with the threshold constant it depends on in `CatalogConstants`.

**Architecture:** Pure internal refactor inside one Catalog Vertical Slice. Adds one private instance helper on `GetCatalogDetailHandler` (uses the already-injected `TimeProvider`) and one named constant `HISTORY_FLOOR_DATE` on `CatalogConstants`. Pattern B handlers are unified with Pattern A — the early-return branch is replaced with a uniform `.Where(... >= fromDate)` filter that uses `HISTORY_FLOOR_DATE` as the floor when full history is requested. No DTOs, MediatR contracts, controllers, OpenAPI client output, EF mappings, or migrations are touched.

**Tech Stack:** .NET 8, C# (nullable enabled), MediatR handlers, xUnit + FluentAssertions + Moq for tests, `dotnet build` / `dotnet format` / `dotnet test` for validation.

---

## File Structure

Files modified by this plan:

- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogConstants.cs` — add `HISTORY_FLOOR_DATE`.
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` — add `ComputeFromDate` helper; refactor four methods to use it; remove four `currentDate`-for-`fromDate` declarations; remove two early-return branches in Pattern B.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogConstantsTests.cs` — update member-count and field assertions for the new constant; assert the new constant's value.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs` — add one regression test that pins the post-refactor Pattern B boundary semantics with a `2019-12-31` fixture record.

No new files are created. No frontend, OpenAPI, or migration changes.

---

## Reading list (read before touching code)

- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` (full file, especially lines 109–301).
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogConstants.cs`.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs`.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogConstantsTests.cs`.
- The spec `artifacts/feat-arch-review-catalog-getcatalogdetailhand/spec.r1.md` and the architecture review `artifacts/feat-arch-review-catalog-getcatalogdetailhand/arch-review.r1.md`.

Key facts the engineer must internalize before any edit:

1. **There are six private "get history" methods** on `GetCatalogDetailHandler`. Only four are in scope for this refactor:
   - In scope (Pattern A — already filter-based): `GetManufactureCostHistoryFromMargins` (lines 203–242), `GetMarginHistoryFromMargins` (lines 244–301).
   - In scope (Pattern B — early-return branch): `GetPurchaseHistoryFromAggregate` (lines 109–145), `GetManufactureHistoryFromAggregate` (lines 165–201).
   - **Out of scope, DO NOT TOUCH**: `GetSalesHistoryFromAggregate` (lines 87–107) and `GetConsumedHistoryFromAggregate` (lines 147–163). They have no "all history" branch — they always filter by `currentDate.AddMonths(-monthsBack)`.
2. `CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD == 999`. The branch uses `>=` (exactly 999 triggers full history). Do not flip this to `>`.
3. `_timeProvider` (`TimeProvider`) is already injected into the handler. The helper must use it.
4. **DTOs in this repo are classes, never C# records** (project rule). This refactor does not add DTOs, so this is just a heads-up.
5. Existing test fixture (`CreateTestCatalogAggregateWithExtensiveHistory` in `GetCatalogDetailHandlerFullHistoryTests.cs`) earliest record is `2020-01-10` — strictly above the floor. The current suite would not catch a regression on a 2019 record. Task 4 adds that coverage.

---

## Task 0: Pre-flight data verification (R1 gating step)

**Why:** The architecture review's Risk R1 flags that Pattern B unification (`.Where(p.Date >= 2020-01-01)`) is equivalent to the prior early-return only if no production record has `Date < 2020-01-01`. This verification is the gate that decides whether `HISTORY_FLOOR_DATE` should be `2020-01-01` (as proposed) or an earlier sentinel.

**Files:** none — this is a database / staging snapshot query, not a code change.

- [ ] **Step 1: Confirm the data invariant against staging**

The check the executing engineer must perform (or escalate to whoever has access). Two collections need to be inspected: `CatalogPurchaseRecord.Date` and `CatalogManufactureRecord.Date`. The exact mechanism depends on how those are persisted in this repo — they may live behind `ICatalogRepository`, in the cache layer, or in source systems. The minimal sufficient check is one of:

- Run a one-off `dotnet run` script (or LINQPad / `dotnet-ef` query) against staging that prints `MIN(Date)` for each collection.
- Or, ask the project owner whether any pre-2020 purchase / manufacture records exist in staging or production.

Expected: **no record has `Date < 2020-01-01`**.

- [ ] **Step 2: Decide on the floor value**

If the expected result holds (no pre-2020 records), proceed with `HISTORY_FLOOR_DATE = new DateTime(2020, 1, 1)` (Task 2). If pre-2020 records exist, **halt this plan** and surface the finding to the user — the floor value must be revisited before continuing. Do not silently change the floor value without confirmation.

- [ ] **Step 3: Record the verification outcome in the PR description**

When the PR is opened (after Task 6), include a one-line note in the description:

```
Pre-flight R1 check: confirmed no CatalogPurchaseRecord or CatalogManufactureRecord
has Date < 2020-01-01 (verified <how / against what>).
```

No commit in this task — this is documentation captured for review trail.

---

## Task 1: Establish green baseline

**Why:** Before any refactor, prove the current test suite is green on the target branch. This is the safety net that the rest of the plan relies on.

**Files:** none modified.

- [ ] **Step 1: Build the solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: build succeeds, zero new warnings.

- [ ] **Step 2: Run all Catalog tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Catalog" \
  --no-build
```
Expected: all tests pass. Note the count; you'll compare against this number after each subsequent task.

- [ ] **Step 3: Run `dotnet format` and confirm clean**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: exit code 0, no diff. If this fails on the baseline, **stop** — sort that out first (it is not part of this refactor) before proceeding.

No commit in this task.

---

## Task 2: Add `HISTORY_FLOOR_DATE` to `CatalogConstants` (arch-review Decision 2)

**Why:** Arch-review amendment 1 — pair the floor date with the threshold constant it depends on, so the "full history" concept lives in one place. Eliminates the cross-file conceptual split flagged in the review.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogConstants.cs`
- Modify (tests): `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogConstantsTests.cs`

### Test-first

- [ ] **Step 1: Update `CatalogConstants_ContainsOnlyExpectedMembers` to expect two fields**

In `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogConstantsTests.cs`, replace the body of the existing test (currently lines 102–116) with the version below. Note: the existing assertion `fields.Should().HaveCount(1, ...)` and `fields.Single().Name.Should().Be(...)` will no longer hold after we add the new constant, so they must be updated **as the failing test that drives the change**.

Replace this block:

```csharp
[Fact]
public void CatalogConstants_ContainsOnlyExpectedMembers()
{
    // Arrange & Act
    var type = typeof(CatalogConstants);
    var fields = type.GetFields();
    var properties = type.GetProperties();
    var methods = type.GetMethods().Where(m => m.DeclaringType == type); // Exclude inherited methods

    // Assert
    fields.Should().HaveCount(1, "Should have exactly one constant field");
    fields.Single().Name.Should().Be(nameof(CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD));
    properties.Should().BeEmpty("Constants class should not have properties");
    methods.Should().BeEmpty("Constants class should not have methods");
}
```

With:

```csharp
[Fact]
public void CatalogConstants_ContainsOnlyExpectedMembers()
{
    // Arrange & Act
    var type = typeof(CatalogConstants);
    var fields = type.GetFields();
    var properties = type.GetProperties();
    var methods = type.GetMethods().Where(m => m.DeclaringType == type); // Exclude inherited methods

    // Assert
    fields.Select(f => f.Name).Should().BeEquivalentTo(new[]
    {
        nameof(CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD),
        nameof(CatalogConstants.HISTORY_FLOOR_DATE)
    });
    properties.Should().BeEmpty("Constants class should not have properties");
    methods.Should().BeEmpty("Constants class should not have methods");
}
```

- [ ] **Step 2: Add a new test pinning the value of `HISTORY_FLOOR_DATE`**

In the same file, add this test at the end of the `CatalogConstantsTests` class (just before the final closing `}`):

```csharp
[Fact]
public void HISTORY_FLOOR_DATE_HasExpectedValue()
{
    // Arrange & Act
    var floor = CatalogConstants.HISTORY_FLOOR_DATE;

    // Assert
    floor.Should().Be(new DateTime(2020, 1, 1));
}

[Fact]
public void HISTORY_FLOOR_DATE_IsStaticReadonlyDateTime()
{
    // Arrange & Act
    var type = typeof(CatalogConstants);
    var field = type.GetField(nameof(CatalogConstants.HISTORY_FLOOR_DATE));

    // Assert
    field.Should().NotBeNull();
    field!.IsStatic.Should().BeTrue();
    field.IsInitOnly.Should().BeTrue("HISTORY_FLOOR_DATE is static readonly, not a literal");
    field.FieldType.Should().Be<DateTime>();
}
```

- [ ] **Step 3: Run the tests and verify they FAIL**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogConstantsTests" \
  --no-build
```
Expected: build error (compile failure) on the three test methods that reference `CatalogConstants.HISTORY_FLOOR_DATE`. This is the RED state.

### Implementation

- [ ] **Step 4: Add `HISTORY_FLOOR_DATE` to `CatalogConstants`**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogConstants.cs` with:

```csharp
namespace Anela.Heblo.Application.Features.Catalog;

public static class CatalogConstants
{
    /// <summary>
    /// Magic number used to indicate "all history" when requesting historical data.
    /// When MonthsBack >= ALL_HISTORY_MONTHS_THRESHOLD, all available historical records are returned without date filtering.
    /// </summary>
    public const int ALL_HISTORY_MONTHS_THRESHOLD = 999;

    /// <summary>
    /// Earliest date used as the lower bound when "all history" is requested (MonthsBack >= ALL_HISTORY_MONTHS_THRESHOLD).
    /// Paired with ALL_HISTORY_MONTHS_THRESHOLD to define what "all history" means in one place.
    /// </summary>
    public static readonly DateTime HISTORY_FLOOR_DATE = new(2020, 1, 1);
}
```

- [ ] **Step 5: Rebuild and re-run the constants tests; verify GREEN**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogConstantsTests" \
  --no-build
```
Expected: build succeeds; all `CatalogConstantsTests` pass (including the two new tests and the modified `ContainsOnlyExpectedMembers`).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogConstants.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogConstantsTests.cs
git commit -m "refactor(catalog): add HISTORY_FLOOR_DATE constant alongside ALL_HISTORY_MONTHS_THRESHOLD"
```

---

## Task 3: Introduce `ComputeFromDate` helper and refactor Pattern A handlers (FR-1, FR-2, FR-4)

**Why:** Add the single source of truth for the "full history vs. N months back" date computation, then point the two Pattern A methods at it. Pattern A already uses `.Where(... >= fromDate)`, so this step is a pure inlining-into-helper refactor — behaviour must be identical and all existing tests must stay green throughout.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`

### Safety net first

- [ ] **Step 1: Confirm baseline tests still green**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Catalog" \
  --no-build
```
Expected: all tests pass — same count as Task 1 Step 2 plus the three new tests added in Task 2.

### Add the helper

- [ ] **Step 2: Add `ComputeFromDate` private method to `GetCatalogDetailHandler`**

Insert this method immediately **before** the closing `}` of the `GetCatalogDetailHandler` class (i.e. after `GetMarginHistoryFromMargins`, current line 301):

```csharp
    /// <summary>
    /// Encodes the "all history vs. N months back" convention used by the four
    /// history-projection helpers in this handler. NOT a general "now()" accessor —
    /// it deliberately collapses the two branches governed by
    /// <see cref="CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD"/>.
    /// </summary>
    private DateTime ComputeFromDate(int monthsBack)
    {
        if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        {
            return CatalogConstants.HISTORY_FLOOR_DATE;
        }

        return _timeProvider.GetUtcNow().Date.AddMonths(-monthsBack);
    }
```

### Refactor `GetManufactureCostHistoryFromMargins`

- [ ] **Step 3: Replace the Pattern A block in `GetManufactureCostHistoryFromMargins`**

In `GetCatalogDetailHandler.cs`, replace the existing method body (currently lines 203–242) with the version below. Note: the `try`/`catch` is preserved; only the `currentDate` / `fromDate` block is collapsed.

Replace:

```csharp
    private List<ManufactureCostDto> GetManufactureCostHistoryFromMargins(CatalogAggregate catalogItem, int monthsBack)
    {
        try
        {
            var currentDate = _timeProvider.GetUtcNow().Date;

            // Calculate date range
            DateTime fromDate;

            if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
            {
                // For "all history", start from a very early date
                fromDate = new DateTime(2020, 1, 1);
            }
            else
            {
                fromDate = currentDate.AddMonths(-monthsBack);
            }

            // Use pre-calculated margin data from CatalogAggregate.Margins
            var marginHistory = catalogItem.Margins;

            // Filter and convert margin history to ManufactureCostDto format
            return marginHistory.MonthlyData
                .Where(m => m.Key >= fromDate)
                .OrderByDescending(m => m.Key)
                .Select(m => new ManufactureCostDto
                {
                    Date = m.Key,
                    MaterialCost = m.Value.M0.CostLevel,
                    HandlingCost = m.Value.M1_A.CostLevel, // Map ManufacturingCost to HandlingCost
                    Total = m.Value.M0.CostLevel + m.Value.M1_A.CostLevel
                }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting manufacture cost history for product {ProductCode}", catalogItem.ProductCode);
            return new List<ManufactureCostDto>();
        }
    }
```

With:

```csharp
    private List<ManufactureCostDto> GetManufactureCostHistoryFromMargins(CatalogAggregate catalogItem, int monthsBack)
    {
        try
        {
            var fromDate = ComputeFromDate(monthsBack);

            // Use pre-calculated margin data from CatalogAggregate.Margins
            var marginHistory = catalogItem.Margins;

            // Filter and convert margin history to ManufactureCostDto format
            return marginHistory.MonthlyData
                .Where(m => m.Key >= fromDate)
                .OrderByDescending(m => m.Key)
                .Select(m => new ManufactureCostDto
                {
                    Date = m.Key,
                    MaterialCost = m.Value.M0.CostLevel,
                    HandlingCost = m.Value.M1_A.CostLevel, // Map ManufacturingCost to HandlingCost
                    Total = m.Value.M0.CostLevel + m.Value.M1_A.CostLevel
                }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting manufacture cost history for product {ProductCode}", catalogItem.ProductCode);
            return new List<ManufactureCostDto>();
        }
    }
```

### Refactor `GetMarginHistoryFromMargins`

- [ ] **Step 4: Replace the Pattern A block in `GetMarginHistoryFromMargins`**

In `GetCatalogDetailHandler.cs`, replace the existing method body (currently lines 244–301) with the version below.

Replace:

```csharp
    private List<MarginHistoryDto> GetMarginHistoryFromMargins(CatalogAggregate catalogItem, int monthsBack)
    {
        var currentDate = _timeProvider.GetUtcNow().Date;

        // Calculate date range
        DateTime fromDate;

        if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        {
            // For "all history", start from a very early date
            fromDate = new DateTime(2020, 1, 1);
        }
        else
        {
            fromDate = currentDate.AddMonths(-monthsBack);
        }

        // Use pre-calculated margin data from CatalogAggregate.Margins
        var marginHistory = catalogItem.Margins;

        // Filter and convert to DTOs with all M0-M2 margin levels
        return marginHistory.MonthlyData
            .Where(m => m.Key >= fromDate)
            .OrderByDescending(m => m.Key)
            .Select(m => new MarginHistoryDto
            {
                Date = m.Key,
                SellingPrice = m.Value.M2.CostTotal + m.Value.M2.Amount, // Reconstructed selling price from M2 (highest level now)
                TotalCost = m.Value.M0.CostBase, // Base cost (material + manufacturing)

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
                    Percentage = m.Value.M1_A.Percentage,
                    Amount = m.Value.M1_A.Amount,
                    CostLevel = m.Value.M1_A.CostLevel,
                    CostTotal = m.Value.M1_A.CostTotal
                },

                // M2 - M1 + Sales costs (final margin level now)
                M2 = new MarginLevelDto
                {
                    Percentage = m.Value.M2.Percentage,
                    Amount = m.Value.M2.Amount,
                    CostLevel = m.Value.M2.CostLevel,
                    CostTotal = m.Value.M2.CostTotal
                }
            }).ToList();
    }
```

With:

```csharp
    private List<MarginHistoryDto> GetMarginHistoryFromMargins(CatalogAggregate catalogItem, int monthsBack)
    {
        var fromDate = ComputeFromDate(monthsBack);

        // Use pre-calculated margin data from CatalogAggregate.Margins
        var marginHistory = catalogItem.Margins;

        // Filter and convert to DTOs with all M0-M2 margin levels
        return marginHistory.MonthlyData
            .Where(m => m.Key >= fromDate)
            .OrderByDescending(m => m.Key)
            .Select(m => new MarginHistoryDto
            {
                Date = m.Key,
                SellingPrice = m.Value.M2.CostTotal + m.Value.M2.Amount, // Reconstructed selling price from M2 (highest level now)
                TotalCost = m.Value.M0.CostBase, // Base cost (material + manufacturing)

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
                    Percentage = m.Value.M1_A.Percentage,
                    Amount = m.Value.M1_A.Amount,
                    CostLevel = m.Value.M1_A.CostLevel,
                    CostTotal = m.Value.M1_A.CostTotal
                },

                // M2 - M1 + Sales costs (final margin level now)
                M2 = new MarginLevelDto
                {
                    Percentage = m.Value.M2.Percentage,
                    Amount = m.Value.M2.Amount,
                    CostLevel = m.Value.M2.CostLevel,
                    CostTotal = m.Value.M2.CostTotal
                }
            }).ToList();
    }
```

### Verify

- [ ] **Step 5: Build and run all Catalog tests; verify GREEN**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Catalog" \
  --no-build
```
Expected: build succeeds with zero new warnings; all Catalog tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs
git commit -m "refactor(catalog): extract ComputeFromDate helper; collapse Pattern A duplication in GetCatalogDetailHandler"
```

---

## Task 4: Refactor Pattern B handlers with a new boundary regression test (FR-3, arch-review amendment 2)

**Why:** Pattern B (`GetPurchaseHistoryFromAggregate`, `GetManufactureHistoryFromAggregate`) currently short-circuits with an unconditional return when the threshold is reached. Replacing that with `.Where(p.Date >= HISTORY_FLOOR_DATE)` is equivalent **only if** no source record predates the floor. The arch-review's amendment 2 mandates locking the new semantics down with one explicit test fixture record at `2019-12-31` so the refactor's effect is observable, not implicit.

The existing test fixture (`CreateTestCatalogAggregateWithExtensiveHistory`) earliest record is `2020-01-10` — strictly above the floor — so it could not catch a regression. We add a focused test next to it.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`

### Test-first (RED)

- [ ] **Step 1: Add the Pattern B boundary test**

Add this test method to `GetCatalogDetailHandlerFullHistoryTests` (insert it directly after `Handle_Should_Filter_Records_When_MonthsBack_Is_13` and before `CreateTestCatalogAggregateWithExtensiveHistory`). It asserts the **post-refactor** behaviour: a `2019-12-31` purchase record is excluded when full history is requested. Under the pre-refactor early-return code path, that record would be included, so this test will fail until Task 4 Step 4 lands.

```csharp
    [Fact]
    public async Task Handle_Should_Exclude_PreFloor_Records_When_MonthsBack_Is_999()
    {
        // Arrange — pre-2020 record below HISTORY_FLOOR_DATE must be excluded by the
        // unified filter-based Pattern B implementation. This test pins that semantics.
        var currentDate = new DateTime(2024, 6, 15);
        var request = new GetCatalogDetailRequest
        {
            ProductCode = "TEST002",
            MonthsBack = 999
        };

        var catalogItem = new CatalogAggregate
        {
            Id = "TEST002",
            ProductName = "Boundary Fixture"
        };

        catalogItem.PurchaseHistory = new List<CatalogPurchaseRecord>
        {
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2019, 12, 31),
                SupplierName = "Pre-floor Supplier",
                Amount = 10,
                PricePerPiece = 1.0M,
                PriceTotal = 10.0M,
                DocumentNumber = "PRE-FLOOR-001",
                ProductCode = "TEST002"
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2020, 1, 1),
                SupplierName = "At-floor Supplier",
                Amount = 20,
                PricePerPiece = 2.0M,
                PriceTotal = 40.0M,
                DocumentNumber = "AT-FLOOR-001",
                ProductCode = "TEST002"
            }
        };

        catalogItem.ManufactureHistory = new List<CatalogManufactureRecord>
        {
            new CatalogManufactureRecord
            {
                Date = new DateTime(2019, 12, 31),
                Amount = 5,
                PricePerPiece = 3.0M,
                PriceTotal = 15.0M,
                ProductCode = "TEST002",
                DocumentNumber = "MFG-PRE-FLOOR-001"
            },
            new CatalogManufactureRecord
            {
                Date = new DateTime(2020, 1, 1),
                Amount = 7,
                PricePerPiece = 4.0M,
                PriceTotal = 28.0M,
                ProductCode = "TEST002",
                DocumentNumber = "MFG-AT-FLOOR-001"
            }
        };

        catalogItem.SaleHistorySummary = new SaleHistorySummary
        {
            MonthlyData = new Dictionary<string, MonthlySalesSummary>(),
            LastUpdated = DateTime.UtcNow
        };
        catalogItem.PurchaseHistorySummary = new PurchaseHistorySummary
        {
            MonthlyData = new Dictionary<string, MonthlyPurchaseSummary>(),
            LastUpdated = DateTime.UtcNow
        };
        catalogItem.ConsumedHistorySummary = new ConsumedHistorySummary
        {
            MonthlyData = new Dictionary<string, MonthlyConsumedSummary>(),
            LastUpdated = DateTime.UtcNow
        };

        var catalogItemDto = new CatalogItemDto
        {
            ProductCode = "TEST002",
            ProductName = "Boundary Fixture",
            Price = new PriceDto()
        };

        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(currentDate));
        _catalogRepositoryMock
            .Setup(r => r.SingleOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);
        _mapperMock.Setup(m => m.Map<CatalogItemDto>(catalogItem)).Returns(catalogItemDto);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        result.HistoricalData.PurchaseHistory.Select(p => p.Date)
            .Should().BeEquivalentTo(new[] { new DateTime(2020, 1, 1) },
                "records with Date < HISTORY_FLOOR_DATE must be excluded under the unified Pattern B filter");

        result.HistoricalData.ManufactureHistory.Select(m => m.Date)
            .Should().BeEquivalentTo(new[] { new DateTime(2020, 1, 1) },
                "records with Date < HISTORY_FLOOR_DATE must be excluded under the unified Pattern B filter");
    }
```

- [ ] **Step 2: Add the missing `using` for `CatalogManufactureRecord`**

The test fixture above adds `CatalogManufactureRecord`, which is not yet imported in this test file. At the top of `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs`, after the existing `using` block (currently lines 1–13), confirm there is a `using` for the namespace that contains `CatalogManufactureRecord`. If the build error in Step 3 reports it missing, add the appropriate `using` based on the actual namespace (search for `class CatalogManufactureRecord` under `backend/src/Anela.Heblo.Domain/Features/Catalog/`).

- [ ] **Step 3: Build the test project and confirm RED**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetCatalogDetailHandlerFullHistoryTests.Handle_Should_Exclude_PreFloor_Records_When_MonthsBack_Is_999" \
  --no-build
```
Expected: test fails. Specifically, the `PurchaseHistory` assertion fails because the pre-refactor early-return code path returns *both* records (the 2019-12-31 record is included). Similarly for `ManufactureHistory`. This is the RED state that drives Step 4.

### Implementation (GREEN)

- [ ] **Step 4: Refactor `GetPurchaseHistoryFromAggregate` to use `ComputeFromDate`**

In `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`, replace the existing method body (currently lines 109–145) with:

Replace:

```csharp
    private List<CatalogPurchaseRecordDto> GetPurchaseHistoryFromAggregate(CatalogAggregate catalogItem, int monthsBack)
    {
        // Return individual purchase records instead of monthly summaries
        var currentDate = _timeProvider.GetUtcNow().Date;

        // For very high monthsBack values (like ALL_HISTORY_MONTHS_THRESHOLD), return all records without date filtering
        // to avoid potential issues with very old dates
        if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        {
            return catalogItem.PurchaseHistory
                .OrderByDescending(p => p.Date)
                .Select(p => new CatalogPurchaseRecordDto
                {
                    Date = p.Date,
                    SupplierName = p.SupplierName,
                    Amount = p.Amount,
                    PricePerPiece = p.PricePerPiece,
                    PriceTotal = p.PriceTotal,
                    DocumentNumber = p.DocumentNumber
                }).ToList();
        }

        var fromDate = currentDate.AddMonths(-monthsBack);

        return catalogItem.PurchaseHistory
            .Where(p => p.Date >= fromDate)
            .OrderByDescending(p => p.Date)
            .Select(p => new CatalogPurchaseRecordDto
            {
                Date = p.Date,
                SupplierName = p.SupplierName,
                Amount = p.Amount,
                PricePerPiece = p.PricePerPiece,
                PriceTotal = p.PriceTotal,
                DocumentNumber = p.DocumentNumber
            }).ToList();
    }
```

With:

```csharp
    private List<CatalogPurchaseRecordDto> GetPurchaseHistoryFromAggregate(CatalogAggregate catalogItem, int monthsBack)
    {
        // Return individual purchase records instead of monthly summaries.
        // Date floor is governed by ComputeFromDate — for monthsBack >= ALL_HISTORY_MONTHS_THRESHOLD
        // the floor is HISTORY_FLOOR_DATE; otherwise it is currentDate - monthsBack.
        var fromDate = ComputeFromDate(monthsBack);

        return catalogItem.PurchaseHistory
            .Where(p => p.Date >= fromDate)
            .OrderByDescending(p => p.Date)
            .Select(p => new CatalogPurchaseRecordDto
            {
                Date = p.Date,
                SupplierName = p.SupplierName,
                Amount = p.Amount,
                PricePerPiece = p.PricePerPiece,
                PriceTotal = p.PriceTotal,
                DocumentNumber = p.DocumentNumber
            }).ToList();
    }
```

- [ ] **Step 5: Refactor `GetManufactureHistoryFromAggregate` to use `ComputeFromDate`**

In the same file, replace the existing method body (currently lines 165–201) with:

Replace:

```csharp
    private List<CatalogManufactureRecordDto> GetManufactureHistoryFromAggregate(CatalogAggregate catalogItem, int monthsBack)
    {
        // Return individual manufacture records instead of monthly summaries
        var currentDate = _timeProvider.GetUtcNow().Date;

        // For very high monthsBack values (like ALL_HISTORY_MONTHS_THRESHOLD), return all records without date filtering
        // to avoid potential issues with very old dates
        if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        {
            return catalogItem.ManufactureHistory
                .OrderByDescending(m => m.Date)
                .Select(m => new CatalogManufactureRecordDto
                {
                    Date = m.Date,
                    Amount = m.Amount,
                    PricePerPiece = m.PricePerPiece,
                    PriceTotal = m.PriceTotal,
                    ProductCode = m.ProductCode,
                    DocumentNumber = m.DocumentNumber
                }).ToList();
        }

        var fromDate = currentDate.AddMonths(-monthsBack);

        return catalogItem.ManufactureHistory
            .Where(m => m.Date >= fromDate)
            .OrderByDescending(m => m.Date)
            .Select(m => new CatalogManufactureRecordDto
            {
                Date = m.Date,
                Amount = m.Amount,
                PricePerPiece = m.PricePerPiece,
                PriceTotal = m.PriceTotal,
                ProductCode = m.ProductCode,
                DocumentNumber = m.DocumentNumber
            }).ToList();
    }
```

With:

```csharp
    private List<CatalogManufactureRecordDto> GetManufactureHistoryFromAggregate(CatalogAggregate catalogItem, int monthsBack)
    {
        // Return individual manufacture records instead of monthly summaries.
        // Date floor is governed by ComputeFromDate — see GetPurchaseHistoryFromAggregate for rationale.
        var fromDate = ComputeFromDate(monthsBack);

        return catalogItem.ManufactureHistory
            .Where(m => m.Date >= fromDate)
            .OrderByDescending(m => m.Date)
            .Select(m => new CatalogManufactureRecordDto
            {
                Date = m.Date,
                Amount = m.Amount,
                PricePerPiece = m.PricePerPiece,
                PriceTotal = m.PriceTotal,
                ProductCode = m.ProductCode,
                DocumentNumber = m.DocumentNumber
            }).ToList();
    }
```

- [ ] **Step 6: Build and run the new boundary test; verify GREEN**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetCatalogDetailHandlerFullHistoryTests.Handle_Should_Exclude_PreFloor_Records_When_MonthsBack_Is_999" \
  --no-build
```
Expected: the new boundary test passes — both `PurchaseHistory` and `ManufactureHistory` assertions hold.

- [ ] **Step 7: Run all Catalog tests; verify GREEN**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Catalog" \
  --no-build
```
Expected: all Catalog tests pass — including the existing `Handle_Should_Return_All_Records_When_MonthsBack_Is_999` (its fixture records are all `>= 2020-01-10`, all `>= HISTORY_FLOOR_DATE`, so the filter still includes them) and `Handle_Should_Filter_Records_When_MonthsBack_Is_13` (filter cutoff `2023-05-15` is well above the floor, so behaviour is unchanged in the `monthsBack < threshold` branch).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs
git commit -m "refactor(catalog): unify Pattern B history projections via ComputeFromDate; add pre-floor boundary test"
```

---

## Task 5: Repo-wide audit — confirm `2020-01-01` literal eliminated from Catalog source

**Why:** FR-1 acceptance criterion (and arch-review amendment 1) requires that the literal `new DateTime(2020, 1, 1)` appears exactly once in the Catalog source tree — inside `CatalogConstants.HISTORY_FLOOR_DATE`. This task confirms the audit and removes any straggler if present.

**Files:** no automatic edits — read-only check followed by targeted edits only if violations are found.

- [ ] **Step 1: Grep for the literal in Catalog source**

Run:
```bash
grep -rn 'new DateTime(2020, 1, 1)' backend/src/Anela.Heblo.Application/Features/Catalog/ || echo "NO_MATCHES"
```
Expected: a single hit inside `CatalogConstants.cs` — the `HISTORY_FLOOR_DATE` initializer. No other hits.

- [ ] **Step 2: If additional hits are reported, replace each with `CatalogConstants.HISTORY_FLOOR_DATE`**

For each non-`CatalogConstants.cs` hit, open the file at the reported line and replace `new DateTime(2020, 1, 1)` with `CatalogConstants.HISTORY_FLOOR_DATE` (add the appropriate `using Anela.Heblo.Application.Features.Catalog;` if not already present). Re-run the grep until only the single `CatalogConstants.cs` hit remains.

- [ ] **Step 3: If Step 2 made changes, commit**

```bash
git add -u
git commit -m "refactor(catalog): replace residual 2020-01-01 literal with HISTORY_FLOOR_DATE"
```

If Step 2 made no changes, no commit is needed in this task.

**Note on test code:** The literal `new DateTime(2020, 1, 1)` may appear in test files (e.g. the new `Handle_Should_Exclude_PreFloor_Records_When_MonthsBack_Is_999` test fixture uses it as the *at-floor* sentinel). Tests intentionally pin the floor value explicitly — they must keep the literal. This audit covers `backend/src/Anela.Heblo.Application/Features/Catalog/` only.

---

## Task 6: Final validation gate (per CLAUDE.md)

**Why:** Before declaring the work done, run the full validation suite mandated by `CLAUDE.md`.

**Files:** none modified.

- [ ] **Step 1: `dotnet build`**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: build succeeds, zero new warnings introduced by this change.

- [ ] **Step 2: `dotnet format` reports clean**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: exit code 0, no diff. If this reports changes inside the four edited methods or `CatalogConstants.cs`, accept the formatter's output (`dotnet format backend/Anela.Heblo.sln`) and commit as a fixup.

- [ ] **Step 3: Full Catalog test suite GREEN**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Catalog" \
  --no-build
```
Expected: all tests pass — original count from Task 1 Step 2 **plus 3** new `CatalogConstantsTests` plus **1** new boundary test in `GetCatalogDetailHandlerFullHistoryTests`. No skipped tests.

- [ ] **Step 4: Full backend test run (regression safety net)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
```
Expected: all tests pass. The handler is referenced only via MediatR; no callers should break.

- [ ] **Step 5: Visual diff sanity check**

Run:
```bash
git diff --stat main...HEAD -- backend/src/Anela.Heblo.Application/Features/Catalog/
git diff --stat main...HEAD -- backend/test/Anela.Heblo.Tests/Features/Catalog/
```
Confirm the change set is limited to:
- `CatalogConstants.cs` (+~6 lines)
- `GetCatalogDetailHandler.cs` (net negative — duplication removed, one helper added)
- `CatalogConstantsTests.cs` (+~20 lines for new tests + modified one)
- `GetCatalogDetailHandlerFullHistoryTests.cs` (+~80 lines for the new boundary test and any required `using`)

If unrelated files appear in the diff, investigate before pushing — they likely indicate accidental `dotnet format` reformatting of adjacent code (Risk R4 in the arch review).

No commit in this task unless Step 2 required a `dotnet format` fixup.

---

## Self-review checklist (already run against this plan)

- **Spec coverage:** FR-1 → Task 3 Step 2. FR-2 → Task 3 Steps 3–4. FR-3 → Task 4 Steps 4–5 (with new test in Steps 1–3). FR-4 → enforced inside the replacement bodies in Tasks 3 and 4. NFR-1 → Tasks 1, 3 Step 5, 4 Step 7, 6 Steps 3–4. NFR-2 → Task 5 + Task 6 Steps 1–2. NFR-3 → covered by NFR-1 (no perf-relevant change). Arch-review amendments 1, 2, 3 → Tasks 2, 4, FR-3 clarification baked into the new test assertions.
- **Placeholders:** none. Every code block is the exact replacement text. Every command is runnable.
- **Type consistency:** `ComputeFromDate(int monthsBack) : DateTime` is referenced identically in Tasks 3 and 4. `HISTORY_FLOOR_DATE` and `ALL_HISTORY_MONTHS_THRESHOLD` are used with their exact `CatalogConstants` names throughout.
- **Out-of-scope respected:** `GetSalesHistoryFromAggregate` and `GetConsumedHistoryFromAggregate` are explicitly listed in the reading list as untouched; no task edits them.
- **R1 verification:** Task 0 is a hard gate before Task 2.
