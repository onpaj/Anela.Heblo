## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs:100` — `LogMcpRequestException` duplicates header/session-id extraction with `LogBadMcpRequest`; a shared helper could reduce duplication. Not blocking.

Verified: exception is rethrown unchanged (`throw;`), only the exception type name is logged (no message, no Authorization, session id truncated), client-aborted cancellations are not logged, 5932 path unchanged. 37 McpBadRequestMiddleware tests pass; process-docs check reports no docs affected.
