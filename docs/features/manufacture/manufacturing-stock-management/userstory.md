# Manufacturing Stock Management User Story

## Feature Overview
The Manufacturing Stock Management feature provides comprehensive stock monitoring and production decision support by analyzing sales patterns, stock levels, and production history for manufactured products. This specialized view over catalog data enables production managers to identify critical stock shortages, forecast requirements, and optimize production planning through sophisticated filtering and analysis capabilities with flexible time period selection.

## Business Requirements

### Primary Use Case
As a production manager, I want to analyze stock levels and sales patterns across all manufactured products so that I can identify critical stock shortages, forecast future production requirements, prioritize production orders, and maintain optimal inventory levels while avoiding stockouts and overproduction.

### Acceptance Criteria
1. The system shall analyze stock levels against configured minimum and optimal thresholds from CatalogProperties
2. The system shall calculate sales rates based on selectable time periods (previous quarter, future quarter Y2Y, previous season)
3. The system shall forecast stock depletion dates using historical sales data from selected period
4. The system shall provide comprehensive filtering by stock status and configuration completeness
5. The system shall support flexible analysis periods with user-selectable time windows
6. The system shall integrate product family information and sales history
7. The system shall prioritize results by stock efficiency percentage
8. The system shall focus on finished products (excluding raw materials and semi-products)
9. UI should display filtered list of products showing: Code, Name, Current Stock (Skladem), Sales for selected period, Daily Sales Rate, Optimal Stock Days Setup, Stock Days Available (Zásoba dni), Minimum Stock Setup, Overstock % (Nádžsklad %), Batch Size (ks/šarže)

## Data
1. Primary source of data is CatalogRepository (CatalogAggregate object), module has no own persistent data
2. Time period selection determines sales analysis scope:
   - **Previous Quarter**: Last 3 months sales data
   - **Future Quarter Y2Y**: Same 3 months from previous year (forecasting)
   - **Previous Season**: October-January of previous year
   - **Custom Period**: User-defined date range

## Happy Day Scenario

1. **Analysis Request**: Production manager selects "Previous Quarter" time period for stock analysis
2. **Data Retrieval**: System fetches catalog data filtered for finished products only
3. **Sales Calculation**: Calculate sales velocity based on selected time period
4. **Stock Assessment**: Evaluate current stock against configured CatalogProperties thresholds
5. **Days Calculation**: Calculate stock days available and overstock percentage
6. **Filtering Application**: Apply user-specified filters to focus on problem areas
7. **Production Recommendations**: Calculate production quantities for critical items
8. **Results Presentation**: Display prioritized list sorted by stock efficiency

## Error Handling

### Data Validation Errors
- **Invalid Time Period**: Ensure time period selection is valid and contains data
- **Missing Configuration**: Handle products without CatalogProperties.OptimalStockDaysSetup gracefully
- **Zero Sales**: Avoid division by zero in stock days calculations
- **Negative Stock**: Validate stock levels and sales data integrity

### Integration Errors
- **Catalog Access Failures**: Fallback to cached data or partial results
- **Mapping Errors**: Handle mismatched product types or missing CatalogProperties
- **Performance Issues**: Implement timeouts and chunked processing for large datasets
- **Sales Data Issues**: Handle missing or invalid sales information for selected periods

### Business Logic Errors
- **Configuration Conflicts**: Detect inconsistent min/optimal stock settings in CatalogProperties
- **Sales Anomalies**: Identify unusual sales patterns requiring review in selected periods
- **Forecasting Limits**: Handle extreme forecasting scenarios (very high/low sales)
- **Seasonal Adjustments**: Handle seasonal products during off-season periods

## Business Rules

### Product Classification
1. **Finished Products**: Focus analysis on final manufactured products ready for sale
2. **Exclude Semi-Products**: Semi-products are not included in this analysis
3. **Exclude Raw Materials**: Raw materials belong to purchase analysis, not manufacturing

### Stock Thresholds (from CatalogProperties)
1. **Minimum Stock**: CatalogProperties.StockMinSetup - absolute minimum before stockout risk
2. **Optimal Stock Days**: CatalogProperties.OptimalStockDaysSetup - target days of inventory to maintain
3. **Batch Size**: CatalogProperties.BatchSize - production batch size for efficiency
4. **Configuration Completeness**: OptimalStockDaysSetup and StockMinSetup must be set for full analysis

