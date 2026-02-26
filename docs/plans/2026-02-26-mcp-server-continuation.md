# MCP Server Implementation - Continuation Plan

**Status:** Implementation Complete - Waiting for Package Update
**Created:** 2026-02-26
**Branch:** `feature/mcp_server`

## Current Status

### ✅ Completed Work

**Implementation (464 lines):**
- ✅ `CatalogMcpTools.cs` - 7 methods for catalog operations
- ✅ `ManufactureOrderMcpTools.cs` - 4 methods for manufacture orders
- ✅ `ManufactureBatchMcpTools.cs` - 4 methods for batch calculations
- ✅ `McpToolException.cs` - Custom exception for MCP protocol errors
- ✅ `McpModule.cs` - DI registration for all tool classes

**Testing (445 lines):**
- ✅ 16 unit tests covering all 15 MCP tool methods
- ✅ Success path testing
- ✅ Error handling testing with `McpToolException`
- ✅ All tests passing (16/16)

**Documentation:**
- ✅ `CLAUDE.md` - MCP Server section added
- ✅ `docs/testing/mcp-testing.md` - Comprehensive testing guide (482 lines)
- ✅ Design and implementation documentation

**Quality:**
- ✅ All 1,665 tests passing
- ✅ Build successful (0 errors)
- ✅ Code formatted (`dotnet format` passing)
- ✅ Clean git status

### ⏳ Blocked - Waiting for Package Update

**Current Package:** `Microsoft.Extensions.AI` v9.0.0-preview.9.24556.5

**Issue:** This preview version does not include the MCP server registration API. The full MCP server functionality requires v9.2+ which is not yet released.

**What's Missing:**
- `[McpTool]` attribute for marking methods as MCP tools
- `AddMcpServer()` extension method for DI registration
- MCP transport configuration (stdio/SSE)
- Tool discovery and registration API

## TODO Markers in Code

### 1. Tool Method Attributes (15 locations)

**Files with TODO markers:**
- `CatalogMcpTools.cs` (7 methods)
- `ManufactureOrderMcpTools.cs` (4 methods)
- `ManufactureBatchMcpTools.cs` (4 methods)

**Current state:**
```csharp
// TODO: Add [McpTool("catalog.get_list", "List products with filtering and pagination")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetCatalogListResponse> GetCatalogList(/* ... */)
{
    // Implementation complete
}
```

**Required change when v9.2+ available:**
```csharp
[McpTool("catalog.get_list", "List products with filtering and pagination")]
public async Task<GetCatalogListResponse> GetCatalogList(/* ... */)
{
    // Implementation complete
}
```

### 2. MCP Server Registration (1 location)

**File:** `McpModule.cs`

**Current state:**
```csharp
public static IServiceCollection AddMcpServices(this IServiceCollection services)
{
    // Register MCP tool classes as transient (new instance per request)
    services.AddTransient<CatalogMcpTools>();
    services.AddTransient<ManufactureOrderMcpTools>();
    services.AddTransient<ManufactureBatchMcpTools>();

    // TODO: Register MCP server when Microsoft.Extensions.AI v9.2+ is available
    // services.AddMcpServer(options =>
    // {
    //     options.DiscoverToolsFrom<CatalogMcpTools>();
    //     options.DiscoverToolsFrom<ManufactureOrderMcpTools>();
    //     options.DiscoverToolsFrom<ManufactureBatchMcpTools>();
    // });

    return services;
}
```

**Required change when v9.2+ available:**
```csharp
public static IServiceCollection AddMcpServices(this IServiceCollection services)
{
    // Register MCP tool classes as transient (new instance per request)
    services.AddTransient<CatalogMcpTools>();
    services.AddTransient<ManufactureOrderMcpTools>();
    services.AddTransient<ManufactureBatchMcpTools>();

    // Register MCP server
    services.AddMcpServer(options =>
    {
        options.DiscoverToolsFrom<CatalogMcpTools>();
        options.DiscoverToolsFrom<ManufactureOrderMcpTools>();
        options.DiscoverToolsFrom<ManufactureBatchMcpTools>();
    });

    return services;
}
```

### 3. MCP Endpoint Configuration (1 location)

**File:** `Program.cs` (needs to be added)

**Required change when v9.2+ available:**
```csharp
// In Program.cs, after app.UseAuthorization();

// Map MCP endpoint
app.MapMcpServer("/mcp");  // Or configure stdio transport
```

## Remaining Work Checklist

When Microsoft.Extensions.AI v9.2+ is released:

### Phase 1: Package Update
- [ ] Update `Anela.Heblo.API.csproj` to Microsoft.Extensions.AI v9.2+
- [ ] Update `Anela.Heblo.Tests.csproj` to Microsoft.Extensions.AI v9.2+
- [ ] Run `dotnet restore` and verify no package conflicts
- [ ] Run `dotnet build` to verify compilation

### Phase 2: Add MCP Attributes
- [ ] Add `[McpTool]` attribute to all 7 methods in `CatalogMcpTools.cs`
- [ ] Add `[McpTool]` attribute to all 4 methods in `ManufactureOrderMcpTools.cs`
- [ ] Add `[McpTool]` attribute to all 4 methods in `ManufactureBatchMcpTools.cs`
- [ ] Follow naming convention: `{domain}.{action}` (e.g., `catalog.get_list`)
- [ ] Add descriptions to each attribute for better AI assistant UX

