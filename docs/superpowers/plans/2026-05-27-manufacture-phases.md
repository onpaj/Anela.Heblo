# Manufacture Phases on Složení Tab — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the Složení tab in Catalog Detail to display and edit named manufacture phases (A, B, C, …) as visual brackets alongside ingredients, persisting the phase letter to the Flexi BoM line item's `nazevC` / `NameC` field via the existing SDK.

**Architecture:** `PhaseLabel` is stored per-ingredient; no separate phase entity exists. Reads map `BoMItemFlexiDto.NameC → Ingredient.PhaseLabel`. Writes call the existing `IBoMClient.UpdateBoMItemAsync` with the `nameC` parameter once per item. The frontend adds `draftPhases: string[]` state and per-phase `useDroppable` zones inside the existing `DndContext`; a new rightmost "Fáze" column shows bracket indicators using CSS border styling.

**Tech Stack:** .NET 8 / C# (xUnit, FluentAssertions, Moq), React / TypeScript (@dnd-kit/core + @dnd-kit/sortable, React Query, Tailwind CSS)

---

## File Map

**Backend — modify:**
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Ingredient.cs` — add `PhaseLabel` property
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs` — add `SetBomItemsOrderAndPhaseAsync` method
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs` — map `NameC → PhaseLabel` in the ingredient projection (lines 86–95)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs` — implement `SetBomItemsOrderAndPhaseAsync`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/IngredientDto.cs` — add `PhaseLabel` JSON property
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs` — forward `PhaseLabel` in the Select projection (line 25–31)
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderRequest.cs` — add `PhaseLabel` to `IngredientOrderItem`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs` — normalize + forward `PhaseLabel`; call `SetBomItemsOrderAndPhaseAsync` instead of `SetBomItemsOrderAsync`

**Backend — modify (tests):**
- `backend/test/Anela.Heblo.Tests/Features/Catalog/UpdateProductCompositionOrderHandlerTests.cs` — update existing tests + add phase-label tests
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientOrderTests.cs` — add `SetBomItemsOrderAndPhaseAsync` tests

**Frontend — modify:**
- `frontend/src/api/hooks/useCatalog.ts` — add `phaseLabel?: string | null` to `IngredientDto` (line 179–185)
- `frontend/src/api/hooks/useUpdateProductCompositionOrder.ts` — add `phaseLabel?: string | null` to `IngredientOrderItem`
- `frontend/src/components/catalog/detail/tabs/CompositionTabRow.tsx` — add `isFirstInPhase` / `isLastInPhase` props; render the Fáze column cell
- `frontend/src/components/catalog/detail/tabs/CompositionTab.tsx` — phase grouping, brackets, `draftPhases` state, add/remove phase UI, phase drop zones, updated `handleDragEnd`, updated `saveOrder`

---

## Task 1 — Domain types: Ingredient.PhaseLabel + IManufactureClient new method

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Ingredient.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs`

No unit tests for pure contract types. Correctness verified by compiler in later tasks.

- [ ] **Step 1: Add PhaseLabel to Ingredient.cs**

Replace the class body so it reads:

```csharp
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Domain.Features.Manufacture;

public class Ingredient
{
    public int TemplateId { get; set; }
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double Amount { get; set; }
    public double OriginalAmount { get; set; }
    public decimal Price { get; set; }
    public ProductType ProductType { get; set; }
    public bool HasLots { get; set; }
    public bool HasExpiration { get; set; }
    /// <summary>Display order from Abra Flexi BoM (poradi). 0 means unordered.</summary>
    public int Order { get; set; }
    /// <summary>Manufacture phase label (single uppercase letter A–Z) from Flexi nazevC. Null means unassigned.</summary>
    public string? PhaseLabel { get; set; }
}
```

- [ ] **Step 2: Add SetBomItemsOrderAndPhaseAsync to IManufactureClient.cs**

Append this method to the interface (after `SetBomItemsOrderAsync`):

```csharp
/// <summary>
/// Writes display order AND phase label for BoM line items to Abra Flexi, then invalidates the template cache.
/// </summary>
/// <param name="productCode">Product code whose BoM is being saved (used for cache invalidation).</param>
/// <param name="items">Triples of (BoMItemId, Order, PhaseLabel). BoMItemId is <see cref="Ingredient.TemplateId"/>. PhaseLabel null clears the field.</param>
Task SetBomItemsOrderAndPhaseAsync(
    string productCode,
    IEnumerable<(int BoMItemId, int Order, string? PhaseLabel)> items,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Verify the solution still compiles**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/backend
dotnet build --no-restore 2>&1 | tail -20
```

