# MCP SDK Migration Design

**Date:** 2026-02-27
**Status:** Approved
**Approach:** Update existing implementation to official MCP C# SDK

## Executive Summary

Migrate the MCP (Model Context Protocol) server implementation from the non-functional Microsoft.Extensions.AI packages to the official MCP C# SDK from https://github.com/modelcontextprotocol/csharp-sdk. The current implementation has all tool classes written but with commented-out attributes that never worked. This migration will replace those attributes with working ones from the official SDK while keeping all business logic unchanged.

**Scope:**
- Replace 2 NuGet packages
- Update 3 tool classes with working attributes
- Delete 1 custom exception class
- Update DI registration and endpoint mapping
- Simplify 16 tests to ~10-12 focused tests
- Fully functional `/mcp` endpoint for Claude Desktop

## Problem Statement

### Current State (Broken)

The application has a partial MCP implementation using Microsoft.Extensions.AI:

- **Packages**: `Microsoft.Extensions.AI` v10.3.0 and `Microsoft.Extensions.AI.Abstractions` v10.3.0
- **Tool Classes**: 3 classes with 15 tools across Catalog, Manufacturing Order, and Batch Planning
- **Attributes**: All `[McpTool]` and `[McpToolParameter]` attributes commented out with TODO notes
- **Server Registration**: Commented out in `McpModule.cs` - not actually running
- **Endpoint**: Not mapped in `Program.cs`
- **Status**: CLAUDE.md incorrectly claims "✅ Active" but server is not functional

### Why It's Invalid

Microsoft.Extensions.AI does not support the MCP attributes needed for proper implementation. The API was never finalized, leaving the implementation in a non-functional state with commented-out code waiting for an API that will never arrive.

### Desired State

- **Official SDK**: Using `ModelContextProtocol.AspNetCore` from official repository
- **Working Attributes**: `[McpServerToolType]`, `[McpServerTool]`, `[Description]` attributes applied and functional
- **Active Server**: MCP endpoint running at `/mcp` with SSE transport
- **Claude Desktop Ready**: Fully functional integration with MCP clients
- **All Tools Available**: 15 tools discoverable and callable

## Architecture

### Overview

Update existing tool classes to use official MCP SDK attributes. Same classes, same structure, just working attributes and proper SDK integration. All MediatR handlers and business logic remain completely unchanged.

### Layer Structure

```
MCP Client (Claude Desktop)
         ↓
    [MCP Endpoint /mcp] ← app.MapMcp()
         ↓
    [Existing Tool Classes] ← UPDATED: Add [McpServerToolType], [McpServerTool]
         ↓
    [MediatR Handlers] ← UNCHANGED: Existing business logic
         ↓
    [Repository/DB] ← UNCHANGED
```

### Code Transformation

**Before (Non-functional):**
```csharp
// CatalogMcpTools.cs
public class CatalogMcpTools
{
    private readonly IMediator _mediator;

    // TODO: Add [McpTool] attribute when Microsoft.Extensions.AI API is finalized
    // [McpTool(Name = "catalog_get_list", Description = "...")]
    public async Task<GetCatalogListResponse> GetCatalogList(
        // [McpToolParameter(Description = "Search term")]
        string? searchTerm = null,
        int pageNumber = 1)
    {
        var request = new GetCatalogListRequest
        {
            SearchTerm = searchTerm,
            PageNumber = pageNumber
        };
        return await _mediator.Send(request);
    }
}
```

**After (Functional):**
```csharp
// CatalogMcpTools.cs
using System.Text.Json;

[McpServerToolType]  // ← Add class attribute
public class CatalogMcpTools
{
    private readonly IMediator _mediator;

    [McpServerTool]  // ← Working attribute
    public async Task<string> GetCatalogList(  // ← Return JSON string
        [Description("Search term to filter by product name or code")]  // ← Parameter description
        string? searchTerm = null,
        [Description("Page number for pagination (default: 1)")]
        int pageNumber = 1)
    {
        var request = new GetCatalogListRequest
        {
            SearchTerm = searchTerm,
            PageNumber = pageNumber
        };
        var response = await _mediator.Send(request);
        return JsonSerializer.Serialize(response);  // ← Serialize to JSON
    }
}
```

