# MCP Product Margins Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose existing product-margin data (M0/M1/M2 + monthly history) through the MCP server via a new `GetProductMargins` tool, gated by the same `Products_ProductMargins` permission as the REST endpoint.

**Architecture:** Add one `[McpServerTool]` method to the existing `CatalogMcpTools` class. It performs a permission check using `ICurrentUserService.IsInRole(...)` (the `/mcp` endpoint is already authenticated and the `PermissionClaimsTransformation` injects permission role claims), then delegates to the existing `GetProductMarginsRequest`/handler via MediatR and serializes the response. No new calculation, DTO, or registration code — tool discovery is reflection-based via `.WithTools<CatalogMcpTools>()`.

**Tech Stack:** .NET 8, MediatR, ModelContextProtocol ASP.NET SDK, xUnit + Moq.

---

## Context

Product margins are already calculated (`MarginCalculationService` → `CatalogAggregate.Margins`) and served over REST at `GET api/productmargins`, which is gated by `[FeatureAuthorize(Feature.Products_ProductMargins)]` (`backend/src/Anela.Heblo.API/Controllers/ProductMarginsController.cs:8`). The MCP server does **not** expose margins. The user wants margins reachable via MCP **and** wants the MCP tool to enforce the same permission gate (any authenticated MCP user must NOT automatically see margins).

The whole backend already exists — this plan only wires a thin MCP tool over `GetProductMarginsRequest` and adds an auth guard.

## File Structure

- **Modify** `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs` — add `ICurrentUserService` dependency + `GetProductMargins` tool method.
- **Modify** `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs` — update fixture for the new constructor arg + add 3 tests.
- **Modify** `docs/integrations/mcp-server.md` — document the new tool, bump Catalog count 7 → 8.

## Reused existing types (do not recreate)
- `Anela.Heblo.Application.Features.Catalog.UseCases.GetProductMargins.GetProductMarginsRequest` — fields: `ProductCode` (string?), `ProductName` (string?), `ProductType` (`ProductType?`), `PageNumber` (int), `PageSize` (int), `SortBy` (string?), `SortDescending` (bool), plus `DateFrom`/`DateTo` (DateTime?, not exposed by the tool).
- `...GetProductMargins.GetProductMarginsResponse : BaseResponse` — `Items` (`List<ProductMarginDto>`), `TotalCount`, `PageNumber`, `PageSize`.
- `Anela.Heblo.Domain.Features.Users.ICurrentUserService` — `bool IsInRole(string role)` (registered via `AddUsersModule()`).
- `Anela.Heblo.Domain.Features.Authorization.AccessRoles.For(Feature, AccessLevel)` and const `AccessRoles.ProductsProductMarginsRead = "products.product_margins.read"`.
- `Anela.Heblo.Domain.Features.Authorization.Feature.Products_ProductMargins`, `AccessLevel.Read`.
- `ModelContextProtocol.McpException` (already used in this file for handler errors).

---

## Task 1: Inject `ICurrentUserService` into `CatalogMcpTools`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs:21-28`
- Test: `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs:20-27`

- [ ] **Step 1: Update the test fixture to construct the tool with the new dependency (RED)**

In `CatalogMcpToolsTests.cs`, add the Moq + using, a mock field, and pass it to the constructor:

Add usings near the top (after existing usings):
```csharp
using Anela.Heblo.Domain.Features.Users;
```

Replace the fixture (lines 20-27) with:
```csharp
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly CatalogMcpTools _tools;

    public CatalogMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _tools = new CatalogMcpTools(_mediatorMock.Object, _currentUserServiceMock.Object);
    }
```

- [ ] **Step 2: Run tests to verify the project fails to compile (RED)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogMcpToolsTests"`
Expected: BUILD FAILS — `CatalogMcpTools` has no constructor taking 2 args.

- [ ] **Step 3: Add the dependency to `CatalogMcpTools` (GREEN)**

In `CatalogMcpTools.cs`, add usings:
```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductMargins;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
```