Expected: build errors about `FlexiManufactureClient` not implementing `SetBomItemsOrderAndPhaseAsync` (that's the interface — we fix it in Task 3). No *other* errors should appear.

- [ ] **Step 4: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/Ingredient.cs \
        backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs
git commit -m "feat(manufacture): add PhaseLabel to Ingredient domain type and IManufactureClient interface"
```

---

## Task 2 — Flexi read path: map NameC → PhaseLabel in template service

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`

This is a one-line mapping addition in the existing ingredient projection. Verified by build + manual smoke test.

- [ ] **Step 1: Add PhaseLabel to the ingredient projection**

In `FlexiManufactureTemplateService.cs`, locate the `return new Ingredient()` block inside `FetchAsync` (currently lines 88–95). Add `PhaseLabel` as the last property:

```csharp
return new Ingredient()
{
    TemplateId = s.Id,
    ProductCode = code,
    ProductName = s.IngredientFullName,
    Amount = s.Amount,
    ProductType = ResolveProductType(s),
    HasLots = hasLotsByProductCode.TryGetValue(code, out var hasLots) && hasLots,
    HasExpiration = false,
    Order = s.Order,
    PhaseLabel = string.IsNullOrWhiteSpace(s.NameC) ? null : s.NameC.Trim(),
};
```

- [ ] **Step 2: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/backend
dotnet build --no-restore 2>&1 | tail -10
```

Expected: same errors as before (only the unimplemented interface method).

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs
git commit -m "feat(flexi): map BoM NameC to Ingredient.PhaseLabel in template service"
```

---

## Task 3 — Flexi adapter: implement SetBomItemsOrderAndPhaseAsync (TDD)

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientOrderTests.cs`

- [ ] **Step 1: Write the failing tests**

Open `FlexiManufactureClientOrderTests.cs` and **append** these two test methods inside the `FlexiManufactureClientOrderTests` class, after the existing `SetBomItemsOrderAsync_EmptyList_StillCallsBomClientAndInvalidatesCache` test:

```csharp
[Fact]
public async Task SetBomItemsOrderAndPhaseAsync_CallsUpdateBoMItemAsyncPerItemAndInvalidatesCache()
{
    // Arrange
    var items = new List<(int BoMItemId, int Order, string? PhaseLabel)>
    {
        (100, 1, "A"),
        (200, 2, "A"),
        (300, 3, null),
    };

    // NOTE: Match the exact IBoMClient.UpdateBoMItemAsync signature below.
    // Expected signature from SDK: UpdateBoMItemAsync(int id, int? order, string? name, string? nameA, string? nameB, string? nameC, CancellationToken ct)
    // If the build fails with "no overload matches", open the IBoMClient interface in the SDK
    // and adjust the parameter count / types in both Setup and Verify calls below.
    _bomClientMock
        .Setup(x => x.UpdateBoMItemAsync(
            It.IsAny<int>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    // Act
    await _client.SetBomItemsOrderAndPhaseAsync("PRD1", items, CancellationToken.None);

    // Assert: called once per item (3 items)
    _bomClientMock.Verify(
        x => x.UpdateBoMItemAsync(
            It.IsAny<int>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
        Times.Exactly(3));

    // Assert: item 100 called with nameC = "A"
    _bomClientMock.Verify(
        x => x.UpdateBoMItemAsync(
            100,
            1,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            "A",
            It.IsAny<CancellationToken>()),
        Times.Once);

    // Assert: item 300 (null phase) called with nameC = "" (empty = clear)
    _bomClientMock.Verify(
        x => x.UpdateBoMItemAsync(
            300,
            3,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            string.Empty,
            It.IsAny<CancellationToken>()),
        Times.Once);

    // Assert: cache invalidated once
    _templateServiceMock.Verify(x => x.InvalidateTemplate("PRD1"), Times.Once);
}

[Fact]
public async Task SetBomItemsOrderAndPhaseAsync_EmptyList_StillInvalidatesCache()
{
    // Arrange
    _bomClientMock
        .Setup(x => x.UpdateBoMItemAsync(
            It.IsAny<int>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    // Act
    await _client.SetBomItemsOrderAndPhaseAsync("PRD1", Array.Empty<(int, int, string?)>(), CancellationToken.None);

    // Assert: no UpdateBoMItemAsync calls (empty list)
    _bomClientMock.Verify(
        x => x.UpdateBoMItemAsync(
            It.IsAny<int>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
        Times.Never);

    // Assert: cache still invalidated
    _templateServiceMock.Verify(x => x.InvalidateTemplate("PRD1"), Times.Once);
}
```

- [ ] **Step 2: Run the tests — verify they FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FlexiManufactureClientOrderTests" --no-build 2>&1 | tail -20
```

Expected: **BUILD FAILURE** because `FlexiManufactureClient` doesn't implement `SetBomItemsOrderAndPhaseAsync` yet (interface change from Task 1). That confirms we're in RED.

- [ ] **Step 3: Implement SetBomItemsOrderAndPhaseAsync in FlexiManufactureClient.cs**

In `FlexiManufactureClient.cs`, **append** this method after `SetBomItemsOrderAsync` (after line 167):

```csharp
public async Task SetBomItemsOrderAndPhaseAsync(
    string productCode,
    IEnumerable<(int BoMItemId, int Order, string? PhaseLabel)> items,
    CancellationToken cancellationToken = default)
{
    foreach (var item in items)
    {
        await _bomClient.UpdateBoMItemAsync(
            item.BoMItemId,
            order: item.Order,
            nameC: item.PhaseLabel ?? string.Empty,
            ct: cancellationToken);
    }
    _templateService.InvalidateTemplate(productCode);
}
```

> **Note on parameter names:** The `UpdateBoMItemAsync` call uses named parameters `order:`, `nameC:`, `ct:`. If the SDK method uses different parameter names (e.g. `cancellationToken` instead of `ct`), adjust accordingly. The parameter order in the mock Verify calls above must also match.

- [ ] **Step 4: Build and run tests — verify they PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/backend
dotnet build --no-restore 2>&1 | tail -10
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FlexiManufactureClientOrderTests" 2>&1 | tail -20
```

Expected: build succeeds (no more unimplemented interface errors), all `FlexiManufactureClientOrderTests` tests PASS.

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientOrderTests.cs
git commit -m "feat(flexi): implement SetBomItemsOrderAndPhaseAsync — persists phase label via UpdateBoMItemAsync nameC"
```

---

## Task 4 — Application DTOs: IngredientDto + UpdateProductCompositionOrderRequest

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/IngredientDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderRequest.cs`

Simple property additions. No dedicated new tests (covered by handler tests in Task 5 and the OpenAPI client regeneration on build).

- [ ] **Step 1: Add PhaseLabel to IngredientDto.cs**

Replace the file content:

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

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("phaseLabel")]
    public string? PhaseLabel { get; set; }
}
```

- [ ] **Step 2: Add PhaseLabel to IngredientOrderItem in UpdateProductCompositionOrderRequest.cs**

Replace only the `IngredientOrderItem` class (keep `UpdateProductCompositionOrderRequest` unchanged):

```csharp
public class IngredientOrderItem
{
    [Required]
    public string IngredientProductCode { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string? PhaseLabel { get; set; }
}
```

- [ ] **Step 3: Update GetProductCompositionHandler to map PhaseLabel**

In `GetProductCompositionHandler.cs`, update the `Select` projection (currently lines 25–31) to include `PhaseLabel`:

```csharp
var sorted = template.Ingredients
    .OrderBy(i => i.Order == 0 ? int.MaxValue : i.Order)
    .ThenBy(i => i.ProductName)
    .Select((i, index) => new IngredientDto
    {
        ProductCode = i.ProductCode,
        ProductName = i.ProductName,
        Amount = i.Amount,
        Unit = "g",
        Order = index + 1,
        PhaseLabel = i.PhaseLabel,
    })
    .ToList();
```

- [ ] **Step 4: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/backend
dotnet build --no-restore 2>&1 | tail -10
```

Expected: clean build (no errors).

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/IngredientDto.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderRequest.cs
git commit -m "feat(catalog): add PhaseLabel to IngredientDto, IngredientOrderItem, and composition handler mapping"
```

---

## Task 5 — Application handler: UpdateProductCompositionOrderHandler with PhaseLabel (TDD)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/UpdateProductCompositionOrderHandlerTests.cs`

- [ ] **Step 1: Update existing tests to use the new method + add phase tests**

Replace the entire content of `UpdateProductCompositionOrderHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class UpdateProductCompositionOrderHandlerTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock = new();
    private readonly Mock<ILogger<UpdateProductCompositionOrderHandler>> _loggerMock = new();
    private readonly UpdateProductCompositionOrderHandler _handler;

    public UpdateProductCompositionOrderHandlerTests()
    {
        _handler = new UpdateProductCompositionOrderHandler(
            _manufactureClientMock.Object,
            _loggerMock.Object);
    }

    private static ManufactureTemplate BuildTemplate(params (int TemplateId, string Code)[] ingredients)
    {
        return new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = ingredients.Select(i => new Ingredient
            {
                TemplateId = i.TemplateId,
                ProductCode = i.Code,
                ProductName = i.Code
            }).ToList()
        };
    }

    private void SetupSetBomItemsOrderAndPhaseAsync()
    {
        _manufactureClientMock
            .Setup(x => x.SetBomItemsOrderAndPhaseAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int, string?)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_ValidOrder_CallsSetBomItemsOrderAndPhaseAsync_WithCorrectPairs()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"), (200, "MAT-B"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        SetupSetBomItemsOrderAndPhaseAsync();

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
                new() { IngredientProductCode = "MAT-B", SortOrder = 2 },
            }
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.UpdatedCount.Should().Be(2);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Any(t => t.Item1 == 100 && t.Item2 == 1) &&
                    seq.Any(t => t.Item1 == 200 && t.Item2 == 2)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TemplateNull_ReturnsZeroAndDoesNotCallSetBomItemsOrderAndPhase()
    {
        // Arrange
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
            }
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.UpdatedCount.Should().Be(0);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int, string?)>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_RequestItemCodeNotInTemplate_IsSkippedWithWarning()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A")); // Only MAT-A in BoM
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        SetupSetBomItemsOrderAndPhaseAsync();

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
                new() { IngredientProductCode = "MAT-GHOST", SortOrder = 2 }, // not in BoM
            }
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.UpdatedCount.Should().Be(1);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Count() == 1 && seq.Single().Item1 == 100),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyOrder_SkipsSetBomItemsOrderAndPhaseAndReturnsZero()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"), (200, "MAT-B"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>()
        };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.UpdatedCount.Should().Be(0);
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<(int, int, string?)>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PhaseLabelLowercase_IsNormalizedToUppercase()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"), (200, "MAT-B"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        SetupSetBomItemsOrderAndPhaseAsync();

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1, PhaseLabel = "a" }, // lowercase
                new() { IngredientProductCode = "MAT-B", SortOrder = 2, PhaseLabel = "A" }, // already uppercase
            }
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert: both normalised to "A"
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Any(t => t.Item1 == 100 && t.Item3 == "A") &&
                    seq.Any(t => t.Item1 == 200 && t.Item3 == "A")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidPhaseLabel_MultipleChars_NormalizesToNull()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        SetupSetBomItemsOrderAndPhaseAsync();

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1, PhaseLabel = "AB" }, // 2 chars — invalid
            }
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert: "AB" normalized to null
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Single().Item3 == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NullPhaseLabel_ForwardedAsNull()
    {
        // Arrange
        var template = BuildTemplate((100, "MAT-A"));
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        SetupSetBomItemsOrderAndPhaseAsync();

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1, PhaseLabel = null },
            }
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _manufactureClientMock.Verify(
            x => x.SetBomItemsOrderAndPhaseAsync(
                "PRD1",
                It.Is<IEnumerable<(int, int, string?)>>(seq =>
                    seq.Single().Item3 == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests — verify they FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "UpdateProductCompositionOrderHandlerTests" 2>&1 | tail -20
```

Expected: FAIL — the handler still calls `SetBomItemsOrderAsync`, but the tests verify `SetBomItemsOrderAndPhaseAsync`.

- [ ] **Step 3: Implement the updated handler**

Replace the entire content of `UpdateProductCompositionOrderHandler.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;

public class UpdateProductCompositionOrderHandler
    : IRequestHandler<UpdateProductCompositionOrderRequest, UpdateProductCompositionOrderResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly ILogger<UpdateProductCompositionOrderHandler> _logger;

    public UpdateProductCompositionOrderHandler(
        IManufactureClient manufactureClient,
        ILogger<UpdateProductCompositionOrderHandler> logger)
    {
        _manufactureClient = manufactureClient ?? throw new ArgumentNullException(nameof(manufactureClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UpdateProductCompositionOrderResponse> Handle(
        UpdateProductCompositionOrderRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _manufactureClient.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);
        if (template is null)
        {
            _logger.LogWarning(
                "Cannot set BoM order for {ProductCode}: manufacture template not found in Flexi",
                request.ProductCode);
            return new UpdateProductCompositionOrderResponse { UpdatedCount = 0 };
        }

        var codeToBomItemId = template.Ingredients.ToDictionary(
            i => i.ProductCode,
            i => i.TemplateId,
            StringComparer.Ordinal);

        var tuples = new List<(int BoMItemId, int Order, string? PhaseLabel)>();
        foreach (var item in request.Order)
        {
            if (!codeToBomItemId.TryGetValue(item.IngredientProductCode, out var bomItemId))
            {
                _logger.LogWarning(
                    "Ingredient {IngredientCode} not found in Flexi BoM for {ProductCode} — skipping",
                    item.IngredientProductCode, request.ProductCode);
                continue;
            }
            tuples.Add((bomItemId, item.SortOrder, NormalizePhaseLabel(item.PhaseLabel)));
        }

        if (tuples.Count == 0)
        {
            return new UpdateProductCompositionOrderResponse { UpdatedCount = 0 };
        }

        await _manufactureClient.SetBomItemsOrderAndPhaseAsync(request.ProductCode, tuples, cancellationToken);

        return new UpdateProductCompositionOrderResponse { UpdatedCount = tuples.Count };
    }

    /// <summary>
    /// Returns a single uppercase letter A–Z if the input is valid, or null otherwise.
    /// Defends against non-letter or multi-character labels from the client.
    /// </summary>
    private static string? NormalizePhaseLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var trimmed = label.Trim().ToUpperInvariant();
        return trimmed.Length == 1 && trimmed[0] is >= 'A' and <= 'Z' ? trimmed : null;
    }
}
```

- [ ] **Step 4: Run the tests — verify they PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "UpdateProductCompositionOrderHandlerTests" 2>&1 | tail -20
```

Expected: all 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/UpdateProductCompositionOrderHandlerTests.cs
git commit -m "feat(catalog): forward PhaseLabel through UpdateProductCompositionOrderHandler with A-Z normalization"
```

---

## Task 6 — Backend build gate

- [ ] **Step 1: Full build + dotnet format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/backend
dotnet build 2>&1 | tail -10
dotnet format --verify-no-changes 2>&1 | tail -10
```

Expected: build succeeds, format reports no changes. If format reports changes, run `dotnet format` (without `--verify-no-changes`) and commit.

- [ ] **Step 2: Full test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/backend
dotnet test 2>&1 | tail -20
```

Expected: all tests pass. Zero failures.

- [ ] **Step 3: Commit (if format applied changes)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga
git add -u backend/
git commit -m "chore(backend): apply dotnet format"
```

(Skip if no changes.)

---

## Task 7 — Frontend types: IngredientDto + IngredientOrderItem

**Files:**
- Modify: `frontend/src/api/hooks/useCatalog.ts`
- Modify: `frontend/src/api/hooks/useUpdateProductCompositionOrder.ts`

- [ ] **Step 1: Add phaseLabel to IngredientDto in useCatalog.ts**

Find the `IngredientDto` interface (currently line 179–185) and add the new field:

```typescript
export interface IngredientDto {
  productCode: string;
  productName: string;
  amount: number;
  unit: string;
  order: number;
  phaseLabel?: string | null;
}
```

- [ ] **Step 2: Add phaseLabel to IngredientOrderItem in useUpdateProductCompositionOrder.ts**

Find the `IngredientOrderItem` interface (currently lines 8–11) and add:

```typescript
export interface IngredientOrderItem {
  ingredientProductCode: string;
  sortOrder: number;
  phaseLabel?: string | null;
}
```

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga
git add frontend/src/api/hooks/useCatalog.ts \
        frontend/src/api/hooks/useUpdateProductCompositionOrder.ts
git commit -m "feat(frontend): add phaseLabel to IngredientDto and IngredientOrderItem types"
```

---

## Task 8 — Frontend: CompositionTabRow — Fáze column cell

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/CompositionTabRow.tsx`

- [ ] **Step 1: Update CompositionTabRow to accept + render the Fáze cell**

Replace the entire file content:

```typescript
import React from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { GripVertical } from 'lucide-react';
import type { IngredientDto } from '../../../../api/hooks/useCatalog';

interface CompositionTabRowProps {
  ingredient: IngredientDto;
  displayOrder: number;
  isEditMode: boolean;
  /** True when this row is the first ingredient of its phase group. */
  isFirstInPhase: boolean;
  /** True when this row is the last ingredient of its phase group. */
  isLastInPhase: boolean;
}

export const CompositionTabRow: React.FC<CompositionTabRowProps> = ({
  ingredient,
  displayOrder,
  isEditMode,
  isFirstInPhase,
  isLastInPhase,
}) => {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: ingredient.productCode });

  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.6 : 1,
  };

  const phaseLabel = ingredient.phaseLabel ?? null;

  // Build Fáze cell class names using left/top/bottom borders as a bracket indicator.
  const fazeCellClasses = [
    'py-3 px-3 text-center w-16',
    phaseLabel ? 'border-l-2 border-indigo-400' : '',
    phaseLabel && isFirstInPhase ? 'border-t-2 border-indigo-400' : '',
    phaseLabel && isLastInPhase ? 'border-b-2 border-indigo-400' : '',
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <tr
      ref={setNodeRef}
      style={style}
      className={`hover:bg-gray-50 ${isDragging ? 'bg-indigo-50' : ''}`}
    >
      {isEditMode && (
        <td
          className="py-3 px-2 w-8 text-gray-400 cursor-grab active:cursor-grabbing"
          {...attributes}
          {...listeners}
        >
          <GripVertical className="h-4 w-4" />
        </td>
      )}
      <td className="py-3 px-4 text-right text-gray-700 w-16">{displayOrder}</td>
      <td className="py-3 px-4 text-gray-900">{ingredient.productName}</td>
      <td className="py-3 px-4 text-gray-900 font-medium">{ingredient.productCode}</td>
      <td className="py-3 px-4 text-right text-gray-900 font-medium">
        {ingredient.amount.toLocaleString('cs-CZ', {
          minimumFractionDigits: 2,
          maximumFractionDigits: 4,
        })}
      </td>
      <td className={fazeCellClasses}>
        {isFirstInPhase && phaseLabel ? (
          <span className="text-indigo-600 font-bold text-sm">{phaseLabel}</span>
        ) : null}
      </td>
    </tr>
  );
};
```

- [ ] **Step 2: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga
git add frontend/src/components/catalog/detail/tabs/CompositionTabRow.tsx
git commit -m "feat(frontend): add Fáze column cell to CompositionTabRow with bracket CSS border styling"
```

---

## Task 9 — Frontend: CompositionTab — phase bracket rendering in read mode

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/CompositionTab.tsx`

This task adds the Fáze column header and read-mode bracket rendering. Edit-mode phase management is added in Task 10.

- [ ] **Step 1: Replace the full CompositionTab.tsx with the phase-aware version**

Replace the entire file:

```typescript
import React, { useEffect, useState } from 'react';
import { Loader2, AlertCircle, Beaker, Pencil, Save, X, Plus } from 'lucide-react';
import {
  DndContext,
  closestCenter,
  PointerSensor,
  useSensor,
  useSensors,
  useDroppable,
  DragEndEvent,
} from '@dnd-kit/core';
import {
  SortableContext,
  arrayMove,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { useProductComposition } from '../../../../api/hooks/useCatalog';
import { useUpdateProductCompositionOrder } from '../../../../api/hooks/useUpdateProductCompositionOrder';
import type { IngredientDto } from '../../../../api/hooks/useCatalog';
import { CompositionTabRow } from './CompositionTabRow';

interface CompositionTabProps {
  productCode: string;
}

/** Drop-zone row rendered for an empty phase in edit mode. */
const PhaseDropZoneRow: React.FC<{ phase: string }> = ({ phase }) => {
  const { setNodeRef, isOver } = useDroppable({ id: `phase:${phase}` });
  return (
    <tr>
      <td
        colSpan={6}
        ref={setNodeRef}
        className={`py-4 text-center text-sm border-2 border-dashed rounded transition-colors ${
          isOver ? 'border-indigo-400 bg-indigo-50 text-indigo-600' : 'border-gray-200 text-gray-400'
        }`}
      >
        Přetáhněte ingredience sem pro fázi {phase}
      </td>
    </tr>
  );
};

const LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';

const CompositionTab: React.FC<CompositionTabProps> = ({ productCode }) => {
  const { data, isLoading, error } = useProductComposition(productCode);
  const updateOrder = useUpdateProductCompositionOrder();

  const [isEditMode, setIsEditMode] = useState(false);
  const [draftOrder, setDraftOrder] = useState<IngredientDto[] | null>(null);
  const [draftPhases, setDraftPhases] = useState<string[]>([]);
  const [sortConfig, setSortConfig] = useState<{
    key: keyof IngredientDto;
    direction: 'asc' | 'desc';
  } | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const ingredients = React.useMemo(() => data?.ingredients ?? [], [data?.ingredients]);

  useEffect(() => {
    if (!isEditMode) {
      setDraftOrder(null);
      setDraftPhases([]);
    }
  }, [ingredients, isEditMode]);

  const sortedIngredients = React.useMemo(() => {
    if (isEditMode) {
      return draftOrder ?? ingredients;
    }
    if (!sortConfig) return ingredients;

    return [...ingredients].sort((a, b) => {
      const aValue = a[sortConfig.key];
      const bValue = b[sortConfig.key];
      if (typeof aValue === 'number' && typeof bValue === 'number') {
        return sortConfig.direction === 'asc' ? aValue - bValue : bValue - aValue;
      }
      return sortConfig.direction === 'asc'
        ? String(aValue).localeCompare(String(bValue), 'cs')
        : String(bValue).localeCompare(String(aValue), 'cs');
    });
  }, [ingredients, sortConfig, isEditMode, draftOrder]);

  const handleSort = (key: keyof IngredientDto) => {
    if (isEditMode) return;
    setSortConfig((current) => {
      if (!current || current.key !== key) return { key, direction: 'asc' };
      if (current.direction === 'asc') return { key, direction: 'desc' };
      return null;
    });
  };

  const getSortIcon = (key: keyof IngredientDto) => {
    if (!sortConfig || sortConfig.key !== key) return null;
    return sortConfig.direction === 'asc' ? ' ↑' : ' ↓';
  };

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
  );

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;

    const overId = String(over.id);

    setDraftOrder((current) => {
      const list = current ?? ingredients;

      if (overId.startsWith('phase:')) {
        // Dropped onto an empty phase drop zone — assign phase and move to end of that phase's block.
        const targetPhase = overId.slice('phase:'.length);
        const oldIndex = list.findIndex((i) => i.productCode === active.id);
        if (oldIndex < 0) return current;

        const activeItem = { ...list[oldIndex], phaseLabel: targetPhase };
        const withoutActive = list.filter((_, idx) => idx !== oldIndex);

        // Insert after the last ingredient already in the target phase (or append to end).
        const lastPhaseIdx = withoutActive.reduce<number>(
          (acc, ing, idx) => (ing.phaseLabel === targetPhase ? idx : acc),
          -1,
        );
        const insertAt = lastPhaseIdx >= 0 ? lastPhaseIdx + 1 : withoutActive.length;
        const result = [...withoutActive];
        result.splice(insertAt, 0, activeItem);
        return result;
      }

      // Dropped onto another ingredient — reorder and inherit that ingredient's phaseLabel.
      const oldIndex = list.findIndex((i) => i.productCode === active.id);
      const newIndex = list.findIndex((i) => i.productCode === over.id);
      if (oldIndex < 0 || newIndex < 0) return current;

      const targetPhase = list[newIndex].phaseLabel ?? null;
      const moved = arrayMove(list, oldIndex, newIndex);
      return moved.map((ing, idx) =>
        idx === newIndex ? { ...ing, phaseLabel: targetPhase } : ing,
      );
    });
  };

  const enterEditMode = () => {
    const initialDraft = ingredients.map((ing) => ({ ...ing }));
    setDraftOrder(initialDraft);
    // Seed draftPhases from existing phase labels (sorted A→Z).
    const existingPhases = [
      ...new Set(
        ingredients
          .map((i) => i.phaseLabel)
          .filter((l): l is string => typeof l === 'string' && l.length === 1),
      ),
    ].sort();
    setDraftPhases(existingPhases);
    setSortConfig(null);
    setSaveError(null);
    setIsEditMode(true);
  };

  const cancelEdit = () => {
    setDraftOrder(null);
    setDraftPhases([]);
    setSaveError(null);
    setIsEditMode(false);
  };

  const addPhase = () => {
    const next = LETTERS.split('').find((l) => !draftPhases.includes(l));
    if (!next) return; // All 26 letters used.
    setDraftPhases((prev) => [...prev, next]);
  };

  const removePhase = (phase: string) => {
    setDraftOrder((current) => {
      const list = current ?? ingredients;
      return list.map((ing) =>
        ing.phaseLabel === phase ? { ...ing, phaseLabel: null } : ing,
      );
    });
    setDraftPhases((prev) => prev.filter((p) => p !== phase));
  };

  const saveOrder = async () => {
    if (!draftOrder) {
      setIsEditMode(false);
      return;
    }
    setSaveError(null);
    try {
      await updateOrder.mutateAsync({
        productCode,
        order: draftOrder.map((ing, idx) => ({
          ingredientProductCode: ing.productCode,
          sortOrder: idx + 1,
          phaseLabel: ing.phaseLabel ?? null,
        })),
      });
      setIsEditMode(false);
      setDraftOrder(null);
      setDraftPhases([]);
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : 'Uložení se nezdařilo');
    }
  };

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

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání složení: {(error as Error).message}</div>
        </div>
      </div>
    );
  }

  if (ingredients.length === 0) {
    return (
      <div className="text-center py-12 bg-gray-50 rounded-lg">
        <Beaker className="h-12 w-12 mx-auto mb-3 text-gray-300" />
        <p className="text-gray-500 mb-2">Tento produkt nemá definované složení</p>
        <p className="text-sm text-gray-400">Výrobní šablona pro tento produkt neexistuje</p>
      </div>
    );
  }

  // Compute empty phases (those in draftPhases not yet referenced by any ingredient).
  const emptyPhases = isEditMode
    ? draftPhases.filter((p) => !sortedIngredients.some((i) => i.phaseLabel === p))
    : [];

  const columnCount = isEditMode ? 6 : 5;

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-medium text-gray-900 flex items-center">
          <Beaker className="h-5 w-5 mr-2 text-gray-500" />
          Složení ({sortedIngredients.length} ingrediencí)
        </h3>
        <div className="flex items-center space-x-2">
          {isEditMode && (
            <button
              type="button"
              onClick={addPhase}
              className="inline-flex items-center px-3 py-1.5 text-sm border border-indigo-300 rounded-md text-indigo-700 bg-indigo-50 hover:bg-indigo-100"
            >
              <Plus className="h-4 w-4 mr-1" />
              Přidat fázi
            </button>
          )}
          {!isEditMode && (
            <button
              type="button"
              onClick={enterEditMode}
              className="inline-flex items-center px-3 py-1.5 text-sm border border-gray-300 rounded-md text-gray-700 bg-white hover:bg-gray-50"
            >
              <Pencil className="h-4 w-4 mr-1.5" />
              Upravit pořadí
            </button>
          )}
          {isEditMode && (
            <>
              <button
                type="button"
                onClick={saveOrder}
                disabled={updateOrder.isPending}
                className="inline-flex items-center px-3 py-1.5 text-sm rounded-md text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-60"
              >
                <Save className="h-4 w-4 mr-1.5" />
                Uložit
              </button>
              <button
                type="button"
                onClick={cancelEdit}
                disabled={updateOrder.isPending}
                className="inline-flex items-center px-3 py-1.5 text-sm border border-gray-300 rounded-md text-gray-700 bg-white hover:bg-gray-50"
              >
                <X className="h-4 w-4 mr-1.5" />
                Zrušit
              </button>
            </>
          )}
        </div>
      </div>

      {saveError && (
        <div className="flex items-center space-x-2 px-3 py-2 text-sm text-red-700 bg-red-50 border border-red-200 rounded mb-4">
          <AlertCircle className="h-4 w-4" />
          <span>{saveError}</span>
        </div>
      )}

      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragEnd={handleDragEnd}
      >
        <div className="flex-1 min-h-0 bg-white rounded-lg border border-gray-200 overflow-hidden">
          <div className="h-full overflow-y-auto">
            <table className="w-full text-sm">
              <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200">
                <tr>
                  {isEditMode && <th className="w-8 py-3 px-2" />}
                  <th className="text-right py-3 px-4 font-medium text-gray-700 w-16">#</th>
                  <th
                    className={`text-left py-3 px-4 font-medium text-gray-700 ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100'}`}
                    onClick={() => handleSort('productName')}
                  >
                    Název{getSortIcon('productName')}
                  </th>
                  <th
                    className={`text-left py-3 px-4 font-medium text-gray-700 ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100'}`}
                    onClick={() => handleSort('productCode')}
                  >
                    Kód{getSortIcon('productCode')}
                  </th>
                  <th
                    className={`text-right py-3 px-4 font-medium text-gray-700 ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100'}`}
                    onClick={() => handleSort('amount')}
                  >
                    Množství{getSortIcon('amount')}
                  </th>
                  <th className="text-center py-3 px-3 font-medium text-gray-700 w-16">Fáze</th>
                </tr>
              </thead>
              <SortableContext
                items={sortedIngredients.map((i) => i.productCode)}
                strategy={verticalListSortingStrategy}
              >
                <tbody className="divide-y divide-gray-100">
                  {sortedIngredients.map((ingredient, index) => {
                    const currentPhase = ingredient.phaseLabel ?? null;
                    const prevPhase =
                      index > 0 ? (sortedIngredients[index - 1].phaseLabel ?? null) : null;
                    const nextPhase =
                      index < sortedIngredients.length - 1
                        ? (sortedIngredients[index + 1].phaseLabel ?? null)
                        : null;
                    const isFirstInPhase = !!currentPhase && currentPhase !== prevPhase;
                    const isLastInPhase = !!currentPhase && currentPhase !== nextPhase;

                    return (
                      <React.Fragment key={ingredient.productCode}>
                        {/* Phase header strip — inserted before the first ingredient of each phase in edit mode */}
                        {isEditMode && isFirstInPhase && (
                          <tr className="bg-indigo-50 border-t border-indigo-200">
                            <td colSpan={columnCount} className="px-4 py-1">
                              <div className="flex items-center justify-between">
                                <span className="text-sm font-semibold text-indigo-700">
                                  Fáze {currentPhase}
                                </span>
                                <button
                                  type="button"
                                  onClick={() => removePhase(currentPhase!)}
                                  className="text-indigo-400 hover:text-red-500 text-lg leading-none px-1"
                                  title={`Odebrat fázi ${currentPhase}`}
                                >
                                  ×
                                </button>
                              </div>
                            </td>
                          </tr>
                        )}
                        <CompositionTabRow
                          ingredient={ingredient}
                          displayOrder={isEditMode ? index + 1 : ingredient.order}
                          isEditMode={isEditMode}
                          isFirstInPhase={isFirstInPhase}
                          isLastInPhase={isLastInPhase}
                        />
                      </React.Fragment>
                    );
                  })}

                  {/* Empty phase drop zones — for phases that have no ingredients yet */}
                  {emptyPhases.map((phase) => (
                    <React.Fragment key={`empty-phase-${phase}`}>
                      <tr className="bg-indigo-50 border-t border-indigo-200">
                        <td colSpan={columnCount} className="px-4 py-1">
                          <div className="flex items-center justify-between">
                            <span className="text-sm font-semibold text-indigo-700">
                              Fáze {phase}
                            </span>
                            <button
                              type="button"
                              onClick={() => removePhase(phase)}
                              className="text-indigo-400 hover:text-red-500 text-lg leading-none px-1"
                              title={`Odebrat fázi ${phase}`}
                            >
                              ×
                            </button>
                          </div>
                        </td>
                      </tr>
                      <PhaseDropZoneRow phase={phase} />
                    </React.Fragment>
                  ))}
                </tbody>
              </SortableContext>
            </table>
          </div>
        </div>
      </DndContext>
    </div>
  );
};

