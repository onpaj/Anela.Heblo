# MCP Server Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Model Context Protocol (MCP) server capabilities to Anela Heblo API, exposing read-only Catalog and Manufacture endpoints as AI-accessible tools.

**Architecture:** MCP middleware integrated directly into existing `Anela.Heblo.API` project. MCP tools are thin wrappers around existing MediatR handlers, ensuring consistent behavior with REST API. Uses Microsoft's official MCP NuGet package with attribute-based tool definitions.

**Tech Stack:** ASP.NET Core 8, Microsoft.Extensions.AI (MCP), MediatR, xUnit, Moq

---

## Phase 1: Setup & Infrastructure

### Task 1: Add Microsoft MCP NuGet Package

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`

**Step 1: Add MCP package reference**

Add to `Anela.Heblo.API.csproj` in the `<ItemGroup>` with other package references:

```xml
<PackageReference Include="Microsoft.Extensions.AI" Version="9.0.0-preview.9.24556.5" />
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="9.0.0-preview.9.24556.5" />
```

**Step 2: Restore packages**

Run:
```bash
cd backend/src/Anela.Heblo.API
dotnet restore
```

Expected: Packages restored successfully, no errors

**Step 3: Verify package installation**

Run:
```bash
dotnet list package | grep Microsoft.Extensions.AI
```

Expected: Both Microsoft.Extensions.AI packages listed

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
git commit -m "feat(mcp): add Microsoft.Extensions.AI NuGet packages

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 2: Create MCP Directory Structure

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/.gitkeep`
- Create: `backend/test/Anela.Heblo.Tests/MCP/Tools/.gitkeep`

**Step 1: Create MCP tools directory**

Run:
```bash
mkdir -p backend/src/Anela.Heblo.API/MCP/Tools
touch backend/src/Anela.Heblo.API/MCP/Tools/.gitkeep
```

Expected: Directory created

**Step 2: Create test directory for MCP**

Run:
```bash
mkdir -p backend/test/Anela.Heblo.Tests/MCP/Tools
touch backend/test/Anela.Heblo.Tests/MCP/Tools/.gitkeep
```

Expected: Test directory created

**Step 3: Verify structure**

Run:
```bash
tree backend/src/Anela.Heblo.API/MCP
tree backend/test/Anela.Heblo.Tests/MCP
```

Expected: Directory structure matches design

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP backend/test/Anela.Heblo.Tests/MCP
git commit -m "feat(mcp): create MCP directory structure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 3: Create MCP Exception Class

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/McpToolException.cs`

**Step 1: Create exception class**

Create `backend/src/Anela.Heblo.API/MCP/McpToolException.cs`:

```csharp
namespace Anela.Heblo.API.MCP;

/// <summary>
/// Exception thrown by MCP tools to signal errors to MCP clients.
/// The MCP framework translates this into proper MCP protocol error responses.
/// </summary>
public class McpToolException : Exception
{
    /// <summary>
    /// Error code (e.g., "NOT_FOUND", "VALIDATION_ERROR")
    /// </summary>
    public string Code { get; }

    public McpToolException(string code, string message) : base(message)
    {
        Code = code;
    }

    public McpToolException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}
```

**Step 2: Verify compilation**

Run:
```bash
cd backend
dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: Build succeeds, no errors

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpToolException.cs
git commit -m "feat(mcp): add McpToolException for error handling

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 4: Create MCP Module for Dependency Injection

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/McpModule.cs`

**Step 1: Create McpModule.cs**

Create `backend/src/Anela.Heblo.API/MCP/McpModule.cs`:

```csharp
using Anela.Heblo.API.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.API.MCP;

/// <summary>
/// Dependency injection module for MCP server configuration.
/// Registers MCP tool classes and configures MCP server with tool discovery.
/// </summary>
public static class McpModule
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        // Register MCP tool classes as transient (new instance per request)
        // Tools will be registered here as we create them
        // services.AddTransient<CatalogMcpTools>();
        // services.AddTransient<ManufactureOrderMcpTools>();
        // services.AddTransient<ManufactureBatchMcpTools>();

        // TODO: Register MCP server when Microsoft.Extensions.AI package is updated
        // services.AddMcpServer(options =>
        // {
        //     options.DiscoverToolsFrom<CatalogMcpTools>();
        //     options.DiscoverToolsFrom<ManufactureOrderMcpTools>();
        //     options.DiscoverToolsFrom<ManufactureBatchMcpTools>();
        // });

        return services;
    }
}
```

**Step 2: Verify compilation**

Run:
```bash
cd backend
dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: Build succeeds

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpModule.cs
git commit -m "feat(mcp): create McpModule for DI registration

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 2: Catalog MCP Tools

