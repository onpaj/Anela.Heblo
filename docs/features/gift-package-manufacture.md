# Gift Package Manufacturing & Disassembly

## Overview

The Gift Package Manufacturing feature allows users to create gift packages by combining multiple product components into a single finished package. The Disassembly feature provides the inverse operation - breaking down manufactured gift packages back into their individual components.

## Features

### 1. Gift Package Manufacturing (Výroba)

#### Purpose
- Combine multiple product components into a finished gift package
- Track manufacturing operations with detailed logs
- Create stock-up operations for finished packages and consumed components

#### Workflow
1. Navigate to **Výroba dárkových balíčků** (Gift Package Manufacturing)
2. Browse available gift packages with suggested quantities based on sales data
3. Click on a gift package to open the detail modal
4. **Výroba tab**:
   - View package composition (bill of materials)
   - View available stock for each component
   - Validation checks ensure sufficient stock before manufacturing
   - Set quantity to manufacture
   - Click **"Zadat k výrobě"** to execute manufacturing

#### Technical Implementation
- **Backend**: `DisassembleGiftPackageHandler` processes the request
- **Service**: `GiftPackageManufactureService.ManufactureAsync()`
- **Database**: Creates `GiftPackageManufactureLog` with `OperationType = Manufacture`
- **Stock Operations**:
  - Stock-UP for finished package (+quantity)
  - Stock-DOWN for each component (-required quantity per component)

### 2. Gift Package Disassembly (Rozebírání)

#### Purpose
- Disassemble finished gift packages back to individual components
- Return components to stock
- Remove finished packages from stock
- Handle seasonal inventory management (e.g., unsold Christmas packages)

#### Workflow
1. Navigate to **Výroba dárkových balíčků** (Gift Package Manufacturing)
2. Click on a gift package to open the detail modal
3. Switch to **Rozebírání tab** (red danger theme)
4. **Disassembly tab**:
   - Warning banner: "Pozor: Destruktivní operace" (destructive operation notice)
   - View available stock (máx. počet k rozebírání)
   - Set quantity to disassemble using:
     - +/- buttons for increment/decrement
     - Manual input
     - Quick buttons: "Půlka" (half), "Vše" (all)
   - Validation checks prevent exceeding available stock
   - Click **"Rozebrat balíček"** (red button) to execute disassembly

#### Technical Implementation
- **Backend**: `DisassembleGiftPackageHandler` processes the request
- **Service**: `GiftPackageManufactureService.DisassembleGiftPackageAsync()`
- **Database**: Creates `GiftPackageManufactureLog` with `OperationType = Disassembly`
- **Stock Operations** (inverted from manufacturing):
  - Stock-DOWN for finished package (-quantity)
  - Stock-UP for each component (+quantity per component)

## Data Model

### GiftPackageManufactureLog

```csharp
public class GiftPackageManufactureLog
{
    public int Id { get; private set; }
    public string GiftPackageCode { get; private set; }
    public int QuantityCreated { get; private set; }
    public bool StockOverrideApplied { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; }
    public GiftPackageOperationType OperationType { get; private set; }

    private List<GiftPackageManufactureLogItem> _consumedItems = new();
    public IReadOnlyCollection<GiftPackageManufactureLogItem> ConsumedItems => _consumedItems.AsReadOnly();
}
```

### GiftPackageOperationType Enum

```csharp
public enum GiftPackageOperationType
{
    Manufacture = 1,  // Default for backward compatibility
    Disassembly = 2
}
```

### Document Number Format

Manufacturing operations:
- **Document Number**: `GPM-{logId:000000}-{productCode}`
- Example: `GPM-000123-DBV001` for package, `GPM-000123-COMP001` for component

Disassembly operations:
- **Document Number**: `GPD-{logId:000000}-{productCode}`
- Example: `GPD-000124-DBV001` for package, `GPD-000124-COMP001` for component

## UI Design

### Tabs Structure

The gift package detail modal uses a tab-based interface:

1. **Výroba Tab (Indigo Theme)**:
   - Primary manufacturing interface
   - Blue/indigo color scheme
   - Focus on creating packages

2. **Rozebírání Tab (Red Danger Theme)**:
   - Disassembly interface
   - Red color scheme indicates destructive operation
   - Warning banners
   - Maximum stock display

### Responsive Design

- Touch-friendly controls (large +/- buttons)
- Mobile-optimized quantity input
- Quick action buttons for common quantities
- Real-time validation feedback

## API Endpoints

### Manufacturing
```
POST /api/logistics/gift-packages/manufacture
Request: { giftPackageCode: string, quantity: number }
Response: { success: boolean, manufacture: GiftPackageManufactureDto }
```

