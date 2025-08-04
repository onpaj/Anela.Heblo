# Purchase Stock Level Analysis User Story

## Feature Overview
The Purchase Stock Analysis feature provides comprehensive stock monitoring and purchasing decision support by analyzing consumption patterns, stock levels, and purchase history. This specialized view over catalog data enables procurement officers to identify stock shortages, forecast requirements, and optimize inventory levels through sophisticated filtering and analysis capabilities.

## Business Requirements

### Primary Use Case
As a procurement officer, I want to analyze stock levels and consumption patterns across all purchasable products so that I can identify critical stock shortages, forecast future requirements, prioritize purchase orders, and maintain optimal inventory levels while avoiding stockouts and excess inventory.

### Acceptance Criteria
1. The system shall analyze stock levels against configured minimum and optimal thresholds
2. The system shall calculate consumption rates based on product type (materials vs goods)
3. The system shall forecast stock depletion dates using historical consumption data
4. The system shall provide comprehensive filtering by stock status and configuration completeness
5. The system shall support flexible analysis periods with default to one year
6. The system shall integrate supplier information and purchase history
7. The system shall prioritize results by stock efficiency percentage
8. The system shall distinguish between materials (manufacturing consumption) and goods (sales consumption)
9. UI should view filtered list of materials and goods showing: Code, Name, AvailableStock, Consumption for defined period (1Y as default), Min stock level, Optimal stock level, MOQ, Last Purchase data (Supplier, amount and price)

## Data
1. Primary source of data is CatalogRepository (CatalogAggregate object), module has no own persistent data

## Happy Day Scenario

1. **Analysis Request**: Procurement officer requests stock analysis for last 6 months
2. **Data Retrieval**: System fetches catalog data filtered for materials and goods
3. **Consumption Calculation**: Calculate consumption based on product type (manufacturing vs sales)
4. **Stock Assessment**: Evaluate current stock against configured thresholds
5. **Severity Analysis**: Classify each product by stock urgency level
6. **Filtering Application**: Apply user-specified filters to focus on problem areas
7. **Recommendation Generation**: Calculate purchase quantities for critical items
8. **Results Presentation**: Display prioritized list sorted by stock efficiency

## Error Handling

### Data Validation Errors
- **Invalid Date Range**: Ensure fromDate <= toDate and reasonable analysis periods
- **Missing Configuration**: Handle products without min/optimal stock settings gracefully
- **Zero Consumption**: Avoid division by zero in forecasting calculations
- **Negative Stock**: Validate stock levels and consumption data integrity

### Integration Errors
- **Catalog Access Failures**: Fallback to cached data or partial results
- **Mapping Errors**: Handle mismatched product types or missing properties
- **Performance Issues**: Implement timeouts and chunked processing for large datasets
- **Supplier Data Issues**: Handle missing or invalid supplier information

### Business Logic Errors
- **Configuration Conflicts**: Detect inconsistent min/optimal stock settings
- **Consumption Anomalies**: Identify unusual consumption patterns requiring review
- **Forecasting Limits**: Handle extreme forecasting scenarios (very high/low consumption)
- **Currency Mismatches**: Ensure consistent currency handling in cost calculations

## Business Rules

### Product Classification
1. **Materials**: Use manufacturing consumption data for analysis
2. **Goods**: Use sales velocity data for analysis
3. **Other Types**: Exclude from purchase analysis (e.g., services, labor)

### Stock Thresholds
1. **Minimum Stock**: Absolute minimum before stockout risk
2. **Optimal Stock**: Target days of inventory to maintain
3. **Safety Stock**: Additional buffer for consumption volatility
4. **Configuration Completeness**: Both min and optimal must be set for full analysis

### Consumption Analysis
1. **Analysis Period**: Default to 1 year, allow custom periods
2. **Daily Calculation**: Total consumption divided by days in period
3. **Seasonal Adjustment**: Consider seasonal patterns in consumption
4. **Data Quality**: Exclude periods with known data issues

### Purchase Recommendations
1. **Severity-Based Priority**: Critical items get immediate attention
2. **Economic Order Quantities**: Consider supplier minimum orders
3. **Lead Time Consideration**: Factor in supplier delivery times
4. **Cash Flow Impact**: Balance stock needs with financial constraints

## Persistence Layer Requirements
