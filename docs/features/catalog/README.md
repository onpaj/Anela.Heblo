# Catalog Domain

## Overview

The Catalog domain is the central product and inventory management system in the Anela Heblo ERP. It serves as a unified source of truth for product information, aggregating data from multiple external systems including the ERP system, e-commerce platform, and warehouse management systems. The domain provides real-time inventory visibility, historical analytics, and supports complex product tracking requirements such as lot management and expiration dates.

## Domain Model

### Core Aggregate: CatalogAggregate

The `CatalogAggregate` is the primary aggregate root representing a product in the system. It encapsulates all product-related information and enforces business invariants.

#### Key Attributes
- **ProductCode** (Identity): Unique product identifier that serves as the aggregate ID
- **ProductName**: Human-readable product name
- **ErpId**: External reference to the ERP system
- **Type**: Product classification (ProductType enum)
- **Location**: Physical warehouse location
- **Volume & Weight**: Physical characteristics for logistics planning
- **HasExpiration**: Flag indicating if product requires expiration date tracking
- **HasLots**: Flag indicating if product requires lot/batch tracking

#### Computed Properties
- **ProductFamily**: Derived from first 6 characters of product code
- **ProductType**: Derived from 7th character of product code
- **ProductSize**: Derived from last 2 characters of product code
- **IsUnderStocked**: Business rule checking if stock < configured minimum
- **IsInSeason**: Checks if current month is in product's selling season
- **PrimarySupplier**: Automatically determined from supplier list

### Value Objects

#### StockData
Manages multi-source inventory with complex availability calculations:
- **Eshop**: E-commerce platform stock level
- **Erp**: ERP system stock level  
- **Transport**: In-transit quantities
- **Reserve**: Reserved/allocated stock
- **PrimaryStockSource**: Determines which system is authoritative (ERP or Eshop)
- **Lots**: Collection of CatalogLot for batch tracking
- **Available** (computed): Calculates available stock based on primary source + transport

#### CatalogProperties
Configuration settings for inventory management:
- **OptimalStockDaysSetup**: Target days of inventory to maintain
- **StockMinSetup**: Minimum stock threshold triggering reorder
- **BatchSize**: Manufacturing batch size for production planning
- **SeasonMonths**: Array of months when product is in season (1-12)

#### CatalogLot
Represents a specific batch or lot of inventory:
- **Lot**: Batch/lot identifier
- **Amount**: Quantity in this lot
- **Expiration**: Optional expiration date

### Related Entities

#### CatalogSales
Historical sales data with B2B/B2C breakdown:
- **Period**: Monthly aggregation period
- **B2BSold**: Business-to-business sales quantity
- **B2CSold**: Business-to-consumer sales quantity
- **DaysInPeriod**: Number of days for average calculations

#### CatalogConsumedHistory
Tracks materials consumed in manufacturing:
- **Period**: Monthly aggregation
- **MaterialsConsumed**: Quantity used in production

#### CatalogPurchaseHistory
Records purchase transactions:
- **Date**: Transaction date
- **Quantity**: Purchased amount
- **SupplierCode**: Vendor identifier
- **SupplierName**: Vendor name
- **PurchasePrice**: Unit cost

## Business Rules and Invariants

### Stock Management Rules
1. **Stock Availability Calculation**: Available stock = Primary source stock + Transport stock
2. **Primary Source Logic**: Determines whether ERP or Eshop is the authoritative stock source
3. **Understocking Detection**: Stock < StockMinSetup triggers understocking flag
4. **Seasonal Planning**: Products can be configured with active selling seasons

### Product Classification Rules
1. **Product Code Structure**: 
   - Characters 1-6: Product family
   - Character 7: Product type
   - Characters 8-9: Product size
2. **Supplier Management**: Primary supplier is automatically determined from the supplier list

### Data Integrity Rules
1. **Lot Tracking**: Only applicable when HasLots = true
2. **Expiration Tracking**: Only applicable when HasExpiration = true
3. **Historical Data**: Sales, purchase, and consumption history maintain monthly granularity

## Domain Services

