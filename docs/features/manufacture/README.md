# Manufacture Domain

## Overview

The Manufacture domain is the core production planning and manufacturing management system within the Anela Heblo ERP. It handles Bill of Materials (BoM) management, production planning optimization, stock forecasting, and material tracking. The domain integrates deeply with the FlexiBee ERP system to synchronize manufacturing templates and implements sophisticated algorithms for batch distribution optimization and demand forecasting.

## Domain Model

### Core Aggregates

#### ManufactureStockAggregate

The primary aggregate managing manufacturing stock and production planning data.

**Key Attributes**

**Product Identification**
- **ProductId**: Internal product identifier
- **ProductCode**: Standardized product code
- **ProductName**: Product display name
- **ProductFamily**: Derived from first 6 characters of product code
- **ProductType**: Derived from first 3 characters of product code
- **SizeCode**: Remaining characters after product family

**Stock Configuration**
- **OptimalStockDaysSetup**: Target days of inventory to maintain
- **StockMinSetup**: Minimum stock threshold
- **BatchSize**: Manufacturing batch size for planning

**Current Stock Levels**
- **OnStockEshop**: E-commerce platform inventory
- **OnStockTransport**: In-transit inventory
- **OnStockReserve**: Reserved/allocated stock
- **OnStockSum**: Total available stock (computed)

**Sales Analytics**
- **AmountB2C**: Business-to-consumer quantities
- **AmountB2B**: Business-to-business quantities
- **SalesB2C**: B2C sales value
- **SalesB2B**: B2B sales value
- **DailySalesSum**: Combined daily sales velocity

**Computed Properties**
- **IsUnderStocked**: Current stock < minimum threshold
- **IsUnderForecasted**: Forecasted days < optimal setup
- **OptimalStockPercentage**: Current stock as % of optimal
- **OptimalStockDaysForecasted**: Days of supply at current velocity
- **Severity**: Stock urgency classification

#### ManufactureTemplate

Represents a Bill of Materials with scaling capabilities.

**Key Attributes**
- **TemplateId**: Unique template identifier
- **Amount**: Base production quantity
- **Ingredients**: Collection of required materials
- **Description**: Template description

**Business Methods**
- Recipe scaling based on total amount
- Ingredient-based scaling calculations
- Proportional ingredient adjustment

#### ProductVariant

Represents different sizes/variants of manufactured products.

**Key Attributes**
- **VariantId**: Unique variant identifier
- **ProductCode**: Product code for this variant
- **Volume**: Product volume/size
- **Weight**: Product weight
- **DailySales**: Average daily sales
- **CurrentAmount**: Current inventory level

**Business Methods**
- **GetUpstockSuggestion()**: Calculates recommended production quantity
- Sales velocity analysis
- Stock adequacy assessment

#### Ingredient

Component of manufacturing templates representing raw materials.

**Key Attributes**
- **IngredientId**: Unique ingredient identifier
- **ProductCode**: Material product code
- **Amount**: Required quantity
- **PricePerKg**: Material cost per kilogram

### Value Objects

#### ProductBatch

Encapsulates batch production planning data.

**Key Attributes**
- **TotalWeight**: Maximum batch weight capacity
- **ProductVariants**: Collection of variants for optimization
- **ValidVariants**: Filtered variants suitable for production

**Business Methods**
- Variant filtering for production eligibility
- Weight constraint validation
- Batch composition optimization

## Business Rules and Processes

### Stock Management Rules

#### Stock Severity Classification

The system automatically classifies stock urgency:

```csharp
public StockSeverity Severity
{
    get
    {
        if (OptimalStockDaysForecasted < 8 && IsOptimalStockConfigured)
            return StockSeverity.Critical;   // Immediate action required
        if((OptimalStockDaysForecasted < 15 && IsOptimalStockConfigured) || IsUnderStocked)
            return StockSeverity.Major;      // Action needed soon
        if (IsUnderForecasted)
            return StockSeverity.Minor;      // Monitor closely
        return StockSeverity.None;           // Adequate stock
    }
}
```

#### Stock Health Indicators

