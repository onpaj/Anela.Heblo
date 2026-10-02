---
process: sync-entra-directory
kind: sync
module: user-management
summary: On-demand, cached reads of Anela's Microsoft 365 directory (Entra ID) through Microsoft Graph — members of an Entra group, and everyone holding Heblo's heblo_user app role — used for onboarding, the MCP GetGroupMembers tool and the article requester backfill.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/**
  - backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs
  - backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs
  - backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/UserDto.cs
  - backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/**
  - backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/**
  - backend/src/Anela.Heblo.Application/Features/UserManagement/Validators/**
  - backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs
  - backend/src/Anela.Heblo.API/MCP/Tools/UserManagementMcpTools.cs
verified_at: "5e993f9e2"
related: [flow-user-access-onboarding]
---

# Entra directory lookups (Microsoft Graph)

## Purpose
Heblo does not copy Anela's staff list from Microsoft 365. When it needs to know "who is in
this Entra group?" or "who is allowed to use Heblo at all?", it asks Microsoft Graph live and
keeps the answer in memory for 20 minutes. Three places use it:

| Consumer | Question | Who sees it |
|---|---|---|
| Access management → Group detail → "Add Entra user" (`GET /api/admin/authorization/entra-users`) | Everyone assigned the Entra app role **`heblo_user`** on Heblo's app (directly or via an Entra group) | Administrators (`admin.administration.read`), see `flow-user-access-onboarding` |
| `GET /api/UserManagement/group-members?groupId=…` and MCP tool **`GetGroupMembers`** | Direct user members of one Entra group | `admin.administration.read` |
| Article requester backfill (`POST` on `ArticlesController`, `admin.administration.write`) | Members of an Entra group, to map free-text "requested by" names to people | Marketing articles admin (Article module) |

Nothing is written to Entra and nothing is stored in Heblo's database by this process.

## Trigger
On demand only — no Hangfire job, no BackgroundRefresh task. Each lookup is cached per server
process for **20 minutes** (`GraphService._cacheExpiration`):
- `group_members_{groupId}`
- `app_role_members_{appRoleValue}` (in practice `app_role_members_heblo_user`)

A server restart clears the cache.

## Data flow
Authentication: app-only token for scope `https://graph.microsoft.com/.default` via
`ITokenAcquisition.GetAccessTokenForAppAsync` (Heblo's own app registration, no user context).
HTTP client: named client `MicrosoftGraph`.

**Group members** (`GraphService.GetGroupMembersAsync(groupId)`):
1. `GET https://graph.microsoft.com/v1.0/groups/{groupId}/members?$select=id,displayName,mail,userPrincipalName`
2. `ParseMembersFromJson` keeps an entry if `@odata.type` contains "user" or it has a
   `userPrincipalName`; nested groups, devices and service principals are dropped.
3. `UserDto { Id = Entra object id, DisplayName, Email = mail ?? userPrincipalName ?? "" }`.

**App-role members** (`GraphService.GetAppRoleMembersAsync("heblo_user")`):
1. `GET /v1.0/servicePrincipals(appId='{AzureAd:ClientId}')?$select=id,appRoles` → service
   principal id and the app role id whose `value` is `heblo_user`.
2. `GET /v1.0/servicePrincipals/{spId}/appRoleAssignedTo?$top=100`, following
   `@odata.nextLink`; keep assignments with that `appRoleId`; `principalType = User` → user id,
   `principalType = Group` → expanded with the group-members call above.
3. `POST /v1.0/$batch` in chunks of 20 (`GraphBatchSize`):
   `GET /users/{id}?$select=id,displayName,mail,userPrincipalName` → `UserDto`.
4. `EntraAccessUserSourceAdapter` maps the result to `EntraAccessUserRecord(Id, Email, DisplayName)`
   for the Authorization module.

**Article backfill**: `GraphArticleUserResolver.ResolveByGroupAsync(groupId)` → group members →
`ArticleUserMatch(Id, DisplayName)`; the matching itself belongs to the Article module.

## Logic & formulas
Error mapping:

| Failure | Group members (API / MCP) | App-role members (onboarding) |
|---|---|---|
| Token/MSAL failure | `GraphServiceAuthException` → `ConfigurationError` | same → `ConfigurationError` |
| Graph returns non-2xx | `GraphServiceException` → `ExternalServiceError` | service-principal, assignment or `$batch` failure → **empty list, no error** (logged) |
| Caller not permitted (`UnauthorizedAccessException`) | `Forbidden` | — |
| `AzureAd:ClientId` missing | — | empty list (logged) |
| Role `heblo_user` not defined on the app | — | empty list (logged warning) |
| Single user in `$batch` not 200 | — | that user skipped (warning) |

`groupId` must be non-blank (`GetGroupMembersRequestValidator`). MCP surfaces failures as
`McpException("[<ErrorCode>] …")`.

With `UseMockAuth` or `BypassJwtValidation` = true, `MockGraphService` is registered instead
and every lookup returns an empty list.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `AzureAd:ClientId` | `8b34be89-f86f-422f-af40-7dbcd30cb66a` (`appsettings.json`; Production: injected) | Which app registration's `heblo_user` assignments are read |
| `UseMockAuth` / `BypassJwtValidation` | `false` (Test: `UseMockAuth = true`) | `true` → `MockGraphService`, empty results |
| Graph application permissions | granted in Entra | `GroupMember.Read.All`, `User.Read.All`, plus `Application.Read.All` (or `Directory.Read.All`) for app-role assignments — per `docs/features/entra-member-provisioning.md` |
| Cache TTL (code) | 20 min | Both cache keys |

## Runtime facts
None.

## Known quirks
- **Group members are not paged.** `GetGroupMembersAsync` reads only the first page of
  `/members` (Graph default page size is 100) and ignores `@odata.nextLink`, so a large group
  — or a large Entra group assigned to `heblo_user` — is silently truncated. The
  `appRoleAssignedTo` call does follow paging.
- **Only direct members.** `/members` is not transitive; users inside a nested Entra group are
  not returned.
- **Silent empty onboarding list.** Most failures of the app-role lookup return an empty list
  instead of an error, so the "Add Entra user" picker just shows nothing. Check the app logs
  (`GraphService`) and the Graph permission `Application.Read.All`.
- **Up to 20 minutes stale.** A person just given `heblo_user` in Entra appears in the picker
  only after the cache entry expires or the app restarts.
- **Mock/dev returns nothing.** Locally with mock auth the picker and `GetGroupMembers` are
  always empty.
- **Doc path drift.** `docs/features/entra-member-provisioning.md` still names
  `Application/Features/UserManagement/Services/GraphService.cs`; the implementation is in the
  Microsoft365 adapter.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — all Graph calls, parsing, cache keys
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` — mock vs real registration, `MicrosoftGraph` client
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs` — onboarding candidates
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs` — API/MCP error mapping
- `backend/src/Anela.Heblo.API/MCP/Tools/UserManagementMcpTools.cs` — MCP `GetGroupMembers`