### IErpStockTakingDomainService
Handles inventory count submissions to the ERP system:
- Processes stock taking requests
- Manages lot-level adjustments
- Returns operation results with success/failure status

### IEshopStockTakingDomainService
Manages e-commerce platform inventory:
- Handles stock count submissions
- Processes stock-up requests (inventory additions)
- Provides stock adjustment results

## Repository Pattern

### ICatalogRepository
Extends standard repository with specialized operations:
- Standard CRUD operations
- `UpdateStockAsync`: Updates inventory levels
- Multiple refresh methods for different data sources

### CatalogRepository Implementation
Sophisticated caching repository that:
- Uses in-memory caching for performance
- Implements complex merge logic for data aggregation
- Supports configurable refresh intervals per data source
- Handles concurrent data updates safely

## Application Services

### CatalogDataRefresher
Background hosted service that maintains data freshness:
- Runs multiple refresh loops in parallel
- Each data source has independent refresh interval
- Graceful error handling with automatic retry
- Configurable through CatalogRepositoryOptions

### PurchaseHistoryAppService
Provides purchase history analytics:
- Retrieves historical purchase data
- Supports date range filtering
- Returns purchase details including supplier and pricing

## Integration Points

### External System Clients

#### Inventory Systems
- **IErpStockClient**: Retrieves stock levels from ERP
- **IEshopStockClient**: Gets e-commerce inventory data

#### Sales and Analytics
- **ICatalogSalesClient**: Fetches sales transaction history
- **ICatalogAttributesClient**: Retrieves product attributes and metadata

#### Supply Chain
- **IPurchaseHistoryClient**: Accesses purchase order history
- **IConsumedMaterialsClient**: Gets manufacturing consumption data

#### Warehouse Operations
- **ILotsClient**: Manages batch/lot information
- Transport box integration for in-transit inventory

## Inter-Domain Communication

### Direct Dependencies
The Catalog domain directly depends on:
- **Logistics Domain**: For Supplier entity and TransportBox data
- **Manufacture Domain**: Integration with ManufactureStockAggregate
- **Purchase Domain**: Shared purchase history data
- **ConsumedMaterials Domain**: Manufacturing consumption tracking

### Data Flow Patterns
1. **Pull-based Updates**: Background service pulls data from external systems
2. **Cache Invalidation**: Stock updates trigger cache refresh
3. **Eventual Consistency**: Accepts temporary inconsistency for performance

### Shared Concepts
- ProductType enumeration (shared with other domains)
- Common DTOs in Application.Contracts layer
- Sync data patterns for tracking data freshness

## Use Cases

### Primary Use Cases
1. **Product Information Management**: Centralized product master data
2. **Inventory Visibility**: Real-time stock levels across all channels
3. **Stock Taking**: Physical inventory count processing
4. **Purchase Planning**: Historical data for demand forecasting
5. **Manufacturing Planning**: Available materials for production

### Operational Workflows
1. **Stock Synchronization**: Continuous background refresh from all sources
2. **Inventory Adjustment**: Stock taking results update system inventory
3. **Multi-Channel Inventory**: Unified view of ERP and e-commerce stock
4. **Lot Tracking**: Batch-level inventory for regulated products

## Performance Considerations

### Caching Strategy
- In-memory caching of entire catalog
- Configurable cache expiration
- Lazy loading of related data
- Thread-safe cache operations

### Scalability
- Asynchronous data refresh
- Parallel processing of different data sources
- Minimal locking for concurrent access
- Efficient merge algorithms for data aggregation

## Configuration

### CatalogRepositoryOptions
Configures refresh intervals for each data source:
- Stock refresh interval
- Sales history refresh interval
- Purchase history refresh interval
- Transport data refresh interval
- Consumed materials refresh interval
- Attributes refresh interval
- Lots refresh interval

## Business Value

The Catalog domain provides critical business capabilities:
1. **Unified Inventory View**: Single source of truth across all systems
2. **Real-time Visibility**: Near real-time stock levels for decision making
3. **Historical Analytics**: Sales and purchase trends for planning
4. **Compliance Support**: Lot and expiration tracking for regulated products
5. **Operational Efficiency**: Automated data synchronization reduces manual work