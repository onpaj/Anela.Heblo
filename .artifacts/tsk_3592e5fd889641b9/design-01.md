# Design: Lock down `DiagnosticsController`

No UI involved — this is a backend-only controller change (one file) plus tests. UX/UI section omitted.

## Design decision: clarifying the plan's "404 in Production" acceptance criterion

Plan FR-1/FR-2 asked for both an environment guard (404 in Production, mirroring `E2ETestController`) and a class-level `[Authorize]`. Combining them changes the observed status code for *anonymous* callers, because of ASP.NET Core's request pipeline order:

```
routing → authorization middleware (evaluates [Authorize] on the matched endpoint) → controller action body
```

`[Authorize]` is enforced by middleware **before** the action method runs. The environment guard the plan describes (`if (_environment.IsProduction()) return NotFound();`) lives *inside* the action body, so it only ever executes for callers who already passed authorization. Net behavior, precisely:

| Caller | Environment | Response |
|---|---|---|
| Anonymous | any (Prod/Staging/Dev) | `401` (auth middleware short-circuits; action never runs) |
| Authenticated, `AccessRoles.Base` | Production | `404` (auth passes, action's env guard fires) |
| Authenticated, `AccessRoles.Base` | Staging/Development | `200` with trimmed body |

This still satisfies the finding's actual security requirement — no anonymous caller can reach the App Insights key material or fire telemetry, in any environment — it's just that the specific status code an anonymous caller sees is `401`, not `404`, since they never reach the in-action check. This matches the existing `E2ETestController` codebase pattern (which has the same layering: pipeline-level auth + in-action env check on `auth-status`/`app`) rather than introducing a new one. The design below (and its tests) target this precise table, not the plan's blanket "always 404 in Production."

## Component design

### `DiagnosticsController` (`backend/src/Anela.Heblo.API/Controllers/DiagnosticsController.cs`)

**New responsibilities added to the existing controller** (no new files/classes):

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]                                   // NEW — enforces DefaultPolicy (RequireAuthenticatedUser + AccessRoles.Base)
public class DiagnosticsController : ControllerBase
{
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly IWebHostEnvironment _environment;    // NEW constructor dependency

    public DiagnosticsController(
        ILogger<DiagnosticsController> logger,
        TelemetryClient telemetryClient,
        IWebHostEnvironment environment)                  // NEW parameter
    { ... }

    private bool TryBlockInProduction(out IActionResult result)
    {
        if (_environment.IsProduction())
        {
            result = NotFound();
            return true;
        }
        result = null;
        return false;
    }

