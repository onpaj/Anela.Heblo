# MCP Testing Guide

This document provides comprehensive guidance for testing Model Context Protocol (MCP) tools in the Anela Heblo application.

## Overview

**MCP Tools** enable Claude to interact with the Anela Heblo backend through standardized tool interfaces using the official ModelContextProtocol.AspNetCore SDK.

**Key characteristics:**
- Tests validate MCP tool behavior (parameter mapping, JSON serialization, error handling)
- Uses **Moq** to mock `IMediator` dependencies
- Verifies JSON serialization of responses
- Tests both success and error scenarios
- Uses official SDK attributes: `[McpServerToolType]`, `[McpServerTool]`, `[Description]`
- Follows standard AAA (Arrange-Act-Assert) pattern

## Test Organization

### Test Location

**Path:** `backend/test/Anela.Heblo.Tests/MCP/Tools/`

**Test Classes:**
- `CatalogMcpToolsTests.cs` - Tests for Catalog tools
- `ManufactureOrderMcpToolsTests.cs` - Tests for Manufacture Order tools
- `ManufactureBatchMcpToolsTests.cs` - Tests for Manufacture Batch tools

**Total Tests:** 26 (parameter mapping + JSON serialization + error handling)

### Test Coverage

Current coverage:
- ✅ All 15 MCP tool methods tested
- ✅ Success paths tested
- ✅ Error handling tested (selected tools)
- ⚠️ Edge cases not fully covered (future work)

## Testing Pattern

All MCP tool tests follow the same pattern using Moq and standard xUnit practices.

### 1. Arrange
- Create mock `IMediator` instance
- Configure mock to return expected response for MediatR request
- Create MCP tool instance with mocked mediator

### 2. Act
- Call the MCP tool method with test parameters

### 3. Assert
- Verify correct MediatR request was sent via `IMediator.Send()`
- Verify parameters were mapped correctly from MCP interface to request
- Verify response returned successfully

## Example Test

