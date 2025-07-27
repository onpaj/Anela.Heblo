# Purchase Domain

## Overview

The Purchase domain provides procurement planning and stock analysis capabilities. It analyzes consumption patterns, stock levels, and purchase history to support purchasing decisions. The domain creates a specialized view over the catalog data specifically for procurement officers to identify stock shortages, forecast requirements, and track supplier performance.

## Domain Model

### Core Aggregate: PurchaseStockAggregate

The `PurchaseStockAggregate` represents a purchasable product with comprehensive stock analysis and consumption tracking.

#### Key Attributes

**Product Identification**
- **ProductCode**: Unique product identifier
- **ProductName**: Product display name

**Stock Configuration**
- **OptimalStockDaysSetup**: Target days of inventory to maintain
- **StockMinSetup**: Minimum stock threshold

**Current State**
- **OnStockNow**: Current inventory level
- **Consumed**: Total consumption in period
- **ConsumedDaily**: Average daily consumption rate

**Analysis Period**
- **DateFrom**: Analysis start date
- **DateTo**: Analysis end date
- **Days**: Number of days in analysis period

**Supplier Information**
- **Suppliers**: Collection of supplier entities
- **PurchaseHistory**: Historical purchase transactions

#### Computed Properties

**Stock Analysis**
- **ConsumedDaily**: `Consumed / Days` - Average daily consumption
- **OptimalStockDaysForecasted**: `OnStockNow / ConsumedDaily` - Days until stock depletion
- **OptimalStockPercentage**: Current stock efficiency ratio

**Configuration Status**
- **IsMinStockConfigured**: Minimum stock threshold is set
- **IsOptimalStockConfigured**: Optimal stock days are configured

**Stock Health Indicators**
- **IsUnderStocked**: Current stock below minimum threshold
- **IsUnderForecasted**: Projected days below optimal target
- **IsOk**: All conditions met (configured and adequately stocked)

### Value Objects

#### PurchaseHistoryData
Historical purchase transaction record:
- **Date**: Purchase date
- **Quantity**: Purchased amount
- **SupplierCode**: Vendor identifier
- **SupplierName**: Vendor name
- **PurchasePrice**: Unit cost

## Business Rules and Logic

### Stock Assessment Rules

1. **Under-Stocked Detection**
   ```csharp
   IsUnderStocked = OnStockNow < StockMinSetup && IsMinStockConfigured
   ```

2. **Forecasting Logic**
   ```csharp
   IsUnderForecasted = OptimalStockDaysForecasted < OptimalStockDaysSetup && IsOptimalStockConfigured
   ```

3. **Overall Health Status**
   ```csharp
   IsOk = IsMinStockConfigured && IsOptimalStockConfigured && !IsUnderStocked && !IsUnderForecasted
   ```

### Product Type Filtering

The domain processes only specific product types:
- **Material**: Raw materials and components
- **Goods**: Finished goods for resale

Different consumption calculation methods:
- **Materials**: Use manufacturing consumption data
- **Goods**: Use sales velocity data

### Consumption Calculation

**For Materials**:
- Uses `GetConsumed(dateFrom, dateTo)` from catalog
- Tracks material usage in manufacturing

**For Goods**:
- Uses `GetTotalSold(dateFrom, dateTo)` from catalog
- Tracks B2B and B2C sales combined

## Application Services

### PurchaseStockAppService

Primary service for purchase stock analysis and querying.

#### Core Operations

**Query Interface**
```csharp
GetListAsync(PurchaseStockQueryDto input)
```

Supports comprehensive filtering:
- Date range specification
- Product code/name search
- Status-based filtering
- Pagination and sorting

#### Query Parameters

**PurchaseStockQueryDto**
- **DateFrom/DateTo**: Analysis period
- **Filter**: Text search in product code/name
- **ShowOk**: Include products with adequate stock
- **ShowUnderStocked**: Include under-stocked products
- **ShowUnderForecasted**: Include under-forecasted products
- **ShowMissingMinStock**: Include products without min stock config
- **ShowMissingOptimalStock**: Include products without optimal config

