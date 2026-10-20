# Margin Calculation Cache Implementation

## Overview

Implemented in-memory caching architecture for margin calculation cost sources to eliminate redundant ILedgerService queries.

## Architecture

- **Cache Services**: Singleton services per cost source (M0, M1, M2, M3)
- **Storage**: IMemoryCache (in-memory, no expiration)
- **Hydration**: Tier 2 (after catalog refresh)
- **Refresh**: Periodic via BackgroundRefreshTaskRegistry (6 hours)
- **Pattern**: Stale-while-revalidate (keep old data during refresh)

## Components

### Domain Layer
- `ICostCache` - base cache interface
- `CostCacheData` - immutable cached data wrapper
- `IMaterialCostCache`, `IFlatManufactureCostCache`, `ISalesCostCache`, `IOverheadCostCache` - specific cache interfaces

### Application Layer
- `MaterialCostCache` - M0 cache implementation
- `FlatManufactureCostCache` - M1 cache implementation
- `SalesCostCache` - M2 cache implementation
- `OverheadCostCache` - M3 cache implementation
- `CostCacheOptions` - configuration options

### Cost Sources
Modified to inject cache services and delegate to `GetCachedDataAsync()`:
- `PurchasePriceOnlyMaterialCostSource`
- `ManufactureCostSource`
- `SalesCostSource`
- `OverheadCostProvider`

## Configuration

appsettings.json:
```json
{
  "CostCache": {
    "RefreshInterval": "06:00:00",
    "M1ARollingWindowMonths": 12,
    "HydrationTier": 2,
    "HistoricalDataYears": 2
  }
}
```

## Testing

- Integration tests verify cache hydration and data retrieval
- Cache returns empty data before hydration
- Stale-while-revalidate pattern keeps old data on refresh failure

## Future Work

- Add monitoring/health checks for cache staleness