### Phase 3: Register MCP Server
- [ ] Uncomment MCP server registration in `McpModule.cs`
- [ ] Configure transport (stdio or SSE endpoint)
- [ ] Add MCP endpoint mapping in `Program.cs`
- [ ] Configure authentication/authorization for MCP endpoint
- [ ] Add CORS configuration if needed for web-based MCP clients

### Phase 4: Testing
- [ ] Manual testing with MCP inspector tool
- [ ] Test with Claude Desktop app (if available)
- [ ] Verify all 15 tools are discoverable
- [ ] Test authentication flow
- [ ] Test error handling (McpToolException → MCP error responses)
- [ ] Performance testing (tool invocation latency)

### Phase 5: Documentation Updates
- [ ] Update `CLAUDE.md` with MCP server endpoint configuration
- [ ] Update `docs/testing/mcp-testing.md` with integration testing guide
- [ ] Add configuration guide for MCP clients (Claude Desktop, etc.)
- [ ] Document tool naming conventions and discovery
- [ ] Add troubleshooting section for common MCP issues

### Phase 6: Deployment
- [ ] Update deployment configuration (Docker, Azure Web App)
- [ ] Add MCP endpoint to API documentation
- [ ] Update environment variables if needed
- [ ] Deploy to staging and test
- [ ] Deploy to production

## Estimated Effort

**When v9.2+ is available:**
- Package update: 15 minutes
- Add attributes: 30 minutes (15 methods × 2 min each)
- Register MCP server: 30 minutes
- Configure endpoint: 30 minutes
- Testing: 2 hours
- Documentation: 1 hour
- Deployment: 30 minutes

**Total: ~5 hours** (assuming no package conflicts or API changes)

## Tool Naming Convention

When adding `[McpTool]` attributes, use this naming pattern:

**Catalog Tools:**
- `catalog.get_list` - GetCatalogList
- `catalog.get_detail` - GetCatalogDetail
- `catalog.get_composition` - GetProductComposition
- `catalog.get_materials_for_purchase` - GetMaterialsForPurchase
- `catalog.autocomplete` - GetAutocomplete
- `catalog.get_product_usage` - GetProductUsage
- `catalog.get_warehouse_statistics` - GetWarehouseStatistics

**Manufacture Order Tools:**
- `manufacture.get_orders` - GetManufactureOrders
- `manufacture.get_order` - GetManufactureOrder
- `manufacture.get_calendar` - GetCalendarView
- `manufacture.get_responsible_persons` - GetResponsiblePersons

**Manufacture Batch Tools:**
- `batch.get_template` - GetBatchTemplate
- `batch.calculate_by_size` - CalculateBatchBySize
- `batch.calculate_by_ingredient` - CalculateBatchByIngredient
- `batch.calculate_plan` - CalculateBatchPlan

## Expected MCP Configuration

**Transport Options:**

1. **stdio transport** (recommended for local development):
   ```csharp
   services.AddMcpServer(options =>
   {
       options.UseStdioTransport();
       options.DiscoverToolsFrom<CatalogMcpTools>();
       // ... other tools
   });
   ```

2. **SSE endpoint** (for web-based clients):
   ```csharp
   services.AddMcpServer(options =>
   {
       options.UseSseTransport();
       options.DiscoverToolsFrom<CatalogMcpTools>();
       // ... other tools
   });

   // In Program.cs
   app.MapMcpServer("/mcp");
   ```

## Authentication Considerations

**Current authentication:** Microsoft Entra ID via `[Authorize]` attribute

**MCP endpoint authentication:**
- MCP tools inherit `[Authorize]` from class level
- MCP endpoint will require same authentication
- AI assistants (Claude Desktop) will need to authenticate
- Consider API key or OAuth2 token for MCP clients

## Monitoring Package Release

**Watch for:**
- Microsoft.Extensions.AI v9.2+ release announcement
- GitHub: https://github.com/dotnet/extensions
- NuGet: https://www.nuget.org/packages/Microsoft.Extensions.AI
- Release notes for MCP server API documentation

**Notification channels:**
- .NET blog: https://devblogs.microsoft.com/dotnet/
- Microsoft.Extensions.AI GitHub releases

## Risk Assessment

**Low Risk:**
- Core implementation complete and tested
- Thin wrapper pattern minimizes coupling
- No business logic in MCP layer

**Medium Risk:**
- API changes between preview and stable release
- MCP attribute API might differ from expected
- Transport configuration might require adjustment

**Mitigation:**
- All TODO markers clearly indicate required changes
- Tests verify business logic independently of MCP
- Documentation includes expected API usage

## Success Criteria

When v9.2+ work is complete:

- [ ] All 15 MCP tools discoverable by MCP clients
- [ ] Tools callable from Claude Desktop (or similar MCP client)
- [ ] Authentication working correctly
- [ ] Error responses properly formatted for MCP protocol
- [ ] All tests passing (including any new integration tests)
- [ ] Documentation updated with actual configuration
- [ ] Successfully deployed to staging and production

## Notes

- This branch (`feature/mcp_server`) can be merged to `main` in its current state
- The TODO markers are intentional and documented
- When v9.2+ is available, create new branch from `main` to complete activation
- All groundwork is complete - activation should be straightforward

## References

- **Design Document:** `docs/plans/2026-02-26-mcp-server-design.md`
- **Implementation Plan:** `docs/plans/2026-02-26-mcp-server-implementation.md`
- **Testing Guide:** `docs/testing/mcp-testing.md`
- **CLAUDE.md:** See "MCP Server" section

---

**Last Updated:** 2026-02-26
**Status:** Ready for merge - Waiting for Microsoft.Extensions.AI v9.2+ release
