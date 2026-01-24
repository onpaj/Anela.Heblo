# Gift Package Disassembly Feature - Design Document

**Date**: 2026-01-22
**Author**: Ondřej Pajgrt + Claude Code
**Status**: Approved for Implementation

## Overview

Enable users to disassemble gift packages back to individual components. This addresses seasonal workflow: manufacture gift packages before season (Christmas, Valentine's), disassemble unsold packages after season ends.

## Business Context

**Problem**: Gift packages manufactured for seasonal demand remain unsold after season. Components tied up in finished packages cannot be sold individually.

**Solution**: Add disassembly function to reverse manufacturing - remove finished packages from stock, return components to inventory.

**User Journey**:
1. User opens gift package detail modal
2. Switches to "Rozebírání" (Disassembly) tab
3. Enters quantity to disassemble (max = available stock)
4. Clicks red "Rozebrat balíček" button
5. System creates inverted stock operations
6. Modal closes, list refreshes with updated stock

## Key Design Decisions

### 1. Shared Log Table with Operation Type

**Decision**: Use existing `gift_package_manufacture_logs` table with new enum `OperationType`.

**Rationale**:
- Single source of truth for all gift package operations
- Simpler queries for complete history
- Consistent ID generation for document numbering
- Easy to filter by operation type in reports

**Alternatives Considered**:
- ❌ Separate `gift_package_disassembly_logs` table - Creates duplication, complicates history queries
- ❌ No logging, only stock operations - Loses audit trail, no document ID for numbering

### 2. Inverted Stock Operations

**Decision**: Disassembly creates inverted stock operations vs manufacture:
- Gift package: **Stock-DOWN** (negative amount)
- Components: **Stock-UP** (positive amounts)

**Rationale**:
- Mirrors manufacturing logic (easy to understand)
- Same `StockUpSourceType.GiftPackageManufacture`
- Distinguishable by document prefix (`GPD-` vs `GPM-`)

**Example**:
```
Manufacturing (GPM-000042-BAL0010M):
  - BAL0010M: +50 (stock-up finished product)
  - DAR0010: -50 (stock-down component)
  - ZEN001030: -50 (stock-down component)

Disassembly (GPD-000043-BAL0010M):
  - BAL0010M: -50 (stock-down finished product)
  - DAR0010: +50 (stock-up component)
  - ZEN001030: +50 (stock-up component)
```

### 3. Tab UI Pattern in Detail Modal

**Decision**: Add second tab "Rozebírání" next to existing "Výroba" tab in right panel.

**Rationale**:
- Uses existing modal infrastructure
- Clear visual separation (red danger theme)
- No navigation complexity
- Touch-friendly on mobile

**UI Theme**:
- **Manufacture tab**: Indigo (production/creation)
- **Disassembly tab**: Red (danger/destruction)

**Visual Indicators**:
- Red background tint (`bg-red-50/30`)
- Warning banner with AlertTriangle icon
- Red action button (`bg-red-600`)
- Red borders and focus states

### 4. No Confirmation Dialog

**Decision**: Execute disassembly immediately on button click without confirmation dialog.

**Rationale**:
- Red UI theme provides sufficient warning
- Users already in "danger zone" (red tab)
- Faster workflow for seasonal bulk operations
- Consistent with other stock operations in system

### 5. Document Number Format

**Decision**: Use format `GPD-{logId:000000}-{productCode}` (GPD = Gift Package Disassembly).

**Rationale**:
- Consistent with manufacture format (`GPM-`)
- Easily filterable in reports
- Links all operations via log ID
- Human-readable prefix

## Architecture

### Backend Structure

```
Domain Layer:
  └── GiftPackageManufactureLog (entity)
      ├── OperationType enum (NEW)
      └── New constructor for disassembly

Application Layer:
  ├── Contracts/
  │   ├── GiftPackageDisassemblyDto (NEW)
  │   └── GiftPackageDisassemblyItemDto (NEW)
  └── UseCases/DisassembleGiftPackage/
      ├── DisassembleGiftPackageRequest (NEW)
      ├── DisassembleGiftPackageResponse (NEW)
      └── DisassembleGiftPackageHandler (NEW)

API Layer:
  └── LogisticsController
      └── POST /api/logistics/gift-packages/disassemble (NEW)
```

### Frontend Structure

```
Components:
  └── GiftPackageManufacturing/
      ├── DisassemblyTabContent.tsx (NEW)
      └── GiftPackageManufacturingDetail.tsx (UPDATED)

API:
  └── hooks/useGiftPackageManufacturing.ts
      └── useDisassembleGiftPackage() (NEW)
```

## Database Schema Changes

### Migration: Add Operation Type

```sql
-- Add operation_type column
ALTER TABLE gift_package_manufacture_logs
ADD COLUMN operation_type integer NOT NULL DEFAULT 1;

-- Create index for filtering
CREATE INDEX ix_gift_package_manufacture_logs_operation_type
    ON gift_package_manufacture_logs(operation_type);

-- Enum values:
-- 1 = Manufacture (default, backward compatible)
-- 2 = Disassembly
```

**Backward Compatibility**: Default value `1` ensures existing records are treated as Manufacture operations.

## API Specification

### Endpoint

```http
POST /api/logistics/gift-packages/disassemble
Authorization: Bearer {token}
Content-Type: application/json

Request Body:
{
  "giftPackageCode": "BAL0010M",
  "quantity": 5
}
```

### Request DTO

```csharp
public class DisassembleGiftPackageRequest : IRequest<DisassembleGiftPackageResponse>
{
    public string GiftPackageCode { get; set; } = null!;
    public int Quantity { get; set; }
    public Guid UserId { get; set; }  // Set by controller
}
```

### Response DTO

```csharp
public class DisassembleGiftPackageResponse : BaseResponse
{
    public GiftPackageDisassemblyDto Disassembly { get; set; } = null!;
}

public class GiftPackageDisassemblyDto
{
    public string GiftPackageCode { get; set; } = null!;
    public int QuantityDisassembled { get; set; }
    public DateTime DisassembledAt { get; set; }
    public string DisassembledBy { get; set; } = null!;
    public List<GiftPackageDisassemblyItemDto> ReturnedComponents { get; set; } = new();
}

public class GiftPackageDisassemblyItemDto
{
    public string ProductCode { get; set; } = null!;
    public int QuantityReturned { get; set; }
}
```

### Success Response (200 OK)

```json
{
  "success": true,
  "disassembly": {
    "giftPackageCode": "BAL0010M",
    "quantityDisassembled": 5,
    "disassembledAt": "2026-01-22T14:30:00Z",
    "disassembledBy": "Ondřej Pajgrt",
    "returnedComponents": [
      {
        "productCode": "DAR0010",
        "quantityReturned": 5
      },
      {
        "productCode": "ZEN001030",
        "quantityReturned": 5
      }
    ]
  }
}
```

### Error Response (400 Bad Request)

```json
{
  "success": false,
  "errorCode": "InvalidOperation",
  "params": {
    "ErrorMessage": "Nelze rozebrat 10 ks. Dostupné množství: 5 ks"
  }
}
```

## Business Logic

### Service Method

```csharp
public async Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
    string giftPackageCode,
    int quantity,
    CancellationToken cancellationToken = default)
{
    // 1. Validate quantity
    var giftPackage = await GetGiftPackageDetailAsync(...);

    if (quantity > giftPackage.AvailableStock)
        throw new InvalidOperationException($"Nelze rozebrat {quantity} ks. Dostupné: {giftPackage.AvailableStock} ks");

    if (quantity <= 0)
        throw new ArgumentException("Množství musí být větší než 0");

    // 2. Create log entry with OperationType.Disassembly
    var log = new GiftPackageManufactureLog(
        giftPackageCode,
        quantity,
        DateTime.UtcNow,
        currentUser.Name,
        GiftPackageOperationType.Disassembly);

    await _repository.AddAsync(log);
    await _repository.SaveChangesAsync();  // Get ID for document numbers

    // 3. Stock-DOWN for finished product
    var packageDocNumber = $"GPD-{log.Id:000000}-{giftPackageCode}";
    await _stockUpService.CreateOperationAsync(
        packageDocNumber,
        giftPackageCode,
        -quantity,  // Negative
        StockUpSourceType.GiftPackageManufacture,
        log.Id);

    // 4. Stock-UP for each component
    foreach (var ingredient in giftPackage.Ingredients)
    {
        var returnedQty = (int)(ingredient.RequiredQuantity * quantity);
        var docNumber = $"GPD-{log.Id:000000}-{ingredient.ProductCode}";

        await _stockUpService.CreateOperationAsync(
            docNumber,
            ingredient.ProductCode,
            returnedQty,  // Positive
            StockUpSourceType.GiftPackageManufacture,
            log.Id);

        log.AddConsumedItem(ingredient.ProductCode, returnedQty);
    }

    return new GiftPackageDisassemblyDto { ... };
}
```

### Validation Rules

1. **Quantity > 0**: Must disassemble at least 1 unit
2. **Quantity ≤ Available Stock**: Cannot disassemble more than exists
3. **Gift Package Must Exist**: Product code must be valid Set-type product
4. **BOM Must Exist**: Components must be defined in Manufacture module

## Frontend UI Design

### Tab Structure

```
┌─────────────────────────────────────────────────────────┐
│ Detail Modal Header                            [X]       │
├──────────────────────┬──────────────────────────────────┤
│                      │ ┌─ Tab Headers ─────────────────┐│
│                      │ │ [Výroba] [Rozebírání]        ││
│  Left Panel:         │ └──────────────────────────────┘│
│  Components Grid     │                                  │
│                      │  ┌─ Active Tab Content ────────┐│
│  (3/4 width)         │  │                              ││
│                      │  │  [Tab-specific controls]     ││
│                      │  │                              ││
│                      │  └──────────────────────────────┘│
│                      │  (1/4 width, scrollable)         │
└──────────────────────┴──────────────────────────────────┘
```

### Disassembly Tab Content

**Layout (top to bottom)**:

1. **Warning Banner** (red background)
   - AlertTriangle icon
   - Text: "Pozor: Destruktivní operace"
   - Subtext: "Rozebráním balíčku vrátíte komponenty zpět na sklad..."

2. **Statistics Card** (white with red border)
   - Aktuální sklad: X ks
   - Dostupné k rozebírání: X ks

3. **Quantity Input** (white with red border)
   - Title: "Množství k rozebírání"
   - Large +/- buttons (w-16 h-16, red theme)
   - Number input (center, red borders)
   - Quick buttons: "Půlka (X)", "Vše (X)"

4. **Validation Status** (green/red background)
   - ✓ "Množství je v pořádku" (green)
   - ⚠ "Překročen maximální dostupný počet" (red)

5. **Action Button** (full width, red)
   - Text: "Rozebrat balíček (X ks)"
   - Icon: PackageOpen
   - Loading state: "Rozebírám..."

### Color Scheme

**Disassembly Tab** (Danger Theme):
- Background: `bg-red-50/30`
- Borders: `border-red-200`, `border-red-300`
- Text: `text-red-700`, `text-red-800`
- Button: `bg-red-600 hover:bg-red-700`
- Focus: `focus:ring-red-500`
- Tab active: `bg-red-50 text-red-700 border-b-2 border-red-600`

**Manufacture Tab** (Production Theme):
- Background: `bg-indigo-50`
- Button: `bg-indigo-600 hover:bg-indigo-700`
- Tab active: `bg-indigo-50 text-indigo-700 border-b-2 border-indigo-600`

### Component Structure

```tsx
<DisassemblyTabContent
  selectedPackage={selectedPackage}
  quantity={disassemblyQuantity}
  setQuantity={setDisassemblyQuantity}
  maxQuantity={selectedPackage.availableStock}
  onDisassemble={handleDisassemble}
  isPending={disassemblyMutation.isPending}
/>
```

## Data Flow

```
User Action: Click "Rozebrat balíček"
    ↓
Frontend: disassemblyMutation.mutateAsync()
    ↓
API: POST /api/logistics/gift-packages/disassemble
    ↓
Handler: DisassembleGiftPackageHandler
    ↓
Service: DisassembleGiftPackageAsync()
    ↓
┌─────────────────────────────────────────┐
│ 1. Validate quantity ≤ availableStock   │
│ 2. Create log (OperationType=Disassembly)│
│ 3. Save to DB → Get log ID             │
│ 4. Create stock operations:             │
│    - Package: GPD-XXX → -qty           │
│    - Components: GPD-XXX → +qty        │
└─────────────────────────────────────────┘
    ↓
Response: DisassemblyDto
    ↓
Frontend:
    - Invalidate cache (available, log queries)
    - Show success toast
    - Close modal
    ↓
UI Updates: List refreshes with new stock levels
```

## Testing Strategy

### Backend Unit Tests

**Test File**: `GiftPackageDisassemblyTests.cs`

```csharp
// Positive cases
[Test] DisassembleGiftPackage_ValidQuantity_CreatesLogWithDisassemblyType()
[Test] DisassembleGiftPackage_ValidQuantity_CreatesInvertedStockOperations()
[Test] DisassembleGiftPackage_DocumentNumberFormat_StartsWithGPD()

// Validation cases
[Test] DisassembleGiftPackage_QuantityExceedsStock_ThrowsInvalidOperationException()
[Test] DisassembleGiftPackage_ZeroQuantity_ThrowsArgumentException()
[Test] DisassembleGiftPackage_NegativeQuantity_ThrowsArgumentException()
[Test] DisassembleGiftPackage_NonexistentPackage_ThrowsArgumentException()

// Integration cases
[Test] DisassembleGiftPackage_MultipleComponents_CreatesCorrectOperations()
[Test] DisassembleGiftPackage_LogsUserAndTimestamp_Correctly()
```

### Frontend E2E Tests (Playwright)

**Test File**: `gift-package-disassembly.spec.ts`

```typescript
test('should open disassembly tab with red theme', async ({ page }) => {
  // Navigate to gift packages
  // Open detail modal
  // Click Rozebírání tab
  // Assert red background, warning banner visible
});

test('should validate quantity against available stock', async ({ page }) => {
  // Open modal, switch to disassembly
  // Enter quantity > available stock
  // Assert button is disabled
  // Assert validation message shows
});

test('should execute disassembly and update list', async ({ page }) => {
  // Open modal, switch to disassembly
  // Enter valid quantity
  // Click "Rozebrat balíček"
  // Assert success toast appears
  // Assert modal closes
  // Assert list refreshes with updated stock
});

test('quick buttons should work correctly', async ({ page }) => {
  // Click "Půlka" button
  // Assert quantity = availableStock / 2
  // Click "Vše" button
  // Assert quantity = availableStock
});
```

## Implementation Checklist

### Phase 1: Backend Foundation

- [ ] Add `GiftPackageOperationType` enum to domain
- [ ] Update `GiftPackageManufactureLog` entity
- [ ] Create database migration (operation_type column + index)
- [ ] Update `GiftPackageManufactureLogConfiguration`
- [ ] Run migration: `dotnet ef database update`

### Phase 2: Backend Application Layer

- [ ] Create `GiftPackageDisassemblyDto` and `GiftPackageDisassemblyItemDto`
- [ ] Create `DisassembleGiftPackageRequest` and `DisassembleGiftPackageResponse`
- [ ] Create `DisassembleGiftPackageHandler`
- [ ] Add `DisassembleGiftPackageAsync()` to `IGiftPackageManufactureService`
- [ ] Implement `DisassembleGiftPackageAsync()` in `GiftPackageManufactureService`
- [ ] Add AutoMapper mappings

### Phase 3: Backend API Layer

- [ ] Add `DisassembleGiftPackage()` endpoint to `LogisticsController`
- [ ] Add XML documentation
- [ ] Build backend → Generate OpenAPI spec

### Phase 4: Backend Testing

- [ ] Write unit tests for validation logic
- [ ] Write unit tests for stock operation creation
- [ ] Write integration tests for complete workflow
- [ ] Run tests: `dotnet test`

### Phase 5: Frontend API Client

- [ ] Regenerate TypeScript client (frontend prebuild)
- [ ] Verify `logistics_DisassembleGiftPackage()` exists in generated client
- [ ] Create `useDisassembleGiftPackage()` hook in `useGiftPackageManufacturing.ts`

### Phase 6: Frontend Components

- [ ] Create `DisassemblyTabContent.tsx` component
- [ ] Update `GiftPackageManufacturingDetail.tsx` with tab structure
- [ ] Add state management (activeTab, disassemblyQuantity)
- [ ] Implement `handleDisassemble()` function
- [ ] Add toast notifications (success/error)
- [ ] Style with red danger theme

### Phase 7: Frontend Testing

- [ ] Write Playwright test for tab switching
- [ ] Write Playwright test for quantity validation
- [ ] Write Playwright test for disassembly execution
- [ ] Write Playwright test for quick buttons
- [ ] Run tests: `./scripts/run-playwright-tests.sh`

### Phase 8: Documentation & Deployment

- [ ] Update feature documentation (`gift-package-manufacture.md`)
- [ ] Update CHANGELOG
- [ ] Create PR with screenshots
- [ ] Deploy to staging
- [ ] Manual QA testing
- [ ] Deploy to production

## Success Criteria

Implementation is complete when:

1. ✅ User can switch between Výroba and Rozebírání tabs
2. ✅ Disassembly tab has red danger theme
3. ✅ Quantity validation works (max = available stock)
4. ✅ Quick buttons (Půlka, Vše) function correctly
5. ✅ Disassembly creates log entry with OperationType.Disassembly
6. ✅ Stock operations have correct signs (package -, components +)
7. ✅ Document numbers use GPD- prefix
8. ✅ Success toast appears and modal closes after execution
9. ✅ List refreshes automatically with updated stock
10. ✅ All tests pass (backend + frontend)

## Risks & Mitigations

### Risk 1: Backward Compatibility

**Risk**: Adding operation_type column breaks existing code.

**Mitigation**:
- Use DEFAULT 1 in migration (existing records = Manufacture)
- Update all queries to work with both types
- Test with existing manufacture data

### Risk 2: Stock Operation Confusion

**Risk**: Users confuse manufacture vs disassembly stock operations in reports.

**Mitigation**:
- Clear document prefix distinction (GPM vs GPD)
- Operation type visible in log queries
- Red theme makes disassembly visually distinct

### Risk 3: Invalid Stock States

**Risk**: Disassembling more than available creates negative stock.

**Mitigation**:
- Strict validation: quantity ≤ availableStock
- Frontend prevents invalid input
- Backend validates before creating operations

## Future Enhancements

1. **Bulk Disassembly**: Disassemble multiple gift package types in one operation
2. **Scheduled Disassembly**: Automatic post-season disassembly via background job
3. **Partial Disassembly**: Disassemble only specific components (not all)
4. **Disassembly History Dashboard**: Report showing seasonal disassembly patterns
5. **Component Allocation**: Reserve components for specific channels before disassembly

## References

- **Manufacturing Documentation**: `/docs/features/gift-package-manufacture.md`
- **Stock-Up Process**: `/docs/features/stock-up-process.md`
- **Clean Architecture Guide**: `/docs/📘 Architecture Documentation – MVP Work.md`
- **UI Design System**: `/docs/ui_design_document.md`

---

**Approval**: ✅ Design approved for implementation on 2026-01-22

**Next Steps**: Create implementation plan and setup git worktree