Replace the field + constructor (lines 23-28) with:
```csharp
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public CatalogMcpTools(IMediator mediator, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
    }
```

- [ ] **Step 4: Run tests to verify they pass (GREEN)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogMcpToolsTests"`
Expected: PASS — all existing catalog tool tests green with the new 2-arg constructor.

> `ICurrentUserService` is already registered (singleton, via `AddUsersModule()` in `Program.cs`), so DI resolution of the tool at runtime needs no change.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs
git commit -m "refactor(mcp): inject ICurrentUserService into CatalogMcpTools"
```

---

## Task 2: Add the `GetProductMargins` MCP tool (authorized path)

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs` (append a new method before the closing brace, after `GetWarehouseStatistics`)
- Test: `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs`

- [ ] **Step 1: Write the failing test for the authorized success path (RED)**

Add usings to the test file if not present:
```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductMargins;
using Anela.Heblo.Domain.Features.Authorization;
```

Add this test method to `CatalogMcpToolsTests`:
```csharp
    [Fact]
    public async Task GetProductMargins_ReturnsSerializedResponse_WhenAuthorized()
    {
        // Arrange
        _currentUserServiceMock
            .Setup(s => s.IsInRole(AccessRoles.ProductsProductMarginsRead))
            .Returns(true);

        var expectedResponse = new GetProductMarginsResponse
        {
            Items = new List<ProductMarginDto>
            {
                new() { ProductCode = "DEO001030", ProductName = "Důvěrný pan Jasmín 30ml" }
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 50
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetProductMarginsRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetProductMargins(
            productCode: "DEO001030",
            pageNumber: 1,
            pageSize: 50,
            sortBy: "m2percentage",
            sortDescending: true);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetProductMarginsRequest>(req =>
                req.ProductCode == "DEO001030" &&
                req.PageNumber == 1 &&
                req.PageSize == 50 &&
                req.SortBy == "m2percentage" &&
                req.SortDescending == true),
            default), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetProductMarginsResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.TotalCount);
        Assert.Single(deserialized.Items);
        Assert.Equal("DEO001030", deserialized.Items[0].ProductCode);
    }
```

- [ ] **Step 2: Run the test to verify it fails (RED)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMargins_ReturnsSerializedResponse_WhenAuthorized"`
Expected: BUILD FAILS — `CatalogMcpTools` has no `GetProductMargins` method.

- [ ] **Step 3: Implement the `GetProductMargins` tool method (GREEN)**

In `CatalogMcpTools.cs`, add this method as the last member of the class (after `GetWarehouseStatistics`, before the closing `}`):
```csharp
    [McpServerTool]
    public async Task<string> GetProductMargins(
        [Description("Filter by product code (partial match)")]
        string? productCode = null,
        [Description("Filter by product name (partial match)")]
        string? productName = null,
        [Description("Filter by product type (Product, Material, SemiProduct)")]
        ProductType? productType = null,
        [Description("Page number for pagination (default: 1)")]
        int pageNumber = 1,
        [Description("Page size for pagination (default: 50)")]
        int pageSize = 50,
        [Description("Sort field: productcode, productname, pricewithoutvat, purchaseprice, " +
                    "manufacturedifficulty, m0amount, m1amount, m2amount, m0percentage, m1percentage, m2percentage")]
        string? sortBy = null,
        [Description("Sort descending (default: false)")]
        bool sortDescending = false,
        CancellationToken cancellationToken = default)
    {
        var requiredRole = AccessRoles.For(Feature.Products_ProductMargins, AccessLevel.Read);
        if (!_currentUserService.IsInRole(requiredRole))
        {
            throw new McpException(
                $"[FORBIDDEN] You do not have permission to access Product Margins (requires {requiredRole}).");
        }

        var request = new GetProductMarginsRequest
        {
            ProductCode = productCode,
            ProductName = productName,
            ProductType = productType,
            PageNumber = pageNumber,
            PageSize = pageSize,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }
```