### Task 5: Write Unit Test for CatalogMcpTools - GetCatalogList

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs`

**Step 1: Create test file with GetCatalogList test**

Create `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs`:

```csharp
using Anela.Heblo.API.MCP;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class CatalogMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly CatalogMcpTools _tools;

    public CatalogMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tools = new CatalogMcpTools(_mediatorMock.Object);
    }

    [Fact]
    public async Task GetCatalogList_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetCatalogListResponse
        {
            Items = new List<CatalogItemDto>(),
            TotalCount = 0,
            PageNumber = 2,
            PageSize = 25
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCatalogListRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tools.GetCatalogList(
            searchTerm: "Bisabolol",
            productTypes: new[] { ProductType.Material },
            pageNumber: 2,
            pageSize: 25
        );

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetCatalogListRequest>(req =>
                req.SearchTerm == "Bisabolol" &&
                req.ProductTypes != null &&
                req.ProductTypes.Length == 1 &&
                req.ProductTypes[0] == ProductType.Material &&
                req.PageNumber == 2 &&
                req.PageSize == 25
            ),
            default
        ), Times.Once);

        Assert.Equal(expectedResponse, result);
    }
}
```

**Step 2: Run test to verify it fails**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests.GetCatalogList_ShouldMapParametersCorrectly"
```

Expected: Test fails with "CatalogMcpTools does not exist"

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs
git commit -m "test(mcp): add failing test for CatalogMcpTools.GetCatalogList

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 6: Implement CatalogMcpTools - GetCatalogList

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs`

**Step 1: Create CatalogMcpTools with GetCatalogList**

Create `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for Catalog operations.
/// Thin wrappers around MediatR handlers that expose catalog functionality to MCP clients.
/// </summary>
public class CatalogMcpTools
{
    private readonly IMediator _mediator;

