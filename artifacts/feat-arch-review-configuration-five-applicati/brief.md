## Module
Configuration

## Finding
`ConfigurationConstants.BYPASS_JWT_VALIDATION = "BypassJwtValidation"` is defined in `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs:14` precisely to give callers a single, rename-safe reference. Five Application-layer modules ignore it and use the raw string literal instead:

| File | Line |
|------|------|
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` | 21 |
| `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs` | 38 |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` | 58 |
| `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` | 27 |
| `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` | 41 |

By contrast the API layer (`HangfireDashboardTokenAuthorizationFilter.cs`, `AuthenticationExtensions.cs`, `HangfireAuthenticationMiddleware.cs`) and `UserManagementModule.cs` correctly use the constant.

## Why it matters
If the config key is ever renamed (e.g. during an appsettings restructure), a global find-and-replace on the constant catches all sites automatically. The five magic-string sites would silently silently start reading `false` (the `GetValue` default), disabling JWT-bypass checks without any compile-time or test-time signal. It also defeats the purpose of having a central constants file.

## Suggested fix
In each of the five files listed above, replace `"BypassJwtValidation"` with `ConfigurationConstants.BYPASS_JWT_VALIDATION` and add the necessary `using Anela.Heblo.Domain.Features.Configuration;`. No other change needed.

---
_Filed by daily arch-review routine on 2026-06-03._