# Product Margins Domain

## Overview

The Product Margins domain provides comprehensive margin analysis and profitability tracking for products across the catalog. It calculates and displays various margin metrics by comparing selling prices from the e-shop with purchase costs from the ERP system, enabling business decisions about pricing strategies and product profitability.

## Domain Model

### Core Entity: ProductMarginDto

The `ProductMarginDto` represents margin calculations and cost analysis for individual products.

#### Key Attributes

**Product Identification**
- **ProductCode**: Unique product identifier matching catalog system
- **ProductName**: Human-readable product name for display

**Pricing Information**
- **PriceWithVat**: Selling price including VAT from e-shop
- **PurchasePrice**: Base purchase cost from ERP system

**Cost Analysis**
- **AverageCost**: Historical average cost calculation
- **Cost30Days**: Recent 30-day period cost analysis

**Margin Metrics**
- **AverageMargin**: Margin percentage based on average costs
- **Margin30Days**: Margin percentage for recent 30-day period

#### Computed Properties

**Margin Calculation Formula**
```csharp
Margin = ((SellingPrice - Cost) / SellingPrice) * 100
```

**Margin Interpretation**
- Values expressed as percentages
- Negative margins indicate loss-making products
- Rounded to 2 decimal places for display

## Business Rules and Logic

### Margin Calculation Rules

**Basic Margin Formula**
1. Retrieve selling price with VAT from e-shop
2. Obtain purchase/cost price from ERP or calculations
3. Apply margin formula: `((Price - Cost) / Price) * 100`
4. Handle null/zero values appropriately

**Margin Thresholds**
- **Critical** (< 10%): Red indicator - requires immediate attention
- **Low** (10-20%): Orange indicator - needs review
- **Acceptable** (20-30%): Yellow indicator - monitor performance
- **Good** (> 30%): Green indicator - healthy margin

### Cost Calculation Methods

**Average Cost Tracking**
- Historical cost averaging (currently using mock data)
- Variance calculation: ±10% from base purchase price
- Future: Integration with actual historical purchase data

**30-Day Cost Analysis**
- Recent period cost tracking (currently using mock data)
- Variance calculation: ±15% from base purchase price
- Future: Real-time cost updates from recent purchases

### Data Filtering Rules

**Product Type Filtering**
- Only displays products (excludes materials and semi-products)
- Filter: `Type == ProductType.Product`

**Search Capabilities**
- Product code: Case-insensitive partial match
- Product name: Case-insensitive partial match
- Filters applied on server-side for performance

## Integration with Catalog System

### Data Sources

**Catalog Repository Integration**
```csharp
ICatalogRepository → GetAllAsync() → Filter by ProductType.Product
```

**Price Data Aggregation**
- **ERP Price**: `ErpPrice.PurchasePrice` - base cost from ERP
- **E-shop Price**: `EshopPrice.PriceWithVat` - selling price
- Null handling for missing price data

### Query Capabilities

**GetProductMarginsRequest Parameters**
- `ProductCode`: Optional filter by product code
- `ProductName`: Optional filter by product name
- `PageNumber`: Pagination page number (default: 1)
- `PageSize`: Items per page (default: 20)

**GetProductMarginsResponse Structure**
- `Items`: List of ProductMarginDto objects
- `TotalCount`: Total matching products
- `PageNumber`: Current page
- `PageSize`: Items per page
- `TotalPages`: Calculated total pages

## Application Layer Implementation

### GetProductMarginsHandler

Primary handler for margin queries using MediatR pattern.

#### Core Operations

**Data Retrieval Flow**
1. Fetch all catalog items from repository
2. Apply product type filter (products only)
3. Apply optional search filters
4. Calculate pagination
5. Transform to DTOs with margin calculations
6. Return paginated response

**Mock Data Generation** (Temporary)
```csharp
GenerateMockAverageCost() // ±10% variance from base
GenerateMockCost30Days() // ±15% variance from base
CalculateMockMargin() // Margin calculation with mock costs
```

## API Layer

### ProductMarginsController

RESTful API endpoint for margin data access.

**Endpoint**
```
GET /api/productmargins
```

**Query Parameters**
- `productCode`: Filter by product code (optional)
- `productName`: Filter by product name (optional)
- `pageNumber`: Page number for pagination
- `pageSize`: Number of items per page

