# MCP SDK Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Migrate MCP server from non-functional Microsoft.Extensions.AI to official ModelContextProtocol.AspNetCore SDK with fully operational /mcp endpoint.

**Architecture:** Update existing tool classes with working SDK attributes ([McpServerToolType], [McpServerTool]), return JSON strings, use SDK's McpException. All MediatR handlers stay unchanged - zero business logic risk.

**Tech Stack:** .NET 8, ModelContextProtocol.AspNetCore, MediatR, xUnit, Moq, System.Text.Json

---

## Task 1: Package Migration

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj:36-38`

**Step 1: Remove Microsoft.Extensions.AI packages**

```bash
cd backend/src/Anela.Heblo.API
```

Edit `Anela.Heblo.API.csproj`, remove lines 37-38:
```xml
<!-- REMOVE THESE LINES -->
<PackageReference Include="Microsoft.Extensions.AI" Version="10.3.0" />
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.3.0" />
```

**Step 2: Add ModelContextProtocol.AspNetCore package**

Add after line 36 (after System.Text.Json):
```xml
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.0.0" />
```

**Step 3: Restore packages**

Run: `dotnet restore`

Expected: Package restore succeeds with new MCP SDK package

**Step 4: Verify build fails (expected - missing types)**

Run: `dotnet build`

Expected: Build FAILS with errors about missing McpServerToolType, McpServerTool attributes (this is correct - we'll fix in next tasks)

**Step 5: Commit package changes**

```bash
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
git commit -m "build: replace Microsoft.Extensions.AI with ModelContextProtocol.AspNetCore

Remove non-functional Microsoft.Extensions.AI packages and add official
MCP SDK. Build will fail until tool classes are updated with SDK attributes.

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 2: Update CatalogMcpTools (Test-First)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs`
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs`

### Step 1: Read current test file

Run: `cat backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs | head -100`

**Step 2: Update test to expect JSON string response**

Update first test in `CatalogMcpToolsTests.cs`:

```csharp
[Fact]
public async Task GetCatalogList_ShouldReturnSerializedJson()
{
    // Arrange
    var expectedResponse = new GetCatalogListResponse
    {
        Items = new List<CatalogItemDto>(),
        TotalCount = 0,
        PageNumber = 1,
        PageSize = 50
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetCatalogListRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var jsonResult = await _tools.GetCatalogList();

    // Assert - Verify mediator called
    _mediatorMock.Verify(m => m.Send(
        It.IsAny<GetCatalogListRequest>(),
        default), Times.Once);

    // Assert - Verify JSON serialization
    var deserialized = JsonSerializer.Deserialize<GetCatalogListResponse>(jsonResult);
    Assert.NotNull(deserialized);
    Assert.Equal(0, deserialized.TotalCount);
}
```

Add System.Text.Json using at top:
```csharp
using System.Text.Json;
```

**Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests.GetCatalogList_ShouldReturnSerializedJson"`

Expected: FAIL with "Cannot convert string to GetCatalogListResponse" (current code returns typed object)

**Step 4: Update CatalogMcpTools class with SDK attributes**

In `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs`:

Add using statements at top:
```csharp
using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol;
```

Add class attribute:
```csharp
[McpServerToolType]
public class CatalogMcpTools
```

**Step 5: Update GetCatalogList method**

Replace the existing GetCatalogList method:

```csharp
[McpServerTool]
public async Task<string> GetCatalogList(
    [Description("Search term to filter by product name or code")]
    string? searchTerm = null,
    [Description("Filter by product types (Product, Material, SemiProduct)")]
    ProductType[]? productTypes = null,
    [Description("Page number for pagination (default: 1)")]
    int pageNumber = 1,
    [Description("Page size for pagination (default: 50, max: 100)")]
    int pageSize = 50)
{
    var request = new GetCatalogListRequest
    {
        SearchTerm = searchTerm,
        ProductTypes = productTypes,
        PageNumber = pageNumber,
        PageSize = pageSize
    };

    var response = await _mediator.Send(request);
    return JsonSerializer.Serialize(response);
}
```

**Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests.GetCatalogList_ShouldReturnSerializedJson"`

Expected: PASS

