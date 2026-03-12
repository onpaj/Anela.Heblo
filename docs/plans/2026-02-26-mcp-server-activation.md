# MCP Server Activation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Activate MCP server functionality by adding attributes and registration code once Microsoft.Extensions.AI v9.2+ is available.

**Architecture:** Thin MCP tool wrappers around existing MediatR handlers are already implemented and tested (464 lines, 16 passing tests). This plan adds the MCP protocol layer using [McpTool] attributes and server registration API when the package becomes available.

**Tech Stack:** .NET 8, Microsoft.Extensions.AI v9.2+, ASP.NET Core, MCP Protocol

**Prerequisites:**
- Microsoft.Extensions.AI v9.2+ must be released
- Current branch: `feature/mcp_server` (or new branch from `main`)
- All existing tests passing (1,665 tests)

---

## Task 1: Verify Package Availability

**Files:**
- Check: NuGet package feed
- Verify: Release notes

**Step 1: Check if Microsoft.Extensions.AI v9.2+ is available**

Run: `dotnet list package Microsoft.Extensions.AI`

Expected: Shows current version (v9.0.0-preview.9.24556.5)

**Step 2: Search for latest version**

Run: `dotnet add backend/src/Anela.Heblo.API package Microsoft.Extensions.AI --version 9.2.*`

Expected: Either finds v9.2+ or shows "Unable to find package" if not yet released

**Step 3: Verify MCP API availability**

If package found, check release notes at: https://github.com/dotnet/extensions/releases

Expected: Release notes mention `[McpTool]` attribute and `AddMcpServer()` method

**Step 4: Document package version**

Create note of exact version number for documentation updates.

---

## Task 2: Update Package References

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- Modify: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

**Step 1: Update API project package reference**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, find:

```xml
<PackageReference Include="Microsoft.Extensions.AI" Version="9.0.0-preview.9.24556.5" />
```

Change to:

```xml
<PackageReference Include="Microsoft.Extensions.AI" Version="9.2.0" />
```

(Replace `9.2.0` with actual version found in Task 1)

**Step 2: Update test project package reference**

In `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, find:

```xml
<PackageReference Include="Microsoft.Extensions.AI" Version="9.0.0-preview.9.24556.5" />
```

Change to:

```xml
<PackageReference Include="Microsoft.Extensions.AI" Version="9.2.0" />
```

**Step 3: Restore packages**

Run: `cd backend && dotnet restore`

Expected: Successfully restored packages with no conflicts

**Step 4: Verify build**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: Build succeeded with 0 errors

**Step 5: Run tests to verify compatibility**

Run: `dotnet test backend/Anela.Heblo.sln`

Expected: All 1,665 tests passing

**Step 6: Commit package updates**

```bash
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
git add backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
git commit -m "chore: update Microsoft.Extensions.AI to v9.2+ for MCP server support"
```

---

## Task 3: Add MCP Attributes to Catalog Tools

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs:19-231`

**Step 1: Add [McpTool] attribute to GetCatalogList**

Find line 19:

```csharp
// TODO: Add [McpTool("catalog.get_list", "List products with filtering and pagination")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetCatalogListResponse> GetCatalogList(
```

Replace with:

```csharp
[McpTool("catalog.get_list", "List products with filtering and pagination")]
public async Task<GetCatalogListResponse> GetCatalogList(
```

**Step 2: Add [McpTool] attribute to GetCatalogDetail**

Find line 56:

```csharp
// TODO: Add [McpTool("catalog.get_detail", "Get detailed product information")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetCatalogDetailResponse> GetCatalogDetail(Guid catalogId)
```

Replace with:

```csharp
[McpTool("catalog.get_detail", "Get detailed product information")]
public async Task<GetCatalogDetailResponse> GetCatalogDetail(Guid catalogId)
```

**Step 3: Add [McpTool] attribute to GetProductComposition**

Find line 75:

```csharp
// TODO: Add [McpTool("catalog.get_composition", "Get product composition and ingredients")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetProductCompositionResponse> GetProductComposition(Guid catalogId)
```

