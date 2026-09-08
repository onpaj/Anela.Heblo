# MCP Server

The application exposes MCP tools for AI assistants to query catalog data, manufacturing orders, perform batch calculations, user-directory lookups, and meeting notes.

## Available Tools

**Catalog (8)** — all require the `Products_Catalog` permission, except `GetProductMargins` which requires `Products_ProductMargins`.
- `GetCatalogList` — list products with filtering/pagination
- `GetCatalogDetail` — detailed product information
- `GetProductComposition` — product composition/ingredients
- `GetMaterialsForPurchase` — materials needed for purchase
- `GetAutocomplete` — product search for autocomplete
- `GetProductUsage` — product usage in compositions
- `GetWarehouseStatistics` — warehouse statistics
- `GetProductMargins` — product margins (M0/M1/M2 + monthly history); requires the Products_ProductMargins permission

**Manufacture Orders (3)** — require the `Manufacture_ManufactureOrders` permission.
- `GetManufactureOrders` — list manufacture orders with filtering
- `GetManufactureOrder` — single manufacture order details
- `GetCalendarView` — calendar view of manufacture orders

**Manufacture Batch (4)** — require the `Manufacture_BatchPlanning` permission.
- `GetBatchTemplate` — batch template for product
- `CalculateBatchBySize` — calculate batch by desired size
- `CalculateBatchByIngredient` — calculate batch by ingredient quantity
- `CalculateBatchPlan` — batch plan for multiple products

**User Management (1)** — requires the `Admin_Administration` permission.
- `GetGroupMembers` — Entra ID group members by group ID

**Knowledge Base (2)** — require the `Customer_KnowledgeBase` permission.
- `SearchKnowledgeBase` — semantic search over ingested documents, returns ranked chunks with source references
- `AskKnowledgeBase` — AI-generated answer grounded in company documents, returns prose answer with cited sources

**Leaflet (1)** — requires the `Marketing_Leaflet` permission.
- `GenerateLeaflet` — generates a marketing leaflet in Czech Markdown using the knowledge base and historical leaflets as style references

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

## Diagnostics — Bad-request telemetry

`McpBadRequestMiddleware` (see `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`) emits a single structured warning-level log entry to Application Insights whenever `/mcp` returns HTTP 400. The middleware observes both `GET` and `POST` responses.

**Event identity**
- `EventName`: `McpBadRequest` (shared by both channels)
- `EventId`: `5931` (GET) / `5932` (POST)
- Log category: `Anela.Heblo.API.MCP.McpBadRequestMiddleware`
- Log level: `Warning`

**customDimensions**

| Key | Type | Notes |
|---|---|---|
| `HTTPMethod` | string | `GET` or `POST` |
| `Path` | string | Full request path including query string |
| `StatusCode` | int | Always `400` |
| `UserAgent` | string | `"(missing)"` sentinel when absent |
| `Origin` | string | `"(missing)"` sentinel when absent |
| `Accept` | string | `"(missing)"` sentinel when absent |
| `ContentType` | string | `"(missing)"` sentinel when absent |
| `McpSessionIdPresent` | bool | `false` if header absent or empty |
| `McpSessionIdPrefix` | string | First 8 chars of `Mcp-Session-Id`, or `"(missing)"` |
| `RemoteIp` | string | `Connection.RemoteIpAddress`, or `"(unknown)"`. May be the Azure Front Door IP until `ForwardedHeaders` middleware is wired — tracked separately. |
| `ElapsedMs` | double | Request duration |

**Not logged** (PII / secret hygiene): request body content, full `Mcp-Session-Id`, `Authorization`, `Cookie`, `X-Api-Key`.

**Canonical Kusto query**

```kusto
traces
| where customDimensions.EventName == "McpBadRequest"
| extend Method   = tostring(customDimensions.HTTPMethod),
         UA       = tostring(customDimensions.UserAgent),
         SidPres  = tobool(customDimensions.McpSessionIdPresent),
         SidPref  = tostring(customDimensions.McpSessionIdPrefix),
         Origin   = tostring(customDimensions.Origin),
         RemoteIp = tostring(customDimensions.RemoteIp),
         Elapsed  = todouble(customDimensions.ElapsedMs)
| summarize count(), avg(Elapsed), max(Elapsed) by bin(timestamp, 1h), Method, SidPres, UA
| order by timestamp desc
```

**Scanner/probe segregation query**

```kusto
traces
| where customDimensions.EventName == "McpBadRequest"
       and customDimensions.HTTPMethod == "POST"
| extend UA      = tostring(customDimensions.UserAgent),
         SidPres = tobool(customDimensions.McpSessionIdPresent),
         IP      = tostring(customDimensions.RemoteIp)
| where UA == "(missing)" and SidPres == false
| summarize probe_count = count() by bin(timestamp, 1h), IP
| order by probe_count desc
```

## Tests

- Location: `backend/test/Anela.Heblo.Tests/MCP/Tools/`
- Coverage: 29 tests (parameter mapping, JSON serialization, error handling)
- See existing test files for patterns.
