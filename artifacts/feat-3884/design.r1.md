# Design: Gate `E2ETestController.GetEnvironmentInfo` behind the same environment check as its siblings

## Component Design
No new components. The only touched component is the existing `E2ETestController.GetEnvironmentInfo` action (`backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs:40-54`).

**Responsibility change:** `GetEnvironmentInfo` gains a single responsibility it currently lacks — refusing to run outside Staging/Development — matching the responsibility already carried by `CreateE2ESession`, `GetAuthStatus`, and `GetE2EApp` in the same controller. No dependency changes: the action already has `IWebHostEnvironment _environment` injected via the constructor and simply needs to call the same guard those three siblings already call.

Guard clause to add at the top of the method body, before the existing `return Ok(...)`:

```csharp
[HttpGet("env-info")]
public ActionResult<object> GetEnvironmentInfo()
{
    // CRITICAL SECURITY: Only allow in Staging or Development environment
    if (!_environment.IsEnvironment("Staging") && !_environment.IsDevelopment())
    {
        return NotFound(new { error = "E2E endpoints only available in Staging or Development environment", currentEnvironment = _environment.EnvironmentName });
    }

    return Ok(new { /* unchanged */ });
}
```

## Data Schemas
No schema/entity changes. Response shape for the in-environment (Staging/Development) case is byte-for-byte unchanged:

```json
{
  "environment": "Staging",
  "isDevelopment": false,
  "isProduction": false,
  "isStaging": true,
  "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Staging" }
}
```

New response shape for the out-of-environment case (e.g. Production), reusing the exact literal already emitted by the three sibling actions — no new error type introduced:

```json
{
  "error": "E2E endpoints only available in Staging or Development environment",
  "currentEnvironment": "Production"
}
```
HTTP status: `404 NotFound` (matching the sibling actions' status code choice).
