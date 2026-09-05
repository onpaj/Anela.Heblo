# Implementation: read-existing-middleware-and-tests

## What was implemented
Read-only orientation task. No production or test code was changed. Read
`McpBadRequestMiddleware.cs`, `McpDiagnosticsMiddleware.cs`, the middleware
registration order in `ApplicationBuilderExtensions.cs`, and the existing
`McpBadRequestMiddlewareTests.cs` test scaffold, then confirmed the backend
still builds and the existing middleware test suite still passes as a
baseline ahead of implementing POST /mcp 400 diagnostics logging.

## Files created/modified
none — read-only task

## Tests
Baseline: `dotnet test --filter "FullyQualifiedName~McpBadRequestMiddlewareTests"`
— **Passed! Failed: 0, Passed: 18, Skipped: 0, Total: 18**, Duration 61 ms.

The suite covers: non-MCP-path pass-through, POST /mcp pass-through (no
short-circuit/logging), GET /mcp probe blocking (missing/invalid Accept
header → 404 without calling `next`, plus an Information-level log
assertion), GET /mcp with a valid `Accept` (event-stream / json / multi-value
list) calling through to `next`, GET /mcp 400-response diagnostics logging
(Warning-level log assertion) vs. 200/401 responses (no warning logged), and
a `[Theory]`-driven unit test of the static `HasValidMcpAcceptHeader` helper
across several Accept-header shapes.

## Notes

- **`dotnet build`**: succeeded, 0 errors (261 pre-existing nullable-reference
  warnings across the test project, unrelated to MCP code).
- **Test run hit the documented nodeReuse deadlock** (see
  `memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md`): the first
  `dotnet test --filter ...` attempt hung after generating the access-matrix
  artifacts (all MSBuild/VBCSCompiler processes frozen in `futex_do_wait`,
  confirmed via `ps -o wchan`). Fixed by `kill -9` on the stuck tree +
  `dotnet build-server shutdown`, then retrying with
  `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 dotnet test ... -m:1 -nodeReuse:false -p:UseSharedCompilation=false`,
  which completed cleanly. Worth reusing that flag combo up front for the
  implementation task rather than re-hitting the hang.
- **`McpBadRequestMiddleware.cs`**:
  - Registered as normal ASP.NET Core middleware via
    `public async Task InvokeAsync(HttpContext context)` (constructor takes
    `RequestDelegate next, ILogger<McpBadRequestMiddleware> logger`).
  - GET-request predicate: `private static bool IsMcpGetRequest(HttpContext context)`
    — `context.Request.Method == HttpMethods.Get && context.Request.Path.StartsWithSegments("/mcp")`.
  - GET short-circuit for headerless/invalid-Accept probes: inside
    `InvokeAsync`, if `IsMcpGetRequest` is true and
    `!HasValidMcpAcceptHeader(context)`, it logs Information and sets
    `context.Response.StatusCode = StatusCodes.Status404NotFound` **without**
    calling `_next(context)`.
  - **Important finding vs. the task's assumption**: there is **no separate
    named method** for the "GET 400" log call — the log-reading-and-emitting
    code is inline in `InvokeAsync` (lines 63–76), directly after
    `await _next(context)`, guarded by
    `if (context.Response.StatusCode == StatusCodes.Status400BadRequest)`. It
    reads `UserAgent`, `Accept`, `Origin` from `context.Request.Headers` and
    calls `_logger.LogWarning(...)` once. If the planned implementation wants
    a reusable single method (e.g. to share with a future POST path), this
    inline block will need to be extracted first — it does not already exist
    as an extractable method with a name to reuse.
- **`McpDiagnosticsMiddleware.cs`**:
  - `TruncateId` is `internal static string TruncateId(string id)` at line 54:
    `id.Length > 8 ? id[..8] + "***" : "***"`.
  - Current sentinel for an absent/short session ID: the literal string
    `"***"` (returned whole when `id.Length <= 8`; appended after an 8-char
    prefix otherwise). Matches the design doc's note.
  - Single caller: inside `InvokeAsync`, in the `if (sessionId is not null)`
    branch, as `TruncateId(sessionId)` passed into the `LogWarning` call's
    `{SessionIdPrefix}` placeholder. No other call sites in the file or
    (per this read-only pass) found elsewhere.
- **`ApplicationBuilderExtensions.cs`** (lines 110–140, actually confirmed at
  ~124–138): `app.MapControllers()` is called, then
  `app.UseMiddleware<McpBadRequestMiddleware>()` (line 128), then
  `app.UseMiddleware<McpDiagnosticsMiddleware>()` (line 132), then
  `app.MapMcp("/mcp").RequireAuthorization().WithRequestTimeout(...)` (line
  136). Order confirmed: `McpBadRequestMiddleware` → `McpDiagnosticsMiddleware`
  → `MapMcp("/mcp")`. No new middleware registration is required for a
  POST-path change — both middlewares are already in the pipeline ahead of
  the MCP endpoint mapping.
- **`McpBadRequestMiddlewareTests.cs`**:
  - `CreateContext(string method, string path, int responseStatus = 200, string? acceptHeader = null, string? userAgent = null, string? origin = null)`
    — a static helper building a `DefaultHttpContext`, setting `Method`,
    `Path`, `Response.StatusCode` (defaults to 200), and optionally the
    `Accept`/`User-Agent`/`Origin` request headers. **It does support setting
    the response status directly** via `responseStatus` (used to simulate
    "next already set 400" before invoking the middleware) — a POST-path 400
    test can reuse this helper as-is by passing `method: "POST"`.
  - `VerifyNoLogCalled()` — an instance helper (uses the class's
    `_loggerMock` field) asserting
    `_loggerMock.Verify(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never)`.
  - Logger mock: `private readonly Mock<ILogger<McpBadRequestMiddleware>> _loggerMock;`
    constructed once in the test class constructor and passed into
    `CreateMiddleware(RequestDelegate next) => new(next, _loggerMock.Object)`.
  - Exact `Verify(...)` shape used to assert one warning with specific
    content (from `InvokeAsync_GetMcpWith400Response_LogsWarningWithDiagnostics`):
    ```csharp
    _loggerMock.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) =>
                v.ToString()!.Contains("400") &&
                v.ToString()!.Contains("mcp-client/2.0")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
    ```
    i.e. it matches the log level exactly, ignores `EventId`, asserts on
    substrings of the formatted-state's `ToString()` (via the
    `It.Is<It.IsAnyType>((v, _) => ...)` idiom needed for Moq's `ILogger`
    generic-state overload), asserts `exception == null`, and ignores the
    formatter delegate. The Information-level probe-log assertion
    (`InvokeAsync_GetMcpWithoutAcceptHeader_LogsInformationWithUserAgent`)
    follows the identical pattern at `LogLevel.Information`.

## PR Summary
Read-only orientation pass over the MCP bad-request middleware, diagnostics middleware, registration extension, and existing test scaffold ahead of implementing POST /mcp 400 diagnostics logging (issue #4042). No code changes.

### Changes
- (none — orientation only)

## Status
DONE