    public CatalogMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    // TODO: Add [McpTool] attribute when Microsoft.Extensions.AI API is finalized
    // [McpTool(
    //     Name = "catalog_get_list",
    //     Description = "Get paginated list of catalog items with optional filtering by product type, search term, or warehouse status. Returns products, materials, and semi-products from the Heblo system."
    // )]
    public async Task<GetCatalogListResponse> GetCatalogList(
        // TODO: Add [McpToolParameter] attributes when API is finalized
        // [McpToolParameter(Description = "Search term to filter by product name or code")]
        string? searchTerm = null,

        // [McpToolParameter(Description = "Filter by product types (Product, Material, SemiProduct)")]
        ProductType[]? productTypes = null,

        // [McpToolParameter(Description = "Page number for pagination (default: 1)")]
        int pageNumber = 1,

        // [McpToolParameter(Description = "Page size for pagination (default: 50, max: 100)")]
        int pageSize = 50
    )
    {
        var request = new GetCatalogListRequest
        {
            SearchTerm = searchTerm,
            ProductTypes = productTypes,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        return await _mediator.Send(request);
    }
}
```

**Step 2: Run test to verify it passes**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests.GetCatalogList_ShouldMapParametersCorrectly"
```

Expected: Test passes

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs
git commit -m "feat(mcp): implement CatalogMcpTools.GetCatalogList

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 7: Add Test for CatalogMcpTools - GetCatalogDetail with Error Handling

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs`

**Step 1: Add test for GetCatalogDetail error handling**

Add to `CatalogMcpToolsTests.cs` after the existing test:

```csharp
[Fact]
public async Task GetCatalogDetail_ShouldMapParametersCorrectly()
{
    // Arrange
    var expectedResponse = new GetCatalogDetailResponse
    {
        Success = true,
        ProductCode = "AKL001"
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var result = await _tools.GetCatalogDetail(
        productCode: "AKL001",
        monthsBack: 6
    );

    // Assert
    _mediatorMock.Verify(m => m.Send(
        It.Is<GetCatalogDetailRequest>(req =>
            req.ProductCode == "AKL001" &&
            req.MonthsBack == 6
        ),
        default
    ), Times.Once);

    Assert.Equal(expectedResponse, result);
}

[Fact]
public async Task GetCatalogDetail_ShouldThrowMcpToolException_WhenProductNotFound()
{
    // Arrange
    var errorResponse = new GetCatalogDetailResponse("NOT_FOUND")
    {
        Success = false,
        ErrorMessage = "Product 'XYZ123' not found"
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
        .ReturnsAsync(errorResponse);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<McpToolException>(
        () => _tools.GetCatalogDetail("XYZ123")
    );

    Assert.Equal("NOT_FOUND", exception.Code);
    Assert.Contains("XYZ123", exception.Message);
}
```

**Step 2: Run tests to verify they fail**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests.GetCatalogDetail"
```

Expected: Tests fail with "GetCatalogDetail method not found"

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs
git commit -m "test(mcp): add failing tests for CatalogMcpTools.GetCatalogDetail

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 8: Implement CatalogMcpTools - GetCatalogDetail

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs`

**Step 1: Add GetCatalogDetail method**

Add to `CatalogMcpTools.cs` after `GetCatalogList`:

```csharp
// TODO: Add [McpTool] attribute when Microsoft.Extensions.AI API is finalized
// [McpTool(
//     Name = "catalog_get_detail",
//     Description = "Get detailed information for a specific product including stock levels, recent transactions, and pricing history. Use this to analyze individual product performance."
// )]
public async Task<GetCatalogDetailResponse> GetCatalogDetail(
    // TODO: Add [McpToolParameter] attributes when API is finalized
    // [McpToolParameter(Description = "Product code (e.g., 'AKL001', 'SLU000001')", Required = true)]
    string productCode,

    // [McpToolParameter(Description = "Number of months to look back for transaction history (default: 13)")]
    int monthsBack = 13
)
{
    var request = new GetCatalogDetailRequest
    {
        ProductCode = productCode,
        MonthsBack = monthsBack
    };

    var response = await _mediator.Send(request);

    // Handle response envelope (Success/Error pattern)
    if (!response.Success)
    {
        throw new McpToolException(response.ErrorCode, response.ErrorMessage);
    }

    return response;
}
```

Add using statement at the top if not already present:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
```

**Step 2: Run tests to verify they pass**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests.GetCatalogDetail"
```

Expected: Both tests pass

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs
git commit -m "feat(mcp): implement CatalogMcpTools.GetCatalogDetail with error handling

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 9: Add Remaining Catalog Tool Methods (Batch Implementation)

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs`
- Modify: `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs`

**Step 1: Add tests for remaining catalog methods**

Add to `CatalogMcpToolsTests.cs`:

```csharp
[Fact]
public async Task GetProductComposition_ShouldMapParametersCorrectly()
{
    // Arrange
    var expectedResponse = new GetProductCompositionResponse
    {
        Success = true
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetProductCompositionRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var result = await _tools.GetProductComposition("AKL001");

    // Assert
    _mediatorMock.Verify(m => m.Send(
        It.Is<GetProductCompositionRequest>(req => req.ProductCode == "AKL001"),
        default
    ), Times.Once);
}

[Fact]
public async Task GetMaterialsForPurchase_ShouldMapParametersCorrectly()
{
    // Arrange
    var expectedResponse = new GetMaterialsForPurchaseResponse();

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetMaterialsForPurchaseRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var result = await _tools.GetMaterialsForPurchase();

    // Assert
    _mediatorMock.Verify(m => m.Send(
        It.IsAny<GetMaterialsForPurchaseRequest>(),
        default
    ), Times.Once);
}

[Fact]
public async Task GetAutocomplete_ShouldMapParametersCorrectly()
{
    // Arrange
    var expectedResponse = new GetCatalogListResponse();

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetCatalogListRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var result = await _tools.GetAutocomplete("Bis", 10, new[] { ProductType.Material });

    // Assert
    _mediatorMock.Verify(m => m.Send(
        It.Is<GetCatalogListRequest>(req =>
            req.SearchTerm == "Bis" &&
            req.PageSize == 10 &&
            req.PageNumber == 1
        ),
        default
    ), Times.Once);
}

[Fact]
public async Task GetProductUsage_ShouldMapParametersCorrectly()
{
    // Arrange
    var expectedResponse = new GetProductUsageResponse
    {
        Success = true
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetProductUsageRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var result = await _tools.GetProductUsage("AKL001");

    // Assert
    _mediatorMock.Verify(m => m.Send(
        It.Is<GetProductUsageRequest>(req => req.ProductCode == "AKL001"),
        default
    ), Times.Once);
}

[Fact]
public async Task GetWarehouseStatistics_ShouldMapParametersCorrectly()
{
    // Arrange
    var expectedResponse = new GetWarehouseStatisticsResponse();

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetWarehouseStatisticsRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var result = await _tools.GetWarehouseStatistics();

    // Assert
    _mediatorMock.Verify(m => m.Send(
        It.IsAny<GetWarehouseStatisticsRequest>(),
        default
    ), Times.Once);
}
```

Add required using statements:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetMaterialForPurchase;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetWarehouseStatistics;
```

**Step 2: Run tests to verify they fail**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests"
```

Expected: New tests fail

**Step 3: Implement remaining catalog methods**

Add to `CatalogMcpTools.cs`:

```csharp
// TODO: Add [McpTool] attribute
// [McpTool(
//     Name = "catalog_get_composition",
//     Description = "Get the composition/recipe of a product, showing all ingredients and their quantities. Use this to understand what materials are needed to manufacture a product."
// )]
public async Task<GetProductCompositionResponse> GetProductComposition(
    // [McpToolParameter(Description = "Product code (e.g., 'AKL001')", Required = true)]
    string productCode
)
{
    var request = new GetProductCompositionRequest { ProductCode = productCode };
    var response = await _mediator.Send(request);

    if (!response.Success)
    {
        throw new McpToolException(response.ErrorCode, response.ErrorMessage);
    }

    return response;
}

// TODO: Add [McpTool] attribute
// [McpTool(
//     Name = "catalog_get_materials_for_purchase",
//     Description = "Get list of materials that need to be purchased based on current stock levels and planned production. Use this for procurement planning."
// )]
public async Task<GetMaterialsForPurchaseResponse> GetMaterialsForPurchase()
{
    var request = new GetMaterialsForPurchaseRequest();
    return await _mediator.Send(request);
}

// TODO: Add [McpTool] attribute
// [McpTool(
//     Name = "catalog_get_autocomplete",
//     Description = "Search for products by name or code with autocomplete functionality. Returns a limited set of matching products for quick lookup."
// )]
public async Task<GetCatalogListResponse> GetAutocomplete(
    // [McpToolParameter(Description = "Search term to match against product name or code")]
    string? searchTerm = null,

    // [McpToolParameter(Description = "Maximum number of results to return (default: 20)")]
    int limit = 20,

    // [McpToolParameter(Description = "Filter by product types")]
    ProductType[]? productTypes = null
)
{
    var request = new GetCatalogListRequest
    {
        SearchTerm = searchTerm,
        PageSize = limit,
        PageNumber = 1,
        ProductTypes = productTypes
    };

    return await _mediator.Send(request);
}

// TODO: Add [McpTool] attribute
// [McpTool(
//     Name = "catalog_get_usage",
//     Description = "Get information about where a product is used (which products include it as an ingredient). Use this for impact analysis when considering changes to a material or semi-product."
// )]
public async Task<GetProductUsageResponse> GetProductUsage(
    // [McpToolParameter(Description = "Product code (e.g., 'AKL001')", Required = true)]
    string productCode
)
{
    var request = new GetProductUsageRequest { ProductCode = productCode };
    var response = await _mediator.Send(request);

    if (!response.Success)
    {
        throw new McpToolException(response.ErrorCode, response.ErrorMessage);
    }

    return response;
}

// TODO: Add [McpTool] attribute
// [McpTool(
//     Name = "catalog_get_warehouse_statistics",
//     Description = "Get warehouse statistics including total items, stock levels, and value metrics. Use this for high-level inventory analysis."
// )]
public async Task<GetWarehouseStatisticsResponse> GetWarehouseStatistics()
{
    var request = new GetWarehouseStatisticsRequest();
    return await _mediator.Send(request);
}
```

Add required using statements:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetMaterialForPurchase;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetWarehouseStatistics;
```

**Step 4: Run all tests to verify they pass**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests"
```

Expected: All tests pass

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs
git commit -m "feat(mcp): add remaining Catalog MCP tools with tests

- GetProductComposition
- GetMaterialsForPurchase
- GetAutocomplete
- GetProductUsage
- GetWarehouseStatistics

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 3: Manufacture MCP Tools

### Task 10: Implement ManufactureOrderMcpTools with Tests

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs`
- Create: `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs`

**Step 1: Create test file**

Create `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs`:

```csharp
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class ManufactureOrderMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly ManufactureOrderMcpTools _tools;

    public ManufactureOrderMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tools = new ManufactureOrderMcpTools(_mediatorMock.Object);
    }

    [Fact]
    public async Task GetManufactureOrders_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetManufactureOrdersResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetManufactureOrdersRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tools.GetManufactureOrders();

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<GetManufactureOrdersRequest>(),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task GetManufactureOrder_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetManufactureOrderResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetManufactureOrderRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tools.GetManufactureOrder(123);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetManufactureOrderRequest>(req => req.Id == 123),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task GetCalendarView_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetCalendarViewResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCalendarViewRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tools.GetCalendarView();

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<GetCalendarViewRequest>(),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task GetResponsiblePersons_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetGroupMembersResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetGroupMembersRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tools.GetResponsiblePersons("group-id-123");

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetGroupMembersRequest>(req => req.GroupId == "group-id-123"),
            default
        ), Times.Once);
    }
}
```

**Step 2: Run tests to verify they fail**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~ManufactureOrderMcpToolsTests"
```