Replace with:

```csharp
[McpTool("catalog.get_composition", "Get product composition and ingredients")]
public async Task<GetProductCompositionResponse> GetProductComposition(Guid catalogId)
```

**Step 4: Add [McpTool] attribute to GetMaterialsForPurchase**

Find line 94:

```csharp
// TODO: Add [McpTool("catalog.get_materials_for_purchase", "Get materials needed for purchase")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetMaterialsForPurchaseResponse> GetMaterialsForPurchase(
```

Replace with:

```csharp
[McpTool("catalog.get_materials_for_purchase", "Get materials needed for purchase")]
public async Task<GetMaterialsForPurchaseResponse> GetMaterialsForPurchase(
```

**Step 5: Add [McpTool] attribute to GetAutocomplete**

Find line 134:

```csharp
// TODO: Add [McpTool("catalog.autocomplete", "Search products for autocomplete suggestions")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetAutocompleteResponse> GetAutocomplete(
```

Replace with:

```csharp
[McpTool("catalog.autocomplete", "Search products for autocomplete suggestions")]
public async Task<GetAutocompleteResponse> GetAutocomplete(
```

**Step 6: Add [McpTool] attribute to GetProductUsage**

Find line 174:

```csharp
// TODO: Add [McpTool("catalog.get_product_usage", "Get product usage in compositions")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetProductUsageResponse> GetProductUsage(Guid catalogId)
```

Replace with:

```csharp
[McpTool("catalog.get_product_usage", "Get product usage in compositions")]
public async Task<GetProductUsageResponse> GetProductUsage(Guid catalogId)
```

**Step 7: Add [McpTool] attribute to GetWarehouseStatistics**

Find line 193:

```csharp
// TODO: Add [McpTool("catalog.get_warehouse_statistics", "Get warehouse inventory statistics")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetWarehouseStatisticsResponse> GetWarehouseStatistics(
```

Replace with:

```csharp
[McpTool("catalog.get_warehouse_statistics", "Get warehouse inventory statistics")]
public async Task<GetWarehouseStatisticsResponse> GetWarehouseStatistics(
```

**Step 8: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.API`

Expected: Build succeeded - attributes recognized by compiler

**Step 9: Run MCP tool tests**

Run: `dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~CatalogMcpToolsTests"`

Expected: All 8 catalog tool tests passing

**Step 10: Commit catalog tool attributes**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/CatalogMcpTools.cs
git commit -m "feat(mcp): add [McpTool] attributes to catalog tools"
```

---

## Task 4: Add MCP Attributes to Manufacture Order Tools

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs:19-147`

**Step 1: Add [McpTool] attribute to GetManufactureOrders**

Find line 19:

```csharp
// TODO: Add [McpTool("manufacture.get_orders", "List manufacture orders with filtering")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetManufactureOrdersResponse> GetManufactureOrders(
```

Replace with:

```csharp
[McpTool("manufacture.get_orders", "List manufacture orders with filtering")]
public async Task<GetManufactureOrdersResponse> GetManufactureOrders(
```

**Step 2: Add [McpTool] attribute to GetManufactureOrder**

Find line 66:

```csharp
// TODO: Add [McpTool("manufacture.get_order", "Get single manufacture order details")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetManufactureOrderResponse> GetManufactureOrder(Guid orderId)
```

Replace with:

```csharp
[McpTool("manufacture.get_order", "Get single manufacture order details")]
public async Task<GetManufactureOrderResponse> GetManufactureOrder(Guid orderId)
```

**Step 3: Add [McpTool] attribute to GetCalendarView**

Find line 85:

```csharp
// TODO: Add [McpTool("manufacture.get_calendar", "Get calendar view of manufacture orders")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetCalendarViewResponse> GetCalendarView(
```

Replace with:

```csharp
[McpTool("manufacture.get_calendar", "Get calendar view of manufacture orders")]
public async Task<GetCalendarViewResponse> GetCalendarView(
```

**Step 4: Add [McpTool] attribute to GetResponsiblePersons**

Find line 108:

```csharp
// TODO: Add [McpTool("manufacture.get_responsible_persons", "Get responsible persons from Entra ID")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetResponsiblePersonsResponse> GetResponsiblePersons()
```

Replace with:

```csharp
[McpTool("manufacture.get_responsible_persons", "Get responsible persons from Entra ID")]
public async Task<GetResponsiblePersonsResponse> GetResponsiblePersons()
```

**Step 5: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.API`

Expected: Build succeeded

**Step 6: Run manufacture order MCP tests**

Run: `dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~ManufactureOrderMcpToolsTests"`

Expected: All 4 manufacture order tool tests passing

**Step 7: Commit manufacture order tool attributes**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs
git commit -m "feat(mcp): add [McpTool] attributes to manufacture order tools"
```

---

## Task 5: Add MCP Attributes to Manufacture Batch Tools

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs:18-135`

**Step 1: Add [McpTool] attribute to GetBatchTemplate**

Find line 18:

```csharp
// TODO: Add [McpTool("batch.get_template", "Get batch template for product")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<GetBatchTemplateResponse> GetBatchTemplate(Guid productId)
```

Replace with:

```csharp
[McpTool("batch.get_template", "Get batch template for product")]
public async Task<GetBatchTemplateResponse> GetBatchTemplate(Guid productId)
```

**Step 2: Add [McpTool] attribute to CalculateBatchBySize**

Find line 37:

```csharp
// TODO: Add [McpTool("batch.calculate_by_size", "Calculate batch by desired size")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<CalculateBatchBySizeResponse> CalculateBatchBySize(
```

Replace with:

```csharp
[McpTool("batch.calculate_by_size", "Calculate batch by desired size")]
public async Task<CalculateBatchBySizeResponse> CalculateBatchBySize(
```

**Step 3: Add [McpTool] attribute to CalculateBatchByIngredient**

Find line 60:

```csharp
// TODO: Add [McpTool("batch.calculate_by_ingredient", "Calculate batch by ingredient quantity")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<CalculateBatchByIngredientResponse> CalculateBatchByIngredient(
```

Replace with:

```csharp
[McpTool("batch.calculate_by_ingredient", "Calculate batch by ingredient quantity")]
public async Task<CalculateBatchByIngredientResponse> CalculateBatchByIngredient(
```

**Step 4: Add [McpTool] attribute to CalculateBatchPlan**

Find line 86:

```csharp
// TODO: Add [McpTool("batch.calculate_plan", "Calculate batch plan for multiple products")]
//       when Microsoft.Extensions.AI v9.2+ is available
public async Task<CalculateBatchPlanResponse> CalculateBatchPlan(
```

Replace with:

```csharp
[McpTool("batch.calculate_plan", "Calculate batch plan for multiple products")]
public async Task<CalculateBatchPlanResponse> CalculateBatchPlan(
```

**Step 5: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.API`

Expected: Build succeeded

**Step 6: Run manufacture batch MCP tests**

Run: `dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~ManufactureBatchMcpToolsTests"`

Expected: All 4 manufacture batch tool tests passing

**Step 7: Commit manufacture batch tool attributes**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/ManufactureBatchMcpTools.cs
git commit -m "feat(mcp): add [McpTool] attributes to manufacture batch tools"
```

---

## Task 6: Register MCP Server in DI Container

**Files:**
- Modify: `backend/src/Anela.Heblo.API/MCP/McpModule.cs:14-27`

**Step 1: Uncomment MCP server registration**

Find lines 21-27:

```csharp
    // TODO: Register MCP server when Microsoft.Extensions.AI v9.2+ is available
    // services.AddMcpServer(options =>
    // {
    //     options.DiscoverToolsFrom<CatalogMcpTools>();
    //     options.DiscoverToolsFrom<ManufactureOrderMcpTools>();
    //     options.DiscoverToolsFrom<ManufactureBatchMcpTools>();
    // });
```

Replace with:

```csharp
    // Register MCP server with tool discovery
    services.AddMcpServer(options =>
    {
        options.DiscoverToolsFrom<CatalogMcpTools>();
        options.DiscoverToolsFrom<ManufactureOrderMcpTools>();
        options.DiscoverToolsFrom<ManufactureBatchMcpTools>();
    });
