# Design: Stop built-in HTTP logging from capturing the raw Authorization bearer token

## Component Design

### `AddCrossCuttingServices` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`)
Responsibility: configures cross-cutting ASP.NET Core services, including the built-in `HttpLoggingMiddleware` via `services.AddHttpLogging(...)`.

Change in responsibility: the `logging.RequestHeaders` allow-list it builds must never include `Authorization` (or any other credential-bearing header). Concretely:

```csharp
services.AddHttpLogging(logging =>
{
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
    logging.RequestHeaders.Add("User-Agent");
    // Authorization intentionally NOT added: HttpLoggingMiddleware redacts any header
    // not explicitly listed here. Adding it would log the real bearer token to
    // stdout/App Insights. See RequestLoggingMiddleware.IsSensitiveHeader for the
    // equivalent policy in the project's own request-logging middleware.
    logging.ResponseHeaders.Add("Content-Type");
    logging.MediaTypeOptions.AddText("application/json");
    logging.RequestBodyLogLimit = 4096;
    logging.ResponseBodyLogLimit = 4096;
});
services.AddHttpLoggingInterceptor<SuppressHealthHttpLogging>();
```

Nothing else about this method's contract changes: same signature, same return type (`IServiceCollection`), same registration order relative to other cross-cutting services.

### `SuppressHealthHttpLogging` (`backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs`)
Unchanged. Continues to suppress all built-in HTTP logging fields for health-check paths (`/health`, `/healthz`, `/health/ready`, `/health/live`). Not touched by this fix and not a place where header redaction needs to happen, since it operates on `LoggingFields`, not individual header values.

### `RequestLoggingMiddleware` (`backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs`)
Unchanged. Its `IsSensitiveHeader` allow-list already excludes `Authorization` correctly; this fix brings the built-in middleware's behavior into agreement with it, without modifying this class.

### `LoggingExtensions.ConfigureApplicationLogging` (`backend/src/Anela.Heblo.API/Extensions/LoggingExtensions.cs`)
Unchanged. Continues to route logs to console (stdout) and, when configured, Application Insights. No changes needed here — the fix stops the sensitive value from ever entering the log pipeline in the first place, upstream of this sink configuration.

## Data Schemas
Not applicable. This change touches no persisted data, no database schema, no API request/response DTOs, and no event payloads — it is a one-line removal from an in-process logging configuration delegate. The only "shape" affected is the set of HTTP headers ASP.NET Core's built-in `HttpLoggingMiddleware` includes in its log entries, which shrinks from `{User-Agent, Authorization}` to `{User-Agent}` for the request-header allow-list (response-header allow-list, `{Content-Type}`, is unchanged).
