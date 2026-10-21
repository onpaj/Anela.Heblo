# Implementation: exception-path-middleware

## What was implemented
Added `RequestExceptionEvent` (5933 "McpRequestException"), private `InvokeNextLoggingExceptionsAsync` (try/catch with `when` filter excluding aborted-request OperationCanceledException, logs then `throw;`) used by POST and GET (after Accept check) paths, and `LogMcpRequestException` (exception type name only, truncated session id, no message/Authorization). Class XML summary updated. 5932 template, 404 short-circuit and status codes untouched.

## Files modified
- `backend/src/Anela.Heblo.API/MCP/McpBadRequestMiddleware.cs`

## Tests
`dotnet build backend/src/Anela.Heblo.API`: 0 errors. `dotnet format` run on the file. `dotnet test --filter McpBadRequestMiddlewareTests`: 37 passed, 0 failed.

## Notes
Field extraction duplicated minimally in LogMcpRequestException rather than refactoring LogBadMcpRequest (surgical change).

## Status
DONE
