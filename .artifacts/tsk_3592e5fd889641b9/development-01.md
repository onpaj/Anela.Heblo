# Development: Lock down `DiagnosticsController`

## Summary

Implemented `design-01.md` exactly as specified: `DiagnosticsController` now requires authentication, blocks all four actions in Production, and no longer returns App Insights key material.

## Files changed

### `backend/src/Anela.Heblo.API/Controllers/DiagnosticsController.cs` (modified)

- Added `[Authorize]` at the class level — enforces the app's `DefaultPolicy` (`RequireAuthenticatedUser()` + `RequireRole(AccessRoles.Base)`), the same mechanism already used for `/mcp`. No new role introduced.
- Added `IWebHostEnvironment _environment` as a constructor dependency (standard ASP.NET DI, already registered — no service-registration changes needed).
- Added a private `TryBlockInProduction(out IActionResult result)` helper that returns `NotFound()` when `_environment.IsProduction()` is true. Invoked as the first line of all four actions (`TestLogging`, `TestException`, `Health`, `GetApplicationInsightsConfig`), matching `E2ETestController`'s existing per-action guard idiom rather than introducing a filter/attribute.
- Trimmed `GetApplicationInsightsConfig`'s response: removed the `InstrumentationKey` (raw GUID) and `ConnectionStringSource` (50-char connection-string prefix) fields entirely. Kept `HasConnectionString`, `HasInstrumentationKey`, `Environment`, `CloudRole`, `CloudRoleInstance` — the booleans still reflect real configuration state, only the key material's serialization is removed.

Net effect, matching the design's status-code table:

| Caller | Environment | Response |
|---|---|---|
| Anonymous | any | `401` (auth middleware short-circuits before the action runs) |
| Authenticated, `AccessRoles.Base` | Production | `404` (in-action guard fires) |
| Authenticated, `AccessRoles.Base` | Staging/Development | `200`, with `appinsights-config`'s trimmed body |

### `backend/test/Anela.Heblo.Tests/Controllers/DiagnosticsControllerTests.cs` (new)

Follows the `DashboardControllerTests` direct-instantiation pattern (no `HebloWebApplicationFactory`, per the design's rationale: the shared factory's `MockAuthenticationHandler` always authenticates, so it can't represent an anonymous caller). 15 tests:

- **Attribute presence** — `Controller_ShouldRequireAuthorization` (reflection check for `[Authorize]` on the class) and `Actions_ShouldNotAllowAnonymous` (theory over all 4 actions, asserts no `[AllowAnonymous]`).
- **Production guard** — each of the 4 actions returns `NotFoundResult` when `IWebHostEnvironment.EnvironmentName == "Production"`; `TestLogging`/`TestException` additionally assert a recording `ITelemetryChannel` fake receives zero items (the concrete regression test for the cost-mandate angle of the finding).
- **Non-Production behavior preserved** — same actions return their original `200`/`500` responses under `"Development"`.
- **Trimmed `appinsights-config` shape** — reflection over the returned anonymous object's property names confirms `InstrumentationKey`/`ConnectionStringSource` are absent and the five kept fields are present, in both a configured-key case and an unconfigured case (booleans still reflect real state).

## Deviation from plan/design note

None — implementation matches `design-01.md` verbatim, which was itself approved without changes in `architecture-01.md`.

## Verification performed

- `dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj` — 0 errors (156 pre-existing warnings unrelated to this change; the one warning this change initially introduced, `CS8625` on the `out` parameter's null assignment, was fixed with `null!` and confirmed gone on rebuild).
- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DiagnosticsControllerTests"` — **15/15 passed** (one iteration failed first: `TelemetryConfiguration.ConnectionString` didn't populate `TelemetryClient.InstrumentationKey` in this SDK version; fixed by setting `TelemetryClient.InstrumentationKey` directly in the test setup, then all passed).
- `dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.API/Controllers/DiagnosticsController.cs backend/test/Anela.Heblo.Tests/Controllers/DiagnosticsControllerTests.cs --verify-no-changes` — clean, no formatting changes needed.

## How to verify

```bash
cd backend
dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DiagnosticsControllerTests"
cd ..
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.API/Controllers/DiagnosticsController.cs backend/test/Anela.Heblo.Tests/Controllers/DiagnosticsControllerTests.cs --verify-no-changes
```

Manual/E2E spot check (not run here — no Docker/staging environment available in this session): with the app running in Development/Staging, an unauthenticated `GET /api/diagnostics/health` should return `401`; authenticated (mock-auth) should return `200`; `GET /api/diagnostics/appinsights-config`'s body should contain no `instrumentationKey`/`connectionStringSource` fields.
