---
process: flow-user-access-onboarding
kind: workflow
module: authorization
summary: How an administrator gives a colleague access to Heblo in Access management — picking them from Entra, putting them into permission groups, editing groups, disabling people, and creating login-less packing operators.
owns:
  - backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs
  - backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs
  - backend/src/Anela.Heblo.Application/Features/Authorization/GroupCycleCheck.cs
  - backend/src/Anela.Heblo.Application/Features/Authorization/Contracts/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/Infrastructure/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UserDtos.cs
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GroupDtos.cs
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AddGroupMember/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AssignUserGroups/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/CreateGroup/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateGroup/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/DeleteGroup/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetGroups/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetGroupDetail/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUsers/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/SetUserActive/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/SetUserCanPack/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateUser/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/CreateLocalUser/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPackingUsers/**
  - backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/**
  - backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs
  - backend/src/Anela.Heblo.Persistence/Features/Authorization/*Configuration.cs
  - frontend/src/pages/AccessManagementPage.tsx
  - frontend/src/pages/GroupDetailPage.tsx
  - frontend/src/pages/UserDetailPage.tsx
  - frontend/src/components/access-management/**
  - frontend/src/components/pages/access/**
  - frontend/src/api/hooks/useAccessManagement.ts
verified_at: "5e993f9e2"
related: [calc-permission-resolution, sync-entra-directory]
---

# Giving people access (Access management)

## Purpose
How Anela's administrator lets a colleague into Heblo and decides what they may do. The
screen is **Access management** (`/admin/access`), with two tabs:
- **Users** (`/admin/access/users`, detail `/admin/access/users/:id`) — everyone Heblo knows:
  name, e-mail, active/disabled, "can pack" flag, groups, last activity, and the computed
  effective permissions.
- **Groups** (`/admin/access/groups`, detail `/admin/access/groups/:id`) — permission groups
  (Skladnik, Ucetni, Vedeni, …): their permissions, the groups they include, their members.

A person's final rights are computed from these groups as described in
`calc-permission-resolution`. Everything here writes only to Heblo's own database; nothing is
written to Entra or any other external system. Entra (Microsoft 365) is only *read* to offer
candidates.

## Trigger
User-driven. Reading the screens needs `admin.administration.read`; every change needs
`admin.administration.write` (controller `api/admin/authorization`, class-level
`[FeatureAuthorize(Feature.Admin_Administration)]` + per-action `AccessLevel.Write`). Seed group
**AccessManager** holds exactly these two plus `anela.process_docs.read`; super users always can.

States of a person (`AppUsers`):

| State | How it arises | Can sign in | Gets permissions |
|---|---|---|---|
| Pre-provisioned Entra user ("Never logged in", `LastLoginAt` null) | Admin adds them to a group from the Entra picker | yes | yes, from first request |
| Entra user, self-created | They signed in before anyone added them | yes | only `heblo_user` until put in a group |
| Disabled (`IsActive = false`) | Admin toggles Active off | token still valid | **none** (everything 403) |
| Local packing operator (`Source = Local`) | Admin creates by name | **no** (no Entra id) | none — only appears in packer pickers |

## Data flow
**1. Find the person in Entra and add them to a group** (Group detail → "Add Entra user")
1. `GET /api/admin/authorization/entra-users` → `GetEntraAccessUsersHandler` →
   `IEntraAccessUserSource.GetBaseMembersAsync` → Graph lookup of everyone assigned the
   **`heblo_user` app role** on Heblo's Entra application, groups expanded (detail in
   `sync-entra-directory`; cached 20 min). Sorted by display name. People already in this
   group are filtered out in the browser.
2. Picking a person immediately calls `POST /api/admin/authorization/groups/{id}/members`
   (`AddGroupMemberHandler`, body `entraObjectId`, `email`, `displayName` — all required):
   - creates an `AppUsers` row (`Source = Entra`, active, `LastLoginAt = null`) if no row has
     that `EntraObjectId`;
   - inserts `UserGroups(UserId, GroupId)` if missing (idempotent);
   - drops that person's permission cache.
   This is saved **at once**, before the page's Save button.
3. When the person first opens Heblo, the resolver finds the row by Entra object id, stamps
   `LastLoginAt`, and the "Never logged in" badge disappears.

**2. Edit a group** (Group detail → Save)
1. `POST /groups` (new) or `PUT /groups/{id}` (`CreateGroupHandler` / `UpdateGroupHandler`):
   name, description, permission list (picked from `GET /catalogue`), included groups
   (`ParentGroupIds`). The handler **replaces** the group's `GroupPermissions` and
   `GroupParents` with the submitted lists.
2. For membership changes made in the members picker the page then sends one
   `PUT /users/{userId}/groups` per affected user with that user's **full** new group list
   (`AssignUserGroupsHandler` deletes all of the user's `UserGroups` rows and re-inserts).
3. After an update, every *direct* member's permission cache is dropped.
4. `DELETE /groups/{id}` removes the group; its permissions, member links and its own
   "includes" links are deleted by cascade.

**3. Edit a user** (Users tab / User detail)
- `PUT /users/{id}` — display name, e-mail, can-pack (`UpdateUserHandler`).
- `PUT /users/{id}/active` — enable/disable (`SetUserActiveHandler`).
- `PUT /users/{id}/groups` — full group list (`AssignUserGroupsHandler`).
- `PUT /users/{id}/can-pack` — toggle from the grid (`SetUserCanPackHandler`).
- `GET /users/{id}/permissions` — effective permissions panel (see `calc-permission-resolution`).

**4. Create a packing operator** (Users tab → name → create)
- `POST /users/local` (`CreateLocalUserHandler`): `AppUsers` row with `Source = Local`,
  `EntraObjectId = null`, `Email = ""`, `CanPack = true`, active.
- The packing desk (**Balení**) lists everyone **active with `CanPack`** —
  `GET /api/packaging/packing-users` (`GetPackingUsersHandler`, ordered by name) — and the
  chosen id is stored as `PackedByUserId` / `PackedBy` on the shipment by the Packaging module.

## Logic & formulas
Validation and error codes (`ErrorCodes`, range 32xx):

| Rule | Error |
|---|---|
| Group name blank | `ValidationError` |
| Group name (trimmed) already used by another group (unique index on `Name`) | `AuthorizationDuplicateGroupName` (3206) |
| A permission string not in `AccessMatrix.AllRoleValues()` | `AuthorizationInvalidPermission` (3203) |
| Included groups would form a cycle, incl. a group including itself | `AuthorizationGroupCycleDetected` (3204) |
| Group / user id not found | `AuthorizationGroupNotFound` (3201) / `AuthorizationUserNotFound` (3202) |
| Add member: `entraObjectId`, `email`, `displayName` empty | validation (FluentValidation) |
| Local user / user edit: display name empty or > 255 chars; e-mail > 255 or not an e-mail | validation |
| Entra candidate lookup: token/config failure → `ConfigurationError`; Graph failure → `ExternalServiceError` | `GetEntraAccessUsersHandler` |

**Cycle check** (`GroupCycleCheck.WouldCreateCycle`): for each proposed included group, walk
*its* includes upwards; if the edited group is reached, the new link would close a loop.

**Cache invalidation** (5-minute permission cache): add member → that person; assign groups /
set active / update user → that user (Entra users only); update group → direct members only;
create group, delete group, set can-pack, create local user → none.

`CreatedBy` on a new group = the current user's e-mail. Names are trimmed; descriptions are not.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `admin.administration.read` / `.write` | granted per group | Who may view / change Access management |
| Entra app role `heblo_user` assignment | managed in Entra | Who appears in the "Add Entra user" picker |
| `AzureAd:ClientId` | `8b34be89-…` (prod: injected) | Which Entra app's role assignments are read |

## Runtime facts
None.

## Known quirks
- **AccessManager can grant itself anything.** `admin.administration.write` allows editing any
  group, including one's own, with any permission in the catalogue. Treat it as full admin.
- **Removing someone from a group = rewriting their whole group list.** There is no
  "remove member" endpoint; the group page sends `PUT /users/{id}/groups` per changed user. Two
  admins editing the same user's memberships at once: the last save wins.
- **Adding from the Entra picker saves immediately**, even if the admin then leaves the group
  page without pressing Save. Other edits on that page are lost if not saved.
- **Deleting a group that another group includes fails** (from code reading): `GroupParents`
  → parent is `OnDelete(Restrict)` and `DeleteGroupHandler` does not remove those links or
  catch the database error, so the request ends in a server error. Remove the "includes" link
  from the child groups first.
- **Deleting a group does not refresh its members' permissions** — they keep the deleted
  group's permissions for up to 5 minutes.
- **Re-adding a disabled person does not re-enable them.** Add member finds the existing row
  and only adds the group; `IsActive` stays false.
- **Only people with the Entra `heblo_user` app role are offered.** If the Graph lookup fails
  partway (service principal or assignment page not readable, `$batch` failure) it returns an
  **empty list without an error**, so the picker just looks empty. See `sync-entra-directory`.
- **A person who signs in before being added** gets an auto-created row with no groups; they
  see only the dashboard shell until an admin assigns a group on the Users tab.
- **Local operators cannot log in** and have no permissions; they exist only so the packing
  desk can record who packed. Disabling one (or clearing can-pack) removes them from the picker;
  creating a shipment with a packer who is missing, disabled or not can-pack fails with
  `PackingUserNotEligible` (3009) (`ShipmentCreationService`); the reprint backfill path does
  not check eligibility and falls back to the signed-in user's e-mail for an unknown id.
- **Stale runbook**: `docs/features/rbac-inapp-permissions-cutover.md` (June 2026) describes
  read-only "system groups" re-synced at startup; all groups are editable now and nothing
  re-syncs. `docs/features/entra-member-provisioning.md` points at an old `GraphService` path —
  it now lives in `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/`.

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs` — every endpoint and its permission
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AddGroupMember/AddGroupMemberHandler.cs` — provisioning a not-yet-signed-in person
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UpdateGroup/UpdateGroupHandler.cs` — group save, validation, cache drop
- `backend/src/Anela.Heblo.Application/Features/Authorization/GroupCycleCheck.cs` — cycle rule
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs` — `SetUserGroupsAsync` replace semantics
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/*Configuration.cs` — tables, keys, cascade/restrict rules
- `frontend/src/pages/GroupDetailPage.tsx` + `frontend/src/components/access-management/EntraMemberSearch.tsx` — what the page sends and when
- `frontend/src/components/pages/access/UsersGrid.tsx` — local users, can-pack, active toggles
