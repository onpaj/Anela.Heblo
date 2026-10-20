# Design: Remove unnecessary async state machine from GetConfigurationHandler

## Component Design

### `GetConfigurationHandler` (`backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`)
Sole component affected. Implements `IRequestHandler<GetConfigurationRequest, GetConfigurationResponse>` (MediatR).

**Responsibility (unchanged):** build and return the current `GetConfigurationResponse` (app version, environment, mock-auth flag, timestamp) from `IConfiguration` and assembly metadata, synchronously.

**Contract change:** none observable. The public method signature remains:

```csharp
public Task<GetConfigurationResponse> Handle(GetConfigurationRequest request, CancellationToken cancellationToken)
```

Internal implementation changes only:
- Remove the `async` modifier from `Handle`.
- Replace `return response;` with `return Task.FromResult(response);`.
- `_logger.LogDebug(...)` calls, `BuildApplicationConfiguration()`, and `GetVersionFromSources()` are untouched — they already run synchronously before the return.

This follows the existing in-repo convention for synchronous MediatR handlers, e.g. `GetMeetingUsersHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetMeetingUsers/GetMeetingUsersHandler.cs`), which has the identical `Task<T> Handle(...) { ...; return Task.FromResult(...); }` shape.

## Data Schemas
No schema changes. `GetConfigurationRequest` (empty marker request) and `GetConfigurationResponse` (`Version`, `Environment`, `UseMockAuth`, `Timestamp`) are unchanged classes — both remain plain DTO classes (not records), per project convention. No API request/response shape, database schema, or event payload is affected by this change.