```

**Step 2: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.API`

Expected: Build succeeded - AddMcpServer extension method recognized

**Step 3: Run all MCP tests**

Run: `dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~MCP"`

Expected: All 16 MCP tool tests passing

**Step 4: Commit MCP server registration**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpModule.cs
git commit -m "feat(mcp): register MCP server in DI container"
```

---

## Task 7: Configure MCP Endpoint in Program.cs

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Program.cs` (after line with `app.UseAuthorization()`)

**Step 1: Find endpoint mapping section**

Locate the middleware configuration section after:

```csharp
app.UseAuthorization();
```

**Step 2: Add MCP endpoint mapping**

After `app.UseAuthorization();`, add:

```csharp
// Map MCP server endpoint for AI assistant integration
app.MapMcpServer("/mcp");
```

**Step 3: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.API`

Expected: Build succeeded

**Step 4: Run all tests**

Run: `dotnet test backend/Anela.Heblo.sln`

Expected: All 1,665+ tests passing

**Step 5: Commit MCP endpoint configuration**

```bash
git add backend/src/Anela.Heblo.API/Program.cs
git commit -m "feat(mcp): add MCP endpoint mapping at /mcp"
```

---

## Task 8: Manual Testing - Local Development

**Files:**
- Test: MCP endpoint via HTTP requests
- Verify: Tool discovery and invocation

**Step 1: Start the application locally**

Run: `cd backend/src/Anela.Heblo.API && dotnet run`

Expected: Application starts on https://localhost:5001

**Step 2: Test MCP endpoint availability**

Run: `curl -i https://localhost:5001/mcp`

Expected: HTTP 200 or appropriate MCP protocol response (not 404)

**Step 3: Test tool discovery (if MCP inspector available)**

Use MCP inspector tool or Claude Desktop to connect to: `https://localhost:5001/mcp`

Expected: All 15 tools discovered:
- 7 catalog tools (catalog.*)
- 4 manufacture order tools (manufacture.*)
- 4 manufacture batch tools (batch.*)

**Step 4: Test authentication**

Verify that MCP endpoint requires Microsoft Entra ID authentication.

Expected: Unauthenticated requests return 401 Unauthorized

**Step 5: Test a sample tool invocation**

Invoke `catalog.get_list` tool with authentication.

Expected: Returns catalog list successfully (same as API endpoint)

**Step 6: Document test results**

Create note of successful tests and any issues found.

---

## Task 9: Update Documentation - CLAUDE.md

**Files:**
- Modify: `CLAUDE.md` (MCP Server section around line 30-50)

**Step 1: Update MCP Server status**

Find:

```markdown
**Future Work:**
- Waiting for Microsoft.Extensions.AI v9.2+ for full MCP server registration
- TODO markers in code indicate where [McpTool] attributes will be added
```

Replace with:

```markdown
**Status:** ✅ Active - MCP server running on `/mcp` endpoint

**Endpoint:** `/mcp` (requires Microsoft Entra ID authentication)

**Transport:** SSE (Server-Sent Events) for web-based MCP clients
```

**Step 2: Add configuration section**

Add new section after tool list:

```markdown
**Configuration:**
- Endpoint: `https://heblo.anela.cz/mcp` (production)
- Endpoint: `https://heblo.stg.anela.cz/mcp` (staging)
- Endpoint: `https://localhost:5001/mcp` (local development)
- Authentication: Microsoft Entra ID (same as API)
- Transport: SSE (Server-Sent Events)

**MCP Client Setup:**
```json
{
  "mcpServers": {
    "anela-heblo": {
      "url": "https://heblo.anela.cz/mcp",
      "description": "Anela Heblo cosmetics workspace"
    }
  }
}
```
```

**Step 3: Remove "Future Work" mentions**

Remove or update any references to waiting for package updates.

**Step 4: Verify documentation**

Read through entire MCP Server section to ensure consistency.

**Step 5: Commit documentation update**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with active MCP server configuration"
```

