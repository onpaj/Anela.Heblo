---
process: module-user-management
kind: module
module: user-management
summary: Who is signed in (identity from the Microsoft 365 token), live look-ups of Anela's Entra directory through Microsoft Graph, and the list of Flexi cost centres (departments) used as filters.
owns: []
verified_at: "5e993f9e2"
related: [sync-entra-directory, sync-flexi-departments, flow-user-access-onboarding]
---

# User management (identity & directory look-ups)

## Purpose
A small supporting module with three unrelated jobs:
1. **Who is calling?** — reads the signed-in person's Entra object id, name and e-mail from the
   Microsoft 365 token (`ICurrentUserService.GetCurrentUser()`), used across Heblo to stamp
   "created by", to look up permissions and to show the user.
2. **Who is in Anela's directory?** — live, cached Microsoft Graph look-ups: members of an
   Entra group, and everyone allowed to use Heblo (`heblo_user` app role). Used by Access
   management onboarding, the MCP tool `GetGroupMembers` and the article requester backfill.
3. **Departments** — the Flexi cost centres (střediska) offered as filters in the financial
   overview and invoice classification.

Heblo's own user records and permissions are **not** here — they belong to the
`authorization` module (`module-authorization`).

## Users & screens
No page of its own. It serves:
| Consumer | Endpoint / tool | Permission |
|---|---|---|
| Access management → "Add Entra user" | via `GET /api/admin/authorization/entra-users` | `admin.administration.read` |
| Admin / MCP clients | `GET /api/UserManagement/group-members?groupId=`; MCP **`GetGroupMembers`** | `admin.administration.read` |
| Financial overview filter, Invoice classification rules | `GET /api/Departments` | `finance.financial_overview.read` **or** `purchase.invoice_classification.read` |

## Processes
- `sync-entra-directory` — Graph: group members and `heblo_user` app-role members, on demand,
  20-min in-memory cache.
- `sync-flexi-departments` — Flexi cost centres, on demand, 10-min in-memory cache.

The current-user service is not a process (no data moves); it is described below.

**Current user** (`CurrentUserService`, singleton over `IHttpContextAccessor`):
- `Id` = Entra object id (`oid`), else `NameIdentifier`, else `sub`.
- `Email` = `ClaimTypes.Email`, `email`, `preferred_username`, `ClaimTypes.Upn`, `upn` (first found).
- `Name` = `Identity.Name`, `ClaimTypes.Name`, `name`, else "Unknown User" / "Anonymous".
- `GetDisplayName()` → "System" when not authenticated; `GetIdentifier()` → id ?? e-mail ?? "system".
- `IsInRole(role)` checks the role claims added by the authorization module (incl. `super_user`).

## Data owned
None in the database. In-memory caches only: `group_members_{groupId}` and
`app_role_members_{role}` (20 min), `flexi_departments` (10 min).

## External systems
| System | Calls | Direction |
|---|---|---|
| Microsoft Graph (Entra ID), app-only token | `GET /groups/{id}/members`, `GET /servicePrincipals(appId=…)`, `GET /servicePrincipals/{id}/appRoleAssignedTo`, `POST /$batch` (`GET /users/{id}`) | read |
| Flexi (ABRA FlexiBee) | cost centres (`stredisko`) via `Rem.FlexiBeeSDK` department client | read |

## Dependencies
- Read by: **authorization** (onboarding candidates, current identity), **article**
  (requester backfill via `IArticleUserResolver`), **finance / financial overview** and
  **purchase / invoice classification** (departments), and nearly every module through
  `ICurrentUserService`.
- Reads from: authorization's role claims (`IsInRole`).

## Known quirks
- **Two different e-mail orders.** `CurrentUserService` prefers `ClaimTypes.Email`/`email`
  before `preferred_username`; the authorization claims transformation prefers
  `preferred_username` first. For Entra tokens (which normally carry no `email` claim) both end
  up on the UPN.
- **`Name` may be the UPN.** For Web API tokens `Identity.Name` maps to `preferred_username`,
  so `GetCurrentUser().Name` can be the e-mail address rather than the display name (the
  authorization module reads the raw `name` claim to avoid this).
- **Graph group look-ups are not paged and not transitive** (first 100 direct members only),
  and most failures of the `heblo_user` look-up return an empty list silently — see
  `sync-entra-directory`.
- **With mock auth all Graph look-ups return empty** (`MockGraphService`).
- **"Departments" are cost centres**, not people's departments; they live here for historical
  reasons only.

## Code entry points
- `backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs` — identity extraction
- `backend/src/Anela.Heblo.Domain/Features/Users/` — `CurrentUser`, extensions
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — Graph calls
- `backend/src/Anela.Heblo.Application/Features/UserManagement/` — use cases, adapters for other modules
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/` — Flexi departments