Here's a complete example from `CatalogMcpToolsTests.cs`:

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
```

### What This Test Validates

1. **Parameter Mapping**: `productCode` and `monthsBack` are correctly mapped to `GetCatalogDetailRequest`
2. **MediatR Invocation**: `IMediator.Send()` is called exactly once with correct request
3. **Response Handling**: Response from mediator is returned unchanged by MCP tool

## Error Handling Tests

MCP tools throw `McpException` when business logic fails. Error handling tests verify this behavior.

### Example Error Test

```csharp
[Fact]
public async Task GetCatalogDetail_ShouldThrowMcpException_WhenProductNotFound()
{
    // Arrange
    var errorResponse = new GetCatalogDetailResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.ProductNotFound,
        Params = new Dictionary<string, string>
        {
            { "ProductCode", "XYZ123" }
        }
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
        .ReturnsAsync(errorResponse);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<McpException>(
        () => _tools.GetCatalogDetail("XYZ123")
    );

    Assert.Contains("ProductNotFound", exception.Message);
    Assert.Contains("XYZ123", exception.Message);
}
```

### Error Handling Verification

Tests verify:
- **Exception thrown** when `response.Success == false`
- **Error code extracted** from `response.ErrorCode?.ToString()`
- **Error message uses** `response.FullError()` for detailed context

## Running Tests

### Run All MCP Tests

```bash
dotnet test --filter "FullyQualifiedName~MCP"
```

### Run Specific Test Class

```bash
dotnet test --filter "FullyQualifiedName~CatalogMcpToolsTests"
dotnet test --filter "FullyQualifiedName~ManufactureOrderMcpToolsTests"
dotnet test --filter "FullyQualifiedName~ManufactureBatchMcpToolsTests"
```

### Run Single Test

```bash
dotnet test --filter "FullyQualifiedName~GetCatalogDetail_ShouldMapParametersCorrectly"
```

### Run Tests in Watch Mode

```bash
dotnet watch test --filter "FullyQualifiedName~MCP"
```

## Writing New Tests

When adding new MCP tools, follow these steps:

### 1. Create Test Class

Create new test file in `backend/test/Anela.Heblo.Tests/MCP/Tools/`

```csharp
using Anela.Heblo.API.MCP;
using Anela.Heblo.API.MCP.Tools;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class YourFeatureMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly YourFeatureMcpTools _tools;

    public YourFeatureMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tools = new YourFeatureMcpTools(_mediatorMock.Object);
    }

    // Tests go here
}
```

### 2. Follow Naming Convention

**Test class:** `{ToolClass}Tests.cs` (e.g., `CatalogMcpToolsTests.cs`)

**Test methods:**
- Success: `{MethodName}_ShouldMapParametersCorrectly`
- Error: `{MethodName}_ShouldThrowMcpException_When{Condition}`

### 3. Use Same Pattern

All tests should follow the Arrange-Act-Assert pattern:

```csharp
[Fact]
public async Task YourMethod_ShouldMapParametersCorrectly()
{
    // Arrange - Mock mediator response
    var expectedResponse = new YourResponse { /* ... */ };
    _mediatorMock
        .Setup(m => m.Send(It.IsAny<YourRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act - Call MCP tool method
    var result = await _tools.YourMethod(param1, param2);

    // Assert - Verify mediator was called correctly
    _mediatorMock.Verify(m => m.Send(
        It.Is<YourRequest>(req =>
            req.Param1 == param1 &&
            req.Param2 == param2
        ),
        default
    ), Times.Once);

    Assert.Equal(expectedResponse, result);
}
```

### 4. Test Both Success and Error Paths

**Success test:**
- Mock successful response (`Success = true`)
- Verify parameters mapped correctly
- Verify response returned

**Error test:**
- Mock error response (`Success = false`, `ErrorCode` set)
- Verify `McpException` thrown
- Verify error code and message extracted correctly

### 5. Verify Parameter Mapping

Critical to verify that MCP tool parameters are correctly mapped to MediatR request properties:

```csharp
_mediatorMock.Verify(m => m.Send(
    It.Is<GetCatalogDetailRequest>(req =>
        req.ProductCode == "AKL001" &&  // Verify each parameter
        req.MonthsBack == 6
    ),
    default
), Times.Once);
```

### 6. Update Documentation

After adding tests:
- Update test count in this guide
- Add new test class to "Test Organization" section
- Document any special testing patterns for new tools

## Best Practices

### Do's

- ✅ Keep tests focused on MCP tool behavior, not business logic
- ✅ Mock `IMediator` consistently across all tests
- ✅ Verify parameter mapping explicitly with `It.Is<T>()`
- ✅ Test error scenarios with `McpException`
- ✅ Use descriptive test method names following convention
- ✅ Follow AAA (Arrange-Act-Assert) pattern strictly

### Don'ts

- ❌ Don't test business logic in MCP tool tests (use handler tests for that)
- ❌ Don't create real database connections or HTTP requests
- ❌ Don't hardcode error messages (use response objects)
- ❌ Don't skip error handling tests
- ❌ Don't test multiple scenarios in one test method

### Code Quality

**Keep tests simple:**
- One assertion focus per test
- Clear test names indicating what's being tested
- Minimal setup in Arrange section

**Use Moq effectively:**
- `It.IsAny<T>()` for setup when exact match not needed
- `It.Is<T>(predicate)` for verification with explicit parameter checks
- `Times.Once` to verify method called exactly once

**Verify behavior:**
- Use `.Verify()` to assert mediator interactions
- Use `Assert.Equal()` for response validation
- Use `Assert.ThrowsAsync<T>()` for exception scenarios

## Common Patterns

### Testing List Operations

```csharp
[Fact]
public async Task GetList_ShouldMapPaginationCorrectly()
{
    // Arrange
    var expectedResponse = new GetListResponse
    {
        Items = new List<ItemDto>(),
        TotalCount = 0,
        PageNumber = 2,
        PageSize = 25
    };

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetListRequest>(), default))
        .ReturnsAsync(expectedResponse);

    // Act
    var result = await _tools.GetList(
        pageNumber: 2,
        pageSize: 25
    );

    // Assert
    _mediatorMock.Verify(m => m.Send(
        It.Is<GetListRequest>(req =>
            req.PageNumber == 2 &&
            req.PageSize == 25
        ),
        default
    ), Times.Once);
}
```

### Testing Enum Parameters

```csharp
[Fact]
public async Task GetList_ShouldMapEnumArrayCorrectly()
{
    // Arrange
    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetListRequest>(), default))
        .ReturnsAsync(new GetListResponse());

    // Act
    await _tools.GetList(
        productTypes: new[] { ProductType.Material, ProductType.Product }
    );

    // Assert
    _mediatorMock.Verify(m => m.Send(
        It.Is<GetListRequest>(req =>
            req.ProductTypes != null &&
            req.ProductTypes.Length == 2 &&
            req.ProductTypes[0] == ProductType.Material &&
            req.ProductTypes[1] == ProductType.Product
        ),
        default
    ), Times.Once);
}
```

### Testing Optional Parameters

```csharp
[Fact]
public async Task GetDetail_ShouldHandleOptionalParameters()
{
    // Arrange
    _mediatorMock
        .Setup(m => m.Send(It.IsAny<GetDetailRequest>(), default))
        .ReturnsAsync(new GetDetailResponse { Success = true });

    // Act - Call without optional parameter
    await _tools.GetDetail(productCode: "AKL001");

    // Assert - Verify default value used
    _mediatorMock.Verify(m => m.Send(
        It.Is<GetDetailRequest>(req =>
            req.ProductCode == "AKL001" &&
            req.MonthsBack == 12  // Default value
        ),
        default
    ), Times.Once);
}
```

## CI/CD Integration

MCP tests run as part of standard backend test suite in CI/CD pipeline.

### In Pull Request Builds

```yaml
- name: Run Backend Tests
  run: dotnet test backend/Anela.Heblo.sln
