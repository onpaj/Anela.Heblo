# Implementation: update-mcp-server-docs

## What was implemented
Added a "Diagnostics — Bad-request telemetry" section to `docs/integrations/mcp-server.md` documenting the `McpBadRequest` structured log event emitted by `McpBadRequestMiddleware` on HTTP 400 responses from `/mcp`. The section covers event identity (EventName, EventId per method, log category/level), the full `customDimensions` schema, what is deliberately not logged, and two canonical Kusto queries (general summary and scanner/probe segregation).

## Files created/modified
- `docs/integrations/mcp-server.md` — inserted a new `## Diagnostics — Bad-request telemetry` section between "Client Setup" and "Tests", matching the `##` heading level used by all peer sections in the file. Content matches the arch-review Amendment 6 spec verbatim, with each Kusto block fenced in a single ` ```kusto ` fence nested inside the normal `##` section (no outer 4-backtick fencing needed in the actual file).

## Tests
N/A (documentation-only change)

## How to verify
- `cat docs/integrations/mcp-server.md` and inspect the new "Diagnostics — Bad-request telemetry" section (between "Client Setup" and "Tests").
- Confirm headings nest correctly and both ` ```kusto ` code fences are balanced (open/close cleanly, no leakage into surrounding sections).
- Confirm `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs` exists (referenced in the new section) — verified present in this checkout.

## Notes
No deviations from the task spec. The insertion point chosen was after "Client Setup" (the last "how to use the server" section) and before "Tests" (the file has no "Out of scope"/appendix section, so this was the most natural landing spot before the file's closing material). Heading level `##` matches all existing peer sections (Available Tools, Implementation, Tool Pattern, Endpoints, Client Setup, Tests).

## PR Summary
Documents the `McpBadRequest` Application Insights log event (emitted by `McpBadRequestMiddleware` on `/mcp` 400 responses) in the MCP server docs, per arch-review Amendment 6, so this observability signal — its `customDimensions` schema and canonical/scanner-segregation Kusto queries — is discoverable from `docs/integrations/mcp-server.md` rather than only from the middleware source.

### Changes
- `docs/integrations/mcp-server.md` — added Diagnostics section

## Status
DONE