export default CompositionTab;
```

- [ ] **Step 2: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga
git add frontend/src/components/catalog/detail/tabs/CompositionTab.tsx
git commit -m "feat(frontend): manufacture phase brackets on Složení tab — read/edit mode, add/remove phases, DnD phase assignment"
```

---

## Task 10 — Frontend build gate

- [ ] **Step 1: TypeScript build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/frontend
npm run build 2>&1 | tail -30
```

Expected: build succeeds with no TypeScript errors.

If there are errors, the most likely causes:
- `useDroppable` not re-exported from `@dnd-kit/core` — check the import and add it: `import { ..., useDroppable } from '@dnd-kit/core';`
- `Plus` not in `lucide-react` — if missing, replace `Plus` with `PlusIcon` or use a `+` text node in the button
- Type mismatch in `handleDragEnd` — `active.id` is `UniqueIdentifier` (string | number); the `.findIndex` callback uses `i.productCode === active.id` which works when IDs are strings

- [ ] **Step 2: Lint**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga/frontend
npm run lint 2>&1 | tail -20
```

Expected: no errors. Fix any reported issues.

- [ ] **Step 3: Commit fixes (if any)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/riga
git add -u frontend/
git commit -m "fix(frontend): address build/lint issues from manufacture-phases implementation"
```

(Skip if clean.)

---

## Verification Checklist (Manual)

After all tasks are committed, perform this smoke test against STG:

- [ ] Open a known BoMof semiproduct in Catalog detail → Složení tab in **read mode**.
  - Confirm ingredients load. Fáze column visible but empty (no phases set yet).
- [ ] Enter edit mode → click **"+ Přidat fázi"**.
  - Confirm "Fáze A" header and drop zone appear at the bottom.
- [ ] Drag two ingredients onto Fáze A's drop zone (or drag an ingredient onto another ingredient already assigned to A).
  - Confirm both show the "A" bracket indicator in the Fáze column.
- [ ] Click **Uložit**. Reload the page.
  - Confirm Fáze A bracket still visible on those two ingredients.
- [ ] Enter edit mode again → drag one ingredient out of phase A (drop onto an unassigned ingredient at the top).
  - Confirm it loses the "A" indicator. Click **Uložit** → reload → confirm unassigned.
- [ ] Enter edit mode → click **×** on Fáze A header.
  - Confirm all ingredients lose the "A" label. Click **Uložit** → reload → confirm no phases.
- [ ] Inspect the Flexi BoM via direct REST (GET `kus-kusovnik/{id}?detail=full`) to confirm `nazevC` is set/cleared correctly. Document empty-string vs space behavior in `docs/integrations/` if Flexi doesn't clear on empty string.
- [ ] Run existing E2E tests: `./scripts/run-playwright-tests.sh --grep "Složení"`. Fix any failures due to added column count.
