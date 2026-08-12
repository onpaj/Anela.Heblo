# Design: Move `InfrastructureConfigurationKeys` out of Domain layer

## Component Design

This is a pure move/rename refactor of a single `static class` of `const string` fields — no behavior, no interfaces, no DI registration involved.

**Source (delete):**
`backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs`
namespace `Anela.Heblo.Domain.Shared`

**Target (create):**
`backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs`
namespace `Anela.Heblo.Application.Shared`

Placed flat, as a sibling of the existing `Application/Shared/` constants holders (`ErrorCodes.cs`, `BaseResponse.cs`, `ListResponse.cs`, `HttpStatusCodeAttribute.cs`) — no new subfolder, consistent with existing convention.

Class body is byte-for-byte unchanged:

```csharp
public static class InfrastructureConfigurationKeys
{
    public const string APP_VERSION = "APP_VERSION";
    public const string USE_MOCK_AUTH = "UseMockAuth";
    public const string BYPASS_JWT_VALIDATION = "BypassJwtValidation";
}
```

No other file in `Domain/Shared/` (`Cooling.cs`, `CurrencyCode.cs`, `Result.cs`, `Rag/`) is touched. No `.csproj` changes — `Application → Domain`, `API → Application`, and `Adapters.Microsoft365 → Application` project references already exist and cover the new location.

**Consumers — `using` directive change only** (`using Anela.Heblo.Domain.Shared;` → `using Anela.Heblo.Application.Shared;`), no other line touched in any of these 10 files:

1. `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`
2. `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs`
3. `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs`
4. `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs`
5. `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs`
6. `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
7. `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
8. `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`
9. `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs`
10. `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

Each file has exactly one `using Anela.Heblo.Domain.Shared;` line, used solely to resolve `InfrastructureConfigurationKeys` — confirmed no other `Domain.Shared` symbol (`Cooling`, `CurrencyCode`, `Result`) is referenced in any of them, so the substitution is safe as a straight one-line replacement. If `dotnet format` reshuffles `using` ordering as a side effect, that is acceptable; no other manual edits.

Net dependency-graph effect: the `API/Adapters.Microsoft365 → Domain` edge (which existed only for this one class) collapses into the already-existing `API/Adapters.Microsoft365 → Application` edge. No new edges are introduced.

## Data Schemas

N/A — no persisted, transmitted, or API data model changes. The three compile-time string constants (`APP_VERSION`, `UseMockAuth`, `BypassJwtValidation`) and their values are unchanged; the `/config` endpoint response shape (backed by `GetConfigurationHandler`) is unaffected.
