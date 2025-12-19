# M1 Margin Calculation Implementation Design

**Date:** 2025-12-19
**Status:** Approved for Implementation
**Related Spec:** [M1 Production Costs Specification](./M1_production_costs_specification.md)

## Overview

This document describes the implementation plan for updating `MarginCalculationService` to calculate M1 manufacturing costs according to the specification using Complexity Points (CP). The current implementation treats M1 as a simple monthly cost lookup, but the specification requires CP-weighted calculations with two distinct metrics: M1_A (economic baseline) and M1_B (actual monthly cost).

## Problem Statement

**Current Implementation Issues:**
- M1 is treated as a single metric (ambiguous between actual and baseline)
- Does not apply Complexity Point weighting from the specification
- Missing M1_A (economic baseline) calculation using rolling 12-month window
- Missing M1_B (actual monthly cost) with conditional presence based on production
- Does not account for company-wide production efficiency

**Specification Requirements:**
- M1_A: Long-term economic cost per unit (baseline for pricing/inventory)
- M1_B: Actual production cost in specific month (efficiency KPI)
- Both must use CP-weighted formulas based on company-wide production data

## Data Foundation

### Existing Infrastructure (No Changes Needed)

1. **Complexity Points:**
   - Field: `CatalogAggregate.ManufactureDifficulty` (double, versioned with ValidFrom/ValidTo)
   - Will be treated as `CP_per_unit` from specification
   - Already has proper versioning support via `ManufactureDifficultySettings`

2. **Production Volume Tracking:**
   - Source: `CatalogAggregate.ManufactureHistory` (imported from ABRA Flexi)
   - Contains: Date, Amount, PricePerPiece, DocumentNumber
   - Provides `units_produced` per month per product

3. **M1 Cost Data:**
   - Repository: `IManufactureCostRepository`
   - Service: `IManufactureCostCalculationService` (accesses ledger)
   - Provides: Monthly M1 production costs (VYROBA department)

## Design Changes

### 1. Data Model Updates

#### MonthlyMarginData (Domain Model)

**Current:**
```csharp
public class MonthlyMarginData
{
    public DateTime Month { get; set; }
    public MarginLevel M0 { get; set; }
    public MarginLevel M1 { get; set; }  // AMBIGUOUS!
    public MarginLevel M2 { get; set; }
    public MarginLevel M3 { get; set; }
    public CostBreakdown CostsForMonth { get; set; }
}
```

**Updated:**
```csharp
public class MonthlyMarginData
{
    public DateTime Month { get; set; }
    public MarginLevel M0 { get; set; }
    public MarginLevel M1_A { get; set; }  // Economic baseline (always present)
    public MarginLevel? M1_B { get; set; } // Actual monthly cost (nullable)
    public MarginLevel M2 { get; set; }
    public MarginLevel M3 { get; set; }
    public CostBreakdown CostsForMonth { get; set; }
}
```

#### MarginData (Averages)

**Updated:**
```csharp
public class MarginData
{
    public MarginLevel M0 { get; set; }
    public MarginLevel M1_A { get; set; }  // Average M1_A across months
    public MarginLevel M1_B { get; set; }  // Average M1_B (only months with production)
    public MarginLevel M2 { get; set; }
    public MarginLevel M3 { get; set; }
}
```

#### CostBreakdown

**Updated:**
```csharp
public class CostBreakdown
{
    public decimal M0CostLevel { get; }      // Material cost
    public decimal M1CostLevel { get; }      // Manufacturing cost (uses M1_A)
    public decimal M2CostLevel { get; }      // Sales cost
    public decimal M3CostLevel { get; }      // Overhead cost

    // M1CostTotal uses M1_A for cumulative calculations
    public decimal M1CostTotal => M0CostLevel + M1CostLevel;
}
```

### 2. Service Signature Changes

#### MarginCalculationService.GetMarginAsync()

**Current:**
```csharp
public async Task<MonthlyMarginHistory> GetMarginAsync(
    CatalogAggregate product,
    DateOnly dateFrom,
    DateOnly dateTo,
    CancellationToken cancellationToken = default)
```

