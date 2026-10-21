# Specification: Make POST /mcp 400 diagnostics fire on the exception path

## Summary
`McpBadRequestMiddleware` (added by #4052) logs a structured `MCP bad request` warning (EventId 5932) only when `_next(context)` returns normally with status 400. Production telemetry shows a 45.6% POST /mcp 400 rate (108/237, 2026-08-30..09-05) and no diagnostic traces for post-merge failures. This feature closes the diagnostic gap so the 400 population can be attributed to clients, and records the verification steps that separate "not deployed" from "middleware cannot see the failure".

## Background
- Issue #4082 follows #4042 / PR #4052 (diagnostics-only). The issue proposes two hypotheses: (1) deployment lag, (2) response already committed before the post-`_next` check.
- Code reading adds a third, stronger hypothesis (H3): the MCP SDK (`ModelContextProtocol.AspNetCore` 1.0.0) may signal malformed requests by throwing (e.g. `BadHttpRequestException`, `JsonException`, `ArgumentException`). The exception propagates through `McpBadRequestMiddleware.HandlePostAsync` (no try/catch, so the post-`_next` status check is skipped) up to `UseExceptionHandler()`, where `ArgumentExceptionHandler` / `ValidationExceptionHandler` convert it to a 400 ProblemDetails. Middleware order is: ExceptionHandler -> Routing -> Auth -> RateLimiter -> Timeouts -> McpBadRequestMiddleware -> endpoint (`ApplicationBuilderExtensions.cs`). The request telemetry would then show 400 while the middleware logged nothing.
- Hypothesis 2 (status not mutable) is unlikely for the status read itself: reading `Response.StatusCode` after `_next` is always allowed. Streamed SSE responses would be 200 anyway.
- Adaptive sampling (`ApplicationInsightsExtensions.cs`, 5 items/s in Production) keeps operation-correlated items together in general, but `Exception` and `Event` types are excluded from sampling, so exception telemetry is the reliable place to check H3.

## Functional Requirements

### FR-1: Log diagnostics when a POST /mcp request throws
When `_next(context)` throws for a POST to `/mcp`, the middleware logs one Warning with a new EventId (5933, name `McpRequestException`) containing the same context fields as the existing bad-request log (method, path, User-Agent, Origin, Accept, Content-Type, Mcp-Session-Id presence and 8-char prefix, remote IP, elapsed ms) plus `ExceptionType`, then rethrows the original exception unchanged (`throw;`).
**Acceptance criteria:**
- A throwing `next` produces exactly one Warning with EventId 5933 and the exception type name in the message.
- The exception is rethrown and is the same instance (no swallowing, no response rewriting).
- The log never contains the Authorization header value or the full session id.
- The existing 400 path (EventId 5932) behaviour is unchanged.

### FR-2: Same behaviour for GET /mcp
The GET path gets the same exception logging so both verbs are covered. Existing GET 404 short-circuit behaviour is unchanged.
**Acceptance criteria:**
- A throwing `next` on GET /mcp with a valid Accept header logs EventId 5933 and rethrows.

### FR-3: Documentation
`docs/integrations/mcp-server.md` section "Diagnostics - Bad-request telemetry" documents EventId 5933, its fields, and how to query it (`traces | where customDimensions.EventId == "5933"`) together with the `exceptions` table correlation by `operation_Id`.
**Acceptance criteria:**
- Doc lists both events and the verification runbook (deployed-SHA check, staging repro with malformed POST body).

## Non-Functional Requirements

### NFR-1: Performance
No allocation or work on the success path beyond a try/catch frame; logging only on failure.

### NFR-2: Security
No request body, tokens or full session ids are logged. Exception message is not logged (type only) to avoid leaking input; the full exception remains available via the existing exception telemetry.

## Data Model
None.

## API / Interface Design
No endpoint or contract changes. `McpBadRequestMiddleware` gains an exception-path log; no DTO or OpenAPI change.

## Dependencies
- `ModelContextProtocol.AspNetCore` 1.0.0 behaviour on malformed POST bodies (to be observed in a local/Staging repro).
- Existing exception handlers in `Infrastructure/ExceptionHandling`.

## Out of Scope
- Changing how the MCP SDK validates requests or rewriting any response.
- Reducing the 400 rate itself (follows once client attribution data exists).
- Changing sampling configuration.
- Operational confirmation of the deployed SHA in Production (manual step documented in FR-3, not automated).

## Open Questions
None.

## Status: COMPLETE
