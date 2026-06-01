# MCP Server Design for Anela Heblo

**Date:** 2026-02-26
**Status:** Approved
**Author:** AI-assisted design with user approval

## Overview

This document outlines the design for adding Model Context Protocol (MCP) server capabilities to the Anela Heblo API, enabling AI assistants like Claude Desktop to interact with Catalog and Manufacture endpoints.

## Goals

- Expose read-only Catalog and Manufacture endpoints as MCP tools
- Use Microsoft's official MCP NuGet package with attribute-based tool definitions
- Leverage existing Microsoft Entra ID authentication (no additional auth layer)
- Provide rich metadata/hints for better AI assistant UX
- Maintain consistency with existing API behavior (same handlers, validation, business logic)

## Non-Goals

- Write operations via MCP (only read operations for now)
- Separate MCP server deployment (integrated into existing API)
- Custom authentication layer (reuse existing auth)

---

## Architecture

### Approach

**MCP Middleware Integration** - Add MCP capabilities directly to the existing `Anela.Heblo.API` project as middleware alongside REST endpoints.

### Project Structure

```
backend/src/Anela.Heblo.API/
├── Controllers/                    # Existing REST controllers
├── MCP/
│   ├── Tools/
│   │   ├── CatalogMcpTools.cs                    # Catalog tool methods
│   │   ├── ManufactureOrderMcpTools.cs           # Manufacture order tool methods
│   │   └── ManufactureBatchMcpTools.cs           # Manufacture batch tool methods
│   └── McpModule.cs                              # DI registration for MCP services
├── Infrastructure/
│   └── Modules/
│       └── ApiModule.cs                          # Updated to register MCP endpoint
└── Program.cs                                     # Updated to add MCP middleware
```

### Endpoint Routing

- **Production**: `https://heblo.anela.cz/mcp`
- **Development**: `http://localhost:5001/mcp`

This endpoint will:
- Accept MCP protocol requests (tool discovery, tool execution)
- Use the same authentication middleware as REST API (Microsoft Entra ID via `[Authorize]`)
- Share the same authorization policies

### Integration Points

**MCP Tool → MediatR Handler Flow:**

```
MCP Client Request
  ↓
MCP Endpoint (/mcp)
  ↓
Authentication Middleware (Microsoft Entra ID)
  ↓
MCP Tool Method (e.g., GetCatalogList)
  ↓
MediatR.Send(GetCatalogListRequest)
  ↓
GetCatalogListHandler
  ↓
Response back to MCP Client
```

**Key principle:** MCP tools are thin wrappers that:
1. Map MCP tool parameters to MediatR request objects
2. Send requests via `IMediator`
3. Return responses directly (MCP framework handles serialization)

This ensures MCP tools and REST controllers use the exact same business logic and validation.

### Why This Approach

**Advantages:**
- ✅ Single deployment (no additional service)
- ✅ Reuses existing authentication without duplication
- ✅ Direct access to MediatR handlers (no HTTP overhead)
- ✅ Easy to add metadata via attributes
- ✅ Shares same dependency injection container
- ✅ Consistent behavior between REST and MCP

**Trade-offs:**
- ⚠️ Couples MCP functionality to main API (acceptable for single-team project)
- ⚠️ All MCP tools run in same process as API (not a concern for current scale)

---

## Components

### MCP Tool Classes

Each tool class is organized by feature area and uses attributes to define MCP tools.

**Example: Catalog MCP Tools**

