# MCP Testing Guide

This document provides comprehensive guidance for testing Model Context Protocol (MCP) tools in the Anela Heblo application.

## Overview

**MCP Tools** enable Claude to interact with the Anela Heblo backend through standardized tool interfaces. This guide explains how to write and run tests for these MCP tool implementations.

**Key characteristics:**
- Tests validate MCP tool behavior (not business logic)
- Uses **Moq** to mock `IMediator` dependencies
- Verifies parameter mapping from MCP interface to MediatR requests
- Tests both success and error scenarios
- Follows standard AAA (Arrange-Act-Assert) pattern

## Test Organization

### Test Location

**Path:** `backend/test/Anela.Heblo.Tests/MCP/Tools/`

**Test Classes:**
- `CatalogMcpToolsTests.cs` - 8 tests for Catalog tools
- `ManufactureOrderMcpToolsTests.cs` - 4 tests for Manufacture Order tools
- `ManufactureBatchMcpToolsTests.cs` - 4 tests for Manufacture Batch tools

**Total Tests:** 16

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

MCP tools throw `McpToolException` when business logic fails. Error handling tests verify this behavior.

### Example Error Test

```csharp
[Fact]
public async Task GetCatalogDetail_ShouldThrowMcpToolException_WhenProductNotFound()
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
    var exception = await Assert.ThrowsAsync<McpToolException>(
        () => _tools.GetCatalogDetail("XYZ123")
    );

    Assert.Equal("ProductNotFound", exception.Code);
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
- Error: `{MethodName}_ShouldThrowMcpToolException_When{Condition}`

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
- Verify `McpToolException` thrown
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
- ✅ Test error scenarios with `McpToolException`
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

**Issue:** `McpToolException` not thrown when expected

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
4. **Error scenarios** are handled with `McpToolException`

Keep tests simple, focused, and follow established patterns for consistency across the codebase.
