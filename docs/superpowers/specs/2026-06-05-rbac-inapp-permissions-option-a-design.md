# Design Spec: In-app Permission System — Option A (claims transformation)

**Date:** 2026-06-05
**Branch:** `claude/rbac-inapp-permissions-spike-wHmHw`
**Status:** Design approved (brainstorming) — pending written-spec review → implementation plan
**Related:** `docs/features/rbac-inapp-permissions-spike.md` (the originating spike)

---

## 1. Summary

Move **authorization data** (groups, group nesting, user assignments) out of Entra ID and
into the application database, while keeping **Entra ID for authentication**. Enforcement
happens via an ASP.NET Core **`IClaimsTransformation`** that injects a user's effective
permissions as `Role` claims, so **every existing `[Authorize(Roles = "feature.level")]`
attribute and `ICurrentUserService.IsInRole` keeps working unchanged**.

This is "Option A" from the spike. It is deliberately the lowest-blast-radius path: the
change is data + one transformation step, not a rewrite of the enforcement surface.

### Decisions locked during brainstorming

| # | Decision | Choice |
|---|---|---|
| 1 | Build approach | **Roll-your-own** `Authorization` vertical slice (no AuthP/Casbin) |
| 2 | Permission source | **Code-defined** — `AccessMatrix` stays the source of truth; only groups & assignments are data |
| 3 | Group relationships | **Full group→group nesting** (cycle-guarded DAG) |
| 4 | New user default | **No access until assigned** (least privilege) |
| 5 | super_user bootstrap | **Entra app role `super_user`** = wildcard, honored directly from the token |
| 6 | Permission freshness | **Short-TTL in-memory cache** (~5 min), no active invalidation |
| 7 | Rollout | **Direct cutover** (single release, no shadow/feature-flag parallel run) |

---

## 2. Architecture & enforcement flow

Authentication is **unchanged**: Microsoft Identity Web (web-app + web-API flows), mock
auth (`UseMockAuth`), and E2E cookie auth all stay as-is. Entra now issues only identity
plus (optionally) the `super_user` app role. All per-feature roles leave the token.

One new cross-cutting slice, `Authorization`:
- `IPermissionResolver` interface in `Domain/Features/Authorization/`.
- EF entities + resolution implementation in `Anela.Heblo.Persistence`.
- DI binding in a new `AuthorizationModule.cs` (ADR-004 — binding lives in the owning
  module, not `PersistenceModule`).

### Request flow

```
Request
  → Entra JWT validated (identity + maybe "super_user" app role)
  → PermissionClaimsTransformation : IClaimsTransformation
        if token has "super_user":
            inject ALL AccessMatrix.AllRoleValues() + "heblo_user" as Role claims
            short-circuit (no DB lookup, no closure)
        else:
            resolve AppUser by Entra objectId  → effective permissions (cached)
            inject each "feature.level" + "heblo_user" as Role claims
  → [Authorize(Roles = "catalog.read")]  // reads Role claims — UNCHANGED
  → MediatR handler, ICurrentUserService.IsInRole(...) — UNCHANGED
```

Because effective permissions become **`Role` claims**, the ~50 existing
`[Authorize(Roles = …)]` attributes, the default policy (`RequireRole("heblo_user")`),
and `CurrentUserService` need **no code change**.

### `super_user` (break-glass)

`super_user` is an **Entra app role**, read **directly from the token claims, before and
independent of any DB lookup**. It grants **all** `AccessMatrix` permissions and
short-circuits resolution. It works even when the user has **no `AppUser` row, no group
assignments, or `IsActive=false`** — nothing in the DB can lock out a super_user. The
`AppUser` row is still upserted for audit/`LastLoginAt`, but super_user permissions never
depend on it. Any permission later added to `AccessMatrix` is automatically granted to
super users with zero config.

This is also how the **first admin** reaches the admin UI before any assignments exist:
Entra (controlled by IT) is the bootstrap and permanent break-glass path.

---

## 3. Data model