### Key Architectural Decisions

**1. Tool Classes as Thin Wrappers**
- Tool classes remain thin wrappers around MediatR handlers
- No business logic in tool classes (stays in handlers)
- Tools only responsible for: parameter mapping, handler invocation, JSON serialization

**2. MediatR Handlers Untouched**
- All 15 existing handlers remain unchanged
- Continue returning typed responses
- All existing handler tests still work
- Zero risk to business logic

**3. JSON as Transport Format**
- Tool methods return `Task<string>` containing JSON
- MCP clients parse JSON naturally (they expect structured data)
- Simpler than formatting complex objects as human-readable text
- Type-safe on both ends: typed responses → JSON → typed consumption

**4. Official SDK Handles Protocol**
- SDK manages SSE transport, tool discovery, JSON-RPC protocol
- We just decorate methods with attributes
- No manual protocol implementation needed

**5. Error Handling via SDK**
- Use SDK's `McpException` instead of custom `McpToolException`
- SDK automatically formats exceptions as proper MCP error responses
- Same error semantics, better integration

## Components

### Files Modified (9 files)

**1. Package References**
- **File**: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- **Changes**:
  - Remove: `<PackageReference Include="Microsoft.Extensions.AI" Version="10.3.0" />`
  - Remove: `<PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.3.0" />`
  - Add: `<PackageReference Include="ModelContextProtocol.AspNetCore" Version="[latest]" />`

**2. Catalog Tools**
- **File**: `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs`
- **Changes**:
  - Add `[McpServerToolType]` attribute to class
  - Add `[McpServerTool]` attribute to each method (7 tools)
  - Replace commented `[McpToolParameter]` with `[Description]` on parameters
  - Change return types from `Task<TResponse>` to `Task<string>`
  - Add `JsonSerializer.Serialize(response)` before returning
  - Replace `McpToolException` with `McpException`

**3. Manufacture Order Tools**
- **File**: `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs`
- **Changes**: Same pattern as CatalogMcpTools (4 tools)

**4. Manufacture Batch Tools**
- **File**: `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs`
- **Changes**: Same pattern as CatalogMcpTools (4 tools)

**5. DI Registration**
- **File**: `backend/src/Anela.Heblo.API/MCP/McpModule.cs`
- **Changes**:
  - Remove commented-out code
  - Replace with:
    ```csharp
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<CatalogMcpTools>()
            .WithTools<ManufactureOrderMcpTools>()
            .WithTools<ManufactureBatchMcpTools>();

        return services;
    }
    ```

**6. Application Startup**
- **File**: `backend/src/Anela.Heblo.API/Program.cs`
- **Changes**:
  - Add service registration (around line 45):
    ```csharp
    builder.Services.AddMcpServices();
    ```
  - Add endpoint mapping (around line 81 in pipeline configuration):
    ```csharp
    app.MapMcp();
    ```

**7-9. Test Files**
- **Files**:
  - `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs`
  - `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureOrderMcpToolsTests.cs`
  - `backend/test/Anela.Heblo.Tests/MCP/Tools/ManufactureBatchMcpToolsTests.cs`
- **Changes**: Update to verify JSON serialization, simplify based on SDK patterns (details in Testing section)

### Files Deleted (1 file)

**1. Custom Exception Class**
- **File**: `backend/src/Anela.Heblo.API/MCP/McpToolException.cs`
- **Reason**: Replaced by SDK's `McpException` class

### Files Unchanged

All business logic remains untouched:
- ✅ All MediatR handlers (`backend/src/Anela.Heblo.Application/Features/*/UseCases/`)
- ✅ All repositories and persistence layer
- ✅ All domain entities
- ✅ Existing handler tests
- ✅ All controllers

