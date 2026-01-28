# Product Composition Tab - Design Document

**Date:** 2026-01-28
**Feature:** Display product composition (ingredient list) in CatalogDetail modal

## Overview

Add a new "Složení" (Composition) tab to the CatalogDetail modal to display the list of ingredients for products and semi-products. The tab will be positioned between the "Marže" and "Deník" tabs and will only be visible for `ProductType.Product` and `ProductType.SemiProduct`.

## Requirements

- Display ingredients in a sortable table format
- Show ingredient name, product code, amount, and unit
- Only visible for Product and SemiProduct types
- Use existing `IManufactureRepository.GetManufactureTemplateAsync()` method
- Show empty state if product has no manufacture template
- Allow custom sorting by any column

## Architecture

### Backend Changes

**New Use Case Structure:**
```
Application/Features/Catalog/UseCases/GetProductComposition/
├── GetProductCompositionRequest.cs
├── GetProductCompositionResponse.cs
└── GetProductCompositionHandler.cs
```

**Request DTO:**
```csharp
public class GetProductCompositionRequest : IRequest<GetProductCompositionResponse>
{
    [Required]
    public string ProductCode { get; set; }
}
```

**Response DTO:**
```csharp
public class GetProductCompositionResponse
{
    public List<IngredientDto> Ingredients { get; set; }
}

public class IngredientDto
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double Amount { get; set; }
    public string Unit { get; set; } // e.g., "g", "ml", "ks"
}
```

**Handler Logic:**
1. Receive `ProductCode` from request
2. Call `IManufactureRepository.GetManufactureTemplateAsync(productCode)`
3. If template is null, return empty `Ingredients` list
4. Map `ManufactureTemplate.Ingredients` to `List<IngredientDto>`
5. Return response

**Controller Endpoint:**
Add to `CatalogController.cs`:
```csharp
[HttpGet("{productCode}/composition")]
public async Task<GetProductCompositionResponse> GetComposition(string productCode)
{
    var request = new GetProductCompositionRequest { ProductCode = productCode };
    return await _mediator.Send(request);
}
```

### Frontend Changes

**New API Hook:**
Add to `frontend/src/api/hooks/useCatalog.ts`:

```typescript
export interface IngredientDto {
  productCode: string;
  productName: string;
  amount: number;
  unit: string;
}

export interface ProductCompositionResponse {
  ingredients: IngredientDto[];
}

export const useProductComposition = (productCode: string) => {
  const apiClient = getAuthenticatedApiClient();

  return useQuery<ProductCompositionResponse>({
    queryKey: ['productComposition', productCode],
    queryFn: async () => {
      const relativeUrl = `/api/catalog/${encodeURIComponent(productCode)}/composition`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET'
      });
      return response.json();
    },
    enabled: !!productCode,
  });
};
```

**New Component:**
Create `frontend/src/components/catalog/detail/tabs/CompositionTab.tsx`

**Component Features:**
- Display ingredients in sortable table
- Table columns: Název (Name), Kód (Code), Množství (Amount), Jednotka (Unit)
- Sortable by any column with visual indicators (↑/↓)
- Right-align numeric values (Amount column)
- Handle loading, error, and empty states

**UI States:**
1. **Loading:** Spinner with "Načítání složení..."
2. **Error:** Error message with icon
3. **Empty:** "Tento produkt nemá definované složení"
4. **Success:** Sortable table with ingredients

**Integration with CatalogDetailTabs:**

Update `CatalogDetailTabs.tsx`:

1. Update type definition:
```typescript
activeTab: "basic" | "history" | "margins" | "composition" | "journal" | "usage"
```

2. Add tab button (between "margins" and "journal"):
```typescript
{(item?.type === ProductType.Product ||
  item?.type === ProductType.SemiProduct) && (
  <button
    onClick={() => onTabChange("composition")}
    className={/* ... */}
  >
    <Flask className="h-4 w-4" />
    <span>Složení</span>
  </button>
)}
```

3. Add tab content rendering:
```typescript
} else if (activeTab === "composition") {
  <CompositionTab productCode={item.productCode || ""} />
```

## Data Flow

1. User opens CatalogDetail for a Product/SemiProduct
2. User clicks on "Složení" tab
3. Frontend calls `GET /api/catalog/{productCode}/composition`
4. Backend handler calls `IManufactureRepository.GetManufactureTemplateAsync(productCode)`
5. Handler maps `ManufactureTemplate.Ingredients` to DTOs
6. Returns `ProductCompositionResponse` with ingredients list
7. Frontend displays ingredients in sortable table
8. If no template exists, shows empty state message

## Tab Ordering

1. Základní informace (Basic)
2. Historie nákupů (History) - Material, Goods only
3. Marže (Margins) - Product, SemiProduct, Goods, Set
4. **Složení (Composition) - Product, SemiProduct only** ← NEW
5. Deník (Journal) - All types
6. Použití (Usage) - SemiProduct, Material only

## UI Design

**Table Structure:**
- Column headers with sort indicators
- Alternating row colors for readability
- Right-aligned numeric values
- Consistent with existing table patterns in the project

**Icon:**
Use `Flask` from `lucide-react` to represent composition/ingredients

## Implementation Notes

- Follow Clean Architecture with Vertical Slice organization
- Use classes (not records) for DTOs to ensure OpenAPI compatibility
- Use absolute URLs with `baseUrl` for API calls (CLAUDE.md rule #4)
- Follow existing table styling patterns from other tabs
- Ingredients displayed in original API order, with custom sorting capability
- Empty ingredient list is valid (shows empty state, not error)

## Testing Considerations

- Unit tests for `GetProductCompositionHandler`
- Frontend tests for `CompositionTab` component
- E2E tests for tab visibility and data display
- Test empty state when no manufacture template exists
- Test sorting functionality

## Implementation Completed

**Date:** 2026-01-28

**Changes:**
- Backend: Added GetProductComposition use case with handler and tests
- Backend: Added GET /api/catalog/{productCode}/composition endpoint
- Frontend: Added useProductComposition API hook
- Frontend: Created CompositionTab component with sortable table
- Frontend: Integrated tab into CatalogDetailTabs between Marže and Deník

**Test Coverage:**
- Backend: 2 handler tests (empty template, valid ingredients)
- Frontend: 4 component tests (loading, error, empty, data display)
- All 1635 backend tests passing
- All 642 frontend tests passing

**Implementation Adjustments:**
- Used `Beaker` icon instead of `Flask` (Flask not available in lucide-react)
- GetProductCompositionResponse inherits from BaseResponse (project standard)
- Query key uses `QUERY_KEYS.catalog` pattern for consistent caching
- React hooks moved before conditional returns (rules of hooks compliance)
- Unit field hardcoded to "g" - future enhancement could use product type or configuration

**Known Limitations:**
- Unit field is hardcoded to "g" in the handler
- No E2E tests added in this implementation (can be added separately)

**Files Created:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/IngredientDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductCompositionHandlerTests.cs`
- `frontend/src/components/catalog/detail/tabs/CompositionTab.tsx`
- `frontend/src/components/catalog/detail/tabs/__tests__/CompositionTab.test.tsx`

**Files Modified:**
- `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs`
- `frontend/src/api/hooks/useCatalog.ts`
- `frontend/src/components/catalog/detail/CatalogDetailTabs.tsx`
- `frontend/src/components/pages/CatalogDetail.tsx`

**Next Steps:**
- Consider adding E2E test for tab visibility and interaction
- Evaluate if unit field should be dynamic based on ingredient type
- Monitor usage and gather feedback on sorting preferences