---

## Task 10: Update Documentation - MCP Testing Guide

**Files:**
- Modify: `docs/testing/mcp-testing.md` (add integration testing section)

**Step 1: Add MCP server integration testing section**

At end of file, add:

```markdown
## MCP Server Integration Testing

### Prerequisites

- MCP inspector tool or MCP-compatible client
- Valid Microsoft Entra ID credentials
- Application running locally or on staging

### Testing MCP Endpoint

**1. Endpoint Availability:**

```bash
# Test endpoint is accessible
curl -i https://localhost:5001/mcp

# Expected: HTTP 401 Unauthorized (requires auth)
```

**2. Tool Discovery:**

Use MCP client to connect and discover tools:

```json
{
  "jsonrpc": "2.0",
  "method": "tools/list",
  "id": 1
}
```

Expected response: List of 15 tools with correct names and descriptions.

**3. Tool Invocation:**

Test individual tool invocation via MCP protocol:

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "catalog.get_list",
    "arguments": {
      "pageSize": 10,
      "pageNumber": 1
    }
  },
  "id": 2
}
```

Expected: Successful response with catalog data.

**4. Error Handling:**

Test McpToolException propagation:

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "catalog.get_detail",
    "arguments": {
      "catalogId": "00000000-0000-0000-0000-000000000000"
    }
  },
  "id": 3
}
```

Expected: MCP error response with appropriate error code.

### Testing with Claude Desktop

**1. Configure Claude Desktop:**

Add to Claude Desktop configuration:

```json
{
  "mcpServers": {
    "anela-heblo-local": {
      "url": "https://localhost:5001/mcp",
      "description": "Local Anela Heblo development"
    }
  }
}
```

**2. Test Tool Discovery:**

Open Claude Desktop and verify tools appear in tool palette.

**3. Test Tool Usage:**

Ask Claude: "Show me the catalog list"

Expected: Claude invokes `catalog.get_list` and displays results.

### Performance Testing

**Tool Invocation Latency:**

Test each tool's response time:

```bash
# Measure GetCatalogList latency
time curl -X POST https://localhost:5001/mcp \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"jsonrpc":"2.0","method":"tools/call","params":{"name":"catalog.get_list","arguments":{"pageSize":10}},"id":1}'
```

Expected: < 500ms for most tools (depends on database query complexity)

### Troubleshooting

**Issue: Tools not discovered**
- Verify `AddMcpServer` called in `McpModule.cs`
- Check `[McpTool]` attributes present on all methods
- Verify tool classes registered in DI container

**Issue: Authentication failures**
- Check Microsoft Entra ID configuration
- Verify `[Authorize]` attribute on tool classes
- Test API endpoints directly first

**Issue: Slow response times**
- Profile underlying MediatR handlers
- Check database query performance
- Verify network latency to database
```

**Step 2: Commit testing guide update**

```bash
git add docs/testing/mcp-testing.md
git commit -m "docs: add MCP server integration testing guide"
```

---

## Task 11: Update Documentation - Continuation Plan

**Files:**
- Modify: `docs/plans/2026-02-26-mcp-server-continuation.md:1-10`

**Step 1: Update status header**

Find:

```markdown
**Status:** Implementation Complete - Waiting for Package Update
**Created:** 2026-02-26
**Branch:** `feature/mcp_server`
```

Replace with:

```markdown
**Status:** ✅ Completed - MCP Server Active
**Created:** 2026-02-26
**Activated:** 2026-02-26 (update with actual date)
**Branch:** `feature/mcp_server`
**Merged to:** `main`
```

**Step 2: Add completion note at top**

After status section, add:

```markdown
## Activation Complete

The MCP server has been successfully activated with Microsoft.Extensions.AI v9.2+:

- ✅ All 15 [McpTool] attributes added
- ✅ MCP server registered in DI container
- ✅ Endpoint mapped at `/mcp`
- ✅ All 16 tests passing
- ✅ Documentation updated
- ✅ Manual testing successful

For implementation details, see: `docs/plans/2026-02-26-mcp-server-activation.md`

---
```

