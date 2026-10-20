# Design: POST /mcp 400 diagnostics on exception path

## Component Design
`McpBadRequestMiddleware` (backend/src/Anela.Heblo.API/MCP):
- `InvokeAsync`: unchanged routing (POST -> `HandlePostAsync`; non-MCP -> next; GET -> Accept check, 404 short-circuit, then next).
- New private `InvokeNextLoggingExceptionsAsync(HttpContext context, double startTimestamp)` used by both the GET and POST paths:
  ```csharp
  try { await _next(context); }
  catch (Exception ex) when (!(ex is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
  {
      LogMcpRequestException(context, ex.GetType().Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
      throw;
  }
  ```
- New `RequestExceptionEvent = new(5933, "McpRequestException")`.
- `LogMcpRequestException` reuses the same field extraction as `LogBadMcpRequest` (extract a private `BuildContext` helper or duplicate minimally) and emits a Warning:
  `MCP request threw: HTTPMethod={HTTPMethod} Path={Path} ExceptionType={ExceptionType} UserAgent=... Origin=... Accept=... ContentType=... McpSessionIdPresent=... McpSessionIdPrefix=... RemoteIp=... ElapsedMs=...`
- The 5932 template and field order stay as they are.

## Data Schemas
Structured log properties (Application Insights `customDimensions`): HTTPMethod, Path, ExceptionType (new, 5933 only), UserAgent, Origin, Accept, ContentType, McpSessionIdPresent, McpSessionIdPrefix, RemoteIp, ElapsedMs. No DB, API or DTO changes.