Permissions remain **code constants** in `AccessMatrix` (capabilities tied to `[Authorize]`
attributes — a DB row must never grant something the code doesn't enforce). Only groups,
nesting, and assignments are data. Five new tables in the existing single
`ApplicationDbContext` (ADR-001), added by one EF migration.

```
AppUser
  Id              (PK, Guid)
  EntraObjectId   (string, unique)     -- from NameIdentifier; the join key
  Email, DisplayName
  IsActive        (bool)               -- soft disable without deleting
  CreatedAt, LastLoginAt
  -- materialized on first successful login

PermissionGroup                        -- a "group" / role bundle
  Id              (PK, Guid)
  Name            (string, unique)
  Description
  IsSystem        (bool)               -- seeded from AccessMatrix.Groups; not deletable
  CreatedAt, CreatedBy

GroupPermission                        -- group → permission (perm = code constant)
  GroupId         (FK → PermissionGroup)
  PermissionValue (string)             -- validated ∈ AccessMatrix.AllRoleValues()
  PK (GroupId, PermissionValue)

GroupParent                            -- group → group nesting (DAG)
  GroupId         (FK → PermissionGroup)
  ParentGroupId   (FK → PermissionGroup)
  PK (GroupId, ParentGroupId)
  -- write-time cycle guard rejects edges that would close a loop

UserGroup                              -- user → group
  UserId          (FK → AppUser)
  GroupId         (FK → PermissionGroup)
  PK (UserId, GroupId)
```

**Notes**
- Permissions are **not** a table — `GroupPermission.PermissionValue` is validated against
  `AccessMatrix.AllRoleValues()` on write; a seed-sync prunes rows whose permission was
  removed from `AccessMatrix` (DB ⊆ code).
- The 10 existing `AccessMatrix.Groups` seed `PermissionGroup` rows with `IsSystem=true`
  and their `GroupPermission` edges → day-one behavior equals today's tiers.
- `super_user` is **not** a DB row — it is the Entra wildcard.

---

## 4. Effective-permission resolution & caching

`IPermissionResolver.GetEffectivePermissions(entraObjectId)`:

1. Load `AppUser` by `EntraObjectId`; materialize on first login. If `IsActive == false`
   → return empty set (passes Entra login, no app access).
2. Collect directly-assigned groups (`UserGroup`).
3. **Transitive closure** over `GroupParent`: BFS over parents with a **visited-set**, so
   shared/diamond parents are counted once and any cycle terminates with a bounded result.
4. Union `GroupPermission.PermissionValue` across all reached groups.
5. Always add `heblo_user` for an active user.

**Caching:** `IMemoryCache`, key `perms:{entraObjectId}`, value = permission set, TTL ~5 min
(configurable). Populated by the claims transformation on a miss. No active invalidation —
admin edits land within the TTL window. Disabling a user (`IsActive=false`) takes effect
within one TTL window; if an instant kill-switch is ever needed, add per-user invalidation
later (YAGNI for now).

**Scale note:** the group graph is tiny (dozens of nodes); the uncached path is a couple of
cheap queries. In-memory cache suits the current single-instance deployment; a scale-out
would move to a distributed cache or accept per-instance TTL.

---

## 5. Admin surface

New admin area under the existing `administration` feature (gated by `administration.read`
/`administration.write`). Backend = MediatR vertical slices in
`Application/Features/Authorization`; thin controllers (ADR-003/005).

**Use-cases (MediatR)**
- *Groups:* `GetGroups`, `GetGroupDetail`, `CreateGroup`, `UpdateGroup`
  (name/description/permissions/parents), `DeleteGroup`.
  - Validation: permissions ∈ `AccessMatrix`; parent edges **cycle-checked**; **system
    groups are not deletable** (permission/description edits allowed — exact lock level to
    confirm at review).
- *Users:* `GetUsers`, `GetUserEffectivePermissions` (read-only closure preview),
  `AssignUserGroups`, `RemoveUserGroups`, `SetUserActive`.
- *Catalogue:* `GetPermissionCatalogue` — returns `AccessMatrix` features/levels/groups so
  the UI renders checkboxes from the source of truth (no hardcoded React list).

**API:** `/api/admin/authorization/{groups,users,catalogue}`; writes `administration.write`,
reads `administration.read`. OpenAPI → TS client auto-generated.

**Frontend:** Administration → Access management screen, reusing the existing design system
(`docs/design/`), gated by `RequireAccess` on `administration.*`:
- **Groups** tab — list; create/edit with a permission checkbox matrix grouped by
  feature/section and a parent-group multi-select; delete (non-system only).
- **Users** tab — list; toggle active; assign groups; "view effective permissions" drawer.

---

## 6. Frontend permission delivery

Feature roles are no longer in the token, so the SPA fetches its own permissions:

- **`GET /api/auth/me`** → `{ email, displayName, isSuperUser, permissions: ["catalog.read", …] }`,
  computed server-side via the same `IPermissionResolver` (single source of truth).
- `useAuth.ts`: replace `idTokenClaims?.roles` with `permissions` from `/api/auth/me`.
  Downstream (`RequireAccess.tsx`, `accessMatrix.generated.ts` route→permission map) is
  unchanged — it just consumes a differently-sourced string array.
- `accessMatrix.generated.ts` stays (route→required-permission map, still generated from
  `AccessMatrix`); only the source of the *user's own* list changes.
- Mock auth (`mockAuth.ts`) and E2E: `/api/auth/me` honors the mock/E2E identity and returns
  the appropriate set (mock = all, matching today).
- Refresh: refetch on app load / token refresh; admin changes land on next session/refetch
  (consistent with the backend short-TTL cache). No live push (YAGNI).

---

## 7. Seed & direct cutover

**Seeding (idempotent; startup or migration):**
1. Upsert the 10 `AccessMatrix.Groups` as `IsSystem=true` `PermissionGroup` rows + their
   `GroupPermission` edges (`Spravce` = all roles, `Vedeni` = all `.read`, …).
2. Prune `GroupPermission` rows whose `PermissionValue` left `AccessMatrix`.
3. No user assignments seeded (decision #4). Until assigned, `super_user` (Entra) is the
   way in.

**Direct cutover (single release — decision #7):**
1. Ship migration + `Authorization` slice + claims transformation + `/api/auth/me` + admin
   UI together.
2. Switch enforcement to the in-app claims transformation (no feature flag, no parallel run).
3. Entra: keep the `super_user` app role; per-feature app roles become unused (can be
   removed from the manifest later — not required for cutover).
4. **Operational runbook (replaces shadow mode):** immediately post-deploy, a super_user
   logs in → admin UI → assigns each staff member to the in-app group matching their old
   Entra role (names already align 1:1: `Marketer`, `Skladnik`, …). System groups mirror
   the old matrix exactly, so this is mechanical.

**Risk (explicit):** between deploy and finishing assignments, non-super_users have **no
access**. Mitigations: low-traffic window; pre-written assignment list; super_user always
available. Rejected fallback (auto-assign from current Entra groups on first login) needs
Graph group data and contradicts decision #4 — left out unless reconsidered at review.

**Dev/test:** mock auth keeps granting everything; nothing changes locally.

---

## 8. Testing strategy

**Unit — resolution (risk concentrates here):**
- Closure: union across groups; diamond/shared parent counted once; deep chains; empty groups.
- Cycle safety: a `GroupParent` cycle still terminates with a bounded set (visited-set).
- Cycle rejection: an `UpdateGroup`/add-parent that would close a loop is rejected.
- `super_user`: returns all permissions with **no AppUser row**, **no groups**, and when
  **`IsActive=false`**.
- Inactive non-super user → empty; active user always has `heblo_user`.
- Validation: writing a `PermissionValue` ∉ `AccessMatrix` is rejected; seed-prune removes
  orphans.

**Integration (use-case / WebApplicationFactory):**
- First login materializes `AppUser`; repeat login updates `LastLoginAt`, no duplicate.
- Claims transformation injects correct `Role` claims → a representative
  `[Authorize(Roles="catalog.read")]` endpoint: 200 with group, 403 without.
- `/api/auth/me` parity with enforcement (same set the transformation computes).
- Admin endpoints gated by `administration.write`; non-admin → 403.
- Cache: change a group → effect within TTL.

**Architecture tests** (extend existing suites):
- `AuthorizationModule` owns its repo DI binding (ADR-004); `PersistenceModule` registers
  no new repo.

**Frontend (Jest):** `useAuth` consumes `/api/auth/me`; `RequireAccess` allow/redirect;
mock path returns all.

**E2E (nightly, staging):** existing role-gated journeys still pass under in-app
enforcement; one new smoke test for the Access-management screen (create group → assign
user → verify access), using `navigateToApp()` + fixtures.

---

## 9. Out of scope / deferred (YAGNI)

- Per-object / relationship-based authorization (OpenFGA/Permify/Zanzibar).
- Active cache invalidation / instant kill-switch (revisit if needed).
- Multi-instance distributed cache (single instance today).
- Removing per-feature Entra app roles from the manifest (optional cleanup, post-cutover).
- Auto-assigning users from Entra groups on first login (rejected; contradicts decision #4).

---

## 10. Impact on existing ADRs

- **ADR-001** (single DbContext): new tables added to `ApplicationDbContext`.
- **ADR-003** (MediatR + controllers): admin use-cases follow it; thin controllers.
- **ADR-004** (repo DI in feature module): `AuthorizationModule.cs` owns its binding.
- **ADR-005** (identity resolution): unchanged; `ICurrentUserService` still resolves the
  current user inside handlers; permission checks remain claim-based via `IsInRole`.

---

## 11. Open questions for spec review

1. **System-group lock level:** are `IsSystem` groups fully read-only, or
   editable-but-not-deletable? (Spec currently assumes the latter.)
2. **`/api/auth/me` shape:** is `isSuperUser` + flat `permissions[]` enough, or do you also
   want the user's assigned group names for display?
3. **Cutover window:** comfortable with the brief "no access until assigned" gap, or do you
   want the rejected Entra-group auto-map fallback reconsidered?
</content>