```csharp
// backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs
using Microsoft.Extensions.AI;
using MediatR;

namespace Anela.Heblo.API.MCP.Tools;

public class CatalogMcpTools
{
    private readonly IMediator _mediator;

    public CatalogMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpTool(
        Name = "catalog_get_list",
        Description = "Get paginated list of catalog items with optional filtering by product type, search term, or warehouse status. Returns products, materials, and semi-products from the Heblo system."
    )]
    public async Task<GetCatalogListResponse> GetCatalogList(
        [McpToolParameter(Description = "Search term to filter by product name or code")]
        string? searchTerm = null,

        [McpToolParameter(Description = "Filter by product types (Product, Material, SemiProduct)")]
        ProductType[]? productTypes = null,

        [McpToolParameter(Description = "Page number for pagination (default: 1)")]
        int pageNumber = 1,

        [McpToolParameter(Description = "Page size for pagination (default: 50, max: 100)")]
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

    [McpTool(
        Name = "catalog_get_detail",
        Description = "Get detailed information for a specific product including stock levels, recent transactions, and pricing history. Use this to analyze individual product performance."
    )]
    public async Task<GetCatalogDetailResponse> GetCatalogDetail(
        [McpToolParameter(Description = "Product code (e.g., 'AKL001', 'SLU000001')", Required = true)]
        string productCode,

        [McpToolParameter(Description = "Number of months to look back for transaction history (default: 13)")]
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

    // Additional tool methods:
    // - catalog_get_composition
    // - catalog_get_materials_for_purchase
    // - catalog_get_autocomplete
    // - catalog_get_usage
    // - catalog_get_warehouse_statistics
}
```

### Metadata Strategy

Each MCP tool includes comprehensive metadata:

**1. Tool-level metadata:**
- `Name`: kebab-case tool identifier (e.g., `catalog_get_list`)
- `Description`: What the tool does and when to use it

**2. Parameter-level metadata:**
- `Description`: What the parameter is and format hints
- `Required`: Whether parameter is mandatory
- Examples in descriptions (e.g., "Product code (e.g., 'AKL001')")

**3. Response hints in descriptions:**
- Explain what kind of data is returned
- When to use this tool vs alternatives

### Tool Naming Convention

**Pattern:** `{feature}_{action}_{entity}`

**Catalog Tools:**
- `catalog_get_list`
- `catalog_get_detail`
- `catalog_get_composition`
- `catalog_get_materials_for_purchase`
- `catalog_get_autocomplete`
- `catalog_get_usage`
- `catalog_get_warehouse_statistics`

**Manufacture Order Tools:**
- `manufacture_order_get_list`
- `manufacture_order_get_detail`
- `manufacture_order_get_calendar`
- `manufacture_order_get_responsible_persons`

**Manufacture Batch Tools:**
- `manufacture_batch_get_template`
- `manufacture_batch_calculate_by_size`
- `manufacture_batch_calculate_by_ingredient`
- `manufacture_batch_calculate_plan`

### Dependency Injection Registration

```csharp
// backend/src/Anela.Heblo.API/MCP/McpModule.cs
public static class McpModule
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        // Register MCP tool classes
        services.AddTransient<CatalogMcpTools>();
        services.AddTransient<ManufactureOrderMcpTools>();
        services.AddTransient<ManufactureBatchMcpTools>();

        // Register MCP server with tool discovery
        services.AddMcpServer(options =>
        {
            options.DiscoverToolsFrom<CatalogMcpTools>();
            options.DiscoverToolsFrom<ManufactureOrderMcpTools>();
            options.DiscoverToolsFrom<ManufactureBatchMcpTools>();
        });

        return services;
    }
}
```

---

## Data Flow

### Complete Request Flow