**Step 7: Update remaining CatalogMcpTools methods (6 more)**

Update GetCatalogDetail:
```csharp
[McpServerTool]
public async Task<string> GetCatalogDetail(
    [Description("Product code (e.g., 'AKL001', 'SLU000001')")]
    string productCode,
    [Description("Number of months to look back for transaction history (default: 13)")]
    int monthsBack = 13)
{
    var request = new GetCatalogDetailRequest
    {
        ProductCode = productCode,
        MonthsBack = monthsBack
    };

    var response = await _mediator.Send(request);

    if (!response.Success)
    {
        throw new McpException(
            response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
            response.FullError()
        );
    }

    return JsonSerializer.Serialize(response);
}
```

Update GetProductComposition:
```csharp
[McpServerTool]
public async Task<string> GetProductComposition(
    [Description("Product code (e.g., 'AKL001')")]
    string productCode)
{
    var request = new GetProductCompositionRequest { ProductCode = productCode };
    var response = await _mediator.Send(request);

    if (!response.Success)
    {
        throw new McpException(
            response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
            response.FullError()
        );
    }

    return JsonSerializer.Serialize(response);
}
```

Update GetMaterialsForPurchase:
```csharp
[McpServerTool]
public async Task<string> GetMaterialsForPurchase()
{
    var request = new GetMaterialsForPurchaseRequest();
    var response = await _mediator.Send(request);
    return JsonSerializer.Serialize(response);
}
```

Update GetAutocomplete:
```csharp
[McpServerTool]
public async Task<string> GetAutocomplete(
    [Description("Search term to match against product name or code")]
    string? searchTerm = null,
    [Description("Maximum number of results to return (default: 20)")]
    int limit = 20,
    [Description("Filter by product types")]
    ProductType[]? productTypes = null)
{
    var request = new GetCatalogListRequest
    {
        SearchTerm = searchTerm,
        PageSize = limit,
        PageNumber = 1,
        ProductTypes = productTypes
    };

    var response = await _mediator.Send(request);
    return JsonSerializer.Serialize(response);
}
```

Update GetProductUsage:
```csharp
[McpServerTool]
public async Task<string> GetProductUsage(
    [Description("Product code (e.g., 'AKL001')")]
    string productCode)
{
    var request = new GetProductUsageRequest { ProductCode = productCode };
    var response = await _mediator.Send(request);

    if (!response.Success)
    {
        throw new McpException(
            response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
            response.FullError()
        );
    }

    return JsonSerializer.Serialize(response);
}
```

Update GetWarehouseStatistics:
```csharp
[McpServerTool]
public async Task<string> GetWarehouseStatistics()
{
    var request = new GetWarehouseStatisticsRequest();
    var response = await _mediator.Send(request);
    return JsonSerializer.Serialize(response);
}
```

**Step 8: Remove all TODO comments**

Delete all commented-out TODO lines that reference Microsoft.Extensions.AI attributes.

**Step 9: Update CatalogMcpToolsTests for other methods**

Update GetCatalogDetail_ShouldThrowMcpException test:

```csharp
[Fact]
public async Task GetCatalogDetail_ShouldThrowMcpException_WhenProductNotFound()
{
    // Arrange
    var errorResponse = new GetCatalogDetailResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.ProductNotFound,
        Params = new Dictionary<string, string> { { "ProductCode", "XYZ123" } }
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
        .ReturnsAsync(errorResponse);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<McpException>(
        () => _tools.GetCatalogDetail("XYZ123")
    );

    Assert.Contains("ProductNotFound", exception.Message);
}
```

Add using for McpException:
```csharp
using ModelContextProtocol;
```

Remove tests that are no longer needed (protocol-level tests):
- Remove any tests about attribute discovery
- Remove duplicate pagination tests
- Keep: parameter mapping, error handling, JSON serialization tests

Expected final test count: ~4-5 tests for CatalogMcpTools

**Step 10: Run all Catalog tests**

Run: `dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests"`

Expected: All tests PASS

**Step 11: Commit CatalogMcpTools changes**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs
git add backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs
git commit -m "feat: update CatalogMcpTools to use official MCP SDK

