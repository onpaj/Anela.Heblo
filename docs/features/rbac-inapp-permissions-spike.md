# Spike: In-app Permission System (replacing EntraID-driven RBAC)

**Status:** Spike / investigation — no implementation, decision pending
**Date:** 2026-06-05
**Branch:** `claude/rbac-inapp-permissions-spike-wHmHw`
**Author:** AI-assisted spike

---

## 1. Goal

Investigate moving the authorization model from **EntraID app-role driven RBAC** to an
**in-app permission system** that the app itself owns:

- **Permissions** — fine-grained, code-defined capability flags.
- **Groups (roles)** — named bundles of permissions, editable by an admin at runtime.
- **User → group** assignment, managed in-app.
- **Group → group** nesting (a group inherits another group's permissions).

Authentication stays on Entra ID (Microsoft Identity Web). Only the **authorization
decision and its data** move into the application database.

This document captures: the current state, the gap, candidate libraries (NuGet / OSS),
a proposed design, a phased migration plan, and a recommendation.

---

## 2. Current state (as-is)

The current model is **100% claim-based with zero database persistence of users, roles,
or permissions**. Everything lives in Entra ID + code constants.

### 2.1 Authentication
- `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs`
  - Real auth: `AddMicrosoftIdentityWebAppAuthentication` + `AddMicrosoftIdentityWebApiAuthentication` (dual web-app + web-API flow), `AddInMemoryTokenCaches`, OBO to Graph.
  - Mock auth: `MockAuthenticationHandler` when `UseMockAuth=true` (grants all roles).
  - E2E auth: cookie scheme `E2ETestCookies` in staging/dev.
- Config keys: `AzureAd:*` (TenantId, ClientId, ClientSecret, Scopes), `DownstreamApi:*` (Graph).

### 2.2 Authorization (roles)
- Roles are **Entra ID app roles**, delivered in the JWT `roles` claim, validated via
  `[Authorize(Roles = AccessRoles.X)]` on controller actions.
- Default policy (`ConfigureAuthorizationPolicies`): `RequireAuthenticatedUser()` +
  `RequireRole("heblo_user")`. **No named policies** — per-feature gating is purely
  `[Authorize(Roles = …)]`.
- The role catalogue is **code-first** and lives in the Domain layer:
  - `Anela.Heblo.Domain/Features/Authorization/AccessMatrix.cs` — 30 features → ~52 roles
    via the `{feature}.{read|write|admin}` convention; plus **10 predefined groups**
    (`Spravce`, `Vedeni`, `Ucetni`, `Marketer`, `Nakupci`, `Vedouci_vyroby`,
    `Pracovnik_vyroby`, `Vedouci_skladu`, `Skladnik`, `Poradenstvi`).
  - `AccessModels.cs` — `AccessFeature`, `AccessRoleDefinition`, `AccessGroup` (records).
  - `AuthorizationConstants.cs` — `AccessRoles` compile-time string constants used in
    `[Authorize]` (+ a backward-compat `AuthorizationConstants.Roles` alias block).
- The matrix is exported (`access-matrix.generated.json` via the `AccessMatrixGen` tool)
  and used to populate the **Entra ID app manifest** and the frontend
  `accessMatrix.generated.ts`.

> **Key observation:** the codebase already models *features → levels → groups* cleanly.
> The "group" concept (`AccessGroup`) exists today but is **static, code-defined, and
> realised as Entra ID security groups**, not editable at runtime. The spike is largely
> about making that existing matrix **dynamic and DB-backed** while keeping the same
> permission vocabulary.

### 2.3 Current user identity
- `Domain/Features/Users/ICurrentUserService` → `GetCurrentUser()`, `IsInRole(string)`.
- `API/Features/Users/CurrentUserService` reads from `ClaimsPrincipal` (no DB). ADR-005
  mandates identity is resolved **inside MediatR handlers** via `ICurrentUserService`.
- `CurrentUser(Id, Name, Email, IsAuthenticated)` — `Id` = Entra object id (NameIdentifier).

### 2.4 Frontend
- MSAL (`@azure/msal-browser`, `@azure/msal-react`), `frontend/src/auth/`.
- Roles read from `idTokenClaims.roles` (`useAuth.ts`); `RequireAccess.tsx` gates routes;
  `AuthGuard.tsx` enforces login; `accessMatrix.generated.ts` maps route → required role.

### 2.5 What that means for a migration
- **No DB schema** for users/roles/permissions exists → greenfield on the data side.
- The **enforcement surface** is large but uniform: ~50+ `[Authorize(Roles = …)]`
  attributes + 1 default policy + the frontend route map. A migration that keeps the
  **same permission strings** can leave most attributes untouched.

---

## 3. The gap (why move at all)

| Capability | Entra-role today | In-app permissions (target) |
|---|---|---|
| Add/rename a group, change which permissions it grants | Edit Entra groups + app manifest, redeploy/reassign | Admin UI, instant, no redeploy |
| Group-to-group nesting / inheritance | Entra nested groups (token bloat, opaque) | First-class, in DB |
| Assign user to group | Entra admin portal (separate system) | In-app admin screen |
| Audit "who can do what / who changed access" | Scattered across Entra logs | App DB + app audit log |
| Per-tenant / per-resource scoping (future) | Hard | Natural to model |
| Onboarding a new employee | Entra group assignment by IT | Self-service in-app |
| Token size | Grows with every role (Entra caps `roles`/`groups` in tokens) | Token stays small (just identity) |

The Entra "groups in token" path also has hard limits (group overage claim when a user is
in too many groups), and app-role assignment requires Entra admin rights — friction for a
solo-operator business app.

---

## 4. Design options for the enforcement layer

Regardless of which storage library we pick, ASP.NET Core needs the permission decision to
flow through its authorization pipeline. Two viable hook points:

### Option A — Claims transformation (recommended, lowest blast radius)
Keep authentication on Entra. After token validation, an `IClaimsTransformation` (or a
custom step) looks up the user (by Entra object id) in the app DB, expands their
groups → permissions (resolving group→group nesting), and **adds the resulting permission
strings as `Role` claims** on the `ClaimsPrincipal`.

- **All existing `[Authorize(Roles = "catalog.read")]` keep working unchanged** — they just
  read claims that now come from the DB instead of the token.
- Cache the expansion per user (memory cache keyed by object id, short TTL + invalidate on
  admin change) to avoid a DB hit per request.
- `CurrentUserService.IsInRole` continues to work (still reads claims).

This is the smallest, safest change and preserves ADR-005 and the whole `[Authorize]` surface.

### Option B — Permission-based policy provider
Switch from "roles" to true "permissions" using a dynamic `IAuthorizationPolicyProvider` +
`AuthorizationHandler` (the classic "permission as policy" pattern, and what
`AuthPermissions.AspNetCore` does internally). Attributes become
`[HasPermission("catalog.read")]`.

- Cleaner semantics, but requires touching every `[Authorize(Roles=…)]` site.
- Better if we want permission **claims packed as a bitset/string** rather than many role
  claims (avoids any claim-count concerns entirely).

> **Recommendation:** start with **Option A** (claims transformation) so the migration is
> data-only and reversible; optionally evolve to Option B later if the role-claim list
> becomes unwieldy.

---

## 5. Library / OSS evaluation

### Summary table

| Option | Model | Group→group nesting | Runtime-editable | EF Core / Postgres | Deployment | License | Maturity | Fit |
|---|---|---|---|---|---|---|---|---|
| **AuthPermissions.AspNetCore** (JonPSmith) | Roles→permissions (enum flags), optional multi-tenant | Via roles + hierarchical tenants | ✅ admin services built-in | ✅ EF Core | In-proc library | MIT | v10, mature, well-documented | **High** |
| **Casbin.NET** (Apache) | ACL/RBAC/ABAC, policy + model files | ✅ role-role mappings | ✅ runtime mgmt API | ✅ EF Core adapter | In-proc library | Apache-2.0 | 1.3k★, v2.20 (2026) | **Medium-High** |
| **Roll-your-own** (DB tables + claims transformation) | Whatever we define | ✅ we model it | ✅ our admin UI | ✅ native ApplicationDbContext | In-proc | n/a | n/a | **High** (full control) |
| **OpenFGA** (CNCF) | ReBAC / Zanzibar (relationship tuples) | ✅ very strong | ✅ | external store | **Separate service** (Docker) | Apache-2.0 | Mature, CNCF | Overkill |
| **Permify** | ReBAC / Zanzibar | ✅ very strong | ✅ | external store | **Separate service** | Apache-2.0 | Mature | Overkill |
| **Gatekeeper** (jchristn) | RBAC users/roles/resources | partial | ✅ | own store | In-proc library | MIT | Small/early | Low |
| **OpenIddict / IdentityServer** | OAuth/OIDC token issuance | n/a | n/a | ✅ | In-proc | varies | Mature | **Wrong tool** (auth, not authz) |

### Notes on the front-runners

**AuthPermissions.AspNetCore (AuthP)** — purpose-built for exactly this scenario:
*"an improved Role authorization system where the features a Role can access can be changed
by an admin user (no redeploy)."* Permissions are a C# enum (flags), Roles bundle
permissions, users get roles, all stored via EF Core with ready-made **admin services**
(roles admin, user admin). It also supports JWT refresh and optional multi-tenant
(hierarchical tenants = a form of group nesting). Permissions are packed into a **single
claim** (no claim-count problem). MIT, actively maintained (v10).
- **Pros:** least code to a working admin-editable system; battle-tested; matches our
  feature/level vocabulary.
- **Cons:** opinionated (its own DbContext/entities, its `[HasPermission]` attribute and
  enum-based permissions) — means re-expressing our 52 string permissions as an enum and
  adopting its policy provider (Option B). Some coupling to its conventions.

**Casbin.NET** — flexible PERM-model engine; RBAC with role hierarchy (group→group) and a
runtime management API; EF Core adapter for policy storage. Great if we want a pure policy
engine and to keep our own user/group admin UI.
- **Pros:** very flexible, supports ABAC later, mature.
- **Cons:** model/policy CONF files are a new concept for the team; we'd still build the
  admin UI and the user/group tables ourselves; the engine is more than we need for plain
  RBAC.

**Roll-your-own** — given the codebase already has a clean `AccessMatrix` (features →
permissions → groups) and a Vertical-Slice + EF Core + MediatR setup, a small bespoke
`Authorization` module is very achievable and keeps full architectural control (no foreign
DbContext, no foreign attribute model, fits ADR rules). The "hard" parts (group→group
expansion, caching, admin UI) are modest.
- **Pros:** zero new coupling; reuses the existing permission vocabulary verbatim; works
  with Option A so **no `[Authorize]` changes**; aligns with module-boundary ADRs.
- **Cons:** we own it (testing, edge cases, admin UI from scratch).

**OpenFGA / Permify (Zanzibar)** — extremely powerful relationship-based authorization,
but they run as a **separate service** with their own datastore. That contradicts the
project's single-Docker-image, single-Postgres, solo-dev simplicity. Only worth it if we
foresee complex per-object sharing (e.g. "user X can edit *this* specific order"). Not the
case today. **Park for the future.**

---

## 6. Proposed data model (roll-your-own / AuthP-agnostic)

A new `Authorization` vertical slice in `Persistence` + `Application`. Permissions remain
**code-defined** (the source of truth stays `AccessMatrix` — they are capabilities, not
data); only **groups, nesting, and assignments** are data.

```
Permission            (code-defined, NOT a table — seeded/validated from AccessMatrix)
  value: "catalog.read"

PermissionGroup        (table)  -- "group" / "role" bundle
  Id, Name, Description, IsSystem (seed-locked), CreatedAt/By

GroupPermission        (table)  -- group → permission (many-to-many to code constants)
  GroupId, PermissionValue

GroupParent            (table)  -- group → group nesting (DAG; guard against cycles)
  GroupId, ParentGroupId

AppUser                (table)  -- materialised on first login from Entra claims
  Id, EntraObjectId (unique), Email, DisplayName, IsActive, LastLoginAt

UserGroup              (table)  -- user → group
  UserId, GroupId
```

**Effective permissions(user)** = union of permissions of all groups reachable from the
user's directly-assigned groups (transitive closure over `GroupParent`, cycle-guarded).

The 10 existing `AccessMatrix.Groups` seed the `PermissionGroup` table as `IsSystem` rows
on first migration, so day-one behaviour matches today exactly.

### Enforcement wiring (Option A)
- `PermissionClaimsTransformation : IClaimsTransformation`
  - On each request (cached): resolve `AppUser` by Entra object id → compute effective
    permissions → add each as a `Role` claim.
  - Cache in `IMemoryCache` keyed by object id, TTL ~5 min, **invalidated** when an admin
    edits any group/assignment (bump a version token).
- Default policy unchanged (`heblo_user` becomes a seeded permission everyone gets, or a
  separate "is active user" check).
- `[Authorize(Roles = "catalog.read")]` everywhere → **no change**.

### New admin surface (MediatR slices)
- `Groups`: list/create/update/delete, set permissions, set parents.
- `Users`: list, view effective permissions, assign/unassign groups.
- Frontend: an Administration screen (the `administration` feature already exists in the
  matrix) replacing reliance on the Entra portal.

---

## 7. Migration plan (phased, reversible)

**Phase 0 — Spike (this doc).** Decide library vs roll-your-own and Option A vs B.

**Phase 1 — Schema + seed (shadow mode).**
- Add the `Authorization` slice, EF migration, seed system groups from `AccessMatrix`.
- Materialise `AppUser` on login. **No enforcement change yet** — Entra roles still drive
  `[Authorize]`. Validate that computed effective permissions == today's Entra roles for
  real users.

**Phase 2 — Switch enforcement to claims transformation (Option A).**
- Enable `PermissionClaimsTransformation`. Behind a **feature flag**
  (`IFeatureFlagChecker`) so we can fall back to token roles instantly.
- Run both in parallel briefly; log diffs.

**Phase 3 — Admin UI.** Ship the Groups/Users management screens; train the operator;
stop maintaining Entra app-role assignments.

**Phase 4 — Decommission Entra app roles** (optional). Remove app roles from the manifest;
Entra keeps doing **authentication only**. Keep `accessMatrix.generated.ts` for the
frontend route map (now driven by the in-app permission list returned from an API).

**Rollback:** at any phase, the feature flag reverts to token-role enforcement; the DB
tables are additive and harmless if unused.

---

## 8. Impact on existing architecture / ADRs

- **ADR-005 (identity resolution):** unaffected — `ICurrentUserService` still resolves the
  current user; we extend it (or add `IPermissionService`) for permission checks inside
  handlers. Keep controllers free of identity logic.
- **Module boundaries:** Authorization is a cross-cutting concern; the interface
  (`IPermissionService`) belongs in `Domain/Features/Authorization`, the EF implementation
  in `Persistence`, DI binding in an `AuthorizationModule.cs` (per ADR-004, repo bindings
  live in the feature module, not `PersistenceModule`).
- **Persistence:** single `ApplicationDbContext` (ADR-001) — add the new DbSets there.
- **Frontend:** `RequireAccess` / `accessMatrix.generated.ts` stay; only the **source** of
  the user's permission list changes from `idTokenClaims.roles` to a backend
  `GET /api/auth/me` (or similar) returning effective permissions.
- **Feature flags:** gate the enforcement switch (`docs/development/feature-flags.md`).

---

## 9. Risks & open questions

1. **Per-request DB lookup cost** — mitigated by caching effective permissions per user;
   need a clean cache-invalidation signal on admin edits.
2. **Group→group cycles** — must validate the DAG on every parent edit (reject cycles).
3. **First-login provisioning** — how does a brand-new Entra user get an initial group?
   Options: default "no access" group, or auto-assign by Entra group/department claim
   during Phase 1 shadow mapping.
4. **Bootstrapping the first admin** — seed one super-admin (by email) so the admin UI is
   reachable before any data exists.
5. **Mock & E2E auth** — `MockAuthenticationHandler` and `E2ETestCookies` must seed/grant
   permissions the same way (today mock grants all roles — keep that for tests).
6. **AuthP vs roll-your-own** — AuthP is faster to a working product but introduces its own
   conventions (enum permissions, its DbContext, `[HasPermission]` = Option B). Roll-your-own
   reuses our exact vocabulary with Option A and zero `[Authorize]` churn but we own the code.
7. **Is full ReBAC (per-object) ever needed?** If yes within ~12 months, evaluate OpenFGA
   now rather than building twice. Current features are role/feature-level only → not needed.

---

## 10. Recommendation

1. **Keep Entra ID for authentication.** Move **authorization data** in-app.
2. **Enforce via claims transformation (Option A)** so the existing ~50 `[Authorize(Roles=…)]`
   attributes and `CurrentUserService` keep working untouched.
3. **Prefer a small roll-your-own `Authorization` slice** over a framework, because the
   codebase already owns a clean permission matrix and the architectural conventions (VSA,
   MediatR, single DbContext, module DI) make a bespoke module low-risk and zero-coupling.
   - **Fallback / accelerator:** if time-to-market dominates, adopt
     **AuthPermissions.AspNetCore** (MIT, purpose-built, admin services included) and accept
     Option B (`[HasPermission]`) + its conventions.
4. **Defer OpenFGA/Permify** unless per-object (ReBAC) sharing becomes a real requirement.
5. Ship behind a **feature flag** in shadow mode first (Phase 1–2) to verify parity before
   cutting over, with instant rollback to token roles.

A follow-up implementation plan should pick: **(roll-your-own + Option A)** *or*
**(AuthP + Option B)**, then proceed with Phase 1.

---

## Sources

- [Casbin.NET (apache/casbin-Casbin.NET)](https://github.com/casbin/Casbin.NET)
- [AuthPermissions.AspNetCore (JonPSmith)](https://github.com/JonPSmith/AuthPermissions.AspNetCore) · [Permissions wiki](https://github.com/JonPSmith/AuthPermissions.AspNetCore/wiki/Permissions-explained) · [Roles admin wiki](https://github.com/JonPSmith/AuthPermissions.AspNetCore/wiki/Roles-admin-service) · [NuGet](https://www.nuget.org/packages/AuthPermissions.AspNetCore)
- [Permify — open-source authorization libraries](https://permify.co/post/open-source-authorization-libraries/) · [RBAC tools 2025](https://permify.co/post/rbac-tools/)
- [WorkOS — best RBAC open-source solutions](https://workos.com/blog/rbac-open-source)
- [Gatekeeper (jchristn)](https://github.com/jchristn/Gatekeeper)
- [Microsoft Learn — implement RBAC for apps](https://learn.microsoft.com/en-us/entra/identity-platform/howto-implement-rbac-for-apps)
- [Milan Jovanović — RBAC in ASP.NET Core](https://www.milanjovanovic.tech/blog/building-secure-apis-with-role-based-access-control-in-aspnetcore)
</content>
</invoke>