```
┌─────────────────┐
│   MCP Client    │ (e.g., Claude Code, other MCP clients)
│  (Authenticated)│
└────────┬────────┘
         │
         │ 1. MCP Protocol Request
         │    POST /mcp
         │    Authorization: Bearer <token>
         │    { tool: "catalog_get_list", params: {...} }
         ↓
┌─────────────────────────────────────────┐
│  ASP.NET Core Middleware Pipeline       │
│  ┌─────────────────────────────────┐   │
│  │ Authentication Middleware        │   │
│  │ (Microsoft Entra ID validation)  │   │
│  └──────────────┬──────────────────┘   │
│                 ↓                        │
│  ┌─────────────────────────────────┐   │
│  │ Authorization Middleware         │   │
│  │ ([Authorize] enforcement)        │   │
│  └──────────────┬──────────────────┘   │
└─────────────────┼──────────────────────┘
                  ↓
         ┌────────────────────┐
         │  MCP Endpoint       │
         │  (Tool Router)      │
         └─────────┬───────────┘
                   │
                   │ 2. Route to Tool Method
                   ↓
         ┌──────────────────────┐
         │  CatalogMcpTools     │
         │  .GetCatalogList()   │
         └─────────┬────────────┘
                   │
                   │ 3. Create MediatR Request
                   │    new GetCatalogListRequest { ... }
                   ↓
         ┌──────────────────────┐
         │  IMediator.Send()    │
         └─────────┬────────────┘
                   │
                   │ 4. Handler Execution
                   ↓
         ┌──────────────────────────┐
         │  GetCatalogListHandler   │
         │  - Validate request      │
         │  - Query repository      │
         │  - Map to response       │
         └─────────┬────────────────┘
                   │
                   │ 5. Response
                   │    GetCatalogListResponse
                   ↓
         ┌──────────────────────┐
         │  CatalogMcpTools     │
         │  (return response)   │
         └─────────┬────────────┘
                   │
                   │ 6. MCP Protocol Response
                   ↓
         ┌──────────────────────┐
         │  MCP Client          │
         │  (receives data)     │
         └──────────────────────┘
```

### Authentication Flow

**No additional authentication needed:**
- MCP endpoint uses the same `[Authorize]` attribute as REST API
- Microsoft Entra ID bearer token is validated by existing middleware
- User context is available via `HttpContext.User`
- Same authorization policies apply

**Token format:** Standard JWT bearer token from Microsoft Entra ID

### Parameter Mapping

MCP tool methods map parameters directly to MediatR request objects:

```csharp
// MCP Tool receives parameters
public async Task<GetCatalogListResponse> GetCatalogList(
    string? searchTerm,
    ProductType[]? productTypes,
    int pageNumber,
    int pageSize
)
{
    // Map to MediatR request (same as REST controller does)
    var request = new GetCatalogListRequest
    {
        SearchTerm = searchTerm,
        ProductTypes = productTypes,
        PageNumber = pageNumber,
        PageSize = pageSize
    };

    // Send to handler (exact same path as REST API)
    return await _mediator.Send(request);
}
```

**This ensures:**
- ✅ Same validation logic (FluentValidation in handlers)
- ✅ Same business rules
- ✅ Same data access patterns
- ✅ No duplication of logic

### Response Handling

**For successful responses:**
- Return the MediatR response directly
- MCP framework serializes to JSON automatically

**For errors:**
- MCP tools check `response.Success` flag
- If error, throw `McpToolException` with error code and message
- MCP framework translates to MCP error protocol

---

## Error Handling

### MCP Error Handling with Claude Desktop

When an MCP tool encounters an error, the flow works as follows:

**1. Error in Heblo API:**

```csharp
[McpTool(Name = "catalog_get_detail")]
public async Task<GetCatalogDetailResponse> GetCatalogDetail(string productCode)
{
    var response = await _mediator.Send(new GetCatalogDetailRequest { ProductCode = productCode });

    if (!response.Success)
    {
        // Throw MCP-specific exception
        throw new McpToolException(
            code: response.ErrorCode,           // e.g., "NOT_FOUND"
            message: response.ErrorMessage      // e.g., "Product 'XYZ123' not found"
        );
    }

    return response;
}
```