### Time Period Analysis
1. **Period Selection**: User selects from predefined periods or custom range
2. **Daily Sales Calculation**: Total sales in period divided by days in period
3. **Seasonal Consideration**: Previous season option for seasonal product planning
4. **Y2Y Forecasting**: Use same period from previous year for demand forecasting

### Stock Calculations
1. **Stock Days Available**: Current stock divided by daily sales rate from selected period
2. **Overstock Percentage**: (Current stock days / Optimal stock days setup) * 100
3. **Production Priority**: Products with lowest stock days get highest priority
4. **Batch Optimization**: Consider CatalogProperties.BatchSize in production recommendations

No persistent data storage required - all data sourced from CatalogRepository on-demand.

## UI Data Display Requirements

### Manufacturing Stock Analysis View

The UI should display a filterable table with the following columns for finished products:

| Column | Source | Calculation | Czech Label |
|--------|--------|-------------|-------------|
| **Code** | CatalogAggregate.Code | Direct | Kód |
| **Name** | CatalogAggregate.Name | Direct | Název |
| **Current Stock** | CatalogAggregate.StockSum | Sum of all stock locations | Skladem |
| **Sales (Period)** | CatalogAggregate.SalesHistory | Sum of sales in selected time period | Prodeje období |
| **Daily Sales** | CatalogAggregate.SalesHistory | Period sales ÷ days in period | Prodeje/den |
| **Optimal Days Setup** | CatalogProperties.OptimalStockDaysSetup | Direct from configuration | Nastavený nadsklad |
| **Stock Days Available** | Calculated | Current stock ÷ daily sales | Zásoba dni |
| **Minimum Stock** | CatalogProperties.StockMinSetup | Direct from configuration | Minimální zásoba |
| **Overstock %** | Calculated | (Stock days available ÷ optimal days setup) × 100 | Nadsklad % |
| **Batch Size** | CatalogProperties.BatchSize | Direct from configuration | ks/šarže |

### Time Period Selection Controls

- **Previous Quarter** (Minulý kvartal): Last 3 completed months
- **Future Quarter Y2Y** (Budoucí kvartal Y2Y): Same 3 months from previous year
- **Previous Season** (Předchozí sezona): October-January of previous year
- **Custom Period** (Vlastní období): User-defined date range

### Filtering Options

1. **Product Family** (Produktová řada): Dropdown with available families
2. **Critical Items Only** (Pouze kritické): Show products with overstock < 100%
3. **Unconfigured Only** (Pouze nedefìnované): Products missing OptimalStockDaysSetup
4. **Search Text** (Hledat): Product code or name search

### Severity Classification

Products are classified into 4 color-coded categories based on stock levels and configuration:

- **Red (Červené)**: Overstock < 100% (Nadsklad < 100%) - Critical shortage situation
- **Orange (Oranžové)**: Current stock < minimum stock setup (Skladem < min zásoba) - Below safety threshold
- **Gray (Nezkonfigurované)**: Missing OptimalStockDaysSetup configuration - Cannot be analyzed
- **Green (OK)**: All conditions met - Current stock > minimum stock AND overstock > 100% AND properly configured

## Implementation Approach

### Data Retrieval Strategy
1. **No separate storage** - query CatalogRepository directly for real-time data
2. **Time period filtering** - apply date range filters to sales history queries  
3. **On-demand calculations** - compute stock days and percentages during query processing
4. **Caching** - cache computed results for 15 minutes to improve performance

### Key Calculations
```csharp
// Stock Days Available
stockDaysAvailable = currentStock / dailySalesRate

// Daily Sales Rate  
dailySalesRate = totalSalesInPeriod / daysInPeriod

// Overstock Percentage
overstockPercentage = (stockDaysAvailable / optimalStockDaysSetup) * 100

// Severity Classification (New Logic)
if (optimalStockDaysSetup <= 0) 
    return Severity.Unconfigured  // Gray - Missing configuration
else if (overstockPercentage < 100) 
    return Severity.Critical      // Red - Overstock < 100%
else if (currentStock < minStockSetup) 
    return Severity.Major         // Orange - Below minimum stock
else 
    return Severity.Adequate      // Green - All conditions OK
```