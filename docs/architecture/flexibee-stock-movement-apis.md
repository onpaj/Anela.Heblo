# FlexiBee Stock Movement APIs Documentation

## Overview

This document describes the FlexiBee stock movement APIs used in the Anela Heblo application. The FlexiBee SDK (Rem.FlexiBeeSDK.Client v2.0.1) provides two main client interfaces for managing stock movements:

1. **IStockMovementClient** - Not currently used in the codebase
2. **IStockItemsMovementClient** - Actively used for stock movement operations

## API Comparison

| Feature | IStockMovementClient | IStockItemsMovementClient |
|---------|---------------------|---------------------------|
| Current Usage | **Not used** | **Actively used** |
| Purpose | Unknown (not documented in codebase) | Creating and querying stock movements with items |
| Namespace | `Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement` | `Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement` |
| Model Namespace | `Rem.FlexiBeeSDK.Model.Products.StockMovement` | `Rem.FlexiBeeSDK.Model.Products.StockMovement` |

## IStockItemsMovementClient API

### Key Methods

#### 1. SaveAsync - Create Stock Movement
Creates a new stock movement document with items.

**Signature:**
```csharp
Task<FlexiBeeApiResult> SaveAsync(
    StockItemsMovementUpsertRequestFlexiDto request,
    CancellationToken cancellationToken = default
)
```

**Request DTO Structure:**
```csharp
var stockMovementRequest = new StockItemsMovementUpsertRequestFlexiDto()
{
    CreatedBy = "user@example.com",                    // User creating the movement
    AccountingDate = DateTime.Now,                      // Accounting date
    IssueDate = DateTime.Now,                           // Issue date
    StockItems = new List<StockItemsMovementUpsertRequestItemFlexiDto>()
    {
        new()
        {
            ProductCode = "SP001001",                   // Product code
            ProductName = "Product Name",               // Product name
            Amount = 10.0,                              // Total amount
            AmountIssued = 10.0,                        // Amount issued/received
            LotNumber = null,                           // Optional lot number
            Expiration = null,                          // Optional expiration date
            UnitPrice = 100.50,                         // Unit price
        }
    },
    Description = "MANUFACTURE-ORDER-001",              // Description
    DocumentTypeCode = "VYROBA-POLOTOVAR",             // Document type code
    StockMovementDirection = StockMovementDirection.Out, // In or Out
    Note = "Additional notes",                          // Optional notes
    WarehouseId = "20",                                 // Warehouse ID (as string)
};

var result = await _stockMovementClient.SaveAsync(stockMovementRequest, cancellationToken);
```

#### 2. GetAsync - Query Stock Movements
Retrieves stock movements by date range and filters.

**Signature:**
```csharp
Task<List<StockItemsMovementFlexiDto>> GetAsync(
    DateTime dateFrom,
    DateTime dateTo,
    StockMovementDirection direction,
    int? documentTypeId = null,
    CancellationToken cancellationToken = default
)
```

**Example Usage:**
```csharp
// Get manufacture movements (incoming) for a date range
var movements = await _stockItemsMovementClient.GetAsync(
    dateFrom: new DateTime(2024, 1, 1),
    dateTo: new DateTime(2024, 12, 31),
    direction: StockMovementDirection.In,
    documentTypeId: 56,  // Manufacture document type
    cancellationToken: cancellationToken
);
```

#### 3. GetAsync (by ID) - Retrieve Specific Movement
Retrieves a specific stock movement document by ID.

**Signature:**
```csharp
Task<List<StockItemsMovementFlexiDto>> GetAsync(int documentId)
```

**Example Usage:**
```csharp
var newDocumentIdString = result?.Result?.Results?.FirstOrDefault()?.Id;
var newDocumentId = Int32.Parse(newDocumentIdString);
var document = await _stockMovementClient.GetAsync(newDocumentId);
var movementReference = document.FirstOrDefault()?.Document.DocumentCode;
```

### Key DTOs

