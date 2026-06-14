## Module
OrgChart

## Finding
`OrgChartService` logs every failure at `LogLevel.Error` before re-throwing:

```csharp
// backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs:60-74
catch (HttpRequestException ex)
{
    _logger.LogError(ex, "HTTP error while fetching organizational structure from {Url}", _options.DataSourceUrl);
    throw new InvalidOperationException($"Failed to fetch organizational structure: {ex.Message}", ex);
}
catch (JsonException ex)
{
    _logger.LogError(ex, "JSON deserialization error for organizational structure from {Url}", _options.DataSourceUrl);
    throw new InvalidOperationException($"Failed to parse organizational structure: {ex.Message}", ex);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error while fetching organizational structure from {Url}", _options.DataSourceUrl);
    throw;
}
```

The controller then catches the re-thrown exception and logs a second error:

```csharp
// backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs:48-50
catch (Exception ex)
{
    _logger.LogError(ex, "Error fetching organizational structure");
    ...
}
```

Every failure produces two `Error`-level log lines for the same event, with different messages and different loggers, making log correlation harder. The handler's own test (`GetOrganizationStructureHandlerTests.cs:63-73`) even acknowledges "the controller owns failure logging" — but the service also logs, creating ambiguity about which layer actually owns error observability.

## Why it matters
- Duplicate error log entries inflate log volume and inflate alert counts if error-rate alerts are configured.
- The two log messages have different context (service logs the URL; controller logs nothing specific), so neither is complete on its own — engineers end up reading both.
- It creates an implicit architectural rule ("who logs?") that is contradicted by the test comment and the actual code simultaneously.

## Suggested fix
Pick one logging site. The simplest fix is to remove the `LogError` calls from `OrgChartService` (keep only the `LogInformation` on the happy path) and let the controller's catch block be the single error-logging site. Alternatively, remove the try/catch from the controller and use global exception middleware for consistent error logging across all modules.

`OrgChartService` can still throw typed exceptions to communicate failure kind to callers without logging them.

---
_Filed by daily arch-review routine on 2026-06-04._