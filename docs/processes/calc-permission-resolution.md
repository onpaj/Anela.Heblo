---
process: calc-permission-resolution
kind: calculation
module: authorization
summary: Works out, on every signed-in request, which Heblo permissions a person holds (their groups, plus the groups those groups include, plus the super_user override), and how the permission catalogue and the starter groups are produced from access-matrix.json.
owns:
  - access-matrix.json
  - access-matrix-entra.generated.json
  - backend/tools/Anela.Heblo.AccessMatrixGen/**
  - backend/tools/Anela.Heblo.AuthorizationSeeder/**
  - scripts/seed-authorization.sh
  - backend/src/Anela.Heblo.Domain/Features/Authorization/*.cs
  - backend/src/Anela.Heblo.Persistence/Features/Authorization/PermissionResolver.cs
  - backend/src/Anela.Heblo.Persistence/Features/Authorization/GroupClosure.cs
  - backend/src/Anela.Heblo.Persistence/Features/Authorization/JsonGroupSeeder.cs
  - backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionClaimsTransformation.cs
  - backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionAuthorizationResultHandler.cs
  - backend/src/Anela.Heblo.API/Controllers/AuthController.cs
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetMe/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUserEffectivePermissions/**
  - backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPermissionCatalogue/**
  - frontend/src/auth/PermissionsContext.tsx
  - frontend/src/auth/accessMatrix.generated.ts
  - frontend/src/api/hooks/usePermissions.ts
verified_at: "5e993f9e2"
related: [flow-user-access-onboarding]
---

# Effective permissions (who may see and do what)

## Purpose
Answers "why can (or can't) this person see this page or press this button?". Every Heblo
screen, API endpoint, dashboard tile and MCP tool is gated by a **permission** such as
`warehouse.packaging.write`. A person never holds permissions directly: they are a member of
one or more **groups** (Czech names such as Skladnik, Ucetni, Vedeni), groups hold permissions,
and a group can **include** other groups ("Zahrnuté skupiny" / parent groups) and inherit all
of their permissions. This doc describes how those memberships are turned into the final
permission list, where the list of possible permissions comes from, and the `super_user`
override.

Where the result is visible:
- The sidebar and buttons in the web app (read from `GET /api/auth/me`).
- The **effective permissions** panel of a user in Access management (`/admin/access/users/:id`,
  `GET /api/admin/authorization/users/{id}/permissions`).
- A **403** with a body naming the missing permission when someone calls an endpoint they
  lack (`PermissionAuthorizationResultHandler`).
- Dashboard tiles: `GetTileDataHandler` hides a tile whose `RequiredPermissions` the user lacks.

## Trigger
No Hangfire job. Runs **on demand**:
1. On every authenticated API request, through the ASP.NET claims transformation
   `PermissionClaimsTransformation` (once per request, guarded by an `authz_applied` claim).
2. On `GET /api/auth/me` (`GetMeHandler`) — the frontend calls it on app load and keeps the
   answer for 5 minutes (`staleTime` in `frontend/src/api/hooks/usePermissions.ts`).
3. Results are cached in process memory for **5 minutes per user** (`perms:{entraObjectId}`),
   and dropped early when an admin edits that user or a group they are a direct member of
   (see `flow-user-access-onboarding`).

Two developer-run steps produce the inputs (not scheduled, not run at deploy):
- **Generator** `Anela.Heblo.AccessMatrixGen` — runs automatically on every *Debug* build of
  `Anela.Heblo.API` (MSBuild target `GenerateAccessMatrix`, `ContinueOnError="true"`), or by hand.
- **Group bootstrap** `Anela.Heblo.AuthorizationSeeder` via `scripts/seed-authorization.sh
  <staging|production> [--reset-group <Name>]`.

## Data flow
**A. Catalogue of permissions (build time)**
1. `access-matrix.json` (repo root) is the single hand-edited source: `baseRole`
   (`heblo_user`), `features` (41 keys such as `Warehouse_Packaging`, each with Czech `label`,
   optional `hasWrite` / `hasAdmin`), `menuPaths` (frontend route → required permissions), and
   `seedGroups` (13 starter groups).
2. `AccessMatrixGen` writes five generated files: `Feature.generated.cs` (the `Feature` enum),
   `AccessMatrix.generated.cs` (features, menu paths, `AllRoleValues()`),
   `AccessRoles.generated.cs` (string constants incl. `Base = "heblo_user"`,
   `SuperUser = "super_user"`), `frontend/src/auth/accessMatrix.generated.ts`, and
   `access-matrix-entra.generated.json` (Entra app-role manifest with deterministic role ids,
   `DeterministicGuid.ForRole`).
3. `GET /api/admin/authorization/catalogue` (`GetPermissionCatalogueHandler`) serves the list
   to the group editor's permission picker. With the current JSON that is **70** permission
   strings (41 `.read`, 28 `.write`, 1 `.admin` — `marketing.photobank.admin`).

**B. Groups in the database (one-off bootstrap)**
4. `JsonGroupSeeder.AddMissingGroupsAsync` inserts every `seedGroups` entry whose `Name` does
   not yet exist in `PermissionGroups`, with its roles as `GroupPermissions` rows. Existing
   groups are **not touched**. `ResetGroupAsync(<Name>)` deletes all `GroupPermissions` of that
   one group and re-adds exactly the JSON list. Production asks for typing `PRODUCTION` and
   the group name. Connection string comes from Key Vault (`ConnectionStrings:<env>`).
5. From then on the groups are maintained only in the app (`/admin/access`, Groups tab).

**C. Per request (runtime)**
6. Identity: the Entra token's object id (`oid` via `GetObjectId()`; mock/E2E auth falls back
   to `NameIdentifier`). Email candidates in order: `preferred_username`, `upn`,
   `ClaimTypes.Email`, `email`, `ClaimTypes.Upn`. Name: `name`, `GivenName`, `ClaimTypes.Name`.
7. `PermissionResolver.ResolveAsync(oid, email, name)`:
   1. Cache hit `perms:{oid}` → return.
   2. Look up `AppUsers` by `EntraObjectId`. **Not found → insert** a new active user
      (Email = email ?? oid, DisplayName = name ?? email ?? oid, `Source = Entra`, no groups).
      A concurrent insert hitting the unique index is caught and re-read.
      Found → set `LastLoginAt = now` and save.
   3. Inactive user → **empty** result (not even `heblo_user`).
   4. Active → read the user's `UserGroups`, load the whole group graph (`GroupPermissions` +
      `GroupParents`) and run `GroupClosure.Resolve`, then add `heblo_user`.
   5. Cache the result for 5 minutes.
8. If the token carries the Entra app role **`super_user`**, the resolver still runs (so the
   user row and `LastLoginAt` are maintained) but its answer is replaced by **all** 70
   permissions + `heblo_user`.
9. Each permission is added to the identity as a role claim of the identity's own
   `RoleClaimType` (`roles` for Entra). `[FeatureAuthorize(Feature.X, AccessLevel.Y)]` is an
   `[Authorize(Roles = "x.y.level")]` underneath, so ASP.NET checks those claims.

## Logic & formulas
**Permission string** (`PermissionString.Format`): feature enum `Module_FeatureName` +
level → `module.feature_name.read|write|admin` (PascalCase → snake_case on each side of the
first `_`). Example: `Manufacture_BatchPlanning` + Write → `manufacture.batch_planning.write`.

**Group closure** (`GroupClosure.Resolve`): breadth-first walk from the user's *direct*
groups up through `GroupParents` (child `GroupId` inherits from `ParentGroupId`); each group is
visited once, so cycles and diamonds are safe. Result = union of `PermissionValue` of every
visited group. There is **no deny** and no precedence: permissions only add up.

**Levels are independent.** `.write` does not imply `.read`; a group needs both strings to
read and write. `.admin` likewise.

**Enforcement rules**
| Rule | Where |
|---|---|
| Every endpoint without an explicit policy requires an authenticated user **and** `heblo_user` | `AuthenticationExtensions.ConfigureAuthorizationPolicies` (`DefaultPolicy`) |
| `[FeatureAuthorize(F, L)]` requires that one permission | `FeatureAuthorizeAttribute` |
| `[FeatureAuthorize(F1, F2, …)]` requires **any one** of the features at `.read` (OR) | same, `params Feature[]` constructor |
| Class-level and method-level attributes both apply (AND) | ASP.NET authorization |
| `GET /api/auth/me` only needs an authenticated user (`AuthenticatedUser` policy) | `AuthController` |
| MCP tools check with `EnsureFeatureAccess(Feature, …)` | `MCP/McpAuthorizationExtensions.cs` |
| Frontend `hasPermission(p)` is true for super users or when `p` is in `/api/auth/me` permissions | `PermissionsContext.tsx` |

**What `/api/auth/me` returns**: `email`, `displayName`, `isSuperUser`, `permissions`,
`groups` (names of *direct* groups only, not inherited ones; empty for super users).

**Effective-permissions panel** (`GetUserEffectivePermissionsHandler`) recomputes the closure
straight from the DB (no cache) by Heblo user id: inactive → empty list; otherwise closure +
`heblo_user`, sorted. It does **not** know about `super_user` (that lives in the token), so a
super user's panel shows only their group permissions.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `access-matrix.json` → `baseRole` | `heblo_user` | Permission every active user gets; required by the default policy |
| `access-matrix.json` → `features[]` | 41 features | The catalogue; edit + regenerate to add a permission |
| `access-matrix.json` → `seedGroups[]` | 13 groups | Bootstrap content only (see quirks) |
| `PermissionResolver.CacheTtl` (code constant) | 5 min | How long a user's permission set is reused |
| Entra app role `super_user` on the API app registration | assigned in Entra, not in repo | Break-glass full access |
| `UseMockAuth` / E2E session | `false` (`true` in `appsettings.Test.json`) | Mock and E2E users carry `super_user` and so see everything |

Seed groups (`seedGroups`, as written in the JSON — production may differ):
Spravce (67 of 70 permissions; lacks `finance.price_analysis.read/.write` and
`warehouse.stock_override.read`), Vedeni (35), Vedouci_vyroby (15), Vedouci_skladu (13),
Skladnik (11), Marketer (10), Ucetni (8), Nakupci (6), Pracovnik_vyroby (6),
Eshop_Administrator (4), Poradenstvi (3), AccessManager (3: `admin.administration.read/.write`,
`anela.process_docs.read`), Zamestnanec (3).

## Runtime facts
- Production groups have drifted from `seedGroups`: in the prod DB Vedouci_skladu gets its
  warehouse permissions by **including Skladnik**, not directly — agent memory
  `gotcha_new_permission_not_granted_in_prod_db` — 2026-09-30.
- `warehouse.gift_packages.read` and `warehouse.stock_override.read` (added by #4198,
  v3.152.0, ~2026-09-16) were held by **no** production group; the gift-package page
  disappeared for everyone except super users and staff reported it on 2026-09-29 — same
  memory note — 2026-09-30.

## Known quirks
- **A new permission reaches nobody until someone grants it.** `seedGroups` only matters when a
  group does not exist yet, so adding a role to `seedGroups` changes nothing in a deployed DB.
  Moving an endpoint/page to a new permission therefore hides it from everyone but super users
  until it is granted at `/admin/access` (grant both `.read` and `.write` where both exist).
  Incident: #4198 (above). Pattern doc: `memory/patterns/adding-a-new-permission.md`.
- **`--reset-group` is destructive**: it wipes every hand-added permission of that group and
  re-adds only the JSON list. It does not touch group nesting or members.
- **Edits to an *included* group reach inheriting users up to 5 minutes late.** Saving a group
  invalidates the cache only for its *direct* members; users who get it through another group
  keep the old set until their 5-minute entry expires. Deleting a group invalidates nobody.
- **The cache is per server process.** With more than one app instance, an invalidation on one
  instance does not reach the others (each still expires after 5 minutes).
- **Any signed-in Entra account becomes an active Heblo user with `heblo_user`** on its first
  request (row auto-created, no groups). It then passes every endpoint that has only the
  default policy and sees the dashboard shell. Who can obtain a token for the Heblo API at all
  is controlled in Entra (app assignment), which this repo does not show.
- **Disabling a user** drops their cache entry at once on the instance that handled the edit
  (other instances: up to 5 minutes). A disabled user gets no permissions at all — not even
  `heblo_user` — so every endpoint answers 403. The web app keeps its own copy of
  `/api/auth/me` for 5 minutes, so the sidebar can lag behind as well.
- **`LastLoginAt` means "last seen", not "last login"**: it is written on every cache miss,
  i.e. at most every 5 minutes while the person is active. Super users are recorded the same way.
- **Super user is invisible in the DB.** `super_user` lives only in the Entra token; the
  Access-management effective-permissions panel and the user's group list cannot show it.
- **Removed or renamed features leave orphan strings.** `GroupPermissions.PermissionValue` is a
  free string. If a feature disappears from `access-matrix.json`, rows holding it remain, still
  become (harmless) role claims, and — from code reading — the group editor sends them back on
  save, which `UpdateGroupHandler` rejects with `AuthorizationInvalidPermission` until the
  string is removed in the DB.
- **Per-feature Entra app roles are unused.** `access-matrix-entra.generated.json` still lists
  one app role per permission, but since the in-app permission cutover only `super_user` and
  the `heblo_user` assignment (used for onboarding candidates, see `flow-user-access-onboarding`)
  matter in Entra.
- **Stale runbook**: `docs/features/rbac-inapp-permissions-cutover.md` says system groups are
  re-synced from `AccessMatrix` on every startup. That is no longer true — nothing seeds at
  startup since #2797; the seeder is the manual tool above.
- **Generator runs only in Debug builds** and with `ContinueOnError`, so a failed generation
  does not fail the build; CI/Release builds compile whatever generated files are committed.

## Code entry points
- `access-matrix.json` — the permission catalogue, menu gates and starter groups
- `backend/tools/Anela.Heblo.AccessMatrixGen/Program.cs` — what gets generated from it
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionClaimsTransformation.cs` — per-request injection, super_user wildcard, claim lookup order
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/PermissionResolver.cs` — user materialisation, inactive rule, cache
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/GroupClosure.cs` — inheritance walk
- `backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs` / `PermissionString.cs` — attribute → role string
- `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` — default policy (`heblo_user`)
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetMe/GetMeHandler.cs` — what the frontend receives
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/JsonGroupSeeder.cs` + `backend/tools/Anela.Heblo.AuthorizationSeeder/Program.cs` — bootstrap / reset
- `memory/patterns/adding-a-new-permission.md` — developer checklist for a new permission