#### StockItemsMovementUpsertRequestFlexiDto (Request)
Used when creating stock movements.

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `CreatedBy` | `string` | Yes | Email/username of creator |
| `AccountingDate` | `DateTime` | Yes | Accounting date for the movement |
| `IssueDate` | `DateTime` | Yes | Issue date for the movement |
| `StockItems` | `List<StockItemsMovementUpsertRequestItemFlexiDto>` | Yes | List of stock items |
| `Description` | `string` | Yes | Movement description |
| `DocumentTypeCode` | `string` | Yes | Document type code (e.g., "VYROBA-POLOTOVAR") |
| `StockMovementDirection` | `StockMovementDirection` | Yes | Direction: `In` or `Out` |
| `Note` | `string` | No | Additional notes |
| `WarehouseId` | `string` | Yes | Target warehouse ID |

#### StockItemsMovementUpsertRequestItemFlexiDto (Request Item)

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `ProductCode` | `string` | Yes | Product code (with or without prefix) |
| `ProductName` | `string` | Yes | Product name |
| `Amount` | `double` | Yes | Total amount |
| `AmountIssued` | `double` | Yes | Amount issued/received |
| `LotNumber` | `string?` | No | Lot/batch number |
| `Expiration` | `DateTime?` | No | Expiration date |
| `UnitPrice` | `double` | Yes | Price per unit |

#### StockItemsMovementFlexiDto (Response)
Returned when querying stock movements.

| Property | Type | Description |
|----------|------|-------------|
| `Date` | `DateTime` | Movement date |
| `ProductCode` | `string` | Product code |
| `Amount` | `double` | Movement amount |
| `PricePerUnit` | `double` | Price per unit |
| `TotalSum` | `double` | Total sum (Amount × PricePerUnit) |
| `Document.DocumentCode` | `string` | Document reference code |

### Stock Movement Direction Enum

```csharp
public enum StockMovementDirection
{
    In,   // Incoming movement (stock increase)
    Out   // Outgoing movement (stock decrease)
}
```

## Usage Patterns

### Pattern 1: Consumption Movement (Stock Reduction)

**Use Case:** Discarding residual semi-products, material consumption

**Example from FlexiManufactureClient.cs:186-217:**
```csharp
// Step 1: Prepare stock movement request
var stockMovementRequest = new StockItemsMovementUpsertRequestFlexiDto()
{
    CreatedBy = request.CompletedBy,
    AccountingDate = _timeProvider.GetLocalNow().DateTime,
    IssueDate = _timeProvider.GetLocalNow().DateTime,
    StockItems = new List<StockItemsMovementUpsertRequestItemFlexiDto>()
    {
        new()
        {
            ProductCode = request.ProductCode,           // e.g., "SP001001"
            ProductName = request.ProductName,
            Amount = currentQuantity,                    // Amount to consume
            AmountIssued = currentQuantity,
            LotNumber = null,
            Expiration = null,
            UnitPrice = (double)semiProductStock.Price,
        }
    },
    Description = request.ManufactureOrderCode,          // Reference to order
    DocumentTypeCode = "VYROBA-POLOTOVAR",              // Semi-product document type
    StockMovementDirection = StockMovementDirection.Out, // ⚠️ OUT = consumption/reduction
    Note = request.ManufactureOrderCode,
    WarehouseId = "20",                                  // Semi-products warehouse
};

// Step 2: Create the movement
var stockMovementResult = await _stockMovementClient.SaveAsync(
    stockMovementRequest,
    cancellationToken
);

// Step 3: Handle result
if (!stockMovementResult.IsSuccess)
{
    // Handle error
    var errorMessage = stockMovementResult.GetErrorMessage();
    throw new InvalidOperationException($"Failed: {errorMessage}");
}

// Step 4: Retrieve created document reference
var newDocumentIdString = stockMovementResult?.Result?.Results?.FirstOrDefault()?.Id;
var newDocumentId = Int32.Parse(newDocumentIdString);
var document = await _stockMovementClient.GetAsync(newDocumentId);
var movementReference = document.FirstOrDefault()?.Document.DocumentCode;
```