#### Business Logic

**Default Behavior**
- Analysis period defaults to last year if not specified
- Sorting by `OptimalStockPercentage` (worst first)
- Authorization required for access

**Filtering Logic**
- Negative filtering approach (exclude unwanted statuses)
- Product type restriction (Material/Goods only)
- Comprehensive status-based segmentation

## Repository Implementation

### PurchaseStockRepository

Adapter implementation over the catalog repository:

```csharp
public class PurchaseStockRepository : IPurchaseStockRepository
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly PurchaseCatalogFilter _filter;
}
```

#### Filter Implementation

**PurchaseCatalogFilter**
- Restricts to Material and Goods product types
- Provides purchase-specific catalog filtering
- Ensures consistent product classification

#### Data Transformation

Three-layer mapping:
1. **CatalogAggregate** → Raw catalog data
2. **PurchaseStockAggregate** → Domain model with purchase logic
3. **PurchaseStockDto** → Application layer DTO

## Integration Points

### Catalog Domain Integration

**Primary Dependency**
- `ICatalogRepository`: Source of product and stock data
- Consumption and sales history access
- Product type classification

### Logistics Domain Integration

**Supplier Management**
- `Anela.Heblo.Logistics.Supplier`: Supplier entity reference
- Supplier relationship tracking
- Primary supplier identification

### External System Integration

**Purchase History Client**
- `IPurchaseHistoryClient`: Historical transaction data
- Supplier performance metrics
- Cost analysis capabilities

## Use Cases

### Primary Use Cases

1. **Stock Level Analysis**
   - Identify under-stocked materials
   - Forecast stock depletion dates
   - Prioritize purchase requirements

2. **Procurement Planning**
   - Generate purchase recommendations
   - Analyze consumption trends
   - Optimize inventory levels

3. **Supplier Management**
   - Track supplier performance
   - Analyze purchase history
   - Manage supplier relationships

4. **Configuration Management**
   - Set minimum stock thresholds
   - Configure optimal stock days
   - Validate configuration completeness

### Operational Workflows

1. **Daily Stock Review**
   - Check critical stock levels
   - Identify immediate purchase needs
   - Update stock configurations

2. **Weekly Procurement Planning**
   - Analyze consumption trends
   - Generate purchase orders
   - Review supplier performance

3. **Monthly Analysis**
   - Stock level optimization
   - Configuration updates
   - Supplier evaluation

## Data Flow

```
Catalog Domain → Purchase Filter → Stock Analysis → Procurement Planning
     ↓                ↓                ↓                ↓
Product Data → Material/Goods → Consumption Calc → Purchase Decisions
     ↓                ↓                ↓                ↓
Sales History → Stock Levels → Forecasting → Order Generation
```

## Query Capabilities

### Filtering Options

**Status-Based Filtering**
- OK: Adequately stocked products
- Under-Stocked: Below minimum threshold
- Under-Forecasted: Below optimal target
- Missing Configuration: Incomplete setup

**Search and Pagination**
- Product code/name text search
- Configurable page sizes
- Sort by stock efficiency

### Performance Optimization

- Efficient catalog repository integration
- Optimized filtering at source
- Pagination for large datasets
- Computed property caching

## Configuration

### Stock Thresholds
- Minimum stock levels per product
- Optimal stock day targets
- Safety stock calculations

### Analysis Parameters
- Default analysis periods
- Consumption calculation methods
- Forecasting algorithms

## Business Value

The Purchase domain provides significant procurement benefits:

1. **Proactive Planning**: Identifies stock shortages before they occur
2. **Cost Optimization**: Prevents emergency purchases and stockouts
3. **Efficiency**: Automated analysis reduces manual effort
4. **Visibility**: Clear status indicators for procurement decisions
5. **Integration**: Leverages existing catalog and sales data
6. **Flexibility**: Configurable thresholds and analysis periods