### Disassembly
```
POST /api/logistics/gift-packages/disassemble
Request: { giftPackageCode: string, quantity: number }
Response: { success: boolean, disassembly: GiftPackageDisassemblyDto }
```

### Get Available Packages
```
GET /api/logistics/gift-packages/available?salesCoefficient=1.0
Response: { packages: GiftPackageDto[] }
```

### Get Package Detail
```
GET /api/logistics/gift-packages/{code}/detail?salesCoefficient=1.0
Response: { giftPackage: GiftPackageDetailDto }
```

## Stock Operations Integration

Both manufacturing and disassembly operations create stock-up operations:

### Manufacturing Stock Flow
```
1. Finished Package: +{quantity} ks (GPM-XXXXXX-{packageCode})
2. Component 1: -{required} ks (GPM-XXXXXX-{comp1Code})
3. Component 2: -{required} ks (GPM-XXXXXX-{comp2Code})
...
```

### Disassembly Stock Flow (Inverted)
```
1. Finished Package: -{quantity} ks (GPD-XXXXXX-{packageCode})
2. Component 1: +{required} ks (GPD-XXXXXX-{comp1Code})
3. Component 2: +{required} ks (GPD-XXXXXX-{comp2Code})
...
```

## Validation Rules

### Manufacturing
- All components must have sufficient stock
- Quantity must be greater than 0
- Package code must exist and be valid

### Disassembly
- Available stock of finished package must be >= requested quantity
- Quantity must be greater than 0
- Package code must exist and be valid

## Error Handling

Both operations return structured error responses:

```typescript
{
  success: false,
  errorCode: ErrorCodes.InvalidOperation | ErrorCodes.InvalidValue,
  params: {
    "ErrorMessage": "Human-readable error message"
  }
}
```

Common error scenarios:
- **InvalidOperation**: Insufficient stock, invalid package
- **InvalidValue**: Invalid quantity (zero or negative)

## Testing

### Backend Tests
- Unit tests for `GiftPackageManufactureService`
- Integration tests for API endpoints
- Repository tests for data access

### Frontend Tests
- Component tests for `DisassemblyTabContent`
- Hook tests for `useDisassembleGiftPackage`
- Integration tests for tab switching

### E2E Tests
- Full workflow test: gift-package-disassembly.spec.ts
- Tab navigation and switching
- Quantity controls verification
- Stock update validation

## Usage Scenarios

### Scenario 1: Seasonal Package Manufacturing
**Context**: Preparing for Christmas season

1. Navigate to Gift Package Manufacturing
2. Select Christmas package "DBV-XMAS-2024"
3. View suggested quantity based on sales forecast
4. Verify all components (ribbons, boxes, products) are in stock
5. Manufacture required quantity

### Scenario 2: Post-Season Disassembly
**Context**: End of Christmas season with unsold packages

1. Navigate to Gift Package Manufacturing
2. Select Christmas package "DBV-XMAS-2024"
3. Switch to **Rozebírání tab**
4. View available stock: 25 packages remaining
5. Click "Vše" (all) to disassemble all remaining packages
6. Confirm operation
7. Components returned to stock for future use

### Scenario 3: Partial Disassembly
**Context**: Need to free up components for urgent order

1. Open package detail modal
2. Switch to Rozebírání tab
3. Use "Půlka" button to disassemble half of available stock
4. Freed components now available for other orders

## Migration Notes

### Database Migration
- Added `operation_type` column to `gift_package_manufacture_logs`
- Default value: `1` (Manufacture) for backward compatibility
- Existing records automatically marked as Manufacturing operations
- Index created on `operation_type` for query performance

### Backward Compatibility
- Existing manufacturing functionality unchanged
- New disassembly feature is additive
- All existing logs remain valid
- No breaking changes to existing API endpoints

## Future Enhancements

Potential improvements for future releases:

1. **Batch Disassembly**: Disassemble multiple package types at once
2. **Scheduled Disassembly**: Auto-disassemble unsold packages after date
3. **Component Quality Tracking**: Track which components were returned from disassembly
4. **Disassembly Reports**: Analytics on disassembly patterns and frequency
5. **Undo Operation**: Ability to reverse recent disassembly operations

## Related Documentation

- [Stock-Up Process](./stock-up-process.md)
- [Manufacturing Batch Planning](../plans/manufacture-batch-planning.md)
- [Inventory Management](../architecture/inventory.md)

## Change History

- **2026-01-22**: Initial implementation of Gift Package Disassembly feature (#293)
  - Added `GiftPackageOperationType` enum
  - Implemented disassembly backend service
  - Created disassembly UI with red danger theme
  - Added E2E tests for disassembly workflow
