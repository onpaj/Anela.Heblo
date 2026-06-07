# Access Management UI — Design

**Date:** 2026-06-07
**Branch:** feature/rbac-inapp-permissions
**Status:** Approved (design), pending implementation plan

## Goal

Build a full-scale access-management UI on top of the existing RBAC backend. The
UI must let an administrator manage:

- **Groups** — create, update, delete.
- **Permission ↔ group** connections.
- **Group ↔ group** connections (nesting / inheritance).
- **User ↔ group** connections, where users are loaded live from **EntraID**.

## What already exists (reused as-is)

The backend RBAC implementation is largely complete and is reused, not rebuilt:

- **Domain** (`backend/src/Anela.Heblo.Domain/Features/Authorization/`): entities
  `PermissionGroup`, `UserGroup`, `GroupPermission`, `GroupParent`, `AppUser`;
  static `AccessMatrix` (features, sections, system groups, role values);
  `IAuthorizationRepository`, `IPermissionResolver`, `EffectivePermissions`.
- **Persistence** (`backend/src/Anela.Heblo.Persistence/Features/Authorization/`):
  EF Core configurations, `AuthorizationRepository`, `AuthorizationSeeder`,
  `PermissionResolver` (IMemoryCache, 5-min TTL, invalidated on writes),
  `GroupClosure` (transitive nesting with cycle detection).
- **API** (`backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs`),
  gated by `AdministrationRead` (view) / `AdministrationWrite` (mutate):
  - `GET /catalogue`, `GET /groups`, `GET /groups/{id}`,
    `POST /groups`, `PUT /groups/{id}`, `DELETE /groups/{id}`
  - `GET /users`, `GET /users/{id}/permissions`,
    `PUT /users/{id}/groups`, `PUT /users/{id}/active`
- **MediatR handlers** for each use case
  (`backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/`),
  including `GroupCycleCheck` cycle detection.
- **Graph** (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`):
  app-permission token acquisition; currently lists Entra **group members**.
- **Frontend** (minimal, to be replaced): `frontend/src/pages/AccessManagementPage.tsx`,
  hooks `frontend/src/api/hooks/useAccessManagement.ts`, generated API client.

**No database schema changes are required** — `AppUsers` / `UserGroups` already
model every connection.

## Decisions

1. **Users loaded from EntraID = live directory browse/search.** Add a Graph
   `/users` search so an admin can find anyone in the directory and assign groups
   even before that person's first login. The app DB stores only the group
   assignments (and a materialized `AppUser` record on first save).
2. **Object-centric editors**, not relationship-matrix views. Each connection is
   edited from the natural object. Matrix/bulk views are explicitly deferred.
3. **Users view is directory-first**: a single search-the-directory list with
   group assignments shown inline, rather than a separate "users with access"
   list.
4. **Replace** the existing minimal page at the existing `/admin/access` route.
5. **E2E tests deferred** (BE + FE unit/component tests in scope).

## Backend additions (the only new server work)

### 1. Directory search endpoint
`GET /api/admin/authorization/directory/users?search=<q>`

- Extends `GraphService` with a directory `/users` query using Microsoft Graph
  `$search` (and/or `$filter`) over displayName / mail / userPrincipalName.
- Requires the `ConsistencyLevel: eventual` header for `$search`.
- Minimum query length (2–3 chars); capped result count.
- Each result is merged with the app DB **by `EntraObjectId`** so it carries the
  user's current `groupIds` and `isActive` when an `AppUser` record exists.
- Returns `{ entraObjectId, displayName, email, groupIds, isActive, hasRecord }`.

### 2. Assign groups by Entra object ID
A searched user may have no `AppUser` row yet. Assignment must:

- Upsert the `AppUser` (materialize `EntraObjectId` / email / displayName from the
  Graph result),
- then set the user's groups,
- then invalidate the permission-resolver cache (existing mechanism).

Implemented either as a new `PUT /directory/users/{entraObjectId}/groups` or by
extending the existing assign use case to accept an Entra object ID. Group-ID
validation (existing) is retained.

### 3. (Optional) Disable by Entra object ID
`SetUserActive` already exists by app-user id. Toggling active for a
directory-sourced user reuses the same materialize-then-update path. Active toggle
is only meaningful once an `AppUser` record exists.

## Frontend

Replaces `AccessManagementPage.tsx` with a full-scale UI at `/admin/access`,
following the existing design system and page-layout conventions. Two areas
(tabs or sections): **Groups** and **Users**.

### Groups area (object-centric)

- **Groups list:** every group with permission / parent / member counts. System
  groups carry a "system" badge and are read-only (no edit/delete). Custom groups
  are editable and deletable.
- **Group editor** (shared create + edit form):
  - Name + description.
  - **Permissions (permission ↔ group):** the `/catalogue` features grouped by
    section (Finance, Produkty, Zákaznické, Nákup, Výroba, Sklad, Marketing,
    Anela, Administrace). Each feature is a row with Read / Write / Admin toggles,
    showing only the levels the feature supports (`HasWrite` / `HasAdmin`).
  - **Parent groups (group ↔ group):** multi-select of other groups. Server-side
    cycle errors are surfaced inline.
  - System groups open in a **read-only** view for inspection.

### Users area (directory-first, user ↔ group)

- **Directory search box** queries Entra live (debounced, min length). Results
  list displayName + email, with assigned groups shown inline as chips.
- Selecting a user opens an **assignment panel**: multi-select of groups → save.
  First save materializes the `AppUser` record, then assigns groups.
- An **active / disabled** toggle is shown for users that already have an app
  record.

## Error handling & validation

User-friendly inline messages, validated on both client and server boundaries:

- Duplicate group name.
- Invalid permission value.
- Group nesting cycle (rejected by `GroupCycleCheck`).
- System-group immutability (edit/delete blocked).
- Graph / directory-search failure (clear message + retry).
- Empty / too-short search queries handled gracefully.

## Testing

- **Backend:** handler/unit tests for directory-search (mock `GraphService`) and
  assign-by-object-id (materialize-then-assign, cache invalidation). Existing
  group/user handler tests remain green.
- **Frontend:** component tests for the group editor (permission toggling per
  supported level, parent multi-select, cycle error surfacing) and the directory
  search → assign flow (mocked hooks).
- **E2E (staging):** deferred unless re-scoped.

## Out of scope (deferred)

- Relationship-matrix / bulk-edit grids.
- Syncing the full Entra directory into the app DB.
- Scoping users to specific Entra security groups.
- Distributed cache for the permission resolver.
- E2E coverage.
