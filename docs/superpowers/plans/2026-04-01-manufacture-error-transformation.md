# Manufacture Error Transformation Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform raw Flexi ERP exceptions into concise Czech user messages before storing them in the manufacture order notes (DB), so non-technical users see meaningful text instead of stack traces.

**Architecture:** A pluggable filter chain (`IManufactureErrorFilter`) where each class handles one error type; a `ManufactureErrorTransformer` iterates all registered filters and picks the first match. Filters are auto-discovered via Scrutor assembly scanning — adding a new filter class is the only change required. The transformer is injected into `SubmitManufactureHandler`, which transforms exceptions before returning the response. `ManufactureOrderApplicationService` stores the transformed message as the note.

**Tech Stack:** .NET 8, C#, Scrutor 4.x (assembly scanning), XUnit, Moq, FluentAssertions

**Related spec:** `docs/superpowers/specs/2026-04-01-manufacture-error-transformation-design.md`

**GitHub issue:** onpaj/Anela.Heblo#475

---

## File Map

**New files:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/IManufactureErrorFilter.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/IManufactureErrorTransformer.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/ManufactureErrorTransformer.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientSemiProductFilter.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientRawMaterialFilter.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ProductCodeNotFoundFilter.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ReferenceTooLongFilter.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ZeroQuantityItemFilter.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientLotAllocationFilter.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/WarehouseNotConfiguredFilter.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/NegativeStockFilter.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/ManufactureErrorTransformerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientSemiProductFilterTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientRawMaterialFilterTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ProductCodeNotFoundFilterTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ReferenceTooLongFilterTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ZeroQuantityItemFilterTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientLotAllocationFilterTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/WarehouseNotConfiguredFilterTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/NegativeStockFilterTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs`

**Modified files:**
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — add Scrutor
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — register transformer + filters via Scrutor
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureResponse.cs` — add `UserMessage` property
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs` — inject transformer, use in catch
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs` — use `UserMessage` instead of `FullError()` for notes

---

## Task 1: Core contracts + fallback transformer

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/IManufactureErrorFilter.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/IManufactureErrorTransformer.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/ManufactureErrorTransformer.cs`
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/ManufactureErrorTransformerTests.cs`

- [ ] **Step 1: Add Scrutor NuGet package**

```bash
cd backend/src/Anela.Heblo.Application
dotnet add package Scrutor --version 4.2.2
```

Expected: package added to `Anela.Heblo.Application.csproj`.

- [ ] **Step 2: Create IManufactureErrorFilter interface**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/IManufactureErrorFilter.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters;

public interface IManufactureErrorFilter
{
    bool CanHandle(Exception exception);
    string Transform(Exception exception);
}
```

- [ ] **Step 3: Create IManufactureErrorTransformer interface**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/IManufactureErrorTransformer.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters;

public interface IManufactureErrorTransformer
{
    string Transform(Exception exception);
}
```

- [ ] **Step 4: Write failing tests for ManufactureErrorTransformer**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/ManufactureErrorTransformerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters;

public class ManufactureErrorTransformerTests
{
    [Fact]
    public void Transform_WhenNoFilterMatches_ReturnsFallbackWithExceptionMessage()
    {
        var filters = new List<IManufactureErrorFilter>();
        var transformer = new ManufactureErrorTransformer(filters);
        var ex = new InvalidOperationException("Some unknown Flexi error");

        var result = transformer.Transform(ex);

        result.Should().Be("Při zpracování výroby došlo k neočekávané chybě. Technické detaily: Some unknown Flexi error");
    }

    [Fact]
    public void Transform_WhenFirstFilterMatches_ReturnsItsMessage()
    {
        var matchingFilter = new Mock<IManufactureErrorFilter>();
        matchingFilter.Setup(f => f.CanHandle(It.IsAny<Exception>())).Returns(true);
        matchingFilter.Setup(f => f.Transform(It.IsAny<Exception>())).Returns("Uživatelsky přívětivá zpráva");

        var transformer = new ManufactureErrorTransformer(new[] { matchingFilter.Object });
        var ex = new InvalidOperationException("raw error");

        var result = transformer.Transform(ex);

        result.Should().Be("Uživatelsky přívětivá zpráva");
    }

