## Module
Configuration

## Finding
`ConfigurationConstants.BYPASS_JWT_VALIDATION` is defined in `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs:13` but is imported and used directly by at least 8 modules and infrastructure files outside the Configuration module:

- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs`
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs`
- `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`

Each consumer reads `IConfiguration` directly using this constant key string, but to access the string they import `Anela.Heblo.Domain.Features.Configuration`, creating a direct dependency from many feature modules into the Configuration module's domain layer.

## Why it matters
The development guidelines require that "no direct references between feature modules" exist and that "communication [happens] only through contracts/interfaces." Importing a string constant from another module's domain namespace is a form of direct coupling: if the constant name, value, or namespace changes, all nine consumers break. It also makes `ConfigurationConstants` a de-facto shared global that belongs to no single module.

## Suggested fix
Move `BYPASS_JWT_VALIDATION` (and `APP_VERSION`, `USE_MOCK_AUTH` if they follow the same pattern) to `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` (or similar cross-cutting shared location under `Domain/Shared/`). This decouples the constant from the Configuration feature module while keeping it in a clearly-owned, single location. Each consumer then imports from `Domain.Shared`, which is already the accepted home for cross-module domain utilities, rather than from another feature's namespace.

---
_Filed by daily arch-review routine on 2026-06-29._