## Data Flow

### Normal Request Flow

```
1. MCP Client (Claude Desktop)
   ↓ (SSE/HTTP)
   Sends: { "method": "tools/call", "params": { "name": "GetCatalogList", "arguments": {...} } }

2. MCP Endpoint (/mcp)
   ↓
   Official SDK receives request, validates protocol, routes to tool

3. CatalogMcpTools.GetCatalogList()
   ↓
   - SDK validates parameters using [Description] attributes
   - Tool creates GetCatalogListRequest from parameters
   - Tool calls: await _mediator.Send(request)

4. GetCatalogListHandler (MediatR)
   ↓
   - Executes business logic (UNCHANGED)
   - Queries database via repository
   - Returns: GetCatalogListResponse (typed object)

5. Back to CatalogMcpTools
   ↓
   - Receives typed response from handler
   - Serializes: JsonSerializer.Serialize(response)
   - Returns: Task<string> with JSON content

6. MCP Endpoint
   ↓
   - SDK wraps in MCP protocol envelope
   - Sends back via SSE

7. MCP Client
   ↓
   - Receives JSON string
   - Parses and uses data
```

### Error Flow

```
1. Handler returns response with Success = false

2. Tool class checks response.Success
   ↓
   if (!response.Success)
   {
       throw new McpException(
           response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
           response.FullError()
       );
   }

3. SDK catches McpException
   ↓
   - Formats as MCP error response
   - Returns to client with error details:
     {
       "jsonrpc": "2.0",
       "error": {
         "code": -32000,
         "message": "ProductNotFound",
         "data": "Product with code 'XYZ123' not found"
       }
     }
```

### Tool Discovery Flow

```
1. Client sends: { "method": "tools/list" }

2. SDK reads [McpServerTool] attributes
   ↓
   - Scans all classes with [McpServerToolType]
   - Reads method signatures and [Description] attributes
   - Builds tool schema automatically

3. Returns tool list:
   {
     "tools": [
       {
         "name": "GetCatalogList",
         "description": "Get paginated list of catalog items...",
         "inputSchema": {
           "type": "object",
           "properties": {
             "searchTerm": { "type": "string", "description": "Search term..." },
             "pageNumber": { "type": "integer", "description": "Page number..." }
           }
         }
       },
       // ... all 15 tools
     ]
   }
```

## Error Handling

### Error Types

**1. Business Logic Errors** (from MediatR handlers)
- Product not found
- Invalid parameters
- Authorization failures
- Database errors

**2. MCP Protocol Errors** (handled by SDK)
- Invalid JSON-RPC request
- Tool not found
- Parameter validation failures
- Type mismatches

### Business Logic Error Handling

**Pattern (used in all tools):**

```csharp
[McpServerTool]
public async Task<string> GetCatalogDetail(
    [Description("Product code (e.g., 'AKL001')")] string productCode)
{
    var request = new GetCatalogDetailRequest { ProductCode = productCode };
    var response = await _mediator.Send(request);

    // Check if handler reported error
    if (!response.Success)
    {
        throw new McpException(  // Changed from McpToolException
            response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
            response.FullError()
        );
    }

    return JsonSerializer.Serialize(response);
}
```

**Error Translation:**
- Handler returns `Success = false` with error code and message
- Tool throws `McpException` with extracted error details
- SDK formats as proper MCP error response
- Client receives structured error with code and message

### MCP Protocol Error Handling

**SDK handles automatically:**
- **Invalid parameters**: Wrong types, missing required params → Validation error
- **Tool not found**: Client calls non-existent tool → Not found error
- **JSON parse errors**: Malformed request → Parse error
- **Authentication failures**: Missing/invalid token → HTTP 401

**We don't need to handle these** - the SDK manages all protocol-level errors.

### Error Response Format

**When tool throws McpException:**

```json
{
  "jsonrpc": "2.0",
  "id": 123,
  "error": {
    "code": -32000,
    "message": "ProductNotFound",
    "data": "Product with code 'XYZ123' not found in catalog. Please verify the product code and try again."
  }
}
```

