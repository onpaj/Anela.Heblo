# Specification: Remove dead try-catch-log-rethrow in GetConfigurationHandler

## Summary
`GetConfigurationHandler.Handle()` in the Configuration module wraps its entire body in a try-catch block that only logs the exception and immediately rethrows it, adding no error-handling value while duplicating what the global ASP.NET Core exception-handling middleware already does. This specification covers removing that dead catch block so the handler relies solely on the global exception handler for logging and translating unhandled exceptions.

## Background
`GetConfigurationHandler.Handle()` (`backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`, lines 27–48) calls `BuildApplicationConfiguration()`, which only reads values from `IConfiguration` and assembly metadata (no I/O, no external calls, no realistic failure mode) and constructs a plain value object. The surrounding try-catch:

```csharp
try
{
    _logger.LogDebug("Handling GetConfiguration request");
    var appConfig = BuildApplicationConfiguration();
    // ...
    return response;
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error retrieving application configuration");
    throw;
}
```

performs no recovery, no exception transformation, and adds no error signal beyond what already happens: the app already registers a global exception-handling pipeline (`app.UseExceptionHandler()` in `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs`, with a handler chain composed via `AddExceptionHandler<T>()` in DI) which logs unhandled exceptions (see the `logger.LogError(...)` call in that same file) before translating them into an HTTP response. Because both the handler's catch block and the global handler would call `LogError` for the same exception, an exception thrown inside this method today is logged twice.

This is a pure internal code-quality cleanup identified by the repository's automated daily arch-review routine (`docs/features/` arch-review tooling) — it changes no observable behavior for any caller of the `GetConfiguration` endpoint.

## Functional Requirements

### FR-1: Remove the dead try-catch block from `GetConfigurationHandler.Handle()`
Remove the `try { ... } catch (Exception ex) { _logger.LogError(...); throw; }` wrapper around the body of `Handle()`. The method body (the `LogDebug` call, the call to `BuildApplicationConfiguration()`, response construction, and the `return`) is otherwise unchanged and keeps its existing statements, including the existing `_logger.LogDebug("Configuration retrieved successfully: {@Config}", response)` call before the return.

**Acceptance criteria:**
- `Handle()` no longer contains a `try`/`catch` block.
- All existing `_logger.LogDebug(...)` calls in the method are preserved exactly as they are today (only the `catch` block and its `_logger.LogError(...)` call are removed).
- If `BuildApplicationConfiguration()` (or any code in the method body) throws, the exception propagates unhandled out of `Handle()` to MediatR's caller, ultimately reaching the global exception-handling middleware — the exception type and message are unchanged (no wrapping, no translation).
- `GetConfigurationResponse`'s field mapping (`Version`, `Environment`, `UseMockAuth`, `Timestamp`) is unchanged.

### FR-2: No change to the global exception-handling pipeline
This change does not add, remove, or modify any `IExceptionHandler` registration, the `app.UseExceptionHandler()` call, or any other exception-handling middleware. The global pipeline continues to be the single place that logs and translates unhandled exceptions into HTTP responses for this endpoint, as it already does for every other handler that does not have its own try-catch.

**Acceptance criteria:**
- No files under `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/` or `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs` are modified.

### FR-3: Existing behavior and tests remain green
The success-path behavior of `GET` configuration (version resolution priority, environment fallback, `UseMockAuth` wiring, timestamp-at-construction-time) is unchanged. Existing unit tests in `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` and `GetConfigurationEndpointTests.cs` continue to pass without modification, since none of them assert on the removed catch block's logging behavior.

**Acceptance criteria:**
- `GetConfigurationHandlerTests.cs` and `GetConfigurationEndpointTests.cs` pass unmodified after the change.
- `dotnet build` and `dotnet format` succeed with no new warnings introduced by this change.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a dead-code removal with no measurable performance impact. (Marginally fewer instructions executed per call; not a target worth measuring.)

### NFR-2: Security
Not applicable — no change to authentication, authorization, or data exposure. The endpoint already returns only non-sensitive configuration metadata (version, environment name, mock-auth flag, timestamp).

## Data Model
No data model changes. `GetConfigurationResponse` and `ApplicationConfiguration` are unchanged.

## API / Interface Design
No change to the `GET` configuration endpoint's request/response contract. Behaviorally, the only observable difference is that an exception during configuration retrieval (which cannot realistically occur given `BuildApplicationConfiguration()`'s current implementation, which only reads `IConfiguration` and assembly metadata) would now be logged once, by the global exception handler, instead of twice (once by the removed catch block, once by the global handler).

## Dependencies
None beyond what already exists: MediatR (`IRequestHandler<GetConfigurationRequest, GetConfigurationResponse>`), `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Logging`, and the existing global exception-handling middleware registered in `Anela.Heblo.API`.

## Out of Scope
- Auditing or changing try-catch-log-rethrow patterns in any other handler in the codebase. This spec is scoped to `GetConfigurationHandler` only, per the filed issue. (A broader sweep, if desired, would be a separate arch-review finding/issue.)
- Any change to `BuildApplicationConfiguration()`'s internal logic (version resolution, environment fallback, etc.).
- Any change to the global exception-handling middleware's behavior, registration, or logging format.
- Adding new tests specifically for "exception propagates unhandled" — the current test suite does not test this path today (there is no realistic way to make `BuildApplicationConfiguration()` throw without mocking `IConfiguration` to throw, which does not reflect a real production scenario), and this spec does not require adding one; FR-3 only requires the existing suite to keep passing.

## Open Questions
None.

## Status: COMPLETE