- [ ] **Step 4: Run the test to verify it passes (GREEN)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMargins_ReturnsSerializedResponse_WhenAuthorized"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs
git commit -m "feat(mcp): add GetProductMargins tool gated by Products_ProductMargins"
```

---

## Task 3: Cover the unauthorized and handler-error paths

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs`

- [ ] **Step 1: Write the failing tests (RED)**

Add these two test methods to `CatalogMcpToolsTests`:
```csharp
    [Fact]
    public async Task GetProductMargins_ThrowsMcpException_WhenUserLacksFeature()
    {
        // Arrange — default mock: IsInRole returns false
        _currentUserServiceMock
            .Setup(s => s.IsInRole(It.IsAny<string>()))
            .Returns(false);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.GetProductMargins(productCode: "DEO001030"));

        Assert.Contains("FORBIDDEN", exception.Message);
        Assert.Contains(AccessRoles.ProductsProductMarginsRead, exception.Message);
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<GetProductMarginsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetProductMargins_ThrowsMcpException_WhenResponseNotSuccess()
    {
        // Arrange
        _currentUserServiceMock
            .Setup(s => s.IsInRole(AccessRoles.ProductsProductMarginsRead))
            .Returns(true);

        var errorResponse = new GetProductMarginsResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.Exception
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetProductMarginsRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.GetProductMargins(productCode: "DEO001030"));

        Assert.Contains("Exception", exception.Message);
    }
```

> Note: `ErrorCodes` (with member `Exception = 0099`) is already imported via `using Anela.Heblo.Application.Shared;` (present at the top of the test file). No new using needed.

- [ ] **Step 2: Run the tests to verify behavior (RED then GREEN)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMargins"`
Expected: All three `GetProductMargins_*` tests PASS (the implementation from Task 2 already satisfies them).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/MCP/Tools/CatalogMcpToolsTests.cs
git commit -m "test(mcp): cover GetProductMargins unauthorized and error paths"
```

---

## Task 4: Document the new MCP tool

**Files:**
- Modify: `docs/integrations/mcp-server.md:7-14`

- [ ] **Step 1: Update the Catalog tool list and count**

Replace the header `**Catalog (7)**` with `**Catalog (8)**` and add this bullet after the `GetWarehouseStatistics` line (line 14):
```markdown
- `GetProductMargins` — product margins (M0/M1/M2 + monthly history); requires the Products_ProductMargins permission
```

- [ ] **Step 2: Update the total tool count if stated elsewhere**

Run: `grep -rn "15 tools\|(15)\|15 MCP" docs/ CLAUDE.md`
For each hit that refers to the MCP tool total, change 15 → 16. (CLAUDE.md's documentation map line `docs/integrations/mcp-server.md — MCP tools, endpoints, client config (15 tools)` becomes `(16 tools)`.)

- [ ] **Step 3: Commit**

```bash
git add docs/integrations/mcp-server.md CLAUDE.md
git commit -m "docs(mcp): document GetProductMargins tool"
```

---

## Final Verification

- [ ] **Backend build + format (CLAUDE.md BE gate):**
  - Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
  - Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes` (or `dotnet format` then review diff)
  - Expected: build succeeds, no format violations.
- [ ] **Full MCP test suite:**
  - Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MCP"`
  - Expected: all MCP tool tests pass (existing + 3 new).
- [ ] **Manual MCP check against staging** (per `docs/integrations/mcp-server.md` client setup):
  - As a user **with** `products.product_margins.read`: call `GetProductMargins` (e.g. `productCode: "DEO001030"`) → returns paged `ProductMarginDto` items with populated `m0`/`m1`/`m2`.
  - As a user **without** it: call the same tool → returns the `[FORBIDDEN] ...` `McpException`, matching the REST endpoint's gate.
  - Cross-check the returned M0/M1/M2 values for one product against `GET api/productmargins?productCode=DEO001030` to confirm parity.

## Out of Scope
- Populating `CatalogItemDto.MarginPercentage`/`MarginAmount` (rejected Option 2). They remain unused — flag as possible future cleanup, not part of this change.
- Any change to margin calculation logic, DTOs, or the REST endpoint.