**Step 3: Commit continuation plan update**

```bash
git add docs/plans/2026-02-26-mcp-server-continuation.md
git commit -m "docs: mark MCP server continuation plan as completed"
```

---

## Task 12: Final Verification

**Files:**
- Verify: All tests passing
- Verify: Build successful
- Verify: Code formatting
- Verify: Git status clean

**Step 1: Run full test suite**

Run: `dotnet test backend/Anela.Heblo.sln`

Expected: All tests passing (1,665+ tests)

**Step 2: Verify build**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: Build succeeded with 0 errors, 0 warnings

**Step 3: Check code formatting**

Run: `cd backend && dotnet format --verify-no-changes`

Expected: No formatting issues

**Step 4: Verify git status**

Run: `git status`

Expected: All changes committed, working tree clean

**Step 5: Review commit history**

Run: `git log --oneline -12`

Expected: 12 commits from this plan (or merged into fewer commits)

---

## Task 13: Deploy to Staging

**Files:**
- Deploy: Staging environment
- Test: Integration tests on staging

**Step 1: Push changes to remote**

Run: `git push origin feature/mcp_server`

Expected: Successfully pushed to remote

**Step 2: Wait for CI/CD pipeline**

Monitor CI/CD pipeline for:
- ✅ Build successful
- ✅ Tests passing (1,665+ tests)
- ✅ Docker image built
- ✅ Deployed to staging

Expected: All pipeline stages pass

**Step 3: Test MCP endpoint on staging**

Run: `curl -i https://heblo.stg.anela.cz/mcp`

Expected: MCP endpoint accessible (requires auth)

**Step 4: Test tool discovery on staging**

Use MCP client to connect to staging endpoint.

Expected: All 15 tools discovered

**Step 5: Test sample tool invocations on staging**

Test 3-5 representative tools on staging.

Expected: All tools work correctly with real data

**Step 6: Document staging test results**

Create note of successful staging tests.

---

## Task 14: Create Pull Request

**Files:**
- Create: GitHub Pull Request
- Update: PR description with checklist

**Step 1: Create pull request**

Run: `gh pr create --title "feat: activate MCP server with [McpTool] attributes" --base main`

Use this PR description:

```markdown
## Summary

Activates MCP server functionality by adding [McpTool] attributes and server registration using Microsoft.Extensions.AI v9.2+.

## Changes

- Updated Microsoft.Extensions.AI to v9.2+ (from preview version)
- Added [McpTool] attributes to 15 tool methods (7 catalog + 4 manufacture + 4 batch)
- Uncommented MCP server registration in McpModule.cs
- Added MCP endpoint mapping in Program.cs (`/mcp`)
- Updated documentation (CLAUDE.md, mcp-testing.md, continuation plan)

## Testing

- ✅ All 1,665+ tests passing
- ✅ All 16 MCP tool tests passing
- ✅ Manual testing: Tool discovery works
- ✅ Manual testing: Tool invocation works
- ✅ Manual testing: Authentication works
- ✅ Staging deployment successful
- ✅ Integration testing on staging passed

## Implementation Details

**MCP Tools (15 total):**
- Catalog: `catalog.get_list`, `catalog.get_detail`, `catalog.get_composition`, `catalog.get_materials_for_purchase`, `catalog.autocomplete`, `catalog.get_product_usage`, `catalog.get_warehouse_statistics`
- Manufacture: `manufacture.get_orders`, `manufacture.get_order`, `manufacture.get_calendar`, `manufacture.get_responsible_persons`
- Batch: `batch.get_template`, `batch.calculate_by_size`, `batch.calculate_by_ingredient`, `batch.calculate_plan`

**Endpoint:** `/mcp` (SSE transport, requires Microsoft Entra ID auth)

## Deployment Notes

- MCP endpoint uses same authentication as API (Microsoft Entra ID)
- No database migrations required
- No breaking changes to existing API
- Backward compatible (MCP is additive)

## References

- Design: `docs/plans/2026-02-26-mcp-server-design.md`
- Original Implementation: `docs/plans/2026-02-26-mcp-server-implementation.md`
- Continuation Plan: `docs/plans/2026-02-26-mcp-server-continuation.md`
- Activation Plan: `docs/plans/2026-02-26-mcp-server-activation.md`
- Testing Guide: `docs/testing/mcp-testing.md`
```

