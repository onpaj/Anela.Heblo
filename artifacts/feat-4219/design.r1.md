# Design: Extract Helper Methods from `GraphService.GetAppRoleMembersAsync`

## Component Design

Single component: `GraphService` (`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`), implementing `IGraphService`. No new components, services, or classes are introduced. `IGraphService`'s public surface (`GetGroupMembersAsync`, `GetAppRoleMembersAsync`) is unchanged.

`GetAppRoleMembersAsync` is decomposed into one orchestrating public method plus four new `private` methods, each responsible for one sequential Graph API step:

| Member | Responsibility | I/O |
|---|---|---|
| `GetAppRoleMembersAsync` (public, existing) | Orchestrates the 5 steps in sequence; owns guard clauses, caching, and the outer try/catch | — |
| `ResolveServicePrincipalAsync` (new, private) | Resolves the service principal id and its `appRoles` array for the configured `AzureAd:ClientId` | 1 Graph GET |
| `FindAppRoleId` (new, private, synchronous) | Finds the `appRoleId` matching the requested role value within an already-fetched `appRoles` array | none (in-memory) |
| `CollectRoleAssigneesAsync` (new, private) | Paginates `appRoleAssignedTo` for the resolved role, bucketing direct users vs. groups to expand | N Graph GET (paginated) |
| *(un-extracted, stays inline per issue instruction)* | Expands each group in `groupIdsToExpand` via the existing `GetGroupMembersAsync` | delegates to existing public method |
| `BatchResolveUserDtosAsync` (new, private) | Resolves display name/email for the collected user ids via Graph `$batch`, chunked by `GraphBatchSize` | ⌈N/20⌉ Graph POST |

Each new private method takes its dependencies (`HttpClient`, the already-acquired `graphToken`, ids) as parameters rather than reading instance state directly for anything beyond `_logger`/`_configuration` where the original code already did — this keeps them structurally ready for isolated unit testing in a future change, without requiring new tests now (see spec Out of Scope / NFR-3).

Failure handling: each new helper detects and logs its own failure case(s) (exact existing log templates/levels) and signals failure to the orchestrator via a `null`/nullable-tuple return, matching the existing "log and return empty list" behavior — see `arch-review.r1.md` → Decision 1 for the full rationale and the exact chosen shapes per helper.

## Data Schemas

No schema changes. No new DTOs, no persisted data, no request/response contract changes. The existing `UserDto { string Id, string DisplayName, string Email }` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/UserDto.cs`) is unchanged and remains the only externally visible type touched by this method. All Graph API request/response JSON shapes (service principal lookup, `appRoleAssignedTo` pagination, `$batch`) are unchanged — this is an internal code-structure refactor only.
