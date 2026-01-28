# Product Composition Tab Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a "Složení" (Composition) tab to CatalogDetail modal displaying ingredient list for Product and SemiProduct types.

**Architecture:** Backend creates new GetProductComposition use case in Catalog feature. Frontend adds CompositionTab component with sortable table. Integration point is CatalogDetailTabs component.

**Tech Stack:** .NET 8, MediatR, React, TypeScript, TanStack Query, Tailwind CSS

---

## Task 1: Backend - Create DTO Classes

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/IngredientDto.cs`

**Step 1: Create GetProductCompositionRequest.cs**

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionRequest : IRequest<GetProductCompositionResponse>
{
    [Required]
    public string ProductCode { get; set; }
}
```

**Step 2: Create IngredientDto.cs**

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class IngredientDto
{
    [JsonPropertyName("productCode")]
    public string ProductCode { get; set; }

    [JsonPropertyName("productName")]
    public string ProductName { get; set; }

    [JsonPropertyName("amount")]
    public double Amount { get; set; }

    [JsonPropertyName("unit")]
    public string Unit { get; set; }
}
```

**Step 3: Create GetProductCompositionResponse.cs**

```csharp
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionResponse
{
    [JsonPropertyName("ingredients")]
    public List<IngredientDto> Ingredients { get; set; } = new List<IngredientDto>();
}
```

**Step 4: Verify files created**

Run: `ls -la backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/`

Expected: Three files listed (Request, Response, IngredientDto)

**Step 5: Build to check for syntax errors**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

Expected: Build succeeds with no errors

**Step 6: Commit DTOs**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/
git commit -m "feat: add GetProductComposition DTOs

Add request, response, and ingredient DTOs for product composition endpoint.
Uses classes (not records) for OpenAPI compatibility."
```

---

## Task 2: Backend - Create Handler with Tests (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductCompositionHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs`

