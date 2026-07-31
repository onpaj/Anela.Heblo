# Plan: Lock down `DiagnosticsController` — auth, environment gate, and stop leaking the App Insights key

## Summary
`DiagnosticsController` ships to Production with no `[Authorize]` and no environment guard, so any anonymous caller can retrieve the App Insights instrumentation key / connection-string prefix and fire arbitrary `Error`/`Exception` telemetry that bypasses sampling. This is a security fix: require authentication, restrict the controller to non-Production environments (matching the existing `E2ETestController` precedent), and stop returning any part of the connection string or instrumentation key in the response body.

## Context
Found by the arch-review routine (Telemetry module). `Program.cs`/`AuthenticationExtensions.cs`/`ApplicationBuilderExtensions.cs` confirm there is no fallback authorization policy and no environment-conditional registration for this controller — `[Authorize]` is opt-in per controller/action, and `MapControllers()` is unconditional. `CostOptimizedTelemetryProcessor.cs` explicitly excludes `/api/diagnostics` from telemetry, which only makes sense if the route is expected to receive live production traffic. The repo already has a precedent for exactly this class of endpoint: `E2ETestController` (`backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs:67-73`) checks `IWebHostEnvironment.IsEnvironment("Staging") || IsDevelopment()` per action and returns `NotFound` outside those environments. No script or test in the repo calls `/api/diagnostics/*` (confirmed via search), so gating it introduces no known breakage.

## Functional requirements

**FR-1: Block all `DiagnosticsController` actions in Production**
- Inject `IWebHostEnvironment` into `DiagnosticsController` and, at the top of each action (or via a shared guard/filter), return `NotFound` when `_environment.IsProduction()` is true — same shape as `E2ETestController`.
- Acceptance criteria:
  - In an environment where `IsProduction()` is true, `GET /api/diagnostics/test-logging`, `/test-exception`, `/health`, and `/appinsights-config` all return 404 and produce no log entries, no telemetry events, and no exception tracking.
  - In Development/Staging, behavior is unchanged apart from FR-2/FR-3 below.

**FR-2: Require authentication for the remaining (non-Production) surface**
- Add `[Authorize]` at the controller level (no attribute currently exists), relying on the app's `DefaultPolicy` (`RequireAuthenticatedUser()` + `RequireRole(AccessRoles.Base)`, `AuthenticationExtensions.cs:104-109`) — consistent with how the rest of the app is gated. Do not introduce a new role; this endpoint doesn't warrant a bespoke "diagnostics" permission.
- Acceptance criteria:
  - In Development/Staging, an unauthenticated request to any of the four actions returns 401 (or 403 per `PermissionAuthorizationResultHandler`), not 200.
  - An authenticated request from a user holding `AccessRoles.Base` succeeds (subject to FR-1's environment check).
  - Mock-auth mode (`UseMockAuth=true`, used in Development/E2E) continues to work unchanged since it also populates the `Base` role claim.

**FR-3: Stop returning App Insights key material**
- In `GetApplicationInsightsConfig` (`DiagnosticsController.cs:91-113`), remove `InstrumentationKey` and `ConnectionStringSource` (the 50-char connection-string prefix) from the response. Keep only the existing boolean flags (`HasConnectionString`, `HasInstrumentationKey`) plus `Environment`, `CloudRole`, `CloudRoleInstance`.
- Acceptance criteria:
  - Response body contains no instrumentation key GUID and no substring of the connection string, in any environment.
  - `HasConnectionString`/`HasInstrumentationKey` booleans still reflect actual configuration state (existing logic unchanged).

## Non-functional requirements
- **Security**: no anonymous network path may read App Insights key material or trigger telemetry after this change, in any environment reachable over the network (Production is blocked entirely; Staging/Development require auth).
- No new dependencies; changes are confined to one controller plus its tests.

## Data model
- N/A — no persisted entities involved.

## Interfaces
- `GET /api/diagnostics/test-logging`, `/test-exception`, `/health`, `/appinsights-config` — same routes/methods, now: 404 in Production; 401/403 unauthenticated in Development/Staging; `appinsights-config`'s 200 response body drops `InstrumentationKey` and `ConnectionStringSource` fields (a breaking response-shape change for any consumer relying on those fields — see Open Questions).

## Dependencies and scope
- In scope: `DiagnosticsController.cs` only (constructor injection of `IWebHostEnvironment`, `[Authorize]` attribute, environment guard, trimmed `appinsights-config` response) plus new/updated controller tests.
- Out of scope:
  - Adding a dedicated "diagnostics" or "admin" role to `AccessRoles` — that file is auto-generated (`AccessRoles.generated.cs`, header: "Do not edit by hand") by `Anela.Heblo.AccessMatrixGen`; introducing a new permission is a larger change than this finding warrants and the existing `AccessRoles.Base` policy is sufficient defense-in-depth once combined with the environment gate.
  - Changing `CostOptimizedTelemetryProcessor`'s existing exclusion of `/api/diagnostics` from telemetry sampling — unrelated to the leak, still correct once the endpoints are gated.
  - Any change to how App Insights connection strings/keys are provisioned or rotated (out of scope for this fix; only the *disclosure* is addressed).
  - Removing the controller/endpoints entirely — they remain useful as manual debugging aids in Development/Staging, per the suggested direction's "restrict to non-Production" option.

## Rough plan
1. Add `IWebHostEnvironment` to `DiagnosticsController`'s constructor; add a private guard method (`IsProductionBlocked()` or similar) called first in each of the four actions, returning `NotFound` when `_environment.IsProduction()`.
2. Add `[Authorize]` at class level.
3. Edit `GetApplicationInsightsConfig` to drop `InstrumentationKey` and `ConnectionStringSource` from the returned anonymous object; keep the two boolean flags and the cloud-role fields.
4. Add controller tests (new file, e.g. `backend/test/Anela.Heblo.Tests/API/Controllers/DiagnosticsControllerTests.cs`) covering: 404 in a Production-hosted test server for all four routes; 401/403 unauthenticated in non-Production; 200 + expected trimmed shape for an authenticated `Base`-role user in non-Production.
5. `dotnet build` + `dotnet format` per repo validation rules; run the new/affected backend test suite.

## Open questions
- Should `appinsights-config`'s trimmed response additionally be considered a breaking contract change for any external tooling? No caller was found in this repo (frontend, scripts, or tests) — defaulting to trimming it per the finding's explicit suggestion ("stop returning any portion of the connection string / instrumentation key from a network endpoint"). Flag to the user if any external dashboard/script depends on the removed fields.
- Whether Production should return 404 (looks like route doesn't exist) vs. 403 (exists but forbidden) is a judgment call; defaulting to 404 to match the existing `E2ETestController` precedent exactly and avoid confirming route existence to a prober.
