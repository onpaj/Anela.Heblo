# Product Cooling Attribute Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the Abra Flexi custom attribute ID 89 (`L1`/`L2`/absent) as a read-only `Cooling` property on catalog products, displayed as a badge in the product detail view.

**Architecture:** A new `Cooling` domain enum travels through the existing Flexi-attribute pipeline: parsed in `FlexiProductAttributesQueryClient` → stored on `CatalogAttributes` → merged onto `CatalogProperties` in `CatalogRepository` → exposed via `PropertiesDto` (AutoMapper maps it automatically by name) → rendered in `ProductPropertiesInfo.tsx`. No editing UI — read-only throughout.

**Tech Stack:** .NET 8 (C#), xUnit + FluentAssertions, React + TypeScript, AutoMapper, OpenAPI TypeScript client (auto-generated on `npm run build`)

---

## File Map

| Status | File | Change |
|--------|------|--------|
| Create | `backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs` | New `Cooling` enum (None/L1/L2) |
| Modify | `backend/src/Anela.Heblo.Domain/Features/Catalog/Attributes/CatalogAttributes.cs` | Add `Cooling Cooling` property |
| Modify | `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogProperties.cs` | Add `Cooling Cooling` property |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/FlexiProductAttributesQueryClient.cs` | Add constant + LINQ assignment + `internal static ParseCooling` helper |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/AssemblyInfo.cs` | `InternalsVisibleTo` for test project |
| Modify | `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` | Add merge assignment (~line 395) |
| Modify | `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/PropertiesDto.cs` | Add `Cooling Cooling` property |
| Create | `backend/test/Anela.Heblo.Adapters.Flexi.Tests/ProductAttributes/FlexiCoolingParserTests.cs` | Unit tests for `ParseCooling` |
| Modify | `frontend/src/components/catalog/detail/tabs/BasicInfoTab/ProductPropertiesInfo.tsx` | Add "Chlazení" badge row |

---

## Task 1: Domain enum `Cooling`

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace Anela.Heblo.Domain.Features.Catalog;

public enum Cooling
{
    None = 0,
    L1 = 1,
    L2 = 2,
}
```

- [ ] **Step 2: Verify it compiles**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --no-restore -q
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/Cooling.cs
git commit -m "feat: add Cooling domain enum (None/L1/L2)"
```

---

## Task 2: Add `Cooling` to `CatalogAttributes`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Attributes/CatalogAttributes.cs`

Current last property in the file:
```csharp
public double AllowedResiduePercentage { get; set; } = 0;
```

- [ ] **Step 1: Write the failing test** (verifies the property exists before we add it to the adapter)

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/ProductAttributes/FlexiCoolingParserTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Flexi.Tests.ProductAttributes;

public class FlexiCoolingParserTests
{
    [Fact]
    public void CatalogAttributes_HasCoolingProperty_DefaultsToNone()
    {
        var attrs = new CatalogAttributes();
        attrs.Cooling.Should().Be(Cooling.None);
    }
}
```

- [ ] **Step 2: Run test – it should FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FlexiCoolingParserTests" -v minimal 2>&1 | tail -20
```

Expected: Compile error — `CatalogAttributes` has no `Cooling` property.

- [ ] **Step 3: Add the property to `CatalogAttributes`**

Open `backend/src/Anela.Heblo.Domain/Features/Catalog/Attributes/CatalogAttributes.cs` and add after `AllowedResiduePercentage`:

```csharp
    public double AllowedResiduePercentage { get; set; } = 0;

    public Cooling Cooling { get; set; } = Cooling.None;
```

The file must include the `Anela.Heblo.Domain.Features.Catalog` namespace via the existing using or namespace declaration — the enum is in the same root namespace so no extra `using` is required.

- [ ] **Step 4: Run test – it should PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FlexiCoolingParserTests" -v minimal 2>&1 | tail -20
```

Expected: 1 passed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/Attributes/CatalogAttributes.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/ProductAttributes/FlexiCoolingParserTests.cs
git commit -m "feat: add Cooling property to CatalogAttributes"
```

---

## Task 3: Add `Cooling` to `CatalogProperties`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogProperties.cs`

- [ ] **Step 1: Write the failing test** (append to existing `FlexiCoolingParserTests.cs`)

```csharp
    [Fact]
    public void CatalogProperties_HasCoolingProperty_DefaultsToNone()
    {
        var props = new CatalogProperties();
        props.Cooling.Should().Be(Cooling.None);
    }
```

- [ ] **Step 2: Run test – it should FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FlexiCoolingParserTests" -v minimal 2>&1 | tail -20
```

Expected: Compile error — `CatalogProperties` has no `Cooling` property.

- [ ] **Step 3: Add the property to `CatalogProperties`**

`CatalogProperties.cs` is a `record`. Add after `AllowedResiduePercentage`:

```csharp
    public double AllowedResiduePercentage { get; set; } = 0;

    public Cooling Cooling { get; set; } = Cooling.None;
```

No extra using needed — `Cooling` is in `Anela.Heblo.Domain.Features.Catalog`, same namespace as `CatalogProperties`.

- [ ] **Step 4: Run test – it should PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FlexiCoolingParserTests" -v minimal 2>&1 | tail -20
```

Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogProperties.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/ProductAttributes/FlexiCoolingParserTests.cs
git commit -m "feat: add Cooling property to CatalogProperties"
```

---

## Task 4: Flexi adapter – parse attribute 89

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/FlexiProductAttributesQueryClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/AssemblyInfo.cs`

- [ ] **Step 1: Write failing tests for `ParseCooling`** (append to `FlexiCoolingParserTests.cs`)

First, create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/AssemblyInfo.cs` so the tests can access the `internal` method:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.Flexi.Tests")]
```

Then append these tests to `FlexiCoolingParserTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.ProductAttributes;

// Add inside the class:

    [Theory]
    [InlineData("L1", Cooling.L1)]
    [InlineData("L2", Cooling.L2)]
    [InlineData("l1", Cooling.L1)]
    [InlineData("l2", Cooling.L2)]
    [InlineData(" L2 ", Cooling.L2)]
    [InlineData(" l1 ", Cooling.L1)]
    public void ParseCooling_WithValidValues_ReturnsParsedEnum(string input, Cooling expected)
    {
        var result = FlexiProductAttributesQueryClient.ParseCooling(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("L3")]
    [InlineData("garbage")]
    [InlineData("NONE")]
    public void ParseCooling_WithInvalidOrMissingValues_ReturnsNone(string? input)
    {
        var result = FlexiProductAttributesQueryClient.ParseCooling(input);
        result.Should().Be(Cooling.None);
    }
```

- [ ] **Step 2: Run tests – they should FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FlexiCoolingParserTests" -v minimal 2>&1 | tail -20
```

Expected: Compile error — `ParseCooling` does not exist.

- [ ] **Step 3: Add constant, helper, and LINQ assignment to `FlexiProductAttributesQueryClient`**

The file is at `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/FlexiProductAttributesQueryClient.cs`.

**3a. Add the constant** after the existing constants block (after line 20 `private const int AllowedResiduePercentageID = 87;`):

```csharp
    private const int AllowedResiduePercentageID = 87;
    private const int CoolingAttributeId = 89;
```

**3b. Add the assignment in `GetAttributesAsync`** — inside the LINQ `new CatalogAttributes()` initializer, after `AllowedResiduePercentage`:

```csharp
            AllowedResiduePercentage = StrToDoubleDef(values.FirstOrDefault(w => w.AttributeId == AllowedResiduePercentageID)?.Value, 0),
            Cooling = ParseCooling(values.FirstOrDefault(w => w.AttributeId == CoolingAttributeId)?.Value),
```

**3c. Add the `ParseCooling` helper** — after the `StrToDoubleDef` helper and before `ParseProductType`:

```csharp
    internal static Cooling ParseCooling(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Cooling.None;

        if (Enum.TryParse<Cooling>(value.Trim(), ignoreCase: true, out var result) && Enum.IsDefined(result))
            return result;

        return Cooling.None;
    }
```

- [ ] **Step 4: Run tests – they should PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FlexiCoolingParserTests" -v minimal 2>&1 | tail -20
```

Expected: All tests pass (10 tests total across Tasks 2–4).

- [ ] **Step 5: Verify full solution build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet build --no-restore -q 2>&1 | tail -10
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/FlexiProductAttributesQueryClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/AssemblyInfo.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/ProductAttributes/FlexiCoolingParserTests.cs
git commit -m "feat: parse Flexi attribute 89 as Cooling enum in adapter"
```

---

## Task 5: Merge `Cooling` in `CatalogRepository`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` (lines ~387–396)

The existing merge block looks like:
```csharp
if (attributesMap.TryGetValue(product.ProductCode, out var attributes))
{
    product.Properties.OptimalStockDaysSetup = attributes.OptimalStockDays;
    product.Properties.StockMinSetup = attributes.StockMin;
    product.Properties.BatchSize = attributes.BatchSize;
    product.Properties.ExpirationMonths = attributes.ExpirationMonths;
    product.Properties.SeasonMonths = attributes.SeasonMonthsArray;
    product.MinimalManufactureQuantity = attributes.MinimalManufactureQuantity;
    product.Properties.AllowedResiduePercentage = attributes.AllowedResiduePercentage;
}
```

- [ ] **Step 1: Add the merge assignment**

Add one line after `AllowedResiduePercentage`:

```csharp
    product.Properties.AllowedResiduePercentage = attributes.AllowedResiduePercentage;
    product.Properties.Cooling = attributes.Cooling;
```

- [ ] **Step 2: Build to verify**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet build --no-restore -q 2>&1 | tail -10
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs
git commit -m "feat: merge Cooling from CatalogAttributes into CatalogProperties"
```

---

## Task 6: Expose `Cooling` on `PropertiesDto`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/PropertiesDto.cs`

`PropertiesDto` is a plain class (not a record, per project rule). AutoMapper already has `CreateMap<CatalogProperties, PropertiesDto>()` configured — matching property names map automatically, so no AutoMapper profile change is needed.

- [ ] **Step 1: Add the property**

Add after `SeasonMonths`:

```csharp
public class PropertiesDto
{
    public int OptimalStockDaysSetup { get; set; }
    public decimal StockMinSetup { get; set; }
    public int BatchSize { get; set; }
    public int[] SeasonMonths { get; set; } = Array.Empty<int>();
    public Cooling Cooling { get; set; }
}
```

The `Cooling` enum is in `Anela.Heblo.Domain.Features.Catalog`. Add the using if not already present:

```csharp
using Anela.Heblo.Domain.Features.Catalog;
```

- [ ] **Step 2: Build and run all backend tests**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet build --no-restore -q 2>&1 | tail -10
dotnet test --no-build --filter "Category!=Integration" -v minimal 2>&1 | tail -20
```

Expected: Build succeeded; all non-integration tests pass.

- [ ] **Step 3: Run dotnet format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/backend
dotnet format --no-restore 2>&1 | tail -10
```

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/PropertiesDto.cs
git commit -m "feat: add Cooling property to PropertiesDto"
```

---

## Task 7: Regenerate API client and add frontend badge

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/BasicInfoTab/ProductPropertiesInfo.tsx`

The TypeScript API client is auto-generated during `npm run build` from the OpenAPI spec. Running the build regenerates `frontend/src/api/generated/api-client.ts`, which will include the `Cooling` enum and `properties.cooling` field automatically.

- [ ] **Step 1: Regenerate the API client**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/frontend
npm run build 2>&1 | tail -30
```

Expected: Build succeeds. `frontend/src/api/generated/api-client.ts` now contains a `Cooling` enum with `None = 0`, `L1 = 1`, `L2 = 2` and `properties.cooling` field.

- [ ] **Step 2: Write failing test — verify `Cooling` type exists in generated client**

This step is a compile-time check: the `ProductPropertiesInfo.tsx` will not compile if `Cooling` enum is absent. We'll write the component change and let the build serve as the test.

- [ ] **Step 3: Update `ProductPropertiesInfo.tsx`**

The existing component renders 4 property cards in a `grid grid-cols-2 lg:grid-cols-4` layout. Add a 5th card for "Chlazení". Extend the grid to `lg:grid-cols-5` to accommodate it cleanly (or keep 4-wide and let it wrap — keeping 4-wide and letting the new card appear on a second row is also acceptable, match the product owner's preference; the spec does not constrain this).

Replace the full component content of `frontend/src/components/catalog/detail/tabs/BasicInfoTab/ProductPropertiesInfo.tsx`:

```tsx
import React from "react";
import { Layers, Settings } from "lucide-react";
import { CatalogItemDto } from "../../../../../api/hooks/useCatalog";
import { Cooling } from "../../../../../api/generated/api-client";

interface ProductPropertiesInfoProps {
  item: CatalogItemDto;
  onManufactureDifficultyClick: () => void;
}

const COOLING_LABELS: Record<Cooling, { label: string; className: string }> = {
  [Cooling.None]: { label: "Bez chlazení", className: "text-gray-400" },
  [Cooling.L1]: { label: "L1", className: "text-blue-600 font-semibold" },
  [Cooling.L2]: { label: "L2", className: "text-indigo-700 font-semibold" },
};

const ProductPropertiesInfo: React.FC<ProductPropertiesInfoProps> = ({
  item,
  onManufactureDifficultyClick,
}) => {
  const cooling = item.properties?.cooling ?? Cooling.None;
  const coolingDisplay = COOLING_LABELS[cooling] ?? COOLING_LABELS[Cooling.None];

  return (
    <div className="space-y-4">
      <h3 className="text-lg font-medium text-gray-900 flex items-center">
        <Layers className="h-5 w-5 mr-2 text-gray-500" />
        Vlastnosti produktu
      </h3>

      <div className="bg-gray-50 rounded-lg p-4">
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 block mb-1">
              Optimální zásoby (dny)
            </span>
            <span className="text-lg font-semibold text-gray-900">
              {item.properties?.optimalStockDaysSetup || "-"}
            </span>
          </div>

          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 block mb-1">
              Min. zásoba
            </span>
            <span className="text-lg font-semibold text-gray-900">
              {item.properties?.stockMinSetup || "-"}
            </span>
          </div>

          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 block mb-1">
              Velikost šarže
            </span>
            <span className="text-lg font-semibold text-gray-900">
              {item.properties?.batchSize || "-"}
            </span>
          </div>

          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 block mb-1">
              Náročnost výroby
            </span>
            <button
              onClick={onManufactureDifficultyClick}
              className="text-lg font-semibold text-indigo-600 hover:text-indigo-700 hover:underline focus:outline-none focus:underline flex items-center space-x-1 mx-auto"
              title="Klikněte pro správu náročnosti výroby"
            >
              <span>
                {item.manufactureDifficulty && item.manufactureDifficulty > 0
                  ? item.manufactureDifficulty.toFixed(2)
                  : "Nenastaveno"}
              </span>
              <Settings className="h-3 w-3" />
            </button>
          </div>
        </div>

        <div className="mt-4 grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 block mb-1">
              Chlazení
            </span>
            <span className={`text-lg ${coolingDisplay.className}`}>
              {coolingDisplay.label}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ProductPropertiesInfo;
```

> **Note on import path:** The exact path to `Cooling` depends on how the generated client exports it. If `Cooling` is not a named export from `api-client.ts`, check the generated file and adjust the import. A common pattern is that enums are exported directly from the generated file.

- [ ] **Step 4: Build frontend – acts as the compile test**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/frontend
npm run build 2>&1 | tail -30
```

Expected: Build succeeds with no TypeScript errors.

- [ ] **Step 5: Run lint**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manila/frontend
npm run lint 2>&1 | tail -20
```

Expected: No lint errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/BasicInfoTab/ProductPropertiesInfo.tsx \
        frontend/src/api/generated/api-client.ts
git commit -m "feat: display Cooling attribute badge in product detail view"
```

---

## Self-Review

**Spec coverage:**
- [x] `Cooling` enum with `None/L1/L2` — Task 1
- [x] `CatalogAttributes.Cooling` — Task 2
- [x] `CatalogProperties.Cooling` — Task 3
- [x] Flexi adapter parses attribute ID 89 → Task 4
- [x] `ParseCooling` unit tests (`L1`, `L2`, `null`, `" l2 "`, garbage) — Task 4
- [x] Merge in `CatalogRepository` — Task 5
- [x] `PropertiesDto.Cooling` — Task 6
- [x] AutoMapper maps automatically (no change needed, documented) — Task 6
- [x] API client regenerated — Task 7
- [x] "Chlazení" badge in `ProductPropertiesInfo.tsx` — Task 7
- [x] `None` shown as neutral placeholder ("Bez chlazení") — Task 7

**Placeholder scan:** No TBDs or incomplete steps.

**Type consistency:** `Cooling` enum used consistently across `CatalogAttributes`, `CatalogProperties`, `PropertiesDto`, and the frontend `COOLING_LABELS` map.
