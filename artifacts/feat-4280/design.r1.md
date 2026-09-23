# Design: Relocate GraphService exception types out of UserManagement/Contracts

## Component Design

### `GraphServiceAuthException` / `GraphServiceException`
- **Location:** `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/` (moved from `Contracts/`).
- **Namespace:** `Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions` (moved from `...Contracts`).
- **Responsibility:** unchanged — each wraps an SDK-specific Microsoft Graph failure (`GraphServiceAuthException` for token/auth failures, `GraphServiceException` for OData/service error responses) behind a plain `Exception` subclass so Application-layer consumers of `IGraphService` stay decoupled from the Graph SDK. Both remain `public sealed class` with the same constructor signature `(string message, Exception innerException)`. No members, base type, or constructor overloads change.
- **Consumers (unchanged behavior, updated imports only):**
  - `Services/IGraphService.cs` — interface contract documents these exceptions via XML `<exception cref>` comments on `GetGroupMembersAsync` and `GetAppRoleMembersAsync`.
  - `UseCases/GetGroupMembers/GetGroupMembersHandler.cs` — catches both, maps to `GetGroupMembersResponse` with `ErrorCodes.ConfigurationError` / `ErrorCodes.ExternalServiceError` respectively.
  - `Infrastructure/EntraAccessUserSourceAdapter.cs` — catches both, re-wraps as `EntraAccessSourceAuthException` / `EntraAccessSourceException`.
  - `Infrastructure/GraphArticleUserResolver.cs` — catches both, re-wraps as `ArticleUserResolverAuthException` / `ArticleUserResolverServiceException`.

### Import (`using`) changes per consumer
This is the only observable change to each consumer file — see the architecture review's file-by-file table for the authoritative per-file breakdown. Summary:

| File | `using ...Contracts;` | `using ...Infrastructure.Exceptions;` |
|---|---|---|
| `Services/IGraphService.cs` | keep (needed for `UserDto`) | add |
| `UseCases/GetGroupMembers/GetGroupMembersHandler.cs` | keep (needed for `UserDto`) | add |
| `Infrastructure/EntraAccessUserSourceAdapter.cs` | keep (needed for `UserDto`) | add |
| `Infrastructure/GraphArticleUserResolver.cs` | remove (no other symbol from `Contracts` is referenced) | add (replaces it) |

No other component's public surface, dependency graph, or DI registration is affected — `IGraphService`'s implementations are registered exactly as before; only the exception types' compile-time location changes.

## Data Schemas
Not applicable. No DTOs, database schemas, API request/response shapes, or event payloads are added, removed, or altered by this change. The two relocated types are internal Application-layer exceptions, never serialized, never exposed through the OpenAPI/generated client surface, and therefore outside the DTO-generation rules in `docs/architecture/development_guidelines.md`.