```

MCP tests are included in overall backend test run (no separate step needed).

### Local Development

Run tests before committing:

```bash
# Run all tests (includes MCP)
dotnet test backend/Anela.Heblo.sln

# Run only MCP tests for faster feedback
dotnet test --filter "FullyQualifiedName~MCP"
```

## Future Improvements

### Planned Enhancements

- **Integration tests** - Test with real MediatR handlers instead of mocks
- **Edge case coverage** - Test null parameters, boundary values, invalid input
- **Performance benchmarks** - Measure tool invocation overhead
- **E2E tests** - Test with actual MCP client connecting to server
- **Contract tests** - Verify MCP tool interface matches protocol specification

### Coverage Gaps

Current gaps to address:
- No null/empty parameter validation tests
- No concurrent request handling tests
- No timeout/cancellation tests
- No performance regression tests

## Troubleshooting

### Common Issues

**Issue:** Test fails with "Expected invocation on the mock exactly 1 times, but was 0 times"

**Solution:** Verify that:
- Mock setup matches actual request type
- Request parameters match setup conditions
- Tool method is actually calling mediator

**Issue:** `McpException` not thrown when expected

**Solution:** Check that:
- Response has `Success = false`
- `ErrorCode` is set correctly
- Tool method checks response success status

**Issue:** Parameter mapping verification fails

**Solution:**
- Use exact parameter values in test
- Check for null/default value handling
- Verify enum/array parameter matching

## Additional Resources

- **MCP Documentation:** `/docs/development/mcp-server.md` - MCP server setup and configuration
- **MCP Tools Implementation:** `/backend/src/Anela.Heblo.API/MCP/Tools/` - Tool implementations
- **MediatR Handlers:** `/backend/src/Anela.Heblo.Application/Features/` - Business logic handlers
- **Moq Documentation:** https://github.com/moq/moq4 - Mocking framework

## Summary

MCP testing focuses on verifying:
1. **Parameter mapping** from MCP interface to MediatR requests
2. **Mediator invocation** happens correctly (once, with right request)
3. **Response handling** returns data or throws appropriate exceptions
4. **Error scenarios** are handled with `McpException`

Keep tests simple, focused, and follow established patterns for consistency across the codebase.

## MCP Server Integration Testing

This section covers testing the MCP server endpoint directly, verifying tool discovery, invocation, and performance.

### Prerequisites

Before running integration tests, ensure:

1. **Server is running:**
   - Local: `dotnet run --project backend/src/Anela.Heblo.API` (runs on https://localhost:5001)
   - Staging: https://heblo.stg.anela.cz
   - Production: https://heblo.anela.cz

2. **Authentication token available:**
   - Microsoft Entra ID bearer token required for all endpoints
   - For local testing, obtain token from `.env.test` file or Azure CLI
   - Token must have valid permissions for the application

3. **Test tools installed:**
   - `curl` - For basic HTTP testing
   - `jq` - For JSON parsing (optional, but helpful)
   - MCP client (Claude Desktop, etc.) - For full client testing

### Testing MCP Endpoint

#### Test 1: Endpoint Availability

Verify the MCP endpoint is accessible and returns correct response:

```bash
# Test local endpoint
curl -X GET https://localhost:5001/mcp \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: text/event-stream" \
  --insecure

# Test staging endpoint
curl -X GET https://heblo.stg.anela.cz/mcp \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: text/event-stream"
```

**Expected response:**
- Status: `200 OK`
- Content-Type: `text/event-stream`
- SSE stream with MCP server events

**Common issues:**
- `401 Unauthorized` - Invalid or missing bearer token
- `404 Not Found` - Endpoint not mapped correctly (check Program.cs)
- `500 Internal Server Error` - Server configuration issue

#### Test 2: Tool Discovery

Use JSON-RPC to discover available MCP tools:

```bash
# Create JSON-RPC request for tools/list
cat > request.json << 'EOF'
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list",
  "params": {}
}
EOF

