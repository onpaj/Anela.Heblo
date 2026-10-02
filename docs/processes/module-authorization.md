---
process: module-authorization
kind: module
module: authorization
summary: Decides who may use Heblo and what each person may see and change — Heblo users, permission groups that can include other groups, and the permission catalogue every page, endpoint, dashboard tile and MCP tool is gated by.
owns: []
verified_at: "5e993f9e2"
related: [calc-permission-resolution, flow-user-access-onboarding, sync-entra-directory]
---

# Authorization (access & permissions)

## Purpose
Heblo is used by people with very different jobs — warehouse staff, production, purchasing,
accounting, marketing, management. This module answers two questions for every click:
**is this a Heblo user?** and **may they do this?**

- Signing in is done by Microsoft 365 (Entra ID). This module does not handle passwords.
- What a person may do is decided **inside Heblo**: people are put into **permission groups**
  (Skladnik, Ucetni, Vedeni, …); each group holds **permissions** such as "read the
  manufacture orders" or "change the catalogue"; a group may **include** other groups and
  inherit their permissions.
- One Entra app role, **`super_user`**, bypasses all of this and grants everything (break-glass
  for the owner and for automated tests).
- It also keeps the list of **packing operators** for the packing desk (Balení), including
  login-less "local" operators.

## Users & screens
| Who | Where | What |
|---|---|---|
| Administrator (`admin.administration.read` / `.write`, seed group AccessManager) | **Access management** `/admin/access` → Users tab `/admin/access/users`, Groups tab `/admin/access/groups`, detail pages `/admin/access/users/:id`, `/admin/access/groups/:id` | Add people from Entra, assign groups, disable users, edit groups and their permissions, create packing operators, see a user's effective permissions |
| Every user | whole app | The sidebar and buttons show only what `GET /api/auth/me` says they may use; a forbidden API call answers 403 naming the missing permission |
| Packing desk (Balení) | packer picker | List of active people marked "can pack" (`GET /api/packaging/packing-users`) |

No MCP tool of its own. MCP tools of other modules check permissions through
`EnsureFeatureAccess` with the same permission strings.

## Processes
- `calc-permission-resolution` — turns group memberships (incl. included groups) + the
  `super_user` role into the permission list on every request; where the permission catalogue
  (`access-matrix.json`) and the starter groups come from. On demand, 5-minute cache.
- `flow-user-access-onboarding` — Access management: add people from Entra into groups, edit
  groups, disable people, create packing operators. User-driven; writes only Heblo's DB.
- Entra candidates for onboarding are read via `sync-entra-directory` (user-management module).

Plain reads with no side effects: group list/detail, user list, permission catalogue.

## Data owned
All in schema `public` of the Heblo database:

| Table | One row means |
|---|---|
| `AppUsers` | A person Heblo knows: Entra object id (null for local operators), e-mail, display name, `IsActive`, `Source` (`Entra` / `Local`), `CanPack`, `CreatedAt`, `LastLoginAt` (really "last seen") |
| `PermissionGroups` | A named group (unique `Name`), description, `CreatedAt`, `CreatedBy` |
| `GroupPermissions` | Group holds one permission string (`PermissionValue`, e.g. `warehouse.packaging.write`) |
| `GroupParents` | Group `GroupId` includes (inherits from) group `ParentGroupId` |
| `UserGroups` | Person is a direct member of a group |

In-memory cache: `perms:{entraObjectId}` (5 min, per server process).

Not in the DB: the permission catalogue — it is code generated from `access-matrix.json`
(41 features → 70 permission strings + base `heblo_user`).

## External systems
- **Microsoft Entra ID** — sign-in tokens (object id, name, e-mail, `super_user` app role) are
  read on every request. Candidates for onboarding are read via Microsoft Graph in
  `sync-entra-directory`. Nothing is written to Entra.

## Dependencies
- Reads from: **user-management** (`IEntraAccessUserSource` → Graph; `ICurrentUserService`
  for the signed-in identity).
- Read by: **every module** (endpoint gates via `[FeatureAuthorize]`), **dashboard**
  (`GetTileDataHandler` filters tiles by permissions), **packaging** (packer list and
  eligibility, `PackedByUserId`), shared **user directory** (`IUserDisplayNameResolver` turns
  stored user ids/e-mails into names on feedback lists of Knowledge base, Leaflet, Smartsupp,
  Article — via `AuthorizationUserDirectorySourceAdapter`, 5-min cache).

## Known quirks
- **New permissions are granted to nobody** until an admin adds them to a group; editing
  `seedGroups` in `access-matrix.json` does not reach existing databases. #4198 hid the gift
  package page from all staff for ~2 weeks (reported 2026-09-29). Details in
  `calc-permission-resolution`.
- **Any signed-in Entra account becomes an active user** with the base permission
  `heblo_user` on first request (no groups) — it sees the dashboard shell only.
- **`super_user` is not visible in Heblo** (it lives in the Entra token), so the user list and
  effective-permissions panel under-report what a super user can do.
- **AccessManager is effectively full admin**: it can edit any group, including its own.
- **Deleting a group that another group includes fails** with a server error (from code
  reading; `GroupParents` restrict). Details in `flow-user-access-onboarding`.
- **Stale design docs**: `docs/features/rbac-inapp-permissions-cutover.md` mentions read-only
  system groups re-synced at startup — no longer true.

## Code entry points
- `access-matrix.json` — permission catalogue, menu gates, starter groups
- `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs` — admin API
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionClaimsTransformation.cs` — per-request enforcement
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/` — resolver, closure, repository, EF configuration, seeder
- `backend/src/Anela.Heblo.Domain/Features/Authorization/` — entities, `FeatureAuthorizeAttribute`, generated `Feature` / `AccessMatrix` / `AccessRoles`
- `frontend/src/pages/AccessManagementPage.tsx`, `frontend/src/auth/PermissionsContext.tsx` — UI
- `memory/patterns/adding-a-new-permission.md` — checklist for adding a permission