**Updated:**
```csharp
public async Task<MonthlyMarginHistory> GetMarginAsync(
    CatalogAggregate product,
    IEnumerable<CatalogAggregate> allProducts, // NEW: For company-wide CP
    DateOnly dateFrom,
    DateOnly dateTo,
    CancellationToken cancellationToken = default)
```

**Rationale:** Avoids circular dependencies by having caller provide all products. Enables efficient batch calculations when computing margins for multiple products.

### 3. New Calculation Methods

#### CalculateCompanyWideProducedCPAsync()

```csharp
private async Task<Dictionary<DateTime, decimal>> CalculateCompanyWideProducedCPAsync(
    IEnumerable<CatalogAggregate> allProducts,
    DateOnly dateFrom,
    DateOnly dateTo,
    CancellationToken cancellationToken)
```

**Purpose:** Calculate total produced CP across all products by month.

**Algorithm:**
1. For each product in allProducts:
   - Get ManufactureHistory records in date range
   - For each production record:
     - Get ManufactureDifficulty valid at production date
     - Calculate: `produced_CP = Amount × ManufactureDifficulty`
     - Aggregate by month (year + month key)
2. Return: `Dictionary<DateTime, decimal>` where key is first day of month

**Edge Cases:**
- Product without ManufactureHistory: Skip (contributes 0 CP)
- ManufactureDifficulty null: Skip record with warning log
- Zero production month: Returns 0 for that month

#### CalculateM1_A_PerMonth()

```csharp
private Dictionary<DateTime, decimal> CalculateM1_A_PerMonth(
    double productComplexityPoints,
    Dictionary<DateTime, decimal> companyWideProducedCP,
    List<MonthlyCost> m1Costs,
    DateOnly dateFrom,
    DateOnly dateTo)
```

**Purpose:** Calculate economic baseline (M1_A) for each month using rolling 12-month window.

**Algorithm:**
```
For each month M in [dateFrom, dateTo]:
  1. Define reference period: 12 months ending at month M
  2. Sum M1 costs in reference period: Σ(M1_costs_period)
  3. Sum produced CP in reference period: Σ(produced_CP_period)
  4. Calculate cost per CP: cost_per_CP = Σ(M1_costs) / Σ(produced_CP)
  5. Calculate M1_A for product: M1_A_M = productComplexityPoints × cost_per_CP
```

**Edge Cases:**
- `Σ(produced_CP) = 0`: Return M1_A = 0, log warning
- Less than 12 months available: Use all available months
- Missing M1 cost data: Exclude month from sum

**Properties:**
- M1_A always has a value for every month (non-nullable)
- Represents "what it would cost based on 12-month average efficiency"
- Constant within period unless recalculated

#### CalculateM1_B_PerMonth()

```csharp
private Dictionary<DateTime, decimal?> CalculateM1_B_PerMonth(
    CatalogAggregate product,
    double productComplexityPoints,
    Dictionary<DateTime, decimal> companyWideProducedCP,
    List<MonthlyCost> m1Costs)
```

**Purpose:** Calculate actual monthly production cost (M1_B) only for months when product was manufactured.

**Algorithm:**
```
For each month M in companyWideProducedCP:
  1. Check if product has production records in month M
  2. If NO production records:
     - M1_B_M = null (not produced)
  3. If YES production records:
     - Get M1_costs_M from m1Costs
     - Get totalProducedCP_M from companyWideProducedCP
     - Calculate: M1_B_per_CP_M = M1_costs_M / totalProducedCP_M
     - Calculate: M1_B_M = productComplexityPoints × M1_B_per_CP_M
```

**Edge Cases:**
- Product not produced in month: Return null
- `totalProducedCP_M = 0`: Return null (no production company-wide)
- M1_costs_M = 0: Valid scenario (zero cost month), return calculated value

**Properties:**
- M1_B only exists for months with production (nullable)
- Reflects actual production efficiency for that specific month
- Used for analytics and KPI tracking only

### 4. Updated Main Flow

#### CalculateMarginHistoryFromData()