- Add [McpServerToolType] and [McpServerTool] attributes
- Change return types to Task<string> with JSON serialization
- Replace McpToolException with McpException
- Update tests to verify JSON serialization
- Remove commented-out TODO code

All 7 Catalog tools now functional with official SDK.

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 3: Update ManufactureOrderMcpTools

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs`
- Modify: `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs`

**Step 1: Add SDK using statements**

In `ManufactureOrderMcpTools.cs`:
```csharp
using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol;
```

**Step 2: Add class attribute**

```csharp
[McpServerToolType]
public class ManufactureOrderMcpTools
```

**Step 3: Update GetManufactureOrders method**

```csharp
[McpServerTool]
public async Task<string> GetManufactureOrders(
    [Description("Filter by manufacture order status")]
    string? status = null,
    [Description("Page number for pagination (default: 1)")]
    int pageNumber = 1,
    [Description("Page size for pagination (default: 50)")]
    int pageSize = 50)
{
    var request = new GetManufactureOrdersRequest
    {
        Status = status,
        PageNumber = pageNumber,
        PageSize = pageSize
    };

    var response = await _mediator.Send(request);
    return JsonSerializer.Serialize(response);
}
```

**Step 4: Update GetManufactureOrder method**

```csharp
[McpServerTool]
public async Task<string> GetManufactureOrder(
    [Description("Manufacture order ID")]
    int orderId)
{
    var request = new GetManufactureOrderRequest { OrderId = orderId };
    var response = await _mediator.Send(request);

    if (!response.Success)
    {
        throw new McpException(
            response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
            response.FullError()
        );
    }

    return JsonSerializer.Serialize(response);
}
```

**Step 5: Update GetCalendarView method**

```csharp
[McpServerTool]
public async Task<string> GetCalendarView(
    [Description("Start date for calendar view (ISO 8601 format)")]
    string startDate,
    [Description("End date for calendar view (ISO 8601 format)")]
    string endDate)
{
    var request = new GetCalendarViewRequest
    {
        StartDate = DateTime.Parse(startDate),
        EndDate = DateTime.Parse(endDate)
    };

    var response = await _mediator.Send(request);
    return JsonSerializer.Serialize(response);
}
```

**Step 6: Update GetResponsiblePersons method**

```csharp
[McpServerTool]
public async Task<string> GetResponsiblePersons()
{
    var request = new GetResponsiblePersonsRequest();
    var response = await _mediator.Send(request);
    return JsonSerializer.Serialize(response);
}
```

**Step 7: Remove TODO comments**

Delete all commented-out code and TODO lines.

**Step 8: Update tests**

In `ManufactureOrderMcpToolsTests.cs`, update tests to expect JSON strings:

```csharp
using System.Text.Json;
using ModelContextProtocol;

