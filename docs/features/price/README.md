# Price Domain

## Overview

The Price domain manages product pricing across ERP and e-commerce systems. It synchronizes prices between FlexiBee ERP and Shoptet e-shop, handles VAT calculations, manages Bill of Materials pricing for manufactured products, and provides comprehensive price management capabilities with caching and audit trails.

## Domain Model

### Core Entity: ProductPriceErp

The `ProductPriceErp` entity represents pricing information from the ERP system with comprehensive VAT handling.

#### Key Attributes

**Product Identification**
- **ProductCode**: Unique product identifier
- **ProductName**: Product display name
- **ProductType**: Product classification

**Pricing Information**
- **Price**: Base selling price
- **PurchasePrice**: Cost price from suppliers
- **VatPerc**: VAT percentage (0%, 15%, 21%)
- **PriceWithVat**: Calculated price including VAT

**Manufacturing Integration**
- **BoMId**: Bill of Materials identifier for manufactured products
- **OriginalPurchasePrice**: Original cost before recalculation

#### Computed Properties

**VAT Calculations**
```csharp
PriceWithVat = Price * ((100 + VatPerc) / 100)
```

**Price Rounding**
- All prices rounded to 2 decimal places
- Consistent across selling and purchase prices

### Value Objects

#### ProductPriceSyncData
Synchronization tracking for price updates:
- **SyncTimestamp**: When sync occurred
- **SourceSystem**: Origin of price data
- **SyncStatus**: Success/failure status

## Business Rules and Logic

### VAT Calculation Rules

**VAT Rate Mapping**
- "ovobozeno" → 0% VAT (exempt products)
- "snížená" → 15% VAT (reduced rate)
- Default → 21% VAT (standard rate)

**Price Calculation Logic**
1. Base price from ERP
2. VAT percentage determination
3. Price with VAT calculation
4. Rounding to 2 decimal places

### Synchronization Rules

**ERP to E-shop Sync**
1. Only products with `PurchasePrice > 0` are synchronized
2. ERP prices override e-shop prices when available
3. Product matching by code between systems
4. VAT calculations applied consistently

**Price Update Strategy**
- Pull pricing from ERP (authoritative source)
- Merge with existing e-shop data
- Apply business rules and calculations
- Push updated prices to e-shop

### Manufacturing Pricing Rules

**Bill of Materials Integration**
- Products with valid BoMId support cost recalculation
- Purchase price calculated from material costs
- Supports both individual and bulk recalculation
- Error handling for missing BoM data

## ERP Integration (FlexiBee)

### FlexiProductPriceErpClient

Primary adapter for FlexiBee ERP integration.

#### Core Capabilities

**Data Retrieval**
- UserQuery 41 for pricing data extraction
- Cached results (5-minute expiration)
- Force reload capability for real-time data

**Field Mapping**
```csharp
// FlexiBee → Domain
kod → ProductCode
cena → Price  
cenanakup → PurchasePrice
typszbdphk → VAT Category
idKusovnik → BoMId
```

**Caching Strategy**
- Memory cache with 5-minute expiration
- Configurable cache duration
- Cache bypass for critical operations

#### VAT Processing

**Czech VAT Categories**
- Automated mapping from Czech descriptors
- Standard/reduced/exempt rate handling
- Consistent VAT calculation across products

## E-commerce Integration (Shoptet)

### ShoptetPriceClient

Manages price synchronization with Shoptet e-commerce platform.

#### Data Exchange Format

**CSV-Based Integration**
- Windows-1250 encoding for Czech characters
- Semicolon delimiter
- Structured import/export format

**File Operations**
```csharp
ImportPricesAsync() // Read current e-shop prices
ExportPricesToCsvAsync() // Generate updated price file
```

#### Synchronization Process

1. **Import Phase**
   - Download current e-shop prices
   - Parse CSV data
   - Store baseline pricing

2. **Merge Phase**
   - Combine ERP and e-shop data
   - Apply business rules
   - Calculate final prices

3. **Export Phase**
   - Generate updated CSV
   - Upload to e-shop system
   - Track synchronization results

## Application Services

### ProductPriceAppService

Primary orchestration service for price management.

#### Core Operations