1. **IsUnderStocked**: Current stock below configured minimum
2. **IsUnderForecasted**: Projected stock depletion before optimal threshold
3. **OptimalStockPercentage**: Current stock efficiency metric
4. **OptimalStockDaysForecasted**: Days until stock runs out at current velocity

### Manufacturing Business Rules

1. **Batch Size Optimization**: Production must respect configured batch sizes
2. **Weight Constraints**: Total production cannot exceed batch weight limits
3. **Demand-Based Planning**: Production allocation based on sales velocity
4. **Material Availability**: Ensure sufficient raw materials before production
5. **Lot Tracking**: Track materials with expiration dates separately

## Batch Distribution Calculation

### Advanced Optimization Algorithm

The `BatchDistributionCalculator` implements sophisticated optimization for production planning.

#### Primary Algorithm: Binary Search Optimization

1. **Maximum Days Calculation**: Finds maximum sustainable production days
2. **Weight Distribution**: Optimally distributes variants within weight constraints
3. **Sales-Based Allocation**: Allocates production based on demand velocity
4. **Residue Minimization**: Distributes remaining capacity to minimize waste

#### Key Methods

```csharp
OptimizeBatch(ProductBatch batch, Dictionary<string, decimal> salesVelocity)
```
- Uses binary search to find optimal production timeframe
- Maximizes production days while respecting constraints
- Returns optimized variant quantities

```csharp
OptimizeBatch2(ProductBatch batch, Dictionary<string, decimal> salesVelocity)
```
- Alternative algorithm focusing on maximum utilization
- Prioritizes filling production capacity
- Handles edge cases with low sales velocity

```csharp
DistributeRemainingWeight(variants, remainingWeight, totalSales)
```
- Allocates residual batch capacity
- Proportional distribution based on sales velocity
- Ensures no variant exceeds practical limits

### Optimization Constraints

1. **Total Weight Limit**: Sum of all variants ≤ batch weight capacity
2. **Individual Variant Limits**: Each variant ≤ configured maximum
3. **Sales Velocity Alignment**: Production aligned with demand patterns
4. **Minimum Production Quantities**: Respect economical production runs

## Stock Taking Operations

### Material Stock Taking Process

**Service**: `ManufactureStockTakingAppService`

#### Capabilities

1. **Physical Count Management**
   - Record actual inventory counts
   - Compare against system records
   - Track count discrepancies

2. **Lot-Based Tracking**
   - Individual lot identification
   - Expiration date management
   - Lot-specific quantity tracking

3. **ERP Integration**
   - Automatic synchronization with FlexiBee
   - Real-time stock level updates
   - Audit trail maintenance

4. **Validation Features**
   - Dry-run stock taking for validation
   - Count verification before commit
   - Error detection and reporting

#### Stock Taking Workflow

1. **Preparation Phase**
   - Generate stock taking lists
   - Identify materials requiring counts
   - Prepare lot tracking sheets

2. **Counting Phase**
   - Physical inventory counts
   - Lot identification and verification
   - Expiration date validation

3. **Validation Phase**
   - Compare counts against system
   - Review significant discrepancies
   - Approve or reject adjustments

4. **Commit Phase**
   - Update ERP system with new quantities
   - Sync with catalog domain
   - Generate adjustment reports

## ERP System Integration

### FlexiBee Integration

#### FlexiManufactureRepository

Manages synchronization with FlexiBee ERP for manufacturing data.

**Key Responsibilities**
- Retrieve Bill of Materials from ERP
- Sync manufacturing templates
- Handle hierarchical BoM structures
- Map ERP data to domain entities

**Data Mapping Rules**
- Level 1 records = BoM headers
- Level ≠ 1 records = Ingredients/components
- Remove "code:" prefix from FlexiBee product codes
- Maintain bidirectional ERP mapping

#### Integration Patterns

```csharp
// BoM Retrieval
GetManufactureTemplatesAsync(IEnumerable<string> templateIds)

// Code Mapping
MapFlexiBeeCode(string flexiCode) // Removes "code:" prefix
MapToFlexiBeeCode(string domainCode) // Adds "code:" prefix

// Hierarchical Processing
ProcessBoMHierarchy(FlexiBeeBoMData data)
```