**2. MCP Framework translates to MCP protocol error:**

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "Product 'XYZ123' not found"
  }
}
```

**3. Claude Desktop receives the error and:**

✅ **Displays it to the user naturally:**
> "I tried to get details for product 'XYZ123' but it wasn't found in the catalog. Could you double-check the product code?"

✅ **Can retry with corrections:**
> "Let me try searching the catalog list first to find the correct code..."

✅ **Provides context:**
> "The API returned an error: Product 'XYZ123' not found. This might mean the product doesn't exist or has been deleted."

### Error Categories Claude Handles Well

**Validation Errors (400-level):**
- Missing required parameters → Claude asks user for missing info
- Invalid format → Claude reformats and retries
- Business rule violations → Claude explains to user

**Authentication Errors (401):**
- Token expired → Claude prompts user to re-authenticate
- Insufficient permissions → Claude informs user

**Not Found Errors (404):**
- Resource doesn't exist → Claude tries alternative searches
- Invalid ID/code → Claude suggests corrections

**Server Errors (500):**
- Unexpected failures → Claude informs user, may retry once

### Example Interaction

**User asks Claude:**
> "What's the stock level for product ABC999?"

**Claude calls MCP tool:**
```
catalog_get_detail(productCode: "ABC999")
```

**API returns error (product not found):**
```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "Product 'ABC999' not found in catalog"
  }
}
```

**Claude responds to user:**
> "I couldn't find product 'ABC999' in the catalog. Let me search for products that might match..."

**Claude then calls:**
```
catalog_get_list(searchTerm: "ABC999")
```

### Best Practices for Error Messages

For good Claude Desktop UX, error messages should be:

1. **Human-readable**: "Product not found" ✅ not "ERR_404_PROD" ❌
2. **Actionable**: "Product 'ABC999' not found. Check the product code." ✅
3. **Specific**: Include the parameter value that caused the issue
4. **Consistent**: Use standard error codes (NOT_FOUND, VALIDATION_ERROR, etc.)

### Existing Error Pattern Compatibility

The current `BaseResponse<T>` pattern with `Success`, `ErrorCode`, and `ErrorMessage` fields is ideal because:
- Error codes are machine-readable (`ErrorCodes.NotFound`)
- Error messages are human-readable
- Claude can parse both and provide intelligent responses

---

## Testing

### Testing Strategy

MCP tools will be tested at three levels:

#### 1. Unit Tests - Tool Method Logic

Test that MCP tools correctly map parameters to MediatR requests:

```csharp
// backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs
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
        var expectedResponse = new GetCatalogListResponse { /* ... */ };
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
                req.ProductTypes.Length == 1 &&
                req.PageNumber == 2 &&
                req.PageSize == 25
            ),
            default
        ), Times.Once);

        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task GetCatalogDetail_ShouldThrowMcpToolException_WhenProductNotFound()
    {
        // Arrange
        var errorResponse = new GetCatalogDetailResponse(ErrorCodes.NotFound)
        {
            ErrorMessage = "Product 'XYZ123' not found"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpToolException>(
            () => _tools.GetCatalogDetail("XYZ123")
        );

        Assert.Equal(ErrorCodes.NotFound, exception.Code);
        Assert.Contains("XYZ123", exception.Message);
    }
}
```

#### 2. Integration Tests - End-to-End MCP Flow

Test the complete MCP endpoint with authentication:

```csharp
// backend/test/Anela.Heblo.Tests/MCP/McpEndpointIntegrationTests.cs
public class McpEndpointIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public McpEndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task McpEndpoint_ShouldRequireAuthentication()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CatalogGetList_ShouldReturnResults_WithValidToken()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GetTestToken());

        var mcpRequest = new
        {
            tool = "catalog_get_list",
            parameters = new
            {
                searchTerm = "Bisabolol",
                pageSize = 10
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", mcpRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GetCatalogListResponse>();
        Assert.NotNull(result);
        Assert.True(result.Items.Any());
    }
}
```

#### 3. Manual Testing with MCP Clients

Test with actual MCP clients (Claude Desktop, MCP Inspector):

**Using Claude Desktop:**
1. Configure Claude Desktop to connect to your MCP server
2. Ask Claude to use the tools naturally
3. Verify responses and error handling

**Using MCP Inspector (dev tool):**
```bash
# Install MCP Inspector
npm install -g @modelcontextprotocol/inspector