**Key Points:**
- `StockMovementDirection.Out` = consumption/stock reduction
- `WarehouseId = "20"` for semi-products warehouse
- `DocumentTypeCode = "VYROBA-POLOTOVAR"` for semi-product movements
- Always retrieve document code after creation for reference

### Pattern 2: Production Movement (Stock Increase)

**Use Case:** Recording manufactured products/semi-products

**Example from FlexiManufactureHistoryClient.cs:21-24:**
```csharp
// Query production movements (incoming)
var movements = await _stockItemsMovementClient.GetAsync(
    dateFrom: startDate,
    dateTo: endDate,
    direction: StockMovementDirection.In,        // ⚠️ IN = production/stock increase
    documentTypeId: 56,                          // Manufacture document type ID
    cancellationToken: cancellationToken
);

// Process results
var statistics = movements
    .Where(m => m.Date != default && !string.IsNullOrEmpty(m.ProductCode))
    .GroupBy(m => new
    {
        Date = m.Date.Date,
        ProductCode = m.ProductCode!.RemoveCodePrefix()
    })
    .Select(g => new ManufactureHistoryRecord
    {
        Date = g.Key.Date,
        ProductCode = g.Key.ProductCode,
        PricePerPiece = (decimal)g.Average(a => a.PricePerUnit),
        PriceTotal = (decimal)g.Sum(s => s.TotalSum),
        Amount = g.Sum(m => m.Amount)
    })
    .OrderBy(s => s.Date)
    .ThenBy(s => s.ProductCode)
    .ToList();
```

**Key Points:**
- `StockMovementDirection.In` = production/stock increase
- `documentTypeId = 56` for manufacture documents
- Results can be aggregated and processed
- Supports filtering by date range and product code

## Error Handling Patterns

### Pattern 1: Check IsSuccess Property

```csharp
var result = await _stockMovementClient.SaveAsync(request, cancellationToken);

if (!result.IsSuccess)
{
    var errorMessage = result.GetErrorMessage();
    throw new InvalidOperationException($"Failed to create movement: {errorMessage}");
}
```

### Pattern 2: Try-Catch with Detailed Logging

```csharp
try
{
    var stockMovementResult = await _stockMovementClient.SaveAsync(
        stockMovementRequest,
        cancellationToken
    );

    if (!stockMovementResult.IsSuccess)
    {
        return new DiscardResidualSemiProductResponse
        {
            Success = false,
            QuantityFound = currentQuantity,
            QuantityDiscarded = 0,
            RequiresManualApproval = true,
            ErrorMessage = $"Failed to create movement: {stockMovementResult.GetErrorMessage()}",
            Details = "Stock movement creation failed - requires manual approval"
        };
    }

    _logger.LogDebug("Successfully created stock movement {MovementReference}", movementReference);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to create stock movement, continuing with discard process");
    return new DiscardResidualSemiProductResponse
    {
        Success = false,
        QuantityFound = currentQuantity,
        QuantityDiscarded = 0,
        RequiresManualApproval = true,
        ErrorMessage = $"Failed to create movement: {ex.Message}",
        Details = "Stock movement creation failed - requires manual approval"
    };
}
```

**Key Error Handling Principles:**
1. Always check `IsSuccess` property on FlexiBeeApiResult
2. Use `GetErrorMessage()` to retrieve detailed error information
3. Log errors with appropriate context (order codes, product codes, etc.)
4. Return meaningful error responses to calling code
5. Consider graceful degradation (manual approval fallback)

### Pattern 3: NULL Safety Checks