    [Fact]
    public void Transform_WhenFirstFilterDoesNotMatch_TriesNextFilter()
    {
        var nonMatchingFilter = new Mock<IManufactureErrorFilter>();
        nonMatchingFilter.Setup(f => f.CanHandle(It.IsAny<Exception>())).Returns(false);

        var matchingFilter = new Mock<IManufactureErrorFilter>();
        matchingFilter.Setup(f => f.CanHandle(It.IsAny<Exception>())).Returns(true);
        matchingFilter.Setup(f => f.Transform(It.IsAny<Exception>())).Returns("Zpráva z druhého filtru");

        var transformer = new ManufactureErrorTransformer(new[] { nonMatchingFilter.Object, matchingFilter.Object });
        var ex = new InvalidOperationException("raw error");

        var result = transformer.Transform(ex);

        result.Should().Be("Zpráva z druhého filtru");
        nonMatchingFilter.Verify(f => f.Transform(It.IsAny<Exception>()), Times.Never);
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests --filter "ManufactureErrorTransformerTests" --no-build 2>&1 | tail -10
```

Expected: build error or type-not-found — `ManufactureErrorTransformer` does not exist yet.

- [ ] **Step 6: Create ManufactureErrorTransformer**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/ManufactureErrorTransformer.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters;

public class ManufactureErrorTransformer : IManufactureErrorTransformer
{
    private readonly IEnumerable<IManufactureErrorFilter> _filters;

    public ManufactureErrorTransformer(IEnumerable<IManufactureErrorFilter> filters)
    {
        _filters = filters;
    }

    public string Transform(Exception exception)
    {
        foreach (var filter in _filters)
        {
            if (filter.CanHandle(exception))
                return filter.Transform(exception);
        }

        return $"Při zpracování výroby došlo k neočekávané chybě. Technické detaily: {exception.Message}";
    }
}
```

- [ ] **Step 7: Register in ManufactureModule.cs**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` and add at the end of `AddManufactureModule`, before `return services`:

```csharp
// Register manufacture error transformation
services.Scan(scan => scan
    .FromAssemblyOf<IManufactureErrorFilter>()
    .AddClasses(c => c.AssignableTo<IManufactureErrorFilter>())
    .AsImplementedInterfaces()
    .WithTransientLifetime());
services.AddTransient<IManufactureErrorTransformer, ManufactureErrorTransformer>();
```

Also add the using at the top of the file:
```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
```

- [ ] **Step 8: Run tests to verify they pass**

```bash
cd backend
dotnet build && dotnet test test/Anela.Heblo.Tests --filter "ManufactureErrorTransformerTests" -v minimal
```

Expected: 3 tests passing.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj \
        backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/ \
        backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/ManufactureErrorTransformerTests.cs
git commit -m "feat(manufacture): add IManufactureErrorFilter contract and fallback transformer"
```

---

## Task 2: Warehouse stock filters (InsufficientSemiProduct + InsufficientRawMaterial)

These two filters handle the most common errors (~80% of production data): a finalized issued order fails because a warehouse doesn't have enough stock. Both errors come from Flexi in Czech and contain structured data.

**Example error message for POLOTOVARY:**
```
Failed to finalize issued order 5260: Nelze vytvořit příjemku výrobku 'OCH006030 - Ochráním bradavky - mast 30 ml' kvůli chybějícímu materiálu 'OCH0060001M - Ochráním bradavky - meziprodukt' na skladu 'POLOTOVARY - Nerozplněné produkty' (požadováno: 4 095,000000, dostupné: 4 000,000000).
```

**Example error message for MATERIAL:**
```
Failed to finalize issued order 5662: Nelze vytvořit příjemku výrobku 'SER001001M - Bezstarostná krása' kvůli chybějícímu materiálu 'OLE037 - Rýžový olej LZS' na skladu 'MATERIAL - Sklad Materialu' (požadováno: 717,846000, dostupné: 542,077000).
```

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientSemiProductFilter.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientRawMaterialFilter.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientSemiProductFilterTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientRawMaterialFilterTests.cs`

- [ ] **Step 1: Write failing tests for InsufficientSemiProductFilter**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientSemiProductFilterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientSemiProductFilterTests
{
    private readonly InsufficientSemiProductFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsPolotovaryKeywords_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to finalize issued order 5260: Nelze vytvořit příjemku výrobku 'OCH006030 - Ochráním bradavky 30 ml' kvůli chybějícímu materiálu 'OCH0060001M - Ochráním bradavky - meziprodukt' na skladu 'POLOTOVARY - Nerozplněné produkty' (požadováno: 4 095,000000, dostupné: 4 000,000000).");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageContainsMaterialWarehouse_ReturnsFalse()
    {
        var ex = new InvalidOperationException(
            "Failed to finalize issued order 5662: Nelze vytvořit příjemku výrobku 'SER001001M - Bezstarostná krása' kvůli chybějícímu materiálu 'OLE037 - Rýžový olej LZS' na skladu 'MATERIAL - Sklad Materialu' (požadováno: 717,846000, dostupné: 542,077000).");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ExtractsMaterialNameAndQuantities()
    {
        var ex = new InvalidOperationException(
            "Failed to finalize issued order 5260: Nelze vytvořit příjemku výrobku 'OCH006030 - Ochráním bradavky - mast pro kojicí maminky 30 ml' kvůli chybějícímu materiálu 'OCH0060001M - Ochráním bradavky - meziprodukt' na skladu 'POLOTOVARY - Nerozplněné produkty' (požadováno: 4 095,000000, dostupné: 4 000,000000).");

        var result = _filter.Transform(ex);

        result.Should().Be("Nedostatek meziproduktu 'OCH0060001M - Ochráním bradavky - meziprodukt' na skladu POLOTOVARY (požadováno: 4 095,000000, dostupné: 4 000,000000).");
    }
}
```

- [ ] **Step 2: Write failing tests for InsufficientRawMaterialFilter**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientRawMaterialFilterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientRawMaterialFilterTests
{
    private readonly InsufficientRawMaterialFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsMaterialWarehouseKeywords_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to finalize issued order 5662: Nelze vytvořit příjemku výrobku 'SER001001M - Bezstarostná krása' kvůli chybějícímu materiálu 'OLE037 - Rýžový olej LZS' na skladu 'MATERIAL - Sklad Materialu' (požadováno: 717,846000, dostupné: 542,077000).");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageContainsPolotovaryWarehouse_ReturnsFalse()
    {
        var ex = new InvalidOperationException(
            "Nelze vytvořit příjemku výrobku 'OCH006030' kvůli chybějícímu materiálu 'OCH0060001M' na skladu 'POLOTOVARY - Nerozplněné produkty' (požadováno: 100, dostupné: 50).");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ExtractsMaterialNameAndQuantities()
    {
        var ex = new InvalidOperationException(
            "Failed to finalize issued order 5662: Nelze vytvořit příjemku výrobku 'SER001001M - Bezstarostná krása' kvůli chybějícímu materiálu 'OLE037 - Rýžový olej LZS' na skladu 'MATERIAL - Sklad Materialu' (požadováno: 717,846000, dostupné: 542,077000).");

        var result = _filter.Transform(ex);

        result.Should().Be("Nedostatek materiálu 'OLE037 - Rýžový olej LZS' na skladu MATERIAL (požadováno: 717,846000, dostupné: 542,077000).");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests --filter "InsufficientSemiProductFilterTests|InsufficientRawMaterialFilterTests" --no-build 2>&1 | tail -5
```

Expected: build error — filter classes do not exist yet.

- [ ] **Step 4: Implement InsufficientSemiProductFilter**

The parsing strategy: extract the material name (between single quotes after "materiálu "), then extract required/available (between parentheses, after "požadováno: " and "dostupné: ").

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientSemiProductFilter.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientSemiProductFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Nelze vytvořit příjemku výrobku") &&
        exception.Message.Contains("POLOTOVARY");

    public string Transform(Exception exception)
    {
        var message = exception.Message;
        var materialName = ExtractBetweenQuotes(message, "materiálu '");
        var (required, available) = ExtractQuantities(message);

        return $"Nedostatek meziproduktu '{materialName}' na skladu POLOTOVARY (požadováno: {required}, dostupné: {available}).";
    }

    private static string ExtractBetweenQuotes(string message, string marker)
    {
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "neznámý";
        start += marker.Length;
        var end = message.IndexOf("'", start, StringComparison.Ordinal);
        return end > start ? message[start..end] : "neznámý";
    }

    private static (string required, string available) ExtractQuantities(string message)
    {
        var parenStart = message.LastIndexOf('(');
        var parenEnd = message.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart)
            return ("?", "?");

        var inside = message[(parenStart + 1)..parenEnd];
        var required = ExtractAfter(inside, "požadováno: ", ",");
        var available = ExtractAfter(inside, "dostupné: ", null);
        return (required.Trim(), available.Trim());
    }

    private static string ExtractAfter(string text, string marker, string? terminator)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "?";
        start += marker.Length;
        if (terminator == null) return text[start..].Trim();
        var end = text.IndexOf(terminator, start, StringComparison.Ordinal);
        return end > start ? text[start..end] : text[start..];
    }
}
```

- [ ] **Step 5: Implement InsufficientRawMaterialFilter**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientRawMaterialFilter.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientRawMaterialFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Nelze vytvořit příjemku výrobku") &&
        exception.Message.Contains("MATERIAL - Sklad Materialu");

    public string Transform(Exception exception)
    {
        var message = exception.Message;
        var materialName = ExtractBetweenQuotes(message, "materiálu '");
        var (required, available) = ExtractQuantities(message);

        return $"Nedostatek materiálu '{materialName}' na skladu MATERIAL (požadováno: {required}, dostupné: {available}).";
    }

    private static string ExtractBetweenQuotes(string message, string marker)
    {
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "neznámý";
        start += marker.Length;
        var end = message.IndexOf("'", start, StringComparison.Ordinal);
        return end > start ? message[start..end] : "neznámý";
    }

    private static (string required, string available) ExtractQuantities(string message)
    {
        var parenStart = message.LastIndexOf('(');
        var parenEnd = message.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart)
            return ("?", "?");

        var inside = message[(parenStart + 1)..parenEnd];
        var required = ExtractAfter(inside, "požadováno: ", ",");
        var available = ExtractAfter(inside, "dostupné: ", null);
        return (required.Trim(), available.Trim());
    }

    private static string ExtractAfter(string text, string marker, string? terminator)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "?";
        start += marker.Length;
        if (terminator == null) return text[start..].Trim();
        var end = text.IndexOf(terminator, start, StringComparison.Ordinal);
        return end > start ? text[start..end] : text[start..];
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd backend
dotnet build && dotnet test test/Anela.Heblo.Tests --filter "InsufficientSemiProductFilterTests|InsufficientRawMaterialFilterTests" -v minimal
```

Expected: 4+4 = 8 tests passing.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientSemiProductFilter.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientRawMaterialFilter.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientSemiProductFilterTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientRawMaterialFilterTests.cs
git commit -m "feat(manufacture): add InsufficientSemiProduct and InsufficientRawMaterial error filters"
```

---

## Task 3: Flexi validation filters (ProductCodeNotFound + ReferenceTooLong + ZeroQuantity)

**Example messages:**
- ProductCodeNotFound: `Failed to create issued order: Zadaný text 'code:MAS001015T' musí identifikovat objekt [VYR51023]`
- ReferenceTooLong: `Failed to create issued order: Pole 'Číslo došlé' nesmí být delší než 40 znaků. [VYR51021]`
- ZeroQuantity: `Item quantity must be greater than zero (Parameter 'item')` — thrown as `ArgumentException`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ProductCodeNotFoundFilter.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ReferenceTooLongFilter.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ZeroQuantityItemFilter.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ProductCodeNotFoundFilterTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ReferenceTooLongFilterTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ZeroQuantityItemFilterTests.cs`

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ProductCodeNotFoundFilterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class ProductCodeNotFoundFilterTests
{
    private readonly ProductCodeNotFoundFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsIdentifyObjectKeyword_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to create issued order: Zadaný text 'code:MAS001015T' musí identifikovat objekt [VYR51023]");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ExtractsProductCode()
    {
        var ex = new InvalidOperationException(
            "Failed to create issued order: Zadaný text 'code:MAS001015T' musí identifikovat objekt [VYR51023]");

        var result = _filter.Transform(ex);

        result.Should().Be("Produkt s kódem 'MAS001015T' nebyl nalezen v systému Flexi.");
    }

    [Fact]
    public void Transform_WhenCodeNotParseable_ReturnsGenericMessage()
    {
        var ex = new InvalidOperationException("musí identifikovat objekt - no code prefix");

        var result = _filter.Transform(ex);

        result.Should().Be("Produkt s kódem 'neznámý' nebyl nalezen v systému Flexi.");
    }
}
```

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ReferenceTooLongFilterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class ReferenceTooLongFilterTests
{
    private readonly ReferenceTooLongFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsBothKeywords_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to create issued order: Pole 'Číslo došlé' nesmí být delší než 40 znaků. [VYR51021]");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenOnlyOneKeyword_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Pole 'Číslo došlé' has some error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ReturnsFixedMessage()
    {
        var ex = new InvalidOperationException(
            "Failed to create issued order: Pole 'Číslo došlé' nesmí být delší než 40 znaků. [VYR51021]");

        var result = _filter.Transform(ex);

        result.Should().Be("Číslo objednávky je příliš dlouhé pro systém Flexi (max. 40 znaků).");
    }
}
```

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ZeroQuantityItemFilterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class ZeroQuantityItemFilterTests
{
    private readonly ZeroQuantityItemFilter _filter = new();

    [Fact]
    public void CanHandle_WhenArgumentExceptionWithZeroQuantityMessage_ReturnsTrue()
    {
        var ex = new ArgumentException("Item quantity must be greater than zero (Parameter 'item')", "item");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenInvalidOperationExceptionWithSameMessage_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Item quantity must be greater than zero");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WhenArgumentExceptionWithDifferentMessage_ReturnsFalse()
    {
        var ex = new ArgumentException("Some other argument error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ReturnsFixedMessage()
    {
        var ex = new ArgumentException("Item quantity must be greater than zero (Parameter 'item')", "item");

        var result = _filter.Transform(ex);

        result.Should().Be("Položka výrobní zakázky má nulové množství.");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests --filter "ProductCodeNotFoundFilterTests|ReferenceTooLongFilterTests|ZeroQuantityItemFilterTests" --no-build 2>&1 | tail -5
```

Expected: build error — filter classes do not exist.

- [ ] **Step 3: Implement ProductCodeNotFoundFilter**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ProductCodeNotFoundFilter.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class ProductCodeNotFoundFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("musí identifikovat objekt");

    public string Transform(Exception exception)
    {
        var code = ExtractCode(exception.Message);
        return $"Produkt s kódem '{code}' nebyl nalezen v systému Flexi.";
    }

    private static string ExtractCode(string message)
    {
        const string marker = "'code:";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "neznámý";
        start += marker.Length;
        var end = message.IndexOf("'", start, StringComparison.Ordinal);
        return end > start ? message[start..end] : "neznámý";
    }
}
```

- [ ] **Step 4: Implement ReferenceTooLongFilter**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ReferenceTooLongFilter.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class ReferenceTooLongFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Číslo došlé") &&
        exception.Message.Contains("40 znaků");

    public string Transform(Exception exception) =>
        "Číslo objednávky je příliš dlouhé pro systém Flexi (max. 40 znaků).";
}
```

- [ ] **Step 5: Implement ZeroQuantityItemFilter**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ZeroQuantityItemFilter.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class ZeroQuantityItemFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception is ArgumentException &&
        exception.Message.Contains("Item quantity must be greater than zero");

    public string Transform(Exception exception) =>
        "Položka výrobní zakázky má nulové množství.";
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd backend
dotnet build && dotnet test test/Anela.Heblo.Tests --filter "ProductCodeNotFoundFilterTests|ReferenceTooLongFilterTests|ZeroQuantityItemFilterTests" -v minimal
```

Expected: 4+3+4 = 11 tests passing.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ProductCodeNotFoundFilter.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ReferenceTooLongFilter.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/ZeroQuantityItemFilter.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ProductCodeNotFoundFilterTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ReferenceTooLongFilterTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/ZeroQuantityItemFilterTests.cs
git commit -m "feat(manufacture): add ProductCodeNotFound, ReferenceTooLong, and ZeroQuantity error filters"
```

---

## Task 4: Lot and stock state filters (InsufficientLotAllocation + WarehouseNotConfigured + NegativeStock)

**Example messages:**
- InsufficientLotAllocation: `Could not allocate sufficient lots for ingredient 'Demineralizovaná voda' (AKL027). Required: 19087.50, Allocated: 15962.77, Missing: 3124.73`
- WarehouseNotConfigured: `Failed to create consumption stock movement for warehouse 20: Pole 'Sklad' musí být vyplněno. [DoklSklad -1]`
- NegativeStock: `Failed to create consumption stock movement for warehouse 20: Není povolen záporný stav skladu.`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientLotAllocationFilter.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/WarehouseNotConfiguredFilter.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/NegativeStockFilter.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientLotAllocationFilterTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/WarehouseNotConfiguredFilterTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/NegativeStockFilterTests.cs`

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientLotAllocationFilterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientLotAllocationFilterTests
{
    private readonly InsufficientLotAllocationFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsLotAllocationKeyword_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Could not allocate sufficient lots for ingredient 'Demineralizovaná voda' (AKL027). Required: 19087.50, Allocated: 15962.77, Missing: 3124.73");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ExtractsIngredientNameAndMissingAmount()
    {
        var ex = new InvalidOperationException(
            "Could not allocate sufficient lots for ingredient 'Demineralizovaná voda' (AKL027). Required: 19087.50, Allocated: 15962.77, Missing: 3124.73");

        var result = _filter.Transform(ex);

        result.Should().Be("Nedostatek šarží pro ingredienci 'Demineralizovaná voda' (chybí: 3124.73).");
    }
}
```

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/WarehouseNotConfiguredFilterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class WarehouseNotConfiguredFilterTests
{
    private readonly WarehouseNotConfiguredFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsWarehouseNotFilledKeyword_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to create consumption stock movement for warehouse 20: Pole 'Sklad' musí být vyplněno. [DoklSklad -1]");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ReturnsFixedMessage()
    {
        var ex = new InvalidOperationException(
            "Failed to create consumption stock movement for warehouse 20: Pole 'Sklad' musí být vyplněno. [DoklSklad -1]");

        var result = _filter.Transform(ex);

        result.Should().Be("Skladový pohyb nemá nastaven sklad.");
    }
}
```

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/NegativeStockFilterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class NegativeStockFilterTests
{
    private readonly NegativeStockFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsNegativeStockKeyword_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to create consumption stock movement for warehouse 20: Není povolen záporný stav skladu.");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ReturnsFixedMessage()
    {
        var ex = new InvalidOperationException(
            "Failed to create consumption stock movement for warehouse 20: Není povolen záporný stav skladu.");

        var result = _filter.Transform(ex);

        result.Should().Be("Operace by způsobila záporný stav skladu.");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests --filter "InsufficientLotAllocationFilterTests|WarehouseNotConfiguredFilterTests|NegativeStockFilterTests" --no-build 2>&1 | tail -5
```

Expected: build error.

- [ ] **Step 3: Implement InsufficientLotAllocationFilter**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientLotAllocationFilter.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientLotAllocationFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Could not allocate sufficient lots");

    public string Transform(Exception exception)
    {
        var message = exception.Message;
        var ingredientName = ExtractIngredientName(message);
        var missing = ExtractMissing(message);
        return $"Nedostatek šarží pro ingredienci '{ingredientName}' (chybí: {missing}).";
    }

    private static string ExtractIngredientName(string message)
    {
        const string marker = "for ingredient '";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "neznámá";
        start += marker.Length;
        var end = message.IndexOf("'", start, StringComparison.Ordinal);
        return end > start ? message[start..end] : "neznámá";
    }

    private static string ExtractMissing(string message)
    {
        const string marker = "Missing: ";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "?";
        start += marker.Length;
        var end = message.IndexOf(",", start, StringComparison.Ordinal);
        return end > start ? message[start..end].Trim() : message[start..].Trim();
    }
}
```

- [ ] **Step 4: Implement WarehouseNotConfiguredFilter**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/WarehouseNotConfiguredFilter.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class WarehouseNotConfiguredFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Pole 'Sklad' musí být vyplněno");

    public string Transform(Exception exception) =>
        "Skladový pohyb nemá nastaven sklad.";
}
```

- [ ] **Step 5: Implement NegativeStockFilter**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/NegativeStockFilter.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

public class NegativeStockFilter : IManufactureErrorFilter
{
    public bool CanHandle(Exception exception) =>
        exception.Message.Contains("Není povolen záporný stav skladu");

    public string Transform(Exception exception) =>
        "Operace by způsobila záporný stav skladu.";
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd backend
dotnet build && dotnet test test/Anela.Heblo.Tests --filter "InsufficientLotAllocationFilterTests|WarehouseNotConfiguredFilterTests|NegativeStockFilterTests" -v minimal
```

Expected: 3+3+3 = 9 tests passing.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientLotAllocationFilter.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/WarehouseNotConfiguredFilter.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/NegativeStockFilter.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/InsufficientLotAllocationFilterTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/WarehouseNotConfiguredFilterTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/ErrorFilters/Filters/NegativeStockFilterTests.cs
git commit -m "feat(manufacture): add InsufficientLotAllocation, WarehouseNotConfigured, and NegativeStock error filters"
```

---

## Task 5: Wire transformer into SubmitManufactureHandler and ManufactureOrderApplicationService

The transformer must be invoked where exceptions are caught and before the message is stored as a note in the DB. The key changes:

1. `SubmitManufactureResponse` gets a new `UserMessage` property (the transformed, user-facing string)
2. `SubmitManufactureHandler` injects `IManufactureErrorTransformer`, transforms the exception in the catch block, and sets `UserMessage`
3. `ManufactureOrderApplicationService` uses `UserMessage` (not `FullError()`) when building the note that goes to the DB

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs`

- [ ] **Step 1: Write failing tests for SubmitManufactureHandler**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class SubmitManufactureHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock = new();
    private readonly Mock<IManufactureClient> _clientMock = new();
    private readonly Mock<IManufactureErrorTransformer> _transformerMock = new();
    private readonly Mock<ILogger<SubmitManufactureHandler>> _loggerMock = new();
    private readonly SubmitManufactureHandler _handler;

    public SubmitManufactureHandlerTests()
    {
        _handler = new SubmitManufactureHandler(
            _repositoryMock.Object,
            _clientMock.Object,
            _transformerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenClientSucceeds_ReturnsSuccessResponse()
    {
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("MAN-001");

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ManufactureId.Should().Be("MAN-001");
        result.UserMessage.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenClientThrows_SetsUserMessageFromTransformer()
    {
        var ex = new InvalidOperationException("Flexi raw error");
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);
        _transformerMock
            .Setup(t => t.Transform(ex))
            .Returns("Nedostatek meziproduktu 'XYZ' na skladu POLOTOVARY.");

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.UserMessage.Should().Be("Nedostatek meziproduktu 'XYZ' na skladu POLOTOVARY.");
    }

    [Fact]
    public async Task Handle_WhenClientThrows_LogsOriginalException()
    {
        var ex = new InvalidOperationException("Flexi raw error");
        _clientMock
            .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);
        _transformerMock.Setup(t => t.Transform(It.IsAny<Exception>())).Returns("any message");

        await _handler.Handle(BuildRequest(), CancellationToken.None);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static SubmitManufactureRequest BuildRequest() => new()
    {
        ManufactureOrderNumber = "MO-001",
        ManufactureInternalNumber = "INT-001",
        ManufactureType = ErpManufactureType.SemiProduct,
        Date = DateTime.UtcNow,
        CreatedBy = "test@anela.cz",
        Items = new List<SubmitManufactureRequestItem>
        {
            new() { ProductCode = "PROD001", Name = "Test Product", Amount = 100 }
        }
    };
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests --filter "SubmitManufactureHandlerTests" --no-build 2>&1 | tail -5
```

Expected: build error — `UserMessage` property and `IManufactureErrorTransformer` constructor parameter do not exist.

- [ ] **Step 3: Add UserMessage to SubmitManufactureResponse**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureResponse.cs` and add the `UserMessage` property:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

public class SubmitManufactureResponse : BaseResponse
{
    public string? ManufactureId { get; set; }
    public string? UserMessage { get; set; }

    public SubmitManufactureResponse() : base() { }

    public SubmitManufactureResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }

    public SubmitManufactureResponse(Exception exception) : base(exception)
    {
    }
}
```

- [ ] **Step 4: Update SubmitManufactureHandler to inject and use the transformer**

Replace the full content of `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

public class SubmitManufactureHandler : IRequestHandler<SubmitManufactureRequest, SubmitManufactureResponse>
{
    private readonly IManufactureOrderRepository _manufactureOrderRepository;
    private readonly IManufactureClient _manufactureClient;
    private readonly IManufactureErrorTransformer _errorTransformer;
    private readonly ILogger<SubmitManufactureHandler> _logger;

    public SubmitManufactureHandler(
        IManufactureOrderRepository manufactureOrderRepository,
        IManufactureClient manufactureClient,
        IManufactureErrorTransformer errorTransformer,
        ILogger<SubmitManufactureHandler> logger)
    {
        _manufactureOrderRepository = manufactureOrderRepository;
        _manufactureClient = manufactureClient;
        _errorTransformer = errorTransformer;
        _logger = logger;
    }

    public async Task<SubmitManufactureResponse> Handle(
        SubmitManufactureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clientRequest = new SubmitManufactureClientRequest
            {
                ManufactureOrderCode = request.ManufactureOrderNumber,
                ManufactureInternalNumber = request.ManufactureInternalNumber,
                Date = request.Date,
                CreatedBy = request.CreatedBy,
                ManufactureType = request.ManufactureType,
                Items = request.Items.Select(item => new SubmitManufactureClientItem
                {
                    ProductCode = item.ProductCode,
                    Amount = item.Amount,
                    ProductName = item.Name,
                }).ToList(),
                LotNumber = request.LotNumber,
                ExpirationDate = request.ExpirationDate,
            };

            var manufactureId = await _manufactureClient.SubmitManufactureAsync(clientRequest, cancellationToken);

            _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {ManufactureOrderId}",
                manufactureId, request.ManufactureOrderNumber);

            return new SubmitManufactureResponse
            {
                ManufactureId = manufactureId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating manufacture for order {ManufactureOrderId}", request.ManufactureOrderNumber);
            return new SubmitManufactureResponse(ex)
            {
                UserMessage = _errorTransformer.Transform(ex)
            };
        }
    }
}
```

- [ ] **Step 5: Update ManufactureOrderApplicationService to use UserMessage for DB notes**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`, find and replace the two places where `submitManufactureResult.FullError()` is used for note storage:

**In `ConfirmSemiProductManufactureAsync` (line ~63)**, replace:
```csharp
submitManufactureResult.Success ? $"Vytvořena vydaná objednávka meziproduktu {submitManufactureResult.ManufactureId}" : submitManufactureResult.FullError(),
```
with:
```csharp
submitManufactureResult.Success ? $"Vytvořena vydaná objednávka meziproduktu {submitManufactureResult.ManufactureId}" : submitManufactureResult.UserMessage!,
```

**In `ConfirmProductCompletionAsync` (line ~115)**, replace:
```csharp
orderNote = submitManufactureResult.FullError();
```
with:
```csharp
orderNote = submitManufactureResult.UserMessage;
```

- [ ] **Step 6: Build and run handler tests**

```bash
cd backend
dotnet build && dotnet test test/Anela.Heblo.Tests --filter "SubmitManufactureHandlerTests" -v minimal
```

Expected: 3 tests passing.

- [ ] **Step 7: Run full test suite to check for regressions**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests -v minimal 2>&1 | tail -20
```

Expected: all tests passing (no regressions from the changed constructor signature).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureResponse.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs
git commit -m "feat(manufacture): wire error transformer into SubmitManufactureHandler and store user message in notes"
```

---

## Final verification

- [ ] **Run all manufacture-related tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests --filter "Manufacture" -v minimal 2>&1 | tail -20
```

Expected: all passing.

- [ ] **Run full backend build**

```bash
cd backend
dotnet build
```

Expected: zero errors, zero warnings from new code.

- [ ] **Run dotnet format**

```bash
cd backend
dotnet format --verify-no-changes
```

Fix any formatting issues before the final commit.
