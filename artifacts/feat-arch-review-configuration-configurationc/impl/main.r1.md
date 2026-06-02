---

# Implementation: Move Infrastructure Constants out of Domain Layer

## What was implemented
Structural refactor restoring Clean Architecture dependency direction: 10 infrastructure-specific string constants were moved from `Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` to a new `Anela.Heblo.API/Infrastructure/InfrastructureConstants.cs`. All 6 API consumer files were updated to reference the new location. No runtime behavior changed — only the declaring namespace/type of the constants.

## Files created/modified
- `backend/src/Anela.Heblo.API/Infrastructure/InfrastructureConstants.cs` — NEW: holds 10 infrastructure constants (AppInsights, DB connection, CORS, auth scheme, health check tags/names)
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — switched to `InfrastructureConstants`; retained `using Anela.Heblo.Domain.Features.Configuration` for `ProductExportOptions`
- `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` — `MOCK_AUTH_SCHEME` → `InfrastructureConstants`; Domain using kept for `USE_MOCK_AUTH`/`BYPASS_JWT_VALIDATION`
- `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs` — `CORS_POLICY_NAME`, `DB_TAG` → `InfrastructureConstants`; Domain using removed
- `backend/src/Anela.Heblo.API/Extensions/LoggingExtensions.cs` — AppInsights constants → `InfrastructureConstants`; Domain using removed
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` — `MOCK_AUTH_SCHEME` → `InfrastructureConstants`; Domain using kept for `USE_MOCK_AUTH`/`BYPASS_JWT_VALIDATION`
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` — stripped to 6 domain-level constants only

## Tests
No test changes required — no test file directly referenced any of the moved constants via `ConfigurationConstants`.

## How to verify
```
dotnet build Anela.Heblo.sln      # 0 errors
dotnet format Anela.Heblo.sln --verify-no-changes   # 0 violations
grep -r "ConfigurationConstants\.\(APPLICATION_INSIGHTS\|DEFAULT_CONNECTION\|CORS\|MOCK_AUTH_SCHEME\|DB_TAG\|POSTGRESQL\|DATABASE_HEALTH\)" backend/src/
# → no output (all moved constants now referenced only via InfrastructureConstants)
```

## Notes
- `ProductExportOptions` lives in `Anela.Heblo.Domain.Features.Configuration` — the same namespace as `ConfigurationConstants`. The initial consumer-update subagent removed the `using` directive from `ServiceCollectionExtensions.cs` thinking it was only needed for `ConfigurationConstants`. A corrective commit restored it. This is architecturally correct: the API project may depend on Domain types; only the infrastructure-specific *constants* were relocated.
- The three orphan Domain constants (`ASPNETCORE_ENVIRONMENT`, `DEFAULT_VERSION`, `DEFAULT_ENVIRONMENT`) remain in Domain unchanged per the arch review's decision (out of scope; dead code cleanup is a separate task).
- `HangfireAuthenticationMiddleware.cs` required no changes — it only uses `USE_MOCK_AUTH`/`BYPASS_JWT_VALIDATION`, which remain in Domain.

## PR Summary
Moved 10 infrastructure-specific string constants out of the Domain layer into a new `InfrastructureConstants` class in the API project, restoring Clean Architecture's dependency direction. Domain no longer needs to be rebuilt when CORS policy naming, health check tags, or Application Insights connection string keys change.

### Changes
- `backend/src/Anela.Heblo.API/Infrastructure/InfrastructureConstants.cs` — new file; holds all moved constants with byte-identical values
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — switched to `InfrastructureConstants`
- `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` — `MOCK_AUTH_SCHEME` switched; Domain constants (`USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION`) remain
- `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs` — CORS/health tag constants switched
- `backend/src/Anela.Heblo.API/Extensions/LoggingExtensions.cs` — AppInsights constants switched
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` — `MOCK_AUTH_SCHEME` switched
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` — stripped to 6 app-level constants

## Status
DONE