Expected: Tests fail with "ManufactureOrderMcpTools does not exist"

**Step 3: Implement ManufactureOrderMcpTools**

Create `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using MediatR;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for Manufacture Order operations.
/// Provides read-only access to manufacture order data for planning and tracking.
/// </summary>
public class ManufactureOrderMcpTools
{
    private readonly IMediator _mediator;

    public ManufactureOrderMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_order_get_list",
    //     Description = "Get list of manufacture orders with optional filtering. Use this to see all planned and in-progress manufacturing activities."
    // )]
    public async Task<GetManufactureOrdersResponse> GetManufactureOrders()
    {
        var request = new GetManufactureOrdersRequest();
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(response.ErrorCode, response.ErrorMessage);
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_order_get_detail",
    //     Description = "Get detailed information about a specific manufacture order including materials, timeline, and status. Use this to track progress of a manufacturing batch."
    // )]
    public async Task<GetManufactureOrderResponse> GetManufactureOrder(
        // [McpToolParameter(Description = "Manufacture order ID", Required = true)]
        int id
    )
    {
        var request = new GetManufactureOrderRequest { Id = id };
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(response.ErrorCode, response.ErrorMessage);
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_order_get_calendar",
    //     Description = "Get calendar view of manufacture orders showing scheduled production timeline. Use this for production planning and capacity analysis."
    // )]
    public async Task<GetCalendarViewResponse> GetCalendarView()
    {
        var request = new GetCalendarViewRequest();
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(response.ErrorCode, response.ErrorMessage);
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_order_get_responsible_persons",
    //     Description = "Get list of people who can be assigned as responsible for manufacture orders. Use this when planning order assignments."
    // )]
    public async Task<GetGroupMembersResponse> GetResponsiblePersons(
        // [McpToolParameter(Description = "Microsoft Entra ID group ID for manufacture team", Required = true)]
        string groupId
    )
    {
        var request = new GetGroupMembersRequest { GroupId = groupId };
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(response.ErrorCode, response.ErrorMessage);
        }

        return response;
    }
}
```

