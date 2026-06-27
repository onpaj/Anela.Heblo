## Module
UserManagement

## Finding
`GetGroupMembersHandler` (Application layer) directly imports and catches infrastructure-specific exception types from external SDK packages:

- `using Microsoft.Identity.Client;` — line 6
- `catch (MsalException ex)` — line 36
- `catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)` — line 44

File: `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs`

The Application project's `.csproj` references `Microsoft.Identity.Web` and `Microsoft.Graph` as direct dependencies (`Anela.Heblo.Application.csproj` lines 28–29), confirming the layer takes on these SDK dependencies.

The correct pattern already exists in this codebase and is even documented in the same module: `ArticleUserResolverAuthException` and `ArticleUserResolverServiceException` (in `Article/Contracts/`) were explicitly created to wrap `MsalException` and `ODataError` so that "Application-layer consumers remain decoupled from SDK packages." `GraphArticleUserResolver` (in `UserManagement/Infrastructure/`) correctly catches the raw exceptions and rethrows as those typed exceptions before they surface in the Application layer.

The `GetGroupMembersHandler` bypasses this wrapper entirely.

## Why it matters
Clean Architecture's dependency rule prohibits the Application layer from knowing about infrastructure SDK types. Here, a change to MSAL's exception hierarchy or Graph's OData error model would require changes to the handler — the wrong layer. It also makes unit-testing the handler harder because tests must either suppress MSAL initialization or catch implementation-specific exceptions.

## Suggested fix
Define application-level exception types for the UserManagement service boundary (e.g., `GraphServiceAuthException` and `GraphServiceException` in `UserManagement/Contracts/`), mirroring `ArticleUserResolverAuthException/ServiceException`. Update `IGraphService.GetGroupMembersAsync`'s declared contract (via XML doc) to throw these. The `GraphService` adapter catches raw MSAL/OData exceptions and rethrows the application-level types. `GetGroupMembersHandler` then catches only the application-level types — no SDK import needed in the handler.

---
_Filed by daily arch-review routine on 2026-06-26._
