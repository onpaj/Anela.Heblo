# M1_A Implementation Notes

## Overview

Implemented flat manufacture cost calculation (M1_A) that distributes manufacturing costs across products using ManufactureDifficulty metric.

## Implementation Details

### Algorithm

1. **Get Manufacturing Costs**: Fetch total costs from ILedgerService for VYROBA department
2. **Get Manufacture History**: Fetch all product manufacture history for the period
3. **Calculate Weighted Points**: For each month:
   - Sum weighted manufacturing points: `Σ(amount × difficulty)` for all products
   - Get manufacturing costs for that month
   - Calculate cost per point: `totalCosts / totalWeightedPoints`
4. **Distribute Costs**: Calculate cost for specific product: `costPerPoint × productDifficulty`

### Key Components

- `FlatManufactureCostProvider`: Main implementation class
- `GetHistoricalDifficultyAsync()`: Helper to get difficulty at specific date
- `CalculateFlatManufacturingCostsAsync()`: Core calculation logic

### Dependencies

- `ILedgerService`: Provides manufacturing costs
- `IManufactureHistoryClient`: Provides manufacture history
- `IManufactureDifficultyRepository`: Provides difficulty settings
- `IFlatManufactureCostCache`: Caches computed results

### Edge Cases Handled

- No manufacture history → returns empty list
- No manufacturing costs → returns zero costs
- Product not manufactured in period → returns cost based on difficulty (flat allocation)
- Missing difficulty setting → uses default value of 1

## Testing

### Test Coverage

- Single product cost distribution
- Multiple products with different difficulties
- Edge cases (no history, no costs, product not manufactured)
- Historical difficulty lookup

### Test Files

- `FlatManufactureCostProviderTests.cs`: Unit tests for M1_A calculation

## Configuration

Uses `CostCacheOptions.M1ARollingWindowMonths` (default: 12 months) for rolling window size.

## Performance Considerations

- Calculation is cached in `FlatManufactureCostCache`
- Refreshed periodically (configurable interval)
- Single semaphore prevents concurrent refreshes