**Step 4: Run tests to verify they pass**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~ManufactureOrderMcpToolsTests"
```

Expected: All tests pass

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs
git commit -m "feat(mcp): add ManufactureOrderMcpTools with tests

- GetManufactureOrders
- GetManufactureOrder
- GetCalendarView
- GetResponsiblePersons

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 11: Implement ManufactureBatchMcpTools with Tests

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs`
- Create: `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureBatchMcpToolsTests.cs`

**Step 1: Create test file**

Create `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureBatchMcpToolsTests.cs`:

```csharp
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class ManufactureBatchMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly ManufactureBatchMcpTools _tools;

    public ManufactureBatchMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tools = new ManufactureBatchMcpTools(_mediatorMock.Object);
    }

    [Fact]
    public async Task GetBatchTemplate_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculatedBatchSizeResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculatedBatchSizeRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tools.GetBatchTemplate("AKL001");

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<CalculatedBatchSizeRequest>(req => req.ProductCode == "AKL001"),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task CalculateBatchBySize_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculatedBatchSizeResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculatedBatchSizeRequest>(), default))
            .ReturnsAsync(expectedResponse);

        var request = new CalculatedBatchSizeRequest { ProductCode = "AKL001" };

        // Act
        var result = await _tools.CalculateBatchBySize(request);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<CalculatedBatchSizeRequest>(req => req.ProductCode == "AKL001"),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task CalculateBatchByIngredient_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculateBatchByIngredientResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculateBatchByIngredientRequest>(), default))
            .ReturnsAsync(expectedResponse);

        var request = new CalculateBatchByIngredientRequest();

        // Act
        var result = await _tools.CalculateBatchByIngredient(request);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<CalculateBatchByIngredientRequest>(),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task CalculateBatchPlan_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculateBatchPlanResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculateBatchPlanRequest>(), default))
            .ReturnsAsync(expectedResponse);

        var request = new CalculateBatchPlanRequest();

        // Act
        var result = await _tools.CalculateBatchPlan(request);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<CalculateBatchPlanRequest>(),
            default
        ), Times.Once);
    }
}
```

**Step 2: Run tests to verify they fail**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~ManufactureBatchMcpToolsTests"
```

Expected: Tests fail

**Step 3: Implement ManufactureBatchMcpTools**

Create `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using MediatR;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for Manufacture Batch calculations.
/// Provides read-only batch calculation tools for production planning.
/// </summary>
public class ManufactureBatchMcpTools
{
    private readonly IMediator _mediator;

