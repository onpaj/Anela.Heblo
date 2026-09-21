# Design: Remove dead try-catch-log-rethrow in GetConfigurationHandler

## Component Design

### `GetConfigurationHandler` (`backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`)
- Responsibility: unchanged — implements `IRequestHandler<GetConfigurationRequest, GetConfigurationResponse>`, building an `ApplicationConfiguration` value object from `IConfiguration`/assembly metadata and mapping it into a `GetConfigurationResponse`.
- Change: the `Handle()` method body is un-nested from its `try { ... } catch (Exception ex) { _logger.LogError(...); throw; }` wrapper. Both `_logger.LogDebug(...)` calls (request start, success) stay exactly where they are, at the same log level, with the same message templates. No new method, field, or constructor parameter is introduced or removed.
- `BuildApplicationConfiguration()` (private helper): unchanged, not touched by this design.

### Global exception-handling pipeline (`backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs` and `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/*`)
- Responsibility: unchanged. Continues to be the single component responsible for logging and translating any unhandled exception from `GetConfigurationHandler.Handle()` (or any other handler) into an HTTP response, exactly as it already does today for the rest of the codebase's handlers (this one was the sole outlier duplicating that responsibility locally).
- No interface, registration, or behavior change to this component is part of this design.

## Data Schemas
No data schema changes. `GetConfigurationRequest`, `GetConfigurationResponse`, and `ApplicationConfiguration` keep their existing shapes:

- `GetConfigurationResponse` (class, per repo convention — DTOs are classes, never records):
  - `Version: string`
  - `Environment: string`
  - `UseMockAuth: bool`
  - `Timestamp: DateTime`

No API request/response wire format changes for the `GetConfiguration` endpoint.
