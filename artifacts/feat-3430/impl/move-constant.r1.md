# Implementation: move-constant

## What was implemented

Moved three configuration key constants (`APP_VERSION`, `USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION`) from `Anela.Heblo.Domain.Features.Configuration.ConfigurationConstants` to the new `Anela.Heblo.Domain.Shared.InfrastructureConfigurationKeys` class. Updated all consumers across infrastructure, API, adapter, application, and test layers. Removed stale using directives from two files that no longer reference the domain configuration namespace. String values are unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` — created with `APP_VERSION`, `USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION`
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` — removed three constants; only `DEFAULT_VERSION` and `DEFAULT_ENVIRONMENT` remain
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` — switched using + constant reference
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — switched using + constant reference
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` — switched using + constant reference
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — switched using + constant reference
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — kept existing using, added `Anela.Heblo.Domain.Shared`, updated USE_MOCK_AUTH and APP_VERSION references
- `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` — switched using + both constant references
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs` — switched using + both constant references
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ETestAuthenticationMiddleware.cs` — removed stale `using Anela.Heblo.Domain.Features.Configuration;` only
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` — switched using + both constant references
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — removed stale `using Anela.Heblo.Domain.Features.Configuration;` only
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` — switched using + both constant references
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — added `using Anela.Heblo.Domain.Shared;`, updated APP_VERSION and USE_MOCK_AUTH references; kept DEFAULT_VERSION on ConfigurationConstants

## Tests

`dotnet build Anela.Heblo.sln`: 0 errors, 253 warnings (pre-existing).
`dotnet test Anela.Heblo.Tests.csproj --no-build`: Passed 5389, Failed 64 (all failures are Docker/Testcontainers not available in this environment — pre-existing, unrelated to this change), Skipped 4.
Configuration-specific tests: 52 passed, 0 failed.

## How to verify

1. `dotnet build Anela.Heblo.sln` — should produce 0 errors
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Configuration"` — 52 tests pass
3. Confirm `InfrastructureConfigurationKeys` is in `Anela.Heblo.Domain.Shared` namespace
4. Confirm `ConfigurationConstants` only has `DEFAULT_VERSION` and `DEFAULT_ENVIRONMENT`
5. Confirm no remaining references to `ConfigurationConstants.APP_VERSION`, `ConfigurationConstants.USE_MOCK_AUTH`, or `ConfigurationConstants.BYPASS_JWT_VALIDATION` in the codebase

## Notes

- Raw string literals `"UseMockAuth"` in module files (not constant references) were left unchanged as specified
- `GetConfigurationHandler` retains both `Anela.Heblo.Domain.Features.Configuration` (for `DEFAULT_VERSION` used indirectly via `ApplicationConfiguration.CreateWithDefaults`) and `Anela.Heblo.Domain.Shared`
- Docker-dependent tests fail in this environment due to no Docker daemon — pre-existing condition, not introduced by this change

## Status
DONE