**Response Format**
```json
{
  "items": [
    {
      "productCode": "PROD001",
      "productName": "Product Name",
      "priceWithVat": 1000.00,
      "purchasePrice": 600.00,
      "averageCost": 580.00,
      "cost30Days": 620.00,
      "averageMargin": 42.00,
      "margin30Days": 38.00
    }
  ],
  "totalCount": 150,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 8
}
```

## Frontend Implementation

### ProductMarginsList Component

React component providing interactive margin analysis interface.

#### Features

**Data Display**
- Tabular view with sortable columns
- Color-coded margin indicators
- Currency formatting (CZK)
- Percentage formatting with 2 decimal places

**Filtering Capabilities**
- Product code search with partial matching
- Product name search with partial matching
- Real-time filter application on Enter key
- Clear all filters functionality

**Pagination Controls**
- Page size selection (10, 20, 50, 100 items)
- Page navigation with numeric buttons
- Previous/Next navigation
- Display of current range and total count

**Visual Indicators**
- Margin color coding based on thresholds
- Loading states with spinner
- Error states with clear messaging
- Hover effects on table rows

### API Integration Hook

**useProductMarginsQuery**
- TanStack Query integration
- 5-minute stale time
- 10-minute cache time
- Automatic refetch on filter changes

## Performance Considerations

### Optimization Strategies

**Server-Side**
- In-memory filtering and pagination
- Efficient LINQ queries
- Future: Database-level filtering

**Client-Side**
- React Query caching
- Debounced search inputs (via Enter key)
- Virtual scrolling for large datasets (future)

### Scalability Features

- Pagination to limit data transfer
- Server-side filtering
- Caching strategy with stale/cache times
- Lazy loading of margin calculations

## Future Enhancements

### Planned Features

**Real Cost Integration**
- Historical purchase price tracking
- Weighted average cost calculation
- Material cost aggregation for manufactured products
- Supplier-specific cost analysis

**Advanced Analytics**
- Trend analysis over time periods
- Margin evolution graphs
- Category-based margin comparison
- Seasonal margin patterns

**Export Capabilities**
- CSV export of filtered results
- PDF reports with charts
- Excel integration for analysis

**Alerting System**
- Low margin notifications
- Negative margin alerts
- Margin target monitoring
- Automated reporting schedules

### Data Source Evolution

**Current State** (MVP)
- Mock data for average and 30-day costs
- Basic margin calculations
- Manual refresh via UI

**Phase 2**
- Integration with purchase order history
- Real average cost calculations
- Automated data refresh

**Phase 3**
- Real-time cost updates
- Predictive margin analysis
- What-if scenario modeling

## Business Value

The Product Margins domain delivers critical business insights:

1. **Profitability Visibility**: Clear view of product-level profitability
2. **Decision Support**: Data-driven pricing decisions
3. **Problem Detection**: Identify loss-making or low-margin products
4. **Performance Tracking**: Monitor margin trends over time
5. **Strategic Planning**: Support for product portfolio optimization
6. **Cost Control**: Visibility into cost variations and trends

## Technical Architecture

### Vertical Slice Organization

Following the application's vertical slice architecture:
- **Controller**: `/backend/src/Anela.Heblo.API/Controllers/ProductMarginsController.cs`
- **Handler**: `/backend/src/Anela.Heblo.Application/Features/Catalog/GetProductMarginsHandler.cs`
- **Models**: `/backend/src/Anela.Heblo.Application/Features/Catalog/Model/GetProductMargins*.cs`
- **Frontend**: `/frontend/src/components/pages/ProductMarginsList.tsx`
- **API Hook**: `/frontend/src/api/hooks/useProductMargins.ts`

### Dependencies

- **Catalog Domain**: Source of product and price data
- **Price Domain**: Integration with ERP and e-shop pricing
- **MediatR**: Request/response pattern implementation
- **TanStack Query**: Frontend data fetching and caching

## Security Considerations

### Access Control
- API endpoint requires authentication
- Role-based access for margin data (future)
- Audit trail for margin queries (future)

### Data Protection
- Sensitive pricing information handling
- Secure API communication
- No margin data in client-side storage

## Monitoring and Observability

### Metrics to Track
- Query response times
- Filter usage patterns
- Most viewed products
- Margin threshold breaches

### Logging
- API request/response logging
- Filter parameter tracking
- Error logging with context
- Performance metrics collection