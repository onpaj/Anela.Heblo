### task: update-mcp-server-docs

**Files:**
- Modify: `docs/integrations/mcp-server.md`

Add a "Diagnostics" section describing the `McpBadRequest` log event, its `customDimensions`, and the canonical Kusto query. This is the arch-review's Amendment 6 — developers should be able to discover the observability signal from the MCP docs.

- [ ] **Step 1: Read the current MCP docs to find the right anchor**

Run: `cat docs/integrations/mcp-server.md | head -80`
Look for a natural insertion point: after the "Endpoints" or "Client config" section, before "Out of scope"/appendix material. Note the heading level used by peers in the file.

- [ ] **Step 2: Append the Diagnostics section**

Insert into `docs/integrations/mcp-server.md` (use `##` if peer sections use `##`, else adjust):

````markdown
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
````

- [ ] **Step 3: Commit**

```bash
git add docs/integrations/mcp-server.md
git commit -m "docs(mcp): document McpBadRequest diagnostic log and Kusto queries

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---
