# IssuedInvoices Domain

## Overview

The IssuedInvoices domain manages the lifecycle of sales invoices from e-commerce platforms to the accounting system. It serves as a critical integration layer between Shoptet (e-commerce platform) and FlexiBee (ERP system), handling invoice imports, transformations, and synchronization. The domain ensures data integrity, tracks synchronization status, and provides comprehensive error handling for financial compliance and operational efficiency.

## Domain Model

### Core Aggregate: IssuedInvoice

The `IssuedInvoice` aggregate root represents a sales invoice and manages its synchronization lifecycle. It extends `AuditedAggregateRoot<string>` using the invoice code as the natural key.

#### Key Attributes

**Invoice Identification**
- **InvoiceCode** (Id): Unique invoice number
- **VarSymbol**: Variable symbol for payment matching
- **OrderCode**: Related e-commerce order reference

**Temporal Properties**
- **InvoiceDate**: Date of invoice issuance
- **DueDate**: Payment due date
- **TaxDate**: Tax point date

**Financial Information**
- **Price**: Total price in primary currency
- **PriceC**: Total price in alternative currency
- **Currency**: Transaction currency (CZK/EUR)
- **ExchangeRate**: Currency conversion rate

**Customer Information**
- **CustomerCode**: Customer identifier
- **CustomerName**: Customer display name
- **VatPayer**: VAT registration status

**Payment and Delivery**
- **BillingMethod**: Payment method enum
- **ShippingMethod**: Delivery method enum

**Synchronization Status**
- **IsSynced**: Current sync status
- **LastSyncTime**: Most recent sync attempt
- **ErrorMessage**: Last error description
- **ErrorType**: Categorized error type
- **SyncHistory**: Collection of all sync attempts

### Related Entities

#### IssuedInvoiceDetail
Detailed invoice information extending the aggregate:
- **DetailId**: Unique detail identifier
- **Customer**: Full customer entity
- **BillingAddress**: Billing address
- **DeliveryAddress**: Shipping address
- **Buyer**: Additional buyer information
- **Note**: Internal/external notes
- **Items**: Collection of invoice line items

#### Customer
Customer entity with comprehensive information:
- **Code**: Customer identifier
- **Name**: Display name
- **Company**: Company name (if applicable)
- **Dic**: Tax ID
- **VatId**: VAT registration number
- **Email**: Contact email
- **Phone**: Contact phone
- **BirthDate**: Date of birth (for individuals)

#### Address
Value object for address information:
- Street, City, Zip, Country
- Standardized format for both billing and delivery

#### IssuedInvoiceDetailItem
Individual invoice line item:
- **ItemId**: Line item identifier
- **Name**: Product/service name
- **Quantity**: Ordered quantity
- **Unit**: Unit of measure
- **Price**: Unit price
- **PriceSum**: Total line price
- **PriceSumVat**: Total including VAT
- **Tax**: Tax rate
- **Type**: Item type (product/service)
- **SupplierCode**: Supplier reference
- **SupplierName**: Supplier name
- **SupplierNumber**: Supplier product number

### Value Objects

#### Price
Multi-component price structure:
- **Without**: Price excluding VAT
- **WithVat**: Price including VAT
- **OnlyVat**: VAT amount
- **Currency**: Price currency

#### BillingMethod
Payment method enumeration:
- BankTransfer
- Cash
- CoD (Cash on Delivery)
- Comgate
- CreditCard

#### ShippingMethod
Delivery method enumeration:
- PickUp
- PPL
- PPLParcelShop
- Zasilkovna
- GLS

#### IssuedInvoiceErrorType
Error categorization:
- **General**: Unspecified errors
- **InvoicePaired**: Invoice already exists in ERP
- **ProductNotFound**: Product code not recognized

## Business Rules and Processes

### Invoice Import Process

1. **Source Acquisition**
   - Playwright automation for direct Shoptet access
   - Dropbox monitoring for file-based imports
   - Support for manual and scheduled imports

2. **Data Parsing**
   - XML parsing using Stormware schema
   - Validation of required fields
   - Currency and date format normalization

3. **Transformation Pipeline**
   - Product code mapping
   - Gift item handling (VAT adjustments)
   - Product code cleanup (suffix removal)
   - Extensible transformation architecture

4. **ERP Synchronization**
   - FlexiBee API integration
   - Batch or individual invoice submission
   - Response processing and error handling

5. **Status Management**
   - Sync result recording
   - Error categorization
   - History tracking for audit

### Business Invariants

1. **Invoice Uniqueness**: Each invoice code must be unique
2. **Sync State Consistency**: Sync status must match latest sync history entry
3. **Error Classification**: All errors must be categorized for proper handling
4. **Date Validity**: Invoice date ≤ Due date
5. **Currency Consistency**: All prices must use same currency within invoice

### Error Handling Strategy

#### Non-Critical Errors
- **InvoicePaired**: Invoice already exists in ERP
  - Can be resolved by unpairing
  - Doesn't block other operations

#### Critical Errors
- **ProductNotFound**: Requires product mapping update
- **General**: Requires manual investigation