### External Data Sources

1. **FlexiBee ERP**: Manufacturing templates and stock data
2. **Catalog Repository**: Product master data and current stock
3. **Sales Analytics**: Historical sales for demand forecasting
4. **Logistics System**: Transport and allocation data

## Application Services

### ManufactureAppService

Primary service managing manufacturing operations.

#### Core Operations

**Template Management**
```csharp
GetManufactureTemplatesAsync(templateIds)
GetCustomAmountAsync(query) // Scaled template calculations
```

**Stock Analysis**
```csharp
GetManufactureStockAsync(query) // Stock level analysis
GetBatchDistributionAsync(request) // Production optimization
```

### ManufactureStockAppService

Handles stock level queries and analysis.

**Filtering Capabilities**
- Product family/type filtering
- Stock severity filtering
- Date range analysis
- Multi-criteria search

### ManufactureStockTakingAppService

Manages inventory counting operations.

**Key Features**
- Lot-based counting
- Expiration date tracking
- ERP synchronization
- Audit trail maintenance

## Inter-Domain Communication

### Catalog Domain Integration

**Dependencies**
- `ICatalogRepository`: Product master data access
- `CatalogAggregate`: Current stock levels and sales history
- Product type classification (Material vs Product)
- Sales velocity calculations

**Data Flow**
```
Catalog → Sales Data → Demand Forecasting → Production Planning
      ↓
Current Stock → Stock Analysis → Restocking Recommendations
```

### Sales Analytics Integration

**Sales Data Consumption**
- B2B and B2C sales tracking
- Daily velocity calculations
- Historical trend analysis
- Seasonal pattern recognition

### Logistics Domain Integration

**Stock Movement Tracking**
- Transport stock allocation
- E-commerce stock levels
- Reserve stock management
- Multi-location inventory

## Use Cases

### Primary Use Cases

1. **Production Planning**
   - Analyze stock levels and demand
   - Generate production recommendations
   - Optimize batch compositions
   - Schedule manufacturing runs

2. **Material Planning**
   - Calculate material requirements
   - Check material availability
   - Plan procurement needs
   - Track material consumption

3. **Stock Monitoring**
   - Monitor stock health across all products
   - Identify critical stock situations
   - Generate restocking alerts
   - Track stock movement trends

4. **Inventory Management**
   - Conduct physical stock counts
   - Manage lot tracking and expiration
   - Synchronize with ERP systems
   - Maintain inventory accuracy

### Operational Workflows

1. **Weekly Production Planning**
   - Review stock severity reports
   - Optimize batch compositions
   - Generate production schedules
   - Coordinate with procurement

2. **Daily Stock Monitoring**
   - Check critical stock alerts
   - Review overnight sales impact
   - Adjust production priorities
   - Update delivery schedules

3. **Monthly Stock Taking**
   - Plan inventory count cycles
   - Execute physical counts
   - Reconcile discrepancies
   - Update system records

## Performance Considerations

### Optimization Strategies

1. **Caching**: Template and product data caching
2. **Batch Processing**: Bulk operations for large datasets
3. **Async Operations**: Non-blocking ERP integration
4. **Memory Management**: Efficient large dataset handling

### Scalability Features

- Linear scaling with product count
- Efficient algorithm complexity (O(n log n) for optimization)
- Parallel processing capabilities
- Database query optimization

## Configuration

### Batch Settings
- Default batch sizes per product family
- Weight constraints per production line
- Optimization algorithm parameters

### Stock Settings
- Optimal stock day targets
- Minimum stock thresholds
- Severity classification rules

### Integration Settings
- FlexiBee connection parameters
- Sync frequencies
- Error handling policies

## Business Value

The Manufacture domain delivers significant operational value:

1. **Production Efficiency**: Optimal batch planning reduces waste and maximizes throughput
2. **Stock Optimization**: Maintains adequate inventory while minimizing carrying costs
3. **Demand Forecasting**: Data-driven production planning based on sales patterns
4. **Material Management**: Ensures material availability while tracking expiration
5. **Integration**: Seamless ERP integration maintains data accuracy
6. **Automation**: Reduces manual planning effort and human error