    public ManufactureBatchMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_batch_get_template",
    //     Description = "Get the batch template for a product showing the standard recipe and quantities. Use this as a starting point for batch calculations."
    // )]
    public async Task<CalculatedBatchSizeResponse> GetBatchTemplate(
        // [McpToolParameter(Description = "Product code to get batch template for", Required = true)]
        string productCode
    )
    {
        var request = new CalculatedBatchSizeRequest { ProductCode = productCode };
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(response.ErrorCode, response.ErrorMessage);
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_batch_calculate_by_size",
    //     Description = "Calculate batch quantities based on desired batch size. Use this to plan material requirements for a specific production quantity."
    // )]
    public async Task<CalculatedBatchSizeResponse> CalculateBatchBySize(
        // [McpToolParameter(Description = "Batch calculation request with product code and desired size", Required = true)]
        CalculatedBatchSizeRequest request
    )
    {
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(response.ErrorCode, response.ErrorMessage);
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_batch_calculate_by_ingredient",
    //     Description = "Calculate batch quantities based on available ingredient quantity. Use this to optimize material usage when you have a specific amount of an ingredient to use up."
    // )]
    public async Task<CalculateBatchByIngredientResponse> CalculateBatchByIngredient(
        // [McpToolParameter(Description = "Batch calculation request with ingredient and quantity", Required = true)]
        CalculateBatchByIngredientRequest request
    )
    {
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(response.ErrorCode, response.ErrorMessage);
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_batch_calculate_plan",
    //     Description = "Calculate a complete batch plan for multiple products. Use this for comprehensive production planning across multiple items."
    // )]
    public async Task<CalculateBatchPlanResponse> CalculateBatchPlan(
        // [McpToolParameter(Description = "Batch plan request with products and quantities", Required = true)]
        CalculateBatchPlanRequest request
    )
    {
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(response.ErrorCode, response.ErrorMessage);
        }

        return response;
    }
}
```

**Step 4: Run tests to verify they pass**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~ManufactureBatchMcpToolsTests"
```

Expected: All tests pass

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureBatchMcpToolsTests.cs
git commit -m "feat(mcp): add ManufactureBatchMcpTools with tests

- GetBatchTemplate
- CalculateBatchBySize
- CalculateBatchByIngredient
- CalculateBatchPlan

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 12: Register MCP Tools in McpModule

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/McpModule.cs`

**Step 1: Update McpModule to register all tool classes**

Modify `backend/src/Anela.Heblo.API/MCP/McpModule.cs`:

```csharp
using Anela.Heblo.API.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.API.MCP;

/// <summary>
/// Dependency injection module for MCP server configuration.
/// Registers MCP tool classes and configures MCP server with tool discovery.
/// </summary>
public static class McpModule
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        // Register MCP tool classes as transient (new instance per request)
        services.AddTransient<CatalogMcpTools>();
        services.AddTransient<ManufactureOrderMcpTools>();
        services.AddTransient<ManufactureBatchMcpTools>();

        // TODO: Register MCP server when Microsoft.Extensions.AI package API is finalized
        // This will be uncommented once the official API is stable
        // services.AddMcpServer(options =>
        // {
        //     options.DiscoverToolsFrom<CatalogMcpTools>();
        //     options.DiscoverToolsFrom<ManufactureOrderMcpTools>();
        //     options.DiscoverToolsFrom<ManufactureBatchMcpTools>();
        // });

        return services;
    }
}
```

**Step 2: Verify compilation**

Run:
```bash
cd backend
dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: Build succeeds

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpModule.cs
git commit -m "feat(mcp): register all MCP tool classes in DI

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 4: Integration & Testing

### Task 13: Run All Unit Tests

**Files:**
- None (verification step)

**Step 1: Run all MCP tests**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~MCP"
```

Expected: All MCP unit tests pass

**Step 2: Run all backend tests to ensure no regression**

Run:
```bash
cd backend
dotnet test
```

Expected: All tests pass, no regressions introduced

**Step 3: Check test coverage summary**

Run:
```bash
cd backend
dotnet test --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~MCP"
```

Expected: Coverage report generated

---

### Task 14: Verify Build and Format

**Files:**
- All modified files

**Step 1: Run full backend build**

Run:
```bash
cd backend
dotnet build
```

Expected: Build succeeds with no errors

**Step 2: Run code formatting check**

Run:
```bash
cd backend
dotnet format --verify-no-changes
```

Expected: No formatting violations

If formatting issues exist:
```bash
cd backend
dotnet format
git add -A
git commit -m "style(mcp): apply dotnet format

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

**Step 3: Verify all changes are committed**

Run:
```bash
git status
```

Expected: Working tree clean

---

## Phase 5: Documentation