**Updated signature:**
```csharp
private MonthlyMarginHistory CalculateMarginHistoryFromData(
    CatalogAggregate product,
    decimal sellingPrice,
    CostData costData,
    Dictionary<DateTime, decimal> companyWideProducedCP, // NEW
    DateOnly dateFrom,
    DateOnly dateTo)
```

**Updated algorithm:**
1. Get product complexity points from `product.ManufactureDifficulty`
2. Calculate M1_A for all months using `CalculateM1_A_PerMonth()`
3. Calculate M1_B for production months using `CalculateM1_B_PerMonth()`
4. For each month in range:
   - Get costs for M0, M2, M3 (existing logic)
   - Use M1_A for cost breakdown and cumulative totals
   - Include both M1_A and M1_B in monthly margin data
5. Calculate averages (M1_A average across all months, M1_B average across production months)

## Calculation Formulas

### M1_A (Economic Baseline)

```
For product P in month M:

M1_A_P_M = CP_P × cost_per_CP_M

Where:
  CP_P = product.ManufactureDifficulty (current value)
  cost_per_CP_M = Σ(M1_costs_12mo) / Σ(produced_CP_12mo)

  12mo period = [M-11, M-10, ..., M-1, M]
```

**Example:**
- Product CP: 5.0
- Last 12 months M1 costs: 120,000 CZK
- Last 12 months produced CP: 10,000
- cost_per_CP = 120,000 / 10,000 = 12 CZK/CP
- M1_A = 5.0 × 12 = 60 CZK per unit

### M1_B (Actual Monthly Cost)

```
For product P in month M (when produced):

M1_B_P_M = CP_P × M1_B_per_CP_M

Where:
  CP_P = product.ManufactureDifficulty
  M1_B_per_CP_M = M1_costs_M / produced_CP_M

  M1_costs_M = Total company M1 costs for month M
  produced_CP_M = Total company produced CP for month M
```

**Example:**
- Product CP: 5.0
- Month M1 costs: 8,000 CZK
- Month produced CP: 800 (all products)
- M1_B_per_CP = 8,000 / 800 = 10 CZK/CP
- M1_B = 5.0 × 10 = 50 CZK per unit

## Edge Cases & Special Scenarios

### 1. No Production in a Month (Specific Product)
- **M1_A:** Always calculated (uses company-wide data)
- **M1_B:** Returns `null` (product not produced)
- **Spec reference:** Section 4.3 - "M1_B is not calculated in months without production"

### 2. No Company-Wide Production
- **Scenario:** `Σ(produced_CP) = 0` for entire company
- **Result:** `cost_per_CP` = division by zero
- **Handling:** M1_A = 0, M1_B = null, log warning
- **Message:** "No production data available for period {period}"

### 3. Product CP Changes During Period
- **Scenario:** ManufactureDifficulty updated (e.g., 5.0 → 8.0 on 2025-06-01)
- **Handling:**
  - Historical calculations: Use CP valid at production date
  - Current M1_A: Use current CP value
  - Production records retain historical CP context via versioning

### 4. Incomplete Cost Data
- **Scenario:** M1 costs missing for some months
- **Handling:**
  - M1 cost = 0: Include in calculation (represents zero cost)
  - M1 cost missing: Exclude from Σ calculations
  - Log warning: "Missing M1 cost data for month {month}"

### 5. Short Historical Period
- **Scenario:** Calculating M1_A in month 3, only 3 months of data available
- **Handling:** Use available data (rolling window < 12 months)
- **Result:** M1_A still valid, based on shorter history
- **Note:** No special handling needed

### 6. Versioned Complexity Points
- **Scenario:** Using historical production data with old CP values
- **Handling:** Query `ManufactureDifficultySettings.GetValueAt(date)` for correct CP
- **Ensures:** Accurate historical calculations respecting technology changes

## Testing Strategy

### Unit Tests

**File:** `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/MarginCalculationServiceTests.cs`

