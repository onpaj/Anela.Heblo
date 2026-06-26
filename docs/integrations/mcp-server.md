# MCP Server

The application exposes MCP tools for AI assistants to query catalog data, manufacturing orders, perform batch calculations, user-directory lookups, and meeting notes.

## Available Tools

**Catalog (8)**
- `GetCatalogList` — list products with filtering/pagination
- `GetCatalogDetail` — detailed product information
- `GetProductComposition` — product composition/ingredients
- `GetMaterialsForPurchase` — materials needed for purchase
- `GetAutocomplete` — product search for autocomplete
- `GetProductUsage` — product usage in compositions
- `GetWarehouseStatistics` — warehouse statistics
- `GetProductMargins` — product margins (M0/M1/M2 + monthly history); requires the Products_ProductMargins permission

**Manufacture Orders (3)**
- `GetManufactureOrders` — list manufacture orders with filtering
- `GetManufactureOrder` — single manufacture order details
- `GetCalendarView` — calendar view of manufacture orders

**Manufacture Batch (4)**
- `GetBatchTemplate` — batch template for product
- `CalculateBatchBySize` — calculate batch by desired size
- `CalculateBatchByIngredient` — calculate batch by ingredient quantity
- `CalculateBatchPlan` — batch plan for multiple products

**User Management (1)**
- `GetGroupMembers` — Entra ID group members by group ID

**Knowledge Base (2)**
- `SearchKnowledgeBase` — semantic search over ingested documents, returns ranked chunks with source references
- `AskKnowledgeBase` — AI-generated answer grounded in company documents, returns prose answer with cited sources

**Meeting Notes (4)** — read-only; all require the `anela.meetings.read` permission. Per-meeting visibility (Public / Private / Restricted) is enforced per caller, same as the web UI.
- `ListMeetings` — list meetings (summary level: subject, summary, status, task counts; no raw transcript) with search, status filter, and pagination
- `GetMeetingSummary` — summary and metadata of a single meeting (no raw transcript, no task detail)
- `GetMeetingTranscript` — full raw transcript text of a single meeting
- `GetMeetingTasks` — proposed task list extracted from a single meeting

## Implementation

- Tool classes: `backend/src/Anela.Heblo.API/MCP/Tools/`
- Registration: `McpModule.cs` (`AddMcpServer` + `WithHttpTransport` + `WithTools`)
- Pattern: thin wrappers around MediatR handlers
- Error handling: `McpException` from `ModelContextProtocol` namespace
- Authentication: Microsoft Entra ID (same as the rest of the API)

## Tool Pattern

```csharp
[McpServerToolType]
public class CatalogTools
{
    [McpServerTool]
    public async Task<string> GetCatalogList([Description("...")] string query)
    {
        // Call MediatR, return JSON-serialized result
        // Throw McpException on errors
    }
}
```

## Endpoints

| Environment | URL |
|---|---|
| Production | `https://heblo.anela.cz/mcp` |
| Staging | `https://heblo.stg.anela.cz/mcp` |
| Local | `https://localhost:5001/mcp` |

Transport: SSE (Server-Sent Events)  
SDK: [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)

## Client Setup

Add to `claude_desktop_config.json` (or equivalent MCP client config):

```json
{
  "mcpServers": {
    "anela-heblo": {
      "url": "https://heblo.anela.cz/mcp",
      "transport": "sse",
      "authentication": {
        "type": "bearer",
        "token": "YOUR_ENTRA_ID_TOKEN"
      }
    }
  }
}
```

For local development use `https://localhost:5001/mcp`.

## Tests

- Location: `backend/test/Anela.Heblo.Tests/MCP/Tools/`
- Coverage: 29 tests (parameter mapping, JSON serialization, error handling)
- See existing test files for patterns.