# Send request to MCP endpoint
curl -X POST https://localhost:5001/mcp \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  --data @request.json \
  --insecure
```

**Expected response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "tools": [
      {
        "name": "GetCatalogList",
        "description": "List products from catalog with filtering and pagination",
        "inputSchema": {
          "type": "object",
          "properties": {
            "pageNumber": { "type": "integer" },
            "pageSize": { "type": "integer" },
            "productTypes": { "type": "array" }
          }
        }
      },
      // ... 14 more tools
    ]
  }
}
```

**Verification:**
- Response contains 15 tools total
- 7 Catalog tools (GetCatalogList, GetCatalogDetail, etc.)
- 4 Manufacture Order tools (GetManufactureOrders, etc.)
- 4 Manufacture Batch tools (GetBatchTemplate, etc.)

#### Test 3: Tool Invocation

Test calling a specific MCP tool:

```bash
# Create JSON-RPC request for GetCatalogList
cat > invoke.json << 'EOF'
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "GetCatalogList",
    "arguments": {
      "pageNumber": 1,
      "pageSize": 10,
      "productTypes": ["Material"]
    }
  }
}
EOF

# Send request to MCP endpoint
curl -X POST https://localhost:5001/mcp \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  --data @invoke.json \
  --insecure | jq
```

**Expected response:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{\"items\":[...],\"totalCount\":42,\"pageNumber\":1,\"pageSize\":10}"
      }
    ]
  }
}
```

**Verification:**
- Response contains `result.content` array
- Content includes JSON-formatted tool response
- Data matches expected structure (items array, pagination info)

### Testing with Claude Desktop

Configure Claude Desktop to connect to your MCP server:

#### Configuration Setup

Edit `claude_desktop_config.json` (location varies by OS):
- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
- **Linux**: `~/.config/Claude/claude_desktop_config.json`

Add your MCP server:

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
    },
    "anela-heblo-staging": {
      "url": "https://heblo.stg.anela.cz/mcp",
      "transport": "sse",
      "authentication": {
        "type": "bearer",
        "token": "YOUR_ENTRA_ID_TOKEN"
      }
    }
  }
}
```

#### Token Management

For local development testing:

```bash
# Option 1: Get token from Azure CLI
az account get-access-token --resource YOUR_APP_ID --query accessToken -o tsv

# Option 2: Use token from .env.test file
cat frontend/.env.test | grep SERVICE_PRINCIPAL_TOKEN
```

**Token expiration:**
- Entra ID tokens typically expire after 1 hour
- Claude Desktop will show authentication errors when token expires
- Refresh configuration with new token as needed

#### Verify Connection

After configuration:

1. **Restart Claude Desktop** - Required to load new configuration
2. **Check connection status** - Look for MCP server in Claude Desktop UI
3. **Test tool discovery** - Ask Claude: "What MCP tools do you have access to?"
4. **Test tool invocation** - Ask Claude: "Get the catalog list from Anela Heblo"

**Expected behavior:**
- Claude recognizes 15 tools from anela-heblo server
- Tools are categorized: Catalog (7), Manufacture Order (4), Manufacture Batch (4)
- Tool invocations return real data from your environment

### Performance Testing

Measure MCP server latency and throughput:

#### Test 1: Response Time

Measure tool invocation latency:

```bash
# Test GetCatalogList response time
time curl -X POST https://localhost:5001/mcp \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  --data '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"GetCatalogList","arguments":{"pageNumber":1,"pageSize":10}}}' \
  --insecure \
  -o /dev/null -s -w "Time: %{time_total}s\n"
```

**Performance baselines:**
- Local: < 500ms for simple queries
- Staging/Production: < 1000ms for simple queries
- Complex queries (batch calculations): < 3000ms

#### Test 2: Concurrent Requests

Test server handling of multiple concurrent requests:

```bash
# Run 10 concurrent requests
for i in {1..10}; do
  curl -X POST https://localhost:5001/mcp \
    -H "Authorization: Bearer YOUR_TOKEN_HERE" \
    -H "Content-Type: application/json" \
    --data '{"jsonrpc":"2.0","id":'$i',"method":"tools/call","params":{"name":"GetCatalogList","arguments":{}}}' \
    --insecure \
    -o /dev/null -s -w "Request $i: %{time_total}s\n" &
done
wait
```

**Expected behavior:**
- All requests complete successfully
- No significant latency degradation
- No 429 (rate limit) or 503 (service unavailable) errors

#### Test 3: Large Dataset Handling

Test with tools returning large datasets:

