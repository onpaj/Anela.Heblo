# Specification: Gate `E2ETestController.GetEnvironmentInfo` behind the same environment/auth check as its siblings

## Summary
`E2ETestController.GetEnvironmentInfo` (`GET /api/E2ETest/env-info`) is the only action in `E2ETestController` with no environment guard and no `[Authorize]`/`[AllowAnonymous]` attribute, so it is anonymously reachable in every environment including Production. The other three actions in the same file (`CreateE2ESession`, `GetAuthStatus`, `GetE2EApp`) all enforce a Staging-or-Development-only guard, and two of them are additionally protected by `[Authorize(AuthenticationSchemes = "E2ETestCookies")]`. This fix brings `GetEnvironmentInfo` in line with its siblings so it can no longer leak environment/configuration data outside Staging or Development.

## Background
The controller's class doc-comment states it is "ONLY for Staging Environment," and the codebase has already fixed the same category of gap twice: #3805 (`DepartmentsController` missing an authorization attribute) and #3785 (`DiagnosticsController` leaking config in Production). `AuthenticationExtensions.ConfigureAuthorizationPolicies` sets `options.DefaultPolicy` (`backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs:104-121`), but ASP.NET Core only applies `DefaultPolicy` to actions carrying a bare `[Authorize]` with no explicit policy — it does not protect actions with no `[Authorize]` attribute at all, and no `FallbackPolicy` is registered anywhere in the app. `GetEnvironmentInfo` therefore falls through every layer of protection and is currently anonymously callable in Production, returning `EnvironmentName`, `IsProduction`, `IsStaging`, and the raw `ASPNETCORE_ENVIRONMENT` value to any caller.

## Functional Requirements

### FR-1: Environment-gate `GetEnvironmentInfo`
Add the same "CRITICAL SECURITY: Only allow in Staging or Development environment" guard used by `CreateE2ESession`, `GetAuthStatus`, and `GetE2EApp` to the top of `GetEnvironmentInfo`, returning `NotFound` with the same shape (`{ error, currentEnvironment }`) when the current environment is neither Staging nor Development.

**Acceptance criteria:**
- In Production (or any environment other than Staging/Development), `GET /api/E2ETest/env-info` returns `404 NotFound` with body `{ error: "E2E endpoints only available in Staging or Development environment", currentEnvironment: <env name> }`, matching the exact shape/wording used by the sibling actions.
- In Staging or Development, `GET /api/E2ETest/env-info` continues to behave exactly as it does today (still anonymously reachable in those environments — no `[Authorize]` change is required by this fix, since existing E2E tests and staging debugging call it without authentication).
- The class doc-comment's claim ("ONLY for Staging Environment") stops being contradicted by this action's behavior in Production.

### FR-2: No behavior change to sibling actions
`CreateE2ESession`, `GetAuthStatus`, and `GetE2EApp` are unmodified by this fix.

**Acceptance criteria:**
- Diff touches only `GetEnvironmentInfo` (and, if needed, a shared private helper it now calls) inside `E2ETestController.cs`.

## Non-Functional Requirements

### NFR-1: Performance
Negligible — one additional `IWebHostEnvironment` environment-name comparison per request, identical in cost to the guard already present in the other three actions.

### NFR-2: Security
Closes the anonymous-in-Production information-disclosure gap described in the brief. Severity: low (the leaked fields — environment name and a boolean flag — are lower-sensitivity than the config values leaked by the #3785 `DiagnosticsController` incident), but the fix should be applied for consistency and defense-in-depth, matching the two prior closed issues (#3805, #3785) addressing the same category of gap.

## Data Model
None — no new entities. The response shape of `GetEnvironmentInfo` is unchanged when called from Staging/Development; only the out-of-environment behavior changes (200 OK → 404 NotFound).

## API / Interface Design
- `GET /api/E2ETest/env-info`
  - Staging or Development: unchanged — `200 OK` with `{ environment, isDevelopment, isProduction, isStaging, environmentVariables: { ASPNETCORE_ENVIRONMENT } }`.
  - Any other environment: **new** — `404 NotFound` with `{ error: "E2E endpoints only available in Staging or Development environment", currentEnvironment: <env name> }`.

## Dependencies
None beyond what `E2ETestController` already injects (`IWebHostEnvironment`). No new services, packages, or config.

## Out of Scope
- Adding `[Authorize]`/`[AllowAnonymous]` attributes to `GetEnvironmentInfo` or its siblings — the brief's suggested direction offers the environment guard as the primary fix ("apply the same environment guard (**or** an explicit `[Authorize]`)"); this spec adopts the environment-guard approach for consistency with the three sibling actions, which is the narrower, lower-risk change and requires no new auth scheme wiring.
- Registering a global `FallbackPolicy` in `AuthenticationExtensions`/`Program.cs` to close this class of gap systemically — noted as a possible follow-up but not required to resolve this specific finding, and out of scope to avoid unintended side effects on other unauthenticated-by-design endpoints (e.g. health checks) in this change.
- Any change to `E2ETestController`'s registration, routing, or the E2E test suite that calls these endpoints.

## Open Questions

None.

## Status: COMPLETE