## External System Integration

### Shoptet E-commerce Platform

#### Playwright Integration
- Browser automation for authenticated access
- Navigation to invoice export section
- Date range selection and export triggering
- XML file download and processing

#### Dropbox Integration
- Folder monitoring for new invoice files
- File movement workflow:
  - Source → Processing
  - Processing → Results (success)
  - Processing → Failures (error)
- Batch processing with memory optimization

#### Cash Register Integration
- Separate Playwright automation for POS data
- HTML table parsing for transaction extraction
- Monthly statistics aggregation

### FlexiBee ERP System

#### API Integration
- RESTful API communication
- Invoice creation endpoint
- Error response parsing
- Batch operation support

#### Data Mapping
- Customer code synchronization
- Product code mapping
- VAT rate alignment
- Payment method translation

## Application Services

### IssuedInvoiceAppService

Primary service managing invoice operations.

#### Core Operations

**Import Methods**
```csharp
ImportInvoicesFromDate(IssuedInvoiceDailyImportArgs args)
ImportSingleInvoice(IssuedInvoiceSingleImportArgs args)
```

**Query Operations**
- Filter by date range
- Filter by sync status
- Filter by error type
- Pagination and sorting support

**Management Operations**
- CRUD operations
- Sync status updates
- Error resolution

### Background Jobs

#### IssuedInvoiceDailyImportJob
- Scheduled daily execution
- Imports previous day's invoices
- Processes all configured sources
- Error notification on failures

#### IssuedInvoiceSingleImportJob
- On-demand single invoice import
- Used for manual corrections
- Immediate feedback

## Transformation Pipeline

### IIssuedInvoiceImportTransformation

Interface for invoice transformations:
```csharp
public interface IIssuedInvoiceImportTransformation
{
    IssuedInvoice Transform(IssuedInvoice invoice);
}
```

### Built-in Transformations

1. **ProductMappingTransformation**
   - Maps e-commerce product codes to ERP codes
   - Configurable mapping table
   - Fallback handling

2. **GiftWithoutVATTransformation**
   - Identifies gift items
   - Adjusts VAT calculations
   - Compliance with tax regulations

3. **RemoveDAtTheEndTransformation**
   - Cleans product code suffixes
   - Standardizes product references
   - Legacy data compatibility

## Data Flow

```
E-commerce Platform → Data Source → Parser → Transformations → ERP Sync → Domain Update
                          ↓                                           ↓
                    (Shoptet XML)                              (FlexiBee API)
                          ↓                                           ↓
                   Domain Objects  ←──────── Sync Results ────────────┘
```

## Use Cases

### Primary Use Cases

1. **Daily Invoice Import**
   - Automated overnight processing
   - All invoices from previous day
   - Email notifications on completion

2. **Manual Invoice Import**
   - Specific invoice by code
   - Date range import
   - Error recovery

3. **Sync Status Monitoring**
   - Dashboard views
   - Error categorization
   - Retry failed imports

4. **Cash Register Reconciliation**
   - POS transaction import
   - Daily cash reconciliation
   - Multi-register support

### Operational Workflows

1. **Standard Daily Flow**
   - 2:00 AM: Automated import starts
   - 2:30 AM: Processing complete
   - 3:00 AM: Error report if any
   - 8:00 AM: Finance team review

2. **Error Resolution Flow**
   - Identify error type
   - Apply appropriate fix
   - Retry import
   - Verify in ERP

3. **Month-End Processing**
   - Verify all invoices imported
   - Reconcile with e-commerce
   - Generate compliance reports

## Performance Considerations

### Optimization Strategies

1. **Batch Processing**
   - Groups invoices for efficient API calls
   - Reduces network overhead
   - Transaction-like semantics

2. **Memory Management**
   - Streaming XML parsing for large files
   - Dispose pattern for Playwright browsers
   - Cached transformation mappings

3. **Parallel Processing**
   - Multiple source processing
   - Async/await patterns
   - Thread-safe operations

### Scalability

- Handles 1000+ invoices per day
- Sub-second processing per invoice
- Linear scaling with volume

## Security and Compliance

### Data Protection
- Customer data encryption in transit
- Secure credential storage
- Audit trail maintenance

### Financial Compliance
- Complete sync history
- Error tracking for audit
- VAT calculation accuracy
- Invoice immutability

### Access Control
- Role-based permissions
- Separate read/write access
- Admin-only error resolution

## Configuration

### Source Configuration
- Multiple source support
- Source-specific settings
- Credential management

### Transformation Configuration
- Product mapping tables
- Business rule parameters
- Error handling preferences

### Integration Settings
- API endpoints
- Retry policies
- Timeout values

## Business Value

The IssuedInvoices domain delivers significant business value:

1. **Automation**: Eliminates manual invoice entry saving hours daily
2. **Accuracy**: Reduces human error in financial data
3. **Compliance**: Ensures all invoices are properly recorded
4. **Visibility**: Real-time sync status and error tracking
5. **Flexibility**: Multiple source support and transformation pipeline
6. **Scalability**: Handles business growth without additional effort