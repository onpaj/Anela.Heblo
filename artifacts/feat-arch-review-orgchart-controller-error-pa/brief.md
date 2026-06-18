## Module
OrgChart

## Finding
`OrgChartController.GetOrganizationStructure` declares its return type as `ActionResult<OrgChartResponse>`, but its catch block returns an anonymous object that is completely outside that contract:

```csharp
// backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs:42-52
catch (Exception ex)
{
    _logger.LogError(ex, "Error fetching organizational structure");
    return StatusCode(StatusCodes.Status500InternalServerError,
        new { error = "Failed to fetch organizational structure", message = ex.Message });
}
```

The shape `{ error, message }` is not `OrgChartResponse`, so the generated OpenAPI/TypeScript client has no idea this response variant exists. The raw `ex.Message` is also forwarded to the client — exposing internal details such as the configured `DataSourceUrl` (e.g. `"Connection refused to http://internal-sharepoint/..."`) or stack-trace fragments that end up in wrapped `InvalidOperationException` messages produced by `OrgChartService`.

The `OrgChartResponse` class already has a constructor that accepts an `ErrorCodes` value (`OrgChartResponse.cs:17-18`), designed exactly for typed error returns.

## Why it matters
- **Broken API contract**: Callers who deserialize `OrgChartResponse` receive an unexpected shape on errors; the TypeScript client will silently get `undefined` for all typed fields.
- **Information leakage**: Raw exception messages (including the configured URL) are forwarded to the browser.
- **Inconsistency**: Every other module in the codebase returns `BaseResponse`-derived typed errors; this module is the only one returning an anonymous object.

## Suggested fix
Replace the anonymous-object return with a typed `OrgChartResponse` error, for example:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error fetching organizational structure");
    return StatusCode(StatusCodes.Status500InternalServerError,
        new OrgChartResponse(ErrorCodes.ServerError));
}
```

Or, if a global exception-handling middleware already exists in the project, remove the try/catch entirely and let the middleware produce a consistent error response.

---
_Filed by daily arch-review routine on 2026-06-04._