[Fact]
public async Task GetManufactureOrders_ShouldReturnSerializedJson()
{
    // Arrange
    var expectedResponse = new GetManufactureOrdersResponse
    {
        Orders = new List<ManufactureOrderDto>(),
        TotalCount = 0
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetManufactureOrdersRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var jsonResult = await _tools.GetManufactureOrders();

    // Assert
    var deserialized = JsonSerializer.Deserialize<GetManufactureOrdersResponse>(jsonResult);
    Assert.NotNull(deserialized);
    Assert.Equal(0, deserialized.TotalCount);
}
```

Expected final test count: ~3-4 tests for ManufactureOrderMcpTools

**Step 9: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~ManufactureOrderMcpToolsTests"`

Expected: All tests PASS

**Step 10: Commit changes**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs
git add backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs
git commit -m "feat: update ManufactureOrderMcpTools to use official MCP SDK

- Add [McpServerToolType] and [McpServerTool] attributes
- Change return types to Task<string> with JSON serialization
- Replace McpToolException with McpException
- Update tests to verify JSON serialization

All 4 ManufactureOrder tools now functional with official SDK.

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 4: Update ManufactureBatchMcpTools

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs`
- Modify: `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureBatchMcpToolsTests.cs`

**Step 1: Add SDK using statements**

In `ManufactureBatchMcpTools.cs`:
```csharp
using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol;
```

**Step 2: Add class attribute**

```csharp
[McpServerToolType]
public class ManufactureBatchMcpTools
```

**Step 3: Update GetBatchTemplate method**

```csharp
[McpServerTool]
public async Task<string> GetBatchTemplate(
    [Description("Product code to get batch template for")]
    string productCode)
{
    var request = new GetBatchTemplateRequest { ProductCode = productCode };
    var response = await _mediator.Send(request);

    if (!response.Success)
    {
        throw new McpException(
            response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
            response.FullError()
        );
    }

    return JsonSerializer.Serialize(response);
}
```

**Step 4: Update CalculateBatchBySize method**

```csharp
[McpServerTool]
public async Task<string> CalculateBatchBySize(
    [Description("Product code to calculate batch for")]
    string productCode,
    [Description("Desired batch size in grams")]
    decimal size)
{
    var request = new CalculateBatchBySizeRequest
    {
        ProductCode = productCode,
        Size = size
    };

    var response = await _mediator.Send(request);

    if (!response.Success)
    {
        throw new McpException(
            response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
            response.FullError()
        );
    }

    return JsonSerializer.Serialize(response);
}
```

**Step 5: Update CalculateBatchByIngredient method**

```csharp
[McpServerTool]
public async Task<string> CalculateBatchByIngredient(
    [Description("Product code to calculate batch for")]
    string productCode,
    [Description("Ingredient product code")]
    string ingredientCode,
    [Description("Desired quantity of ingredient in grams")]
    decimal quantity)
{
    var request = new CalculateBatchByIngredientRequest
    {
        ProductCode = productCode,
        IngredientCode = ingredientCode,
        Quantity = quantity
    };

    var response = await _mediator.Send(request);

    if (!response.Success)
    {
        throw new McpException(
            response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
            response.FullError()
        );
    }

    return JsonSerializer.Serialize(response);
}
```

**Step 6: Update CalculateBatchPlan method**

```csharp
[McpServerTool]
public async Task<string> CalculateBatchPlan(
    [Description("Array of product codes and quantities")]
    BatchPlanItem[] items)
{
    var request = new CalculateBatchPlanRequest { Items = items };
    var response = await _mediator.Send(request);

    if (!response.Success)
    {
        throw new McpException(
            response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
            response.FullError()
        );
    }

    return JsonSerializer.Serialize(response);
}
```

**Step 7: Remove TODO comments**

Delete all commented-out code and TODO lines.

**Step 8: Update tests**

In `ManufactureBatchMcpToolsTests.cs`:

```csharp
using System.Text.Json;
using ModelContextProtocol;

[Fact]
public async Task GetBatchTemplate_ShouldReturnSerializedJson()
{
    // Arrange
    var expectedResponse = new GetBatchTemplateResponse
    {
        Success = true,
        Template = new BatchTemplateDto()
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetBatchTemplateRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var jsonResult = await _tools.GetBatchTemplate("AKL001");

    // Assert
    var deserialized = JsonSerializer.Deserialize<GetBatchTemplateResponse>(jsonResult);
    Assert.NotNull(deserialized);
    Assert.True(deserialized.Success);
}
```

Expected final test count: ~3-4 tests for ManufactureBatchMcpTools

**Step 9: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~ManufactureBatchMcpToolsTests"`

Expected: All tests PASS

**Step 10: Commit changes**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs
git add backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureBatchMcpToolsTests.cs
git commit -m "feat: update ManufactureBatchMcpTools to use official MCP SDK

- Add [McpServerToolType] and [McpServerTool] attributes
- Change return types to Task<string> with JSON serialization
- Replace McpToolException with McpException
- Update tests to verify JSON serialization

All 4 ManufactureBatch tools now functional with official SDK.

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 5: Delete McpToolException

**Files:**
- Delete: `backend/src/Anela.Heblo.API/MCP/McpToolException.cs`

**Step 1: Verify no references exist**

Run: `grep -r "McpToolException" backend/src/Anela.Heblo.API/`

Expected: No results (we replaced all with McpException in previous tasks)

**Step 2: Delete the file**

```bash
rm backend/src/Anela.Heblo.API/MCP/McpToolException.cs
```

**Step 3: Verify build succeeds**

Run: `dotnet build backend/src/Anela.Heblo.API/`

Expected: Build SUCCEEDS

**Step 4: Commit deletion**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpToolException.cs
git commit -m "refactor: remove McpToolException in favor of SDK's McpException

Custom exception class no longer needed - using official SDK's McpException.

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 6: Update McpModule Registration

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/McpModule.cs`

**Step 1: Read current file**

Run: `cat backend/src/Anela.Heblo.API/MCP/McpModule.cs`

**Step 2: Replace entire AddMcpServices method**

Replace the method content:

```csharp
using Anela.Heblo.API.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;

namespace Anela.Heblo.API.MCP;

/// <summary>
/// Dependency injection module for MCP server configuration.
/// Registers MCP tool classes and configures MCP server with tool discovery.
/// </summary>
public static class McpModule
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        // Register MCP server with HTTP transport and tool discovery
        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<CatalogMcpTools>()
            .WithTools<ManufactureOrderMcpTools>()
            .WithTools<ManufactureBatchMcpTools>();

        return services;
    }
}
```

**Step 3: Remove old transient registrations and commented code**

Delete:
- `services.AddTransient<CatalogMcpTools>();` lines
- All commented-out TODO code
- Old AddMcpServer comments

**Step 4: Verify file compiles**

Run: `dotnet build backend/src/Anela.Heblo.API/`

Expected: Build SUCCEEDS

**Step 5: Commit changes**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpModule.cs
git commit -m "feat: update McpModule to use official SDK registration

- Replace commented-out code with AddMcpServer().WithHttpTransport()
- Register all tool classes with WithTools<T>()
- Remove manual transient service registrations

MCP server now properly configured with official SDK.

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 7: Update Program.cs for MCP Endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Program.cs`