```bash
# Request large page size
curl -X POST https://localhost:5001/mcp \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  --data '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"GetCatalogList","arguments":{"pageSize":100}}}' \
  --insecure \
  -w "\nSize: %{size_download} bytes\nTime: %{time_total}s\n"
```

**Performance expectations:**
- Response size: Varies by data (typically 10KB - 500KB)
- Response time: Should scale linearly with page size
- No timeout errors (< 30s)

### Troubleshooting

#### Issue: Tools Not Discovered

**Symptoms:**
- `tools/list` returns empty array
- Claude Desktop shows no tools available

**Solutions:**
1. **Verify tool registration:**
   ```bash
   # Check McpModule.cs has tool services registered
   grep -A 20 "public static IServiceCollection" backend/src/Anela.Heblo.API/MCP/McpModule.cs
   ```

2. **Check server logs:**
   ```bash
   # Run server with logging
   dotnet run --project backend/src/Anela.Heblo.API
   # Look for "MCP tool registered" messages
   ```

3. **Verify endpoint mapping:**
   ```bash
   # Check ApplicationBuilderExtensions.cs
   grep "MapMcpServer\|/mcp" backend/src/Anela.Heblo.API/ApplicationBuilderExtensions.cs
   ```

#### Issue: Authentication Failures

**Symptoms:**
- `401 Unauthorized` responses
- "Invalid token" errors in logs

**Solutions:**
1. **Verify token validity:**
   ```bash
   # Decode JWT token to check expiration
   echo "YOUR_TOKEN" | cut -d. -f2 | base64 -d | jq .exp
   # Compare with current time
   date +%s
   ```

2. **Check token scopes:**
   - Token must have correct audience (application ID)
   - Token must have valid user/service principal claims

3. **Regenerate token:**
   ```bash
   # Get fresh token from Azure CLI
   az account get-access-token --resource YOUR_APP_ID
   ```

#### Issue: Slow Response Times

**Symptoms:**
- Tool calls taking > 5 seconds
- Timeout errors in Claude Desktop

**Solutions:**
1. **Check database performance:**
   ```bash
   # Run with SQL logging enabled
   export ASPNETCORE_ENVIRONMENT=Development
   dotnet run --project backend/src/Anela.Heblo.API
   # Look for slow queries in logs
   ```

2. **Profile tool execution:**
   - Add timing logs in MCP tool classes
   - Check MediatR handler performance
   - Verify database indexes exist

3. **Test network latency:**
   ```bash
   # Measure round-trip time
   ping heblo.stg.anela.cz
   # Check if network is bottleneck
   ```

4. **Review pagination:**
   - Large page sizes (> 100) may cause slow queries
   - Reduce `pageSize` parameter in tool calls
   - Add database query optimization

#### Issue: SSE Connection Drops

**Symptoms:**
- Connection closes unexpectedly
- "Connection reset" errors

**Solutions:**
1. **Check server timeout settings:**
   - SSE connections should stay open
   - Verify no aggressive reverse proxy timeouts

2. **Test with keep-alive:**
   ```bash
   # Use curl with verbose output
   curl -v -X GET https://localhost:5001/mcp \
     -H "Authorization: Bearer YOUR_TOKEN_HERE" \
     -H "Accept: text/event-stream" \
     --no-buffer
   ```

3. **Verify firewall/proxy:**
   - Some proxies block SSE connections
   - Test direct connection vs. through proxy

#### Issue: Tool Returns Wrong Data

**Symptoms:**
- Tool executes but returns unexpected results
- Data doesn't match database state

**Solutions:**
1. **Verify MediatR handler:**
   ```bash
   # Find handler for the tool
   grep -r "GetCatalogListHandler" backend/src/Anela.Heblo.Application/
   # Review handler logic
   ```

2. **Test handler directly:**
   - Write unit test for MediatR handler
   - Verify handler returns correct data
   - Check parameter mapping in MCP tool class

3. **Check database state:**
   ```bash
   # Query database directly
   # Compare with tool response
   ```

### Integration Test Checklist

Before deploying MCP server changes:

- [ ] **Endpoint availability:** Test with curl on all environments
- [ ] **Tool discovery:** Verify all 15 tools returned by `tools/list`
- [ ] **Authentication:** Test with valid and invalid tokens
- [ ] **Tool invocation:** Test at least one tool per category (Catalog, Order, Batch)
- [ ] **Error handling:** Test with invalid parameters, verify error responses
- [ ] **Performance:** Measure response times, verify < 1s for simple queries
- [ ] **Claude Desktop:** Test end-to-end with real MCP client
- [ ] **Staging validation:** Run smoke tests against staging environment
- [ ] **Production readiness:** All tests pass on staging before production deploy