**Clients receive:**
- Error code from `response.ErrorCode`
- Detailed message from `response.FullError()`
- Proper JSON-RPC error envelope (SDK provides)

### What Changes in Error Handling

**Remove:**
- ❌ Custom `McpToolException` class (delete file)
- ❌ Manual error envelope creation
- ❌ Protocol-level error handling code

**Keep:**
- ✅ Business logic error checking (`if (!response.Success)`)
- ✅ Error code and message extraction from handlers
- ✅ Same error semantics for handlers (unchanged)

**Update:**
- `throw new McpToolException(...)` → `throw new McpException(...)`
- Same constructor parameters, same behavior
- Better integration with SDK

## Testing

### Testing Strategy

Focus tests on what we own (parameter mapping, JSON serialization, error handling) and let the SDK handle protocol concerns.

### Test Simplification

**Current state:**
- 16 tests across 3 test classes
- Tests verify parameter mapping and typed response equality

**New state:**
- ~10-12 tests across 3 test classes (~25% reduction)
- Tests verify parameter mapping and JSON serialization
- Simpler assertions, focused on our responsibilities

### Updated Test Pattern

**Before:**
```csharp
[Fact]
public async Task GetCatalogDetail_ShouldMapParametersCorrectly()
{
    // Arrange
    var expectedResponse = new GetCatalogDetailResponse
    {
        Success = true,
        Item = new CatalogItemDto { ProductCode = "AKL001" }
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var result = await _tools.GetCatalogDetail("AKL001", monthsBack: 6);

    // Assert
    _mediatorMock.Verify(m => m.Send(
        It.Is<GetCatalogDetailRequest>(req =>
            req.ProductCode == "AKL001" &&
            req.MonthsBack == 6
        ), default), Times.Once);

    Assert.Equal(expectedResponse, result);  // ← Compare typed objects
}
```

**After:**
```csharp
[Fact]
public async Task GetCatalogDetail_ShouldReturnSerializedResponse()
{
    // Arrange
    var expectedResponse = new GetCatalogDetailResponse
    {
        Success = true,
        Item = new CatalogItemDto { ProductCode = "AKL001" }
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var jsonResult = await _tools.GetCatalogDetail("AKL001");

    // Assert - Verify mediator called correctly
    _mediatorMock.Verify(m => m.Send(
        It.Is<GetCatalogDetailRequest>(req => req.ProductCode == "AKL001"),
        default), Times.Once);

    // Assert - Verify JSON serialization works
    var deserializedResponse = JsonSerializer.Deserialize<GetCatalogDetailResponse>(jsonResult);
    Assert.NotNull(deserializedResponse);
    Assert.True(deserializedResponse.Success);
    Assert.Equal("AKL001", deserializedResponse.Item.ProductCode);
}
```

### Test Categories

| Category | Test Count | Purpose |
|----------|-----------|---------|
| **Parameter Mapping** | 5-6 | Verify MediatR requests built correctly from tool parameters |
| **Error Handling** | 3-4 | Verify `McpException` thrown when `Success = false` |
| **JSON Serialization** | 2-3 | Verify responses serialize and deserialize correctly |

### Tests We Can Remove