```csharp
// Core calculation tests
CalculateM1_A_WithFullYearData_ReturnsCorrectBaseline()
CalculateM1_B_WhenProductNotProduced_ReturnsNull()
CalculateM1_B_WhenProductProduced_ReturnsActualCost()

// Edge case tests
CalculateM1_A_WithLessThan12Months_UsesAvailableData()
CompanyWideCP_WithZeroProduction_ReturnsZero()
VersionedComplexityPoints_UsesCorrectCPForProductionDate()

// Spec compliance tests
M1_A_RollingAverage_MatchesSpecFormula()
M1_B_Monthly_MatchesSpecFormula()
LongTermAverage_M1_B_ApproximatesM1_A()
```

### Integration Tests

```csharp
GetMarginAsync_WithRealRepositories_CalculatesM1Correctly()
```

**Verification:**
- Real database with production records, M1 costs, CP settings
- Returns `MonthlyMarginHistory` with correct M1_A and M1_B values
- Validates against known expected values

### Specification Compliance

**From spec Section 8 (Controls):**
- ✅ `Σ allocated M1_A ≈ Σ M1 costs for the period`
- ✅ `long-term average M1_B ≈ M1_A` (within ~5% tolerance)
- ✅ Significant fluctuations without CP changes = suspected error (logged)

## Implementation Tasks

### Phase 1: Data Model Changes
1. Update `MonthlyMarginData` class (split M1 → M1_A + M1_B)
2. Update `MarginData` class (add M1_A and M1_B)
3. Update `CostBreakdown` to use M1_A for cumulative totals

### Phase 2: Calculation Methods
4. Implement `CalculateCompanyWideProducedCPAsync()`
5. Implement `CalculateM1_A_PerMonth()`
6. Implement `CalculateM1_B_PerMonth()`

### Phase 3: Service Integration
7. Update `GetMarginAsync()` signature (add allProducts parameter)
8. Update `CalculateMarginHistoryFromData()` to use new M1 calculations
9. Update `CalculateMarginForMonth()` to populate M1_A and M1_B
10. Update `CalculateMarginAverages()` to average M1_A and M1_B separately

### Phase 4: Caller Updates
11. Find all callers of `GetMarginAsync()`
12. Update each caller to provide `allProducts` parameter

### Phase 5: Testing
13. Write unit tests for all new calculation methods
14. Write integration tests with real repositories
15. Write specification compliance tests

### Phase 6: Verification
16. Run backend build (`dotnet build`)
17. Run all tests (`dotnet test`)
18. Validate against specification requirements

## Migration & Backward Compatibility

**Breaking Changes:**
- `MonthlyMarginData.M1` → `MonthlyMarginData.M1_A` + `MonthlyMarginData.M1_B`
- `GetMarginAsync()` signature requires additional parameter

**Migration Path:**
1. Update domain models first
2. Update service implementation
3. Update all callers to provide allProducts
4. Frontend API will receive new M1_A/M1_B structure

**Caching Impact:**
- Existing cached margin data in `CatalogAggregate.Margin` will be invalidated
- Next calculation will use new CP-based formulas
- No database migration needed (calculation-only change)

## Performance Considerations

**Company-Wide CP Calculation:**
- Loads all products once per `GetMarginAsync()` call
- Aggregates production history across all products
- For batch operations: Caller can calculate once and reuse

**Optimization Opportunities:**
- Cache company-wide CP data at application level
- Pre-calculate for common date ranges
- Not needed initially (YAGNI)

## Success Criteria

1. ✅ M1_A calculated using rolling 12-month window per specification
2. ✅ M1_B calculated only for production months per specification
3. ✅ Both use CP-weighted formulas: `productCP × cost_per_CP`
4. ✅ Edge cases handled gracefully (zero production, missing data)
5. ✅ All tests pass (unit, integration, spec compliance)
6. ✅ Backend builds without errors
7. ✅ Specification controls validated (averages align)

## References

- [M1 Production Costs Specification](./M1_production_costs_specification.md)
- Current implementation: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/MarginCalculationService.cs`
- Domain models: `backend/src/Anela.Heblo.Domain/Features/Catalog/`
- Architecture docs: `/docs/architecture/`