**Step 1: Add MCP service registration**

Find the service registration section (around line 45), add after `AddCrossCuttingServices()`:

```csharp
// MCP Server
builder.Services.AddMcpServices();
```

Add using statement at top:
```csharp
using Anela.Heblo.API.MCP;
```

**Step 2: Add MCP endpoint mapping**

Find the pipeline configuration section (after `app.ConfigureApplicationPipeline();` around line 81), add before `app.Run()`:

```csharp
// Map MCP endpoint
app.MapMcp();
```

**Step 3: Verify application compiles**

Run: `dotnet build backend/src/Anela.Heblo.API/`

Expected: Build SUCCEEDS

**Step 4: Run all MCP tests**

Run: `dotnet test --filter "FullyQualifiedName~MCP"`

Expected: All MCP tests PASS (~10-12 tests)

**Step 5: Commit changes**

```bash
git add backend/src/Anela.Heblo.API/Program.cs
git commit -m "feat: activate MCP endpoint in application startup

- Add AddMcpServices() to service registration
- Add MapMcp() to pipeline configuration
- MCP server now active at /mcp endpoint

The /mcp endpoint is now fully functional and ready for MCP clients.

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 8: Integration Testing

**Files:**
- None (manual testing)

**Step 1: Start application locally**

Run: `dotnet run --project backend/src/Anela.Heblo.API`

Expected: Application starts on https://localhost:5001

**Step 2: Test MCP endpoint availability**

In another terminal:
```bash
curl -k https://localhost:5001/mcp -H "Accept: text/event-stream"
```

Expected: SSE connection established (may require authentication token)

**Step 3: Run full test suite**

Run: `dotnet test backend/Anela.Heblo.sln`

Expected: All tests PASS (existing handler tests + new MCP tests)

**Step 4: Verify no regressions**

Check that:
- [ ] All MCP tests pass (~10-12 tests)
- [ ] All handler tests pass (unchanged)
- [ ] Application builds without warnings
- [ ] MCP endpoint responds to requests

**Step 5: Document test results**

Create integration test log:
```bash
echo "Integration Test Results - $(date)" > integration-test-log.txt
echo "MCP Tests: $(dotnet test --filter 'FullyQualifiedName~MCP' --nologo --verbosity quiet | grep 'Passed!')" >> integration-test-log.txt
echo "All Tests: $(dotnet test backend/Anela.Heblo.sln --nologo --verbosity quiet | grep 'Passed!')" >> integration-test-log.txt
```

---

## Task 9: Update Documentation

**Files:**
- Modify: `docs/CLAUDE.md`
- Modify: `docs/testing/mcp-testing.md`

**Step 1: Update CLAUDE.md MCP Server section**

In `docs/CLAUDE.md`, update the MCP Server section (around line 45):

Change status from:
```markdown
**Status:** ✅ Active - MCP server running on /mcp endpoint
```

To:
```markdown
**Status:** ✅ Active - MCP server running on /mcp endpoint using official ModelContextProtocol.AspNetCore SDK
```

Add SDK reference after Configuration section:
```markdown
**SDK:** Official MCP C# SDK from https://github.com/modelcontextprotocol/csharp-sdk