- ✅ Protocol format verification (SDK's responsibility)
- ✅ Tool discovery mechanism (SDK's responsibility)
- ✅ Parameter validation edge cases (SDK's responsibility)
- ✅ Duplicate pagination tests that are covered by handler tests

### Tests We Keep/Update

- ✅ Each tool category has success test (3 tests minimum)
- ✅ Error scenarios throw `McpException` (3-4 tests)
- ✅ JSON serialization round-trips work (2-3 tests)
- ✅ Complex parameter mapping (arrays, enums) works correctly

### Test Execution

```bash
# Run all MCP tests
dotnet test --filter "FullyQualifiedName~MCP"

# Run specific test class
dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests"

# Watch mode for development
dotnet watch test --filter "FullyQualifiedName~MCP"
```

**Expected results:**
- All existing handler tests: ✅ Pass (unchanged)
- Updated MCP tests: ✅ Pass (~10-12 tests)
- Total test time: Faster (fewer tests, simpler assertions)

### Integration Testing

**Manual testing with Claude Desktop:**

1. **Setup Claude Desktop**:
   - Edit `claude_desktop_config.json`
   - Add MCP server configuration:
     ```json
     {
       "mcpServers": {
         "anela-heblo-local": {
           "url": "https://localhost:5001/mcp",
           "transport": "sse",
           "authentication": {
             "type": "bearer",
             "token": "YOUR_ENTRA_ID_TOKEN"
           }
         }
       }
     }
     ```

2. **Verification checklist**:
   - [ ] Restart Claude Desktop
   - [ ] Verify connection status shows "anela-heblo-local" server
   - [ ] Ask Claude: "What MCP tools do you have access to?"
   - [ ] Verify 15 tools discovered (7 Catalog, 4 Order, 4 Batch)
   - [ ] Test catalog query: "Get the catalog list from Anela Heblo"
   - [ ] Test error handling: "Get catalog detail for product 'INVALID'"
   - [ ] Verify performance (< 1s for simple queries)

3. **Test environments**:
   - Local: `https://localhost:5001/mcp`
   - Staging: `https://heblo.stg.anela.cz/mcp`
   - Production: `https://heblo.anela.cz/mcp` (after staging validation)

## Tool Inventory

### All 15 Tools (Unchanged Functionality)

**Catalog Tools (7):**
1. `GetCatalogList` - List products with filtering/pagination
2. `GetCatalogDetail` - Get detailed product information
3. `GetProductComposition` - Get product composition/ingredients
4. `GetMaterialsForPurchase` - Get materials needed for purchase
5. `GetAutocomplete` - Search products for autocomplete
6. `GetProductUsage` - Get product usage in compositions
7. `GetWarehouseStatistics` - Get warehouse statistics

**Manufacture Order Tools (4):**
1. `GetManufactureOrders` - List manufacture orders with filtering
2. `GetManufactureOrder` - Get single manufacture order details
3. `GetCalendarView` - Get calendar view of manufacture orders
4. `GetResponsiblePersons` - Get responsible persons from Entra ID

**Manufacture Batch Tools (4):**
1. `GetBatchTemplate` - Get batch template for product
2. `CalculateBatchBySize` - Calculate batch by desired size
3. `CalculateBatchByIngredient` - Calculate batch by ingredient quantity
4. `CalculateBatchPlan` - Calculate batch plan for multiple products

## Implementation Checklist

### Phase 1: Package Migration
- [ ] Remove `Microsoft.Extensions.AI` packages from csproj
- [ ] Add `ModelContextProtocol.AspNetCore` package
- [ ] Restore packages: `dotnet restore`
- [ ] Verify build succeeds: `dotnet build`

### Phase 2: Tool Classes Update
- [ ] Update `CatalogMcpTools.cs` (7 tools)
  - [ ] Add `[McpServerToolType]` to class
  - [ ] Add `[McpServerTool]` to methods
  - [ ] Replace parameter attributes with `[Description]`
  - [ ] Change return types to `Task<string>`
  - [ ] Add JSON serialization
  - [ ] Update error handling to `McpException`
- [ ] Update `ManufactureOrderMcpTools.cs` (4 tools)
- [ ] Update `ManufactureBatchMcpTools.cs` (4 tools)
- [ ] Delete `McpToolException.cs`

### Phase 3: Registration & Endpoint
- [ ] Update `McpModule.cs` with SDK registration pattern
- [ ] Add `builder.Services.AddMcpServices()` in `Program.cs`
- [ ] Add `app.MapMcp()` in `Program.cs`
- [ ] Verify application compiles

### Phase 4: Testing
- [ ] Update test files (3 files)
- [ ] Run tests: `dotnet test --filter "FullyQualifiedName~MCP"`
- [ ] Verify all tests pass
- [ ] Run full test suite: `dotnet test`

### Phase 5: Integration Testing
- [ ] Start application locally
- [ ] Configure Claude Desktop with local endpoint
- [ ] Verify tool discovery (15 tools)
- [ ] Test each tool category
- [ ] Verify error handling works
- [ ] Check performance (< 1s response times)

### Phase 6: Documentation
- [ ] Update `CLAUDE.md` to reflect accurate MCP server status
- [ ] Update `docs/testing/mcp-testing.md` with new SDK patterns
- [ ] Remove references to Microsoft.Extensions.AI
- [ ] Add official SDK documentation links

### Phase 7: Deployment
- [ ] Deploy to staging
- [ ] Run integration tests on staging
- [ ] Verify staging environment works with Claude Desktop
- [ ] Deploy to production (after staging validation)
- [ ] Update production Claude Desktop configurations

## Success Criteria

### Functional Requirements
- ✅ All 15 tools discoverable via `tools/list`
- ✅ All tools callable and return correct data
- ✅ Error handling works (throws McpException, formats correctly)
- ✅ Claude Desktop can connect and use tools
- ✅ All tests pass (~10-12 MCP tests)

### Non-Functional Requirements
- ✅ Response time < 1s for simple queries
- ✅ Response time < 3s for complex queries (batch calculations)
- ✅ No business logic changes (handlers unchanged)
- ✅ No breaking changes to handler contracts
- ✅ All existing handler tests still pass

### Documentation Requirements
- ✅ Design document complete
- ✅ CLAUDE.md updated with accurate status
- ✅ Testing documentation updated
- ✅ Implementation plan created

## Risks and Mitigations

### Risk 1: SDK API Changes
- **Risk**: Official SDK API might change
- **Likelihood**: Low (SDK is stable, v1.0+ release)
- **Impact**: Medium (would require code updates)
- **Mitigation**: Pin specific SDK version, test thoroughly before upgrading

### Risk 2: Performance Regression
- **Risk**: JSON serialization adds overhead
- **Likelihood**: Low (serialization is fast)
- **Impact**: Low (< 50ms added latency)
- **Mitigation**: Performance testing during integration phase

### Risk 3: Breaking Claude Desktop Integration
- **Risk**: Tool signatures change, breaking existing clients
- **Likelihood**: Very Low (we're keeping same functionality)
- **Impact**: High (clients can't connect)
- **Mitigation**: Test with Claude Desktop before deploying to production

### Risk 4: Test Coverage Gaps
- **Risk**: Removing tests might miss edge cases
- **Likelihood**: Low (we're testing what we own)
- **Impact**: Medium (bugs might slip through)
- **Mitigation**: Keep handler tests intact (they cover business logic edge cases)

## Timeline

**Total Effort: 1-2 days**

- **Phase 1-2** (Package + Tool Updates): 3-4 hours
- **Phase 3** (Registration + Endpoint): 1 hour
- **Phase 4** (Testing): 2-3 hours
- **Phase 5** (Integration Testing): 2-3 hours
- **Phase 6** (Documentation): 1-2 hours
- **Phase 7** (Deployment): 1-2 hours

**Recommended approach:** Complete all phases in one session to avoid partial state in repository.

## References

- **Official MCP C# SDK**: https://github.com/modelcontextprotocol/csharp-sdk
- **SDK Documentation**: https://csharp.sdk.modelcontextprotocol.io/
- **MCP Specification**: https://modelcontextprotocol.io/
- **Sample Implementation**: https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/AspNetCoreMcpServer
- **Current MCP Testing Guide**: `docs/testing/mcp-testing.md`
- **Architecture Documentation**: `docs/📘 Architecture Documentation – MVP Work.md`

## Approval

**Design Approved By:** User
**Date:** 2026-02-27

**Next Step:** Create implementation plan using `writing-plans` skill.