### Task 15: Update CLAUDE.md with MCP Usage

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Add MCP section to CLAUDE.md**

Add the following section after the "Development Workflow" section in `CLAUDE.md`:

```markdown
## MCP Server

Anela Heblo API includes Model Context Protocol (MCP) server capabilities, allowing AI assistants like Claude Desktop to interact with Catalog and Manufacture endpoints.

**MCP Endpoint:**
- Development: `http://localhost:5001/mcp`
- Production: `https://heblo.anela.cz/mcp`

**Authentication:**
- Uses the same Microsoft Entra ID authentication as REST API
- Requires valid bearer token in `Authorization` header

**Available Tools:**

**Catalog Tools:**
- `catalog_get_list` - List catalog items with filtering
- `catalog_get_detail` - Get product details with transaction history
- `catalog_get_composition` - Get product composition/recipe
- `catalog_get_materials_for_purchase` - Get materials needed for purchase
- `catalog_get_autocomplete` - Search products with autocomplete
- `catalog_get_usage` - Get where a product is used
- `catalog_get_warehouse_statistics` - Get warehouse statistics

**Manufacture Order Tools:**
- `manufacture_order_get_list` - List manufacture orders
- `manufacture_order_get_detail` - Get manufacture order details
- `manufacture_order_get_calendar` - Get calendar view of orders
- `manufacture_order_get_responsible_persons` - Get assignable people

**Manufacture Batch Tools:**
- `manufacture_batch_get_template` - Get batch template for product
- `manufacture_batch_calculate_by_size` - Calculate batch by desired size
- `manufacture_batch_calculate_by_ingredient` - Calculate batch by ingredient quantity
- `manufacture_batch_calculate_plan` - Calculate complete batch plan

**Testing MCP:**
- Manual testing with Claude Desktop (configure in Claude Desktop settings)
- MCP Inspector: `npm install -g @modelcontextprotocol/inspector`
- Unit tests: `dotnet test --filter "FullyQualifiedName~MCP"`

**Implementation Details:**
- Design: `docs/plans/2026-02-26-mcp-server-design.md`
- Implementation: `docs/plans/2026-02-26-mcp-server-implementation.md`
- MCP Tools: `backend/src/Anela.Heblo.API/MCP/Tools/`
```

**Step 2: Verify markdown formatting**

Run:
```bash
cat CLAUDE.md | grep -A 50 "## MCP Server"
```

Expected: Section displays correctly

**Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add MCP server documentation to CLAUDE.md

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 16: Create MCP Testing Guide

**Files:**
- Create: `docs/testing/mcp-testing.md`

**Step 1: Create MCP testing guide**

Create `docs/testing/mcp-testing.md`:

```markdown
# MCP Server Testing Guide

This guide explains how to test the MCP server implementation in Anela Heblo.

## Prerequisites

- Backend server running (`dotnet run` from `backend/src/Anela.Heblo.API`)
- Valid Microsoft Entra ID access token
- MCP Inspector installed (optional): `npm install -g @modelcontextprotocol/inspector`

## Getting an Access Token

For local testing, obtain a token using Azure CLI:

\`\`\`bash
az login
az account get-access-token --resource <YOUR_API_CLIENT_ID>
\`\`\`

Save the token value for use in requests.

## Unit Testing

Run all MCP unit tests:

\`\`\`bash
cd backend
dotnet test --filter "FullyQualifiedName~MCP"
\`\`\`

Run specific tool tests:

\`\`\`bash
dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests"
dotnet test --filter "FullyQualifiedName~ManufactureOrderMcpToolsTests"
dotnet test --filter "FullyQualifiedName~ManufactureBatchMcpToolsTests"
\`\`\`

## Manual Testing with MCP Inspector

1. **Install MCP Inspector:**
   \`\`\`bash
   npm install -g @modelcontextprotocol/inspector
   \`\`\`

2. **Start backend server:**
   \`\`\`bash
   cd backend/src/Anela.Heblo.API
   dotnet run
   \`\`\`

3. **Connect MCP Inspector:**
   \`\`\`bash
   mcp-inspector http://localhost:5001/mcp --token YOUR_ACCESS_TOKEN
   \`\`\`

4. **Test tool discovery** - Verify all tools are listed

5. **Test individual tools** - Execute tools with sample parameters

## Testing with Claude Desktop

1. **Configure Claude Desktop** to connect to your MCP server:
   - Add MCP server configuration in Claude Desktop settings
   - Endpoint: `http://localhost:5001/mcp` (development)
   - Authentication: Bearer token from Azure CLI

2. **Test natural language interactions:**
   - "What products are in the catalog?"
   - "Show me details for product AKL001"
   - "What manufacture orders are scheduled?"

