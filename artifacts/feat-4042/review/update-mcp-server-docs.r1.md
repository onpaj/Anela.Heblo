# Code Review: update-mcp-server-docs

## Summary
The Diagnostics section was successfully added to `docs/integrations/mcp-server.md` with all required content: event identity (EventName, EventId per method, log category/level), complete customDimensions table, PII/secret hygiene notes, and both canonical Kusto queries. Markdown formatting is well-formed, heading level matches peer sections, and the middleware file referenced in the docs exists.

## Review Result: PASS

### task: update-mcp-server-docs
**Status:** PASS

## Overall Notes
- Spec compliance: Complete. All required subsections present with content matching spec verbatim (Event identity, customDimensions table with all 11 fields, "Not logged" list, both Kusto queries).
- Heading level: `##` — consistent with all peer sections (Available Tools, Implementation, Tool Pattern, Endpoints, Client Setup, Tests).
- Placement: Correctly inserted between "Client Setup" and "Tests", before closing material.
- Markdown: Code fences balanced (two `kusto` blocks properly closed), table well-formed, no leakage between sections.
- Commit verified: `c31b83204c4` adds 58 insertions to `docs/integrations/mcp-server.md`.
- Middleware reference: `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs` exists.