**Price Synchronization**
```csharp
SyncPricesAsync() // Full ERP to e-shop sync
SetProductPricesAsync(productCodes) // Selective sync
GetProductPricesAsync(query) // Price querying
```

**Manufacturing Integration**
```csharp
RecalculatePurchasePriceAsync(request) // BoM-based cost calculation
```

#### Query Capabilities

**ProductPriceQueryDto**
- Product code filtering
- Price range filtering
- VAT category filtering
- Pagination support

**Advanced Features**
- Dry-run capability for testing
- Bulk operations for efficiency
- Error reporting and logging

## Integration Patterns

### Multi-System Architecture

```
FlexiBee ERP → Price Domain → Shoptet E-shop
     ↓              ↓              ↓
Authoritative   Calculation    Updates
   Source        & Rules      Applied
```

### Data Flow

1. **ERP Data Pull**
   - Retrieve pricing from FlexiBee
   - Apply caching for performance
   - Process VAT calculations

2. **Business Logic Application**
   - Merge ERP and e-shop data
   - Apply synchronization rules
   - Calculate final prices

3. **E-shop Data Push**
   - Generate CSV export
   - Upload to Shoptet
   - Track synchronization status

## Caching Strategy

### Memory Cache Implementation

**Cache Configuration**
- 5-minute expiration for ERP data
- Memory-based storage
- Automatic cache invalidation

**Performance Benefits**
- Reduced ERP API calls
- Faster response times
- Improved system resilience

### Cache Management

```csharp
// Cache key strategy
GetCacheKey(forceReload) // Dynamic cache keys
InvalidateCache() // Manual cache clearing
```

## Error Handling and Monitoring

### Synchronization Tracking

**Sync Data Entity**
- Complete operation logging
- Success/failure tracking
- Error message storage

### Error Recovery

1. **Validation Errors**: Data format and constraint violations
2. **Integration Errors**: ERP/e-shop communication failures
3. **Business Rule Errors**: Price calculation or VAT issues
4. **System Errors**: Cache, database, or service failures

### Monitoring Capabilities

- Sync operation analytics
- Performance metrics
- Error rate tracking
- System health monitoring

## Configuration

### ERP Settings
- FlexiBee connection parameters
- UserQuery configuration
- Cache duration settings

### E-shop Settings
- Shoptet API credentials
- CSV format configuration
- File upload parameters

### Business Rules
- VAT rate mappings
- Price rounding rules
- Synchronization frequency

## Use Cases

### Primary Use Cases

1. **Daily Price Synchronization**
   - Automated ERP to e-shop sync
   - VAT calculation and validation
   - Error notification and handling

2. **Manufacturing Cost Management**
   - BoM-based price recalculation
   - Material cost analysis
   - Margin optimization

3. **Price Query and Analysis**
   - Product price lookups
   - Price history tracking
   - Competitive analysis

4. **Manual Price Adjustments**
   - Selective product updates
   - Override capabilities
   - Validation and approval

### Operational Workflows

1. **Morning Sync Process**
   - Pull overnight ERP changes
   - Calculate updated prices
   - Push to e-shop platform

2. **Manufacturing Review**
   - Weekly BoM cost analysis
   - Purchase price recalculation
   - Margin review and adjustment

3. **Price Audit**
   - Monthly price validation
   - Sync history review
   - Error analysis and resolution

## Performance Considerations

### Optimization Strategies

1. **Caching**: Reduced ERP API calls
2. **Bulk Operations**: Efficient batch processing
3. **Async Processing**: Non-blocking operations
4. **Selective Sync**: Update only changed products

### Scalability Features

- Memory-efficient caching
- Pagination for large datasets
- Parallel processing capabilities
- Resource pooling

## Security and Compliance

### Access Control
- Role-based price management
- Audit trail for all changes
- Secure credential storage

### Data Protection
- Encrypted ERP connections
- Secure file transfers
- Compliance logging

## Business Value

The Price domain delivers significant business benefits:

1. **Automation**: Eliminates manual price updates across systems
2. **Accuracy**: Ensures consistent pricing and VAT calculations
3. **Efficiency**: Real-time synchronization reduces delays
4. **Cost Management**: BoM integration optimizes manufacturing costs
5. **Compliance**: Proper VAT handling for regulatory requirements
6. **Scalability**: Handles large product catalogs efficiently