```csharp
// Check for null results
var newDocumentIdString = stockMovementResult?.Result?.Results?.FirstOrDefault()?.Id;
if (string.IsNullOrEmpty(newDocumentIdString))
{
    throw new InvalidOperationException("No document ID returned from SaveAsync");
}

// Parse with validation
if (!Int32.TryParse(newDocumentIdString, out var newDocumentId))
{
    throw new InvalidOperationException($"Invalid document ID: {newDocumentIdString}");
}
```

## Constants and Configuration

### Document Type Codes
```csharp
private const string WarehouseDocumentTypeSemiProduct = "VYROBA-POLOTOVAR";
private const string WarehouseDocumentTypeProduct = "VYROBA-PRODUKT";
```

### Warehouse IDs
```csharp
private const string WarehouseCodeSemiProduct = "POLOTOVARY";
private const string WarehouseCodeProduct = "ZBOZI";

// From FlexiStockClient
public const int SemiProductsWarehouseId = 20;
```

### Document Type IDs
```csharp
private const int ManufactureDocumentTypeId = 56;
```

## Integration Examples

### Example 1: Discard Residual Semi-Product
**Location:** `FlexiManufactureClient.cs:137-278`

**Workflow:**
1. Query current stock quantity using `IErpStockClient.StockToDateAsync()`
2. Validate quantity against auto-discard limits
3. Create stock movement request with `StockMovementDirection.Out`
4. Save movement using `IStockItemsMovementClient.SaveAsync()`
5. Retrieve document reference
6. Return response with movement details

### Example 2: Query Manufacture History
**Location:** `FlexiManufactureHistoryClient.cs:21-55`

**Workflow:**
1. Query movements using `GetAsync(dateFrom, dateTo, StockMovementDirection.In, documentTypeId: 56)`
2. Filter by product code if specified
3. Group by date and product code
4. Aggregate amounts and prices
5. Return statistics

## Best Practices

1. **Always use TimeProvider for dates**
   ```csharp
   AccountingDate = _timeProvider.GetLocalNow().DateTime,
   IssueDate = _timeProvider.GetLocalNow().DateTime,
   ```

2. **Include meaningful descriptions and notes**
   - Use manufacture order codes for traceability
   - Reference source documents

3. **Proper warehouse selection**
   - Semi-products → Warehouse ID 20 ("POLOTOVARY")
   - Products → Warehouse "ZBOZI"

4. **Document type consistency**
   - Use correct document type codes for each warehouse
   - Match direction with business intent (In = production, Out = consumption)

5. **Error handling**
   - Always check `IsSuccess` before processing results
   - Log failures with context
   - Provide fallback mechanisms (manual approval)

6. **NULL safety**
   - Check for null results from API calls
   - Validate parsed IDs before using

7. **Testing considerations**
   - Mock `IStockItemsMovementClient` in unit tests
   - Test error scenarios (IsSuccess = false)
   - Verify proper direction and warehouse selection

## Related Documentation

- **Stock Client:** `FlexiStockClient.cs` - Querying current stock levels
- **Manufacture Client:** `FlexiManufactureClient.cs` - Manufacturing operations
- **Manufacture History:** `FlexiManufactureHistoryClient.cs` - Historical data queries

## API Usage Summary

| Operation | Method | Direction | Document Type | Warehouse |
|-----------|--------|-----------|---------------|-----------|
| Discard semi-product | `SaveAsync` | `Out` | VYROBA-POLOTOVAR | 20 (POLOTOVARY) |
| Record production | `SaveAsync` | `In` | VYROBA-POLOTOVAR / VYROBA-PRODUKT | 20 / ZBOZI |
| Query manufacture history | `GetAsync` | `In` | documentTypeId: 56 | N/A |
| Consume materials | `SaveAsync` | `Out` | (TBD) | (TBD) |

## Notes

- **IStockMovementClient** is not currently used in the codebase
- All stock movement operations use **IStockItemsMovementClient**
- The SDK is registered via `services.AddFlexiBee(configuration)` in `FlexiAdapterServiceCollectionExtensions.cs`
- Stock movement clients are injected directly - no custom registration needed