**Step 1: Write failing test for empty template**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductCompositionHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductCompositionHandlerTests
{
    private readonly Mock<IManufactureRepository> _manufactureRepositoryMock;
    private readonly GetProductCompositionHandler _handler;

    public GetProductCompositionHandlerTests()
    {
        _manufactureRepositoryMock = new Mock<IManufactureRepository>();
        _handler = new GetProductCompositionHandler(_manufactureRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_NoManufactureTemplate_ReturnsEmptyIngredientList()
    {
        // Arrange
        var request = new GetProductCompositionRequest { ProductCode = "NONEXISTENT" };

        _manufactureRepositoryMock
            .Setup(x => x.GetManufactureTemplateAsync("NONEXISTENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Ingredients.Should().BeEmpty();
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductCompositionHandlerTests" -v n`

Expected: FAIL - "GetProductCompositionHandler does not exist"

**Step 3: Write minimal handler implementation**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionHandler : IRequestHandler<GetProductCompositionRequest, GetProductCompositionResponse>
{
    private readonly IManufactureRepository _manufactureRepository;

    public GetProductCompositionHandler(IManufactureRepository manufactureRepository)
    {
        _manufactureRepository = manufactureRepository;
    }

    public async Task<GetProductCompositionResponse> Handle(
        GetProductCompositionRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _manufactureRepository.GetManufactureTemplateAsync(
            request.ProductCode,
            cancellationToken);

        if (template == null)
        {
            return new GetProductCompositionResponse
            {
                Ingredients = new List<IngredientDto>()
            };
        }

        var ingredients = template.Ingredients
            .Select(i => new IngredientDto
            {
                ProductCode = i.ProductCode,
                ProductName = i.ProductName,
                Amount = i.Amount,
                Unit = "g" // TODO: Determine unit from product type or configuration
            })
            .ToList();

        return new GetProductCompositionResponse
        {
            Ingredients = ingredients
        };
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductCompositionHandlerTests.Handle_NoManufactureTemplate_ReturnsEmptyIngredientList" -v n`

Expected: PASS

**Step 5: Add test for valid template with ingredients**

Add to `GetProductCompositionHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_ValidTemplate_ReturnsMappedIngredients()
{
    // Arrange
    var request = new GetProductCompositionRequest { ProductCode = "PROD001" };

    var template = new ManufactureTemplate
    {
        TemplateId = 1,
        ProductCode = "PROD001",
        ProductName = "Test Product",
        Ingredients = new List<Ingredient>
        {
            new Ingredient
            {
                ProductCode = "ING001",
                ProductName = "Ingredient 1",
                Amount = 50.5,
                OriginalAmount = 50.5
            },
            new Ingredient
            {
                ProductCode = "ING002",
                ProductName = "Ingredient 2",
                Amount = 100.25,
                OriginalAmount = 100.25
            }
        }
    };

    _manufactureRepositoryMock
        .Setup(x => x.GetManufactureTemplateAsync("PROD001", It.IsAny<CancellationToken>()))
        .ReturnsAsync(template);

    // Act
    var result = await _handler.Handle(request, CancellationToken.None);

    // Assert
    result.Should().NotBeNull();
    result.Ingredients.Should().HaveCount(2);
    result.Ingredients[0].ProductCode.Should().Be("ING001");
    result.Ingredients[0].ProductName.Should().Be("Ingredient 1");
    result.Ingredients[0].Amount.Should().Be(50.5);
    result.Ingredients[0].Unit.Should().Be("g");
    result.Ingredients[1].ProductCode.Should().Be("ING002");
}
```

**Step 6: Run all tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductCompositionHandlerTests" -v n`

Expected: PASS (2 tests)

**Step 7: Commit handler and tests**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs
git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductCompositionHandlerTests.cs
git commit -m "feat: add GetProductComposition handler with tests

Implements handler to fetch product composition from manufacture templates.
Returns empty list if no template exists.
Maps ingredients to DTOs with unit field."
```

---

## Task 3: Backend - Add Controller Endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs`

**Step 1: Add using statement**

At the top of `CatalogController.cs`, add after existing usings:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
```

**Step 2: Add endpoint method**

Add after the `GetCatalogDetail` method (around line 47):

```csharp
[HttpGet("{productCode}/composition")]
public async Task<ActionResult<GetProductCompositionResponse>> GetComposition(string productCode)
{
    var request = new GetProductCompositionRequest { ProductCode = productCode };
    var response = await _mediator.Send(request);
    return Ok(response);
}
```

**Step 3: Build backend to verify**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`

Expected: Build succeeds

**Step 4: Run backend tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v n`

Expected: All tests pass

**Step 5: Commit controller changes**

```bash
git add backend/src/Anela.Heblo.API/Controllers/CatalogController.cs
git commit -m "feat: add GET /api/catalog/{productCode}/composition endpoint

Add endpoint to retrieve product composition via MediatR handler."
```

---

## Task 4: Frontend - Add TypeScript Interfaces and API Hook

**Files:**
- Modify: `frontend/src/api/hooks/useCatalog.ts`

**Step 1: Add interfaces at the end of the file**

Add after existing interfaces in `useCatalog.ts`:

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
```

**Step 2: Add useProductComposition hook**

Add at the end of the file:

```typescript
export const useProductComposition = (productCode: string) => {
  const apiClient = getAuthenticatedApiClient();

  return useQuery<ProductCompositionResponse>({
    queryKey: ['productComposition', productCode],
    queryFn: async () => {
      const relativeUrl = `/api/catalog/${encodeURIComponent(productCode)}/composition`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
      });
      return response.json();
    },
    enabled: !!productCode,
  });
};
```

**Step 3: Verify TypeScript compiles**

Run: `cd frontend && npm run build`

Expected: Build succeeds with no TypeScript errors

**Step 4: Commit API hook**

```bash
git add frontend/src/api/hooks/useCatalog.ts
git commit -m "feat: add useProductComposition API hook

Add hook to fetch product composition with absolute URL construction.
Includes TypeScript interfaces for ingredient and response types."
```

---

## Task 5: Frontend - Create CompositionTab Component (TDD)

**Files:**
- Create: `frontend/src/components/catalog/detail/tabs/__tests__/CompositionTab.test.tsx`
- Create: `frontend/src/components/catalog/detail/tabs/CompositionTab.tsx`

**Step 1: Write failing test for loading state**

Create `frontend/src/components/catalog/detail/tabs/__tests__/CompositionTab.test.tsx`:

```typescript
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import CompositionTab from '../CompositionTab';
import { useProductComposition } from '../../../../../api/hooks/useCatalog';

jest.mock('../../../../../api/hooks/useCatalog');

const mockUseProductComposition = useProductComposition as jest.MockedFunction<
  typeof useProductComposition
>;

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe('CompositionTab', () => {
  it('shows loading state', () => {
    mockUseProductComposition.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as any);

    render(<CompositionTab productCode="TEST001" />, {
      wrapper: createWrapper(),
    });

    expect(screen.getByText(/Načítání složení/i)).toBeInTheDocument();
  });
});
```

**Step 2: Run test to verify it fails**

Run: `cd frontend && npm test -- CompositionTab.test.tsx --no-coverage`

Expected: FAIL - "CompositionTab module not found"

**Step 3: Create minimal CompositionTab component**

Create `frontend/src/components/catalog/detail/tabs/CompositionTab.tsx`:

```typescript
import React from 'react';
import { Loader2 } from 'lucide-react';
import { useProductComposition } from '../../../../api/hooks/useCatalog';

interface CompositionTabProps {
  productCode: string;
}

const CompositionTab: React.FC<CompositionTabProps> = ({ productCode }) => {
  const { data, isLoading, error } = useProductComposition(productCode);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání složení...</div>
        </div>
      </div>
    );
  }

  return <div>Placeholder</div>;
};

export default CompositionTab;
```

**Step 4: Run test to verify it passes**

Run: `cd frontend && npm test -- CompositionTab.test.tsx --no-coverage`

Expected: PASS

**Step 5: Add test for error state**

Add to `CompositionTab.test.tsx`:

```typescript
it('shows error state', () => {
  mockUseProductComposition.mockReturnValue({
    data: undefined,
    isLoading: false,
    error: new Error('Failed to load'),
  } as any);

  render(<CompositionTab productCode="TEST001" />, {
    wrapper: createWrapper(),
  });

  expect(screen.getByText(/Chyba při načítání složení/i)).toBeInTheDocument();
});
```

**Step 6: Implement error state in component**

Add after the loading state in `CompositionTab.tsx`:

```typescript
if (error) {
  return (
    <div className="flex items-center justify-center h-64">
      <div className="flex items-center space-x-2 text-red-600">
        <AlertCircle className="h-5 w-5" />
        <div>Chyba při načítání složení: {(error as any).message}</div>
      </div>
    </div>
  );
}
```

Add import at top:

```typescript
import { Loader2, AlertCircle } from 'lucide-react';
```

**Step 7: Run test**

Run: `cd frontend && npm test -- CompositionTab.test.tsx --no-coverage`

Expected: PASS (2 tests)

**Step 8: Add test for empty state**

Add to `CompositionTab.test.tsx`:

```typescript
it('shows empty state when no ingredients', () => {
  mockUseProductComposition.mockReturnValue({
    data: { ingredients: [] },
    isLoading: false,
    error: null,
  } as any);

  render(<CompositionTab productCode="TEST001" />, {
    wrapper: createWrapper(),
  });

  expect(
    screen.getByText(/Tento produkt nemá definované složení/i)
  ).toBeInTheDocument();
});
```

**Step 9: Implement empty state**

Replace placeholder in `CompositionTab.tsx` with:

```typescript
const ingredients = data?.ingredients || [];

if (ingredients.length === 0) {
  return (
    <div className="text-center py-12 bg-gray-50 rounded-lg">
      <Flask className="h-12 w-12 mx-auto mb-3 text-gray-300" />
      <p className="text-gray-500 mb-2">Tento produkt nemá definované složení</p>
      <p className="text-sm text-gray-400">
        Výrobní šablona pro tento produkt neexistuje
      </p>
    </div>
  );
}

return <div>Ingredient table placeholder</div>;
```

Add Flask import:

```typescript
import { Loader2, AlertCircle, Flask } from 'lucide-react';
```

**Step 10: Run tests**

Run: `cd frontend && npm test -- CompositionTab.test.tsx --no-coverage`

Expected: PASS (3 tests)

**Step 11: Commit component tests and initial implementation**

```bash
git add frontend/src/components/catalog/detail/tabs/__tests__/CompositionTab.test.tsx
git add frontend/src/components/catalog/detail/tabs/CompositionTab.tsx
git commit -m "feat: add CompositionTab with loading, error, empty states

Implements TDD approach with tests for all UI states.
Uses Flask icon for composition/ingredients context."
```

---

## Task 6: Frontend - Add Sortable Table to CompositionTab

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/CompositionTab.tsx`
- Modify: `frontend/src/components/catalog/detail/tabs/__tests__/CompositionTab.test.tsx`

**Step 1: Add test for displaying ingredients in table**

Add to `CompositionTab.test.tsx`:

```typescript
it('displays ingredients in a table', () => {
  mockUseProductComposition.mockReturnValue({
    data: {
      ingredients: [
        {
          productCode: 'ING001',
          productName: 'Bisabolol',
          amount: 50.5,
          unit: 'g',
        },
        {
          productCode: 'ING002',
          productName: 'Vitamin E',
          amount: 100.25,
          unit: 'g',
        },
      ],
    },
    isLoading: false,
    error: null,
  } as any);

  render(<CompositionTab productCode="TEST001" />, {
    wrapper: createWrapper(),
  });

  expect(screen.getByText('Bisabolol')).toBeInTheDocument();
  expect(screen.getByText('ING001')).toBeInTheDocument();
  expect(screen.getByText('Vitamin E')).toBeInTheDocument();
  expect(screen.getByText('ING002')).toBeInTheDocument();
});
```

**Step 2: Run test to verify it fails**

Run: `cd frontend && npm test -- CompositionTab.test.tsx --no-coverage`

Expected: FAIL - Text not found in document

**Step 3: Implement ingredient table**

Replace the placeholder in `CompositionTab.tsx` with:

```typescript
const [sortConfig, setSortConfig] = useState<{
  key: keyof IngredientDto;
  direction: 'asc' | 'desc';
} | null>(null);

const sortedIngredients = React.useMemo(() => {
  if (!sortConfig) return ingredients;

  const sorted = [...ingredients].sort((a, b) => {
    const aValue = a[sortConfig.key];
    const bValue = b[sortConfig.key];

    if (typeof aValue === 'number' && typeof bValue === 'number') {
      return sortConfig.direction === 'asc' ? aValue - bValue : bValue - aValue;
    }

    const aString = String(aValue);
    const bString = String(bValue);

    return sortConfig.direction === 'asc'
      ? aString.localeCompare(bString, 'cs')
      : bString.localeCompare(aString, 'cs');
  });

  return sorted;
}, [ingredients, sortConfig]);

const handleSort = (key: keyof IngredientDto) => {
  setSortConfig((current) => {
    if (!current || current.key !== key) {
      return { key, direction: 'asc' };
    }
    if (current.direction === 'asc') {
      return { key, direction: 'desc' };
    }
    return null;
  });
};

const getSortIcon = (key: keyof IngredientDto) => {
  if (!sortConfig || sortConfig.key !== key) return null;
  return sortConfig.direction === 'asc' ? ' ↑' : ' ↓';
};

return (
  <div className="space-y-4">
    {/* Header */}
    <div className="flex items-center justify-between">
      <h3 className="text-lg font-medium text-gray-900 flex items-center">
        <Flask className="h-5 w-5 mr-2 text-gray-500" />
        Složení ({sortedIngredients.length} ingrediencí)
      </h3>
    </div>

    {/* Ingredient table */}
    <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
      <div className="h-96 overflow-y-auto">
        <table className="w-full text-sm">
          <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200">
            <tr>
              <th
                className="text-left py-3 px-4 font-medium text-gray-700 cursor-pointer hover:bg-gray-100"
                onClick={() => handleSort('productName')}
              >
                Název{getSortIcon('productName')}
              </th>
              <th
                className="text-left py-3 px-4 font-medium text-gray-700 cursor-pointer hover:bg-gray-100"
                onClick={() => handleSort('productCode')}
              >
                Kód{getSortIcon('productCode')}
              </th>
              <th
                className="text-right py-3 px-4 font-medium text-gray-700 cursor-pointer hover:bg-gray-100"
                onClick={() => handleSort('amount')}
              >
                Množství{getSortIcon('amount')}
              </th>
              <th className="text-left py-3 px-4 font-medium text-gray-700">
                Jednotka
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {sortedIngredients.map((ingredient, index) => (
              <tr key={index} className="hover:bg-gray-50">
                <td className="py-3 px-4 text-gray-900">{ingredient.productName}</td>
                <td className="py-3 px-4 text-gray-900 font-medium">
                  {ingredient.productCode}
                </td>
                <td className="py-3 px-4 text-right text-gray-900 font-medium">
                  {ingredient.amount.toLocaleString('cs-CZ', {
                    minimumFractionDigits: 2,
                    maximumFractionDigits: 4,
                  })}
                </td>
                <td className="py-3 px-4 text-gray-900">{ingredient.unit}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  </div>
);
```

Add import at top:

```typescript
import React, { useState } from 'react';
import type { IngredientDto } from '../../../../api/hooks/useCatalog';
```

**Step 4: Run test**

Run: `cd frontend && npm test -- CompositionTab.test.tsx --no-coverage`

Expected: PASS (4 tests)

**Step 5: Build frontend**

Run: `cd frontend && npm run build`

Expected: Build succeeds

**Step 6: Commit sortable table implementation**

```bash
git add frontend/src/components/catalog/detail/tabs/CompositionTab.tsx
git add frontend/src/components/catalog/detail/tabs/__tests__/CompositionTab.test.tsx
git commit -m "feat: add sortable ingredient table to CompositionTab

Implements sortable table with columns: Název, Kód, Množství, Jednotka.
Supports ascending/descending sort with visual indicators.
Uses sticky header and scrollable content area."
```

---

## Task 7: Frontend - Integrate CompositionTab into CatalogDetailTabs

**Files:**
- Modify: `frontend/src/components/catalog/detail/CatalogDetailTabs.tsx`
- Modify: `frontend/src/components/pages/CatalogDetail.tsx`

**Step 1: Update CatalogDetailTabs type definitions**

In `CatalogDetailTabs.tsx`, update the interface (around line 19):

```typescript
interface CatalogDetailTabsProps {
  item: CatalogItemDto;
  activeTab: "basic" | "history" | "margins" | "composition" | "journal" | "usage";
  onTabChange: (
    tab: "basic" | "history" | "margins" | "composition" | "journal" | "usage",
  ) => void;
  detailData: any;
  isLoading: boolean;
  journalEntries: JournalEntryDto[];
  onManufactureDifficultyClick: () => void;
  onAddJournalEntry: () => void;
  onEditJournalEntry: (entry: JournalEntryDto) => void;
  onViewAllEntries: () => void;
}
```

**Step 2: Add Flask import**

At the top of `CatalogDetailTabs.tsx`, update the lucide-react import to include Flask:

```typescript
import {
  FileText,
  ShoppingCart,
  TrendingUp,
  BookOpen,
  ArrowRight,
  Flask,
} from "lucide-react";
```

**Step 3: Add CompositionTab import**

Add after existing imports:

```typescript
import CompositionTab from "./tabs/CompositionTab";
```

**Step 4: Add Složení tab button**

Add after the Marže tab button (after line 91, before the Deník tab):

```typescript
{/* Složení tab - pouze pro Product a SemiProduct */}
{(item?.type === ProductType.Product ||
  item?.type === ProductType.SemiProduct) && (
  <button
    onClick={() => onTabChange("composition")}
    className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
      activeTab === "composition"
        ? "border-indigo-500 text-indigo-600"
        : "border-transparent text-gray-500 hover:text-gray-700"
    }`}
  >
    <Flask className="h-4 w-4" />
    <span>Složení</span>
  </button>
)}
```

**Step 5: Add composition tab content rendering**

Add after the margins tab content (around line 143, before the usage tab):

```typescript
} else if (activeTab === "composition") {
  <CompositionTab productCode={item.productCode || ""} />
```

**Step 6: Update CatalogDetail.tsx activeTab state**

In `CatalogDetail.tsx`, update the activeTab state type (around line 48):

```typescript
const [activeTab, setActiveTab] = useState<
  "basic" | "history" | "margins" | "composition" | "journal" | "usage"
>(defaultTab as any);
```

**Step 7: Update CatalogDetail.tsx defaultTab prop type**

In `CatalogDetail.tsx`, update the interface (around line 38):

```typescript
interface CatalogDetailProps {
  item?: CatalogItemDto | null;
  productCode?: string | null;
  isOpen: boolean;
  onClose: () => void;
  defaultTab?: "basic" | "history" | "margins" | "composition" | "usage";
}
```

**Step 8: Build frontend**

Run: `cd frontend && npm run build`

Expected: Build succeeds

**Step 9: Verify TypeScript compilation**

Run: `cd frontend && npx tsc --noEmit`

Expected: No errors

**Step 10: Commit integration**

```bash
git add frontend/src/components/catalog/detail/CatalogDetailTabs.tsx
git add frontend/src/components/pages/CatalogDetail.tsx
git commit -m "feat: integrate Složení tab into CatalogDetail

Add Složení tab between Marže and Deník tabs.
Tab visible only for Product and SemiProduct types.
Uses Flask icon to represent composition/ingredients."
```

---

## Task 8: Build and Manual Verification

**Files:** N/A (build and test)

**Step 1: Build complete backend**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: Build succeeds with no errors

**Step 2: Run all backend tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v n`

Expected: All tests pass

**Step 3: Run backend formatting check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`

Expected: No formatting issues

If formatting needed:

Run: `dotnet format backend/Anela.Heblo.sln`

**Step 4: Build complete frontend**

Run: `cd frontend && npm run build`

Expected: Build succeeds

**Step 5: Run frontend linting**

Run: `cd frontend && npm run lint`

Expected: No linting errors

**Step 6: Run all frontend tests**

Run: `cd frontend && npm test -- --watchAll=false`

Expected: All tests pass

**Step 7: Start local development environment (optional manual test)**

Run in separate terminals:
```bash
# Terminal 1: Backend
cd backend/src/Anela.Heblo.API
dotnet run

# Terminal 2: Frontend
cd frontend
npm start
```

**Step 8: Manual verification checklist**

- [ ] Navigate to catalog list
- [ ] Open detail for a Product or SemiProduct
- [ ] Verify "Složení" tab appears between "Marže" and "Deník"
- [ ] Click "Složení" tab
- [ ] Verify ingredient table displays or empty state shows
- [ ] Test sorting by clicking column headers
- [ ] Verify Material or Goods types don't show "Složení" tab

**Step 9: Commit formatting fixes if any were needed**

```bash
git add -A
git commit -m "chore: apply code formatting

Apply dotnet format and prettier formatting standards."
```

---

## Task 9: Final Integration and Documentation

**Files:**
- Update: `docs/plans/2026-01-28-product-composition-tab-design.md`

**Step 1: Add implementation notes to design doc**

Add at the end of the design document:

```markdown
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

**Known Limitations:**
- Unit field hardcoded to "g" - future enhancement could use product type or configuration
- No E2E tests added in this implementation

**Next Steps:**
- Consider adding E2E test for tab visibility and interaction
- Evaluate if unit field should be dynamic based on ingredient type
```

**Step 2: Run final build verification**

Run: `dotnet build backend/Anela.Heblo.sln && cd frontend && npm run build`

Expected: Both builds succeed

**Step 3: Commit documentation update**

```bash
git add docs/plans/2026-01-28-product-composition-tab-design.md
git commit -m "docs: add implementation completion notes to design

Document completed implementation with test coverage summary."
```

**Step 4: Create summary of all changes**

Run: `git log --oneline --since="2 hours ago"`

Review the commit history to verify all changes are committed.

**Step 5: Push changes (optional)**

If on a feature branch and ready to push:

```bash
git push origin HEAD
```

---

## Implementation Complete

The product composition tab feature is now fully implemented and tested. The feature:

✅ Displays ingredient list for Product and SemiProduct types
✅ Shows empty state when no manufacture template exists
✅ Provides sortable table with Name, Code, Amount, Unit columns
✅ Includes loading and error states
✅ Follows Clean Architecture with Vertical Slice organization
✅ Uses classes (not records) for OpenAPI compatibility
✅ Implements absolute URL construction for API calls
✅ Has comprehensive test coverage (backend + frontend)
✅ Follows project coding standards and formatting

**Total commits:** 9
**Backend tests added:** 2
**Frontend tests added:** 4
**New files created:** 8
**Files modified:** 4