    [HttpGet("test-logging")]
    public IActionResult TestLogging()
    {
        if (TryBlockInProduction(out var blocked)) return blocked;
        // ... existing body unchanged
    }
    // same one-line guard prepended to TestException(), Health(), GetApplicationInsightsConfig()
}
```

- `[Authorize]` at class level uses `DefaultPolicy` implicitly — no `Roles`/`Policy` argument, same mechanism as the MCP endpoint (`ApplicationBuilderExtensions.cs:137`, `.MapMcp("/mcp").RequireAuthorization()` with no policy name). This is the one precedented way in this codebase to gate on "any authenticated Base-role user" without inventing a `Feature`/`FeatureAuthorize` entry (out of scope per plan).
- The guard is a private helper invoked as the first line of all four actions, rather than an `IAsyncActionFilter` — matches `E2ETestController`'s existing per-action `if (!...) return NotFound(...)` style instead of introducing a new filter abstraction for one controller.
- `IWebHostEnvironment` is standard ASP.NET DI (already registered), so no service-registration changes are needed anywhere else.

### `GetApplicationInsightsConfig` — response shape change (FR-3)

Only the returned anonymous object changes; all upstream logic (`hasConnectionString`, `hasInstrumentationKey`, cloud role lookup) is untouched.

```csharp
return Ok(new
{
    HasConnectionString = hasConnectionString,
    HasInstrumentationKey = hasInstrumentationKey,
    Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
    CloudRole = _telemetryClient.Context.Cloud.RoleName,
    CloudRoleInstance = _telemetryClient.Context.Cloud.RoleInstance
});
```

`InstrumentationKey` and `ConnectionStringSource` fields are removed entirely (not nulled/masked — removed), so no key material or connection-string substring is serializable from this endpoint under any configuration state.

## Data schemas

No persisted entities or DB schema changes (plan already noted N/A).

### `GET /api/diagnostics/appinsights-config` — response body, before → after

Before:
```json
{
  "hasConnectionString": true,
  "hasInstrumentationKey": true,
  "instrumentationKey": "a1b2c3d4-...",
  "environment": "Production",
  "connectionStringSource": "InstrumentationKey=a1b2c3d4-...;IngestionEndpoint=h...",
  "cloudRole": "Heblo-API-Production",
  "cloudRoleInstance": "Production"
}
```

After:
```json
{
  "hasConnectionString": true,
  "hasInstrumentationKey": true,
  "environment": "Development",
  "cloudRole": "Heblo-API-Dev",
  "cloudRoleInstance": "..."
}
```

(Field casing shown camelCase per the app's default JSON serializer settings; unaffected by this change.)

### Other three routes (`test-logging`, `test-exception`, `health`)

Response shape unchanged in Development/Staging. In Production, body becomes the standard empty `404` (no JSON payload), same as `E2ETestController`'s `NotFound()` calls.

## Test design

Existing unit-test style in this repo constructs controllers directly with Moq'd dependencies (`DashboardControllerTests.cs`) rather than going through the ASP.NET pipeline for controller-logic tests, and reserves `HebloWebApplicationFactory` for true end-to-end route tests (`AuthorizationIntegrationTests.cs`). Two constraints steer this test toward the unit-test style rather than the WebApplicationFactory style:

1. **`[Authorize]` enforcement is framework behavior, not new logic** — testing "does ASP.NET Core's authorization middleware reject anonymous requests" would be testing the framework, not this change. What this change owns is: (a) the attribute is actually present and (b) the in-action Production guard and trimmed response are correct.
2. **`MockAuthenticationHandler` (`Infrastructure/Authentication/MockAuthenticationHandler.cs:21-51`), used by `HebloWebApplicationFactory`'s `Test` environment, unconditionally authenticates every request as a super-user** — there is no way to represent "anonymous caller" through the shared factory today. Standing up a second auth scheme/factory just for this controller's tests is disproportionate to the finding.

New file: `backend/test/Anela.Heblo.Tests/API/Controllers/DiagnosticsControllerTests.cs`, following `DashboardControllerTests`'s direct-instantiation pattern:

- **Attribute presence** — reflection check that `typeof(DiagnosticsController).GetCustomAttribute<AuthorizeAttribute>()` is non-null and that none of the four action methods carry `[AllowAnonymous]`. This is the unit-testable proxy for "anonymous callers are rejected by the framework"; the framework's enforcement itself is out of scope to re-test.
- **Production guard, all four actions** — construct the controller with `Mock<IWebHostEnvironment>` (`EnvironmentName` = `"Production"`), a real `ILogger<DiagnosticsController>` test double (or `NullLogger<T>`), and a real `TelemetryClient` wired to an in-memory `ITelemetryChannel` fake (same technique `HebloWebApplicationFactory` already uses: `new TelemetryClient(new TelemetryConfiguration())`, but with a channel that records `Send()` calls instead of the default no-op). Assert:
  - Each action returns `NotFoundResult`.
  - The fake channel's `SentItems` is empty after calling `TestLogging()`/`TestException()` (no event/metric/exception telemetry emitted) — this is the concrete regression test for "Production callers can't drive telemetry cost."
- **Non-Production behavior unchanged** — same setup with `EnvironmentName` = `"Development"`: existing behavior for `TestLogging`, `TestException`, `Health` is preserved (200/500 as today).
- **Trimmed `appinsights-config` shape** — with `EnvironmentName` = `"Development"`, call `GetApplicationInsightsConfig()` and assert the returned object's property set via reflection (`GetType().GetProperties().Select(p => p.Name)`) does **not** contain `InstrumentationKey` or `ConnectionStringSource`, and does contain `HasConnectionString`/`HasInstrumentationKey`/`Environment`/`CloudRole`/`CloudRoleInstance`. Cover both a configured-key case and an unconfigured case to confirm the booleans still reflect real state.

No changes to `HebloWebApplicationFactory`, `AuthorizationIntegrationTests.cs`, or any other test infrastructure.

## Scope confirmation

Matches plan-01.md's dependencies/scope section: `DiagnosticsController.cs` (constructor + 4 actions) plus one new test file. No changes to `AccessRoles`, `CostOptimizedTelemetryProcessor`, routing/DI registration, or App Insights provisioning.