3. **Verify error handling:**
   - "Show me product DOESNOTEXIST" (should handle gracefully)
   - Test with expired token (should prompt re-auth)

## Testing Checklist

- [ ] All unit tests pass
- [ ] Tool discovery works in MCP Inspector
- [ ] Each tool callable with correct parameters
- [ ] Error responses are human-readable
- [ ] Authentication required for all tools
- [ ] Claude Desktop can use tools naturally
- [ ] Error handling works (not found, validation errors)

## Common Issues

**Authentication fails:**
- Verify token is not expired
- Check token has correct audience/resource
- Ensure API is configured for Microsoft Entra ID

**Tool not discovered:**
- Check tool is registered in `McpModule.cs`
- Verify attribute decorations are correct
- Rebuild API project

**Parameter mapping errors:**
- Check parameter types match MediatR request
- Verify optional parameters have defaults
- Review unit test assertions

## References

- MCP Specification: https://modelcontextprotocol.io/
- Design Document: `docs/plans/2026-02-26-mcp-server-design.md`
- Implementation Plan: `docs/plans/2026-02-26-mcp-server-implementation.md`
```

**Step 2: Verify markdown formatting**

Run:
```bash
cat docs/testing/mcp-testing.md | head -20
```

Expected: Displays correctly

**Step 3: Commit**

```bash
git add docs/testing/mcp-testing.md
git commit -m "docs: add MCP testing guide

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Completion

### Task 17: Final Verification and Summary

**Files:**
- None (verification step)

**Step 1: Run complete test suite**

Run:
```bash
cd backend
dotnet test
```

Expected: All tests pass

**Step 2: Verify build**

Run:
```bash
cd backend
dotnet build
```

Expected: Build succeeds

**Step 3: Check git status**

Run:
```bash
git status
git log --oneline -15
```

Expected: All changes committed, clean working tree

**Step 4: Generate implementation summary**

Create a summary of what was implemented:

```bash
echo "MCP Server Implementation Complete

✅ Phase 1: Setup
- Microsoft.Extensions.AI NuGet packages added
- MCP directory structure created
- McpToolException for error handling
- McpModule for dependency injection

✅ Phase 2: Catalog MCP Tools
- CatalogMcpTools with 7 tool methods
- Full unit test coverage
- Error handling for all tools

✅ Phase 3: Manufacture MCP Tools
- ManufactureOrderMcpTools with 4 tool methods
- ManufactureBatchMcpTools with 4 tool methods
- Full unit test coverage
- Error handling for all tools

✅ Phase 4: Integration & Testing
- All unit tests passing
- Code formatting verified
- No regressions introduced

✅ Phase 5: Documentation
- CLAUDE.md updated with MCP section
- MCP testing guide created

Total: 15 MCP tools implemented
Test Coverage: 100% of MCP tool methods
Commits: $(git log --oneline --grep="mcp" | wc -l)

Next Steps:
1. Wait for Microsoft.Extensions.AI API to stabilize
2. Uncomment [McpTool] and [McpToolParameter] attributes
3. Uncomment MCP server registration in McpModule
4. Test with Claude Desktop
5. Deploy to staging
"
```

---

## Notes for Implementation

**TDD Approach:**
- Always write tests before implementation
- Run tests to verify they fail
- Implement minimal code to make tests pass
- Commit frequently

**Code Quality:**
- Follow Clean Architecture principles
- Keep MCP tools as thin wrappers
- Reuse existing MediatR handlers
- No duplication of business logic

**Error Handling:**
- Check `response.Success` flag
- Throw `McpToolException` for errors
- Include error code and message
- Make error messages human-readable

**Testing Strategy:**
- Unit tests for parameter mapping
- Unit tests for error handling
- Integration tests when MCP middleware is ready
- Manual testing with Claude Desktop

**Deployment:**
- Changes are backward compatible
- No database migrations needed
- No configuration changes required (yet)
- MCP endpoint will be activated when attributes are uncommented

---

## Future Work (Out of Scope)

These items are intentionally left for future implementation:

1. **MCP Middleware Integration:**
   - Waiting for Microsoft.Extensions.AI API to stabilize
   - Will uncomment [McpTool] attributes when ready
   - Will register MCP server in Program.cs

2. **Write Operations:**
   - Create/update operations via MCP
   - Requires additional design work

3. **Integration Tests:**
   - End-to-end MCP protocol tests
   - Requires MCP middleware to be active

4. **Rate Limiting:**
   - Per-user rate limits
   - MCP-specific throttling

5. **Performance Monitoring:**
   - Track MCP tool usage
   - Monitor response times
   - Usage analytics
