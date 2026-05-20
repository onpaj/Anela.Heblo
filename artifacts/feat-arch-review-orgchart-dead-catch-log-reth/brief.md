## Module
OrgChart

## Finding
`GetOrganizationStructureHandler.cs` lines 29–35 wraps the single service call in a try-catch that only logs and rethrows:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error fetching organizational structure");
    throw;
}
```

`OrgChartController.cs` lines 40–53 has an identical catch block that also logs and converts the exception into a 500 response. The result is that every error is logged twice with nearly identical messages before the client ever sees the response.

## Why it matters
The handler's catch adds zero behaviour — it neither recovers, transforms, nor enriches the exception. It just adds a redundant log line, making error traces harder to read and inflating log volume. This violates KISS: every line of code should earn its place.

## Suggested fix
Delete the try-catch from `GetOrganizationStructureHandler.Handle` entirely. The controller is the right place to catch, log, and shape the HTTP error response. The handler should contain only the happy path:

```csharp
public async Task<OrgChartResponse> Handle(GetOrganizationStructureRequest request, CancellationToken cancellationToken)
{
    _logger.LogInformation("Handling request to fetch organizational structure");
    return await _orgChartService.GetOrganizationStructureAsync(cancellationToken);
}
```

---
_Filed by daily arch-review routine on 2026-05-19._