**Step 2: Assign reviewers (if applicable)**

If using AI PR review, trigger code review.

**Step 3: Wait for PR approval**

Expected: PR approved after review

---

## Task 15: Merge and Deploy to Production

**Files:**
- Merge: Pull request to main
- Deploy: Production environment

**Step 1: Merge pull request**

Run: `gh pr merge --squash` (or merge via GitHub UI)

Expected: PR merged to main

**Step 2: Wait for production CI/CD**

Monitor production deployment pipeline.

Expected: Successfully deployed to production

**Step 3: Test MCP endpoint on production**

Run: `curl -i https://heblo.anela.cz/mcp`

Expected: MCP endpoint accessible

**Step 4: Test tool discovery on production**

Use MCP client to connect to production endpoint.

Expected: All 15 tools discovered

**Step 5: Document production verification**

Create note of successful production deployment.

**Step 6: Final commit - update this plan status**

Mark this implementation plan as completed.

---

## Success Criteria Checklist

All criteria must be met before considering this plan complete:

- [ ] Microsoft.Extensions.AI v9.2+ package installed
- [ ] All 15 [McpTool] attributes added (7 catalog + 4 manufacture + 4 batch)
- [ ] MCP server registered in McpModule.cs
- [ ] MCP endpoint mapped in Program.cs
- [ ] All 1,665+ tests passing
- [ ] All 16 MCP tool tests passing
- [ ] Manual testing successful (tool discovery + invocation)
- [ ] Documentation updated (CLAUDE.md, mcp-testing.md, continuation plan)
- [ ] Code formatting verified (dotnet format)
- [ ] Staging deployment successful
- [ ] Integration tests on staging passed
- [ ] Pull request created and approved
- [ ] Production deployment successful
- [ ] MCP endpoint verified on production

## Rollback Plan

If issues are discovered after deployment:

**Step 1: Identify issue severity**
- Critical (service down): Immediate rollback
- Major (tools not working): Rollback within 1 hour
- Minor (single tool issue): Fix forward

**Step 2: Rollback procedure**

```bash
# Revert to previous version
git revert HEAD
git push origin main

# Or restore previous Docker image
docker pull anelapajgr/heblo:previous-tag
# Redeploy previous image to Azure Web App
```

**Step 3: Notify stakeholders**

Document the issue and rollback in incident log.

**Step 4: Root cause analysis**

Investigate and fix issue in separate branch.

## Estimated Time

**Total time:** ~5 hours

- Task 1-2: Package update (30 min)
- Task 3-5: Add attributes (1 hour)
- Task 6-7: Server registration (30 min)
- Task 8: Manual testing (1 hour)
- Task 9-11: Documentation (1 hour)
- Task 12: Verification (30 min)
- Task 13: Staging deployment (30 min)
- Task 14-15: PR and production (30 min)

## Notes

- This plan assumes Microsoft.Extensions.AI v9.2+ is available
- If API changes from expected, adjust attribute/registration syntax accordingly
- All TODO markers in code will be removed during this implementation
- No database migrations required
- No breaking changes to existing API
- MCP server is an additive feature

## References

- **Design Document:** `docs/plans/2026-02-26-mcp-server-design.md`
- **Original Implementation:** `docs/plans/2026-02-26-mcp-server-implementation.md`
- **Continuation Plan:** `docs/plans/2026-02-26-mcp-server-continuation.md`
- **Testing Guide:** `docs/testing/mcp-testing.md`
- **CLAUDE.md:** See "MCP Server" section
- **Microsoft.Extensions.AI Docs:** https://github.com/dotnet/extensions

---

**Created:** 2026-02-26
**Status:** Ready for execution when Microsoft.Extensions.AI v9.2+ is released