**Tool Pattern:**
- Tools decorated with `[McpServerToolType]` (class) and `[McpServerTool]` (methods)
- Parameters use `[Description]` attribute for documentation
- Methods return `Task<string>` with JSON-serialized responses
- Errors thrown as `McpException` (handled by SDK)
```

**Step 2: Update mcp-testing.md**

In `docs/testing/mcp-testing.md`, update the overview section:

```markdown
## Overview

**MCP Tools** enable Claude to interact with the Anela Heblo backend through standardized tool interfaces using the official ModelContextProtocol.AspNetCore SDK.

**Key characteristics:**
- Tests validate MCP tool behavior (parameter mapping, JSON serialization, error handling)
- Uses **Moq** to mock `IMediator` dependencies
- Verifies JSON serialization of responses
- Tests both success and error scenarios
- Uses official SDK attributes: `[McpServerToolType]`, `[McpServerTool]`, `[Description]`
```

Update test count:
```markdown
**Total Tests:** ~10-12 (simplified from original 16)
```

**Step 3: Commit documentation updates**

```bash
git add docs/CLAUDE.md docs/testing/mcp-testing.md
git commit -m "docs: update MCP documentation for official SDK

- Update CLAUDE.md with SDK reference and tool pattern
- Update mcp-testing.md with new test patterns
- Clarify that MCP server is now using official SDK
- Update test count (~10-12 tests)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 10: Final Verification

**Step 1: Run code formatting**

Run: `dotnet format backend/Anela.Heblo.sln`

Expected: All code formatted according to project standards

**Step 2: Run full build**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: Build SUCCEEDS with no warnings

**Step 3: Run full test suite**

Run: `dotnet test backend/Anela.Heblo.sln`

Expected: All tests PASS

**Step 4: Verify git status**

Run: `git status`

Expected: All changes committed, working directory clean

**Step 5: Review commit history**

Run: `git log --oneline -10`

Expected: See all 10 commits for this migration

**Step 6: Commit any formatting changes**

If dotnet format made changes:
```bash
git add -A
git commit -m "style: apply code formatting to MCP files

Auto-format MCP implementation files per project standards.

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Success Criteria

After completing all tasks:

- [x] All 15 MCP tools use official SDK attributes
- [x] All tools return JSON strings (`Task<string>`)
- [x] All tool parameters use `[Description]` attributes
- [x] Error handling uses `McpException` from SDK
- [x] ~10-12 tests pass (parameter mapping + JSON serialization + error handling)
- [x] All existing handler tests still pass (business logic unchanged)
- [x] Application builds without errors or warnings
- [x] MCP endpoint active at `/mcp`
- [x] Code formatted per project standards
- [x] Documentation updated

## Next Steps

After implementation:

1. **Local Testing**: Test with Claude Desktop locally
2. **Staging Deployment**: Deploy to staging environment
3. **Staging Validation**: Test with Claude Desktop against staging
4. **Production Deployment**: Deploy to production after staging validation

## Reference Files

**Tool Classes:**
- `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs`
- `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs`
- `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs`

**Test Classes:**
- `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs`
- `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs`
- `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureBatchMcpToolsTests.cs`

**Configuration:**
- `backend/src/Anela.Heblo.API/MCP/McpModule.cs`
- `backend/src/Anela.Heblo.API/Program.cs`
- `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`

**Documentation:**
- Design: `docs/plans/2026-02-27-mcp-sdk-migration-design.md`
- CLAUDE.md: MCP Server section
- Testing: `docs/testing/mcp-testing.md`