# Connect to your MCP server
mcp-inspector http://localhost:5001/mcp --token YOUR_TOKEN

# Test tool discovery
# Test individual tools
# Verify error responses
```

### Test Coverage Goals

**Unit Tests:**
- ✅ Parameter mapping for all tools
- ✅ Error handling for all error codes
- ✅ Optional parameter defaults

**Integration Tests:**
- ✅ Authentication required for all tools
- ✅ At least one successful call per tool
- ✅ Error scenarios (not found, validation errors)

**Manual Testing Checklist:**
- ✅ Tool discovery works in Claude Desktop
- ✅ All tools callable with correct parameters
- ✅ Error messages display clearly
- ✅ Authentication flow works
- ✅ Response data is usable by Claude

### Testing During Development

**Fast feedback loop:**
1. Write unit test for new MCP tool
2. Implement tool method
3. Run unit tests (`dotnet test`)
4. Test manually with MCP Inspector
5. Test with Claude Desktop for UX validation

**CI/CD Integration:**
- Unit tests run in PR builds (same as existing tests)
- Integration tests run in PR builds
- Manual Claude Desktop testing before production deployment

---

## Implementation Checklist

### Phase 1: Setup (Estimated: 2-3 hours)

- [ ] Add Microsoft MCP NuGet package to `Anela.Heblo.API`
- [ ] Create `MCP/` directory structure
- [ ] Create `McpModule.cs` for DI registration
- [ ] Register MCP middleware in `Program.cs`
- [ ] Configure MCP endpoint routing
- [ ] Verify authentication works on `/mcp` endpoint

### Phase 2: Catalog Tools (Estimated: 3-4 hours)

- [ ] Implement `CatalogMcpTools.cs`
- [ ] Add all Catalog tool methods with metadata
- [ ] Write unit tests for Catalog tools
- [ ] Write integration tests for Catalog endpoint
- [ ] Test with MCP Inspector
- [ ] Test with Claude Desktop

### Phase 3: Manufacture Tools (Estimated: 4-5 hours)

- [ ] Implement `ManufactureOrderMcpTools.cs`
- [ ] Implement `ManufactureBatchMcpTools.cs`
- [ ] Add all Manufacture tool methods with metadata
- [ ] Write unit tests for Manufacture tools
- [ ] Write integration tests for Manufacture endpoints
- [ ] Test with MCP Inspector
- [ ] Test with Claude Desktop

### Phase 4: Documentation & Deployment (Estimated: 2 hours)

- [ ] Update CLAUDE.md with MCP usage instructions
- [ ] Document how to configure Claude Desktop
- [ ] Update deployment configuration (if needed)
- [ ] Create runbook for MCP troubleshooting
- [ ] Deploy to staging and test
- [ ] Deploy to production

**Total Estimated Time:** 11-14 hours

---

## Future Enhancements

Potential future additions (out of scope for initial implementation):

1. **Write Operations** - Add MCP tools for creating/updating entities
2. **Real-time Updates** - MCP resources for streaming data
3. **Batch Operations** - Tools for bulk operations
4. **Advanced Filtering** - More sophisticated search capabilities
5. **Performance Monitoring** - Track MCP tool usage and performance
6. **Rate Limiting** - Per-user rate limits for MCP tools

---

## References

- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [Microsoft.Extensions.AI Documentation](https://learn.microsoft.com/en-us/dotnet/ai/)
- Anela Heblo Architecture: `docs/📘 Architecture Documentation – MVP Work.md`
- Existing API Controllers: `backend/src/Anela.Heblo.API/Controllers/`

---

## Approval

**Design Status:** ✅ Approved
**Next Step:** Create implementation plan
