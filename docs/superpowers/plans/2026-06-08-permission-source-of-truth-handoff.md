# Permission Source of Truth — Handoff Document

**Branch:** `feature/fix-last-login-display`
**Plan:** `docs/superpowers/plans/2026-06-08-permission-source-of-truth.md`
**Spec:** `docs/superpowers/specs/2026-06-08-permission-source-of-truth-design.md`

---

## Progress at context cutoff

### ✅ Completed (Tasks 1–11)

| Task | Commit | Description |
|------|--------|-------------|
| 1 | `fc8967d5f` | `Feature` enum (30 values, module-prefixed) |
| 2 | `48b9d878b` | `FeatureDefinition` + `FeaturePermission` records |
| 3 | `d7e48e0e5` | `MenuPath` record |
| 4 | `eb2f2649d` | `GateOnAttribute` with TDD |
| 5 | `b0a7d58af` | `PermissionString` helper (14 tests green) |
| 6 | `5a636e623` | `AccessMatrix` rebuilt around `Feature` enum + `MenuPaths` |
| 7 | `8ac84ffef` | Generator emits `AccessRoles.generated.cs` + new TS shape; `AuthorizationConstants.cs` deleted |
| 8 | `1c111014f` | Build target passes C# output path |
| 9 | `7a3ab5490` | 4 files migrated from `AuthorizationConstants.Roles.*` → `AccessRoles.*` |
| 10 | `8bd8fcb84` + `43a1824f6` | 43 controllers: `[GateOn]` added, old constant names renamed; 2 fixes (PermissionCatalogue handler, duplicate GateOn) |
| 11 | `2ceb9da1f` | `EveryAuthorizeRole_MatchesGateOn` test — **passes** |

### 🔲 Remaining (Tasks 12–23)

#### Phase 5 — More validation tests (in `GateConsistencyTests.cs` + `AccessMatrixTests.cs`)

**Task 12 — `EveryGatedEndpoint_HasGateOn`** (append to `GateConsistencyTests.cs`)
```csharp
[Fact]
public void EveryGatedEndpoint_HasGateOn()
{
    var problems = new List<string>();
    foreach (var ctl in AllControllers())
    {
        var classGate = ctl.GetCustomAttribute<GateOnAttribute>();
        var classAuth = ctl.GetCustomAttributes<AuthorizeAttribute>().Any();
        if (classAuth && classGate is null)
            problems.Add($"{ctl.Name}: class has [Authorize] but no [GateOn]");

        foreach (var method in ctl.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var hasAuth = method.GetCustomAttributes<AuthorizeAttribute>().Any();
            var allowAnon = method.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            if (!hasAuth || allowAnon) continue;
            var methodGate = method.GetCustomAttribute<GateOnAttribute>();
            if (methodGate is null && classGate is null)
                problems.Add($"{ctl.Name}.{method.Name}: [Authorize] without [GateOn] (class or method)");
        }
    }
    problems.Should().BeEmpty();
}
```
Run: `dotnet test --filter EveryGatedEndpoint_HasGateOn`. Expected: `Passed: 1`.
Commit: `git commit -m "test(authz): assert every gated endpoint declares [GateOn]"`

**Task 13 — `EveryMenuPath_PermissionsResolveToKnownRoles`** (append to `AccessMatrixTests.cs` in the existing class)
```csharp
[Fact]
public void EveryMenuPath_PermissionsResolveToKnownRoles()
{
    var defs = AccessMatrix.Features.ToDictionary(f => f.Key);
    var problems = new List<string>();

    foreach (var menu in AccessMatrix.MenuPaths)
    foreach (var req in menu.Requires)
    {
        if (!defs.TryGetValue(req.Feature, out var def))
        {
            problems.Add($"MenuPath '{menu.Key}' references unknown feature {req.Feature}");
            continue;
        }
        var ok = req.Level switch
        {
            AccessLevel.Read => true,
            AccessLevel.Write => def.HasWrite,
            AccessLevel.Admin => def.HasAdmin,
            _ => false,
        };
        if (!ok)
            problems.Add($"MenuPath '{menu.Key}' requires {req.Feature}.{req.Level} but feature does not support that level");
    }
    problems.Should().BeEmpty();
}
```
Run: `dotnet test --filter EveryMenuPath_PermissionsResolveToKnownRoles`. Expected: `Passed: 1`.
Commit: `git commit -m "test(authz): assert menu path requirements are valid features+levels"`

**Task 14 — `EveryMenuPath_FeatureHasController`** (append to `GateConsistencyTests.cs`)
```csharp
[Fact]
public void EveryMenuPath_FeatureHasController()
{
    var featuresWithControllers = AllControllers()
        .SelectMany(c => new[] { c.GetCustomAttribute<GateOnAttribute>() }
            .Concat(c.GetMethods().Select(m => m.GetCustomAttribute<GateOnAttribute>())))
        .Where(g => g is not null)
        .Select(g => g!.Feature)
        .ToHashSet();

    var problems = new List<string>();
    foreach (var menu in AccessMatrix.MenuPaths)
    {
        if (menu.Key.StartsWith("#")) continue; // virtual external item, no controller
        foreach (var req in menu.Requires)
            if (!featuresWithControllers.Contains(req.Feature))
                problems.Add($"MenuPath '{menu.Key}' requires {req.Feature} but no controller is gated on it");
    }
    problems.Should().BeEmpty();
}
```
Run: `dotnet test --filter EveryMenuPath_FeatureHasController`. Expected: `Passed: 1`.
Commit: `git commit -m "test(authz): assert every menu feature is backed by a controller"`

**Task 15 — Run full backend test suite**
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Authorization" 2>&1 | tail -5
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -10
```
Fix any failures from role constant changes in existing tests. All must pass.

---

#### Phase 6 — Frontend swap

**Files to understand before starting:**
- `frontend/src/components/Layout/Sidebar.tsx` — currently uses `ACCESS_ROUTES: Record<string, string>` (old shape, single role string). Must migrate to `{ permissions: string[] }` shape.
- `frontend/src/components/auth/RequireAccess.tsx` — single-role gate, will be deleted.
- `frontend/src/App.tsx` — has `<RequireAccess requiredRole={...}>` wrapping routes.
- `frontend/src/auth/accessMatrix.generated.ts` — already has new `MenuRequirement` shape (generated in Task 7).

**Task 16 — Update Sidebar** (`frontend/src/components/Layout/Sidebar.tsx`)

Replace the `canSeeRoute` helper with:
```ts
const canSeeKey = (key: string): boolean => {
  const req = ACCESS_ROUTES[key];
  if (!req) return false;
  return req.permissions.every(p => hasPermission(p));
};
```

In `allSections`, each sub-item needs a `key` field. Internal items: `key === href`. External onClick items: `key` is the virtual key from the matrix (e.g. `#hangfire`, `#org-chart`, `#terminal`, `#baleni-external`). Remove all `requiredRole` fields.

Update `canSeeItem`:
```ts
const canSeeItem = (item: { key: string }): boolean => canSeeKey(item.key);
```

Build: `cd frontend && npm run build`. Commit: `git commit -m "feat(authz): Sidebar uses MenuRequirement[] for AND-of-permissions gating"`

**Task 17 — Create `RequireMenuPath`** (TDD)

Test file: `frontend/src/components/auth/__tests__/RequireMenuPath.test.tsx` (see plan lines 1479–1550)
Component: `frontend/src/components/auth/RequireMenuPath.tsx`

```tsx
import React from 'react';
import { Navigate } from 'react-router-dom';
import { usePermissionsContext } from '../../auth/PermissionsContext';
import { ACCESS_ROUTES } from '../../auth/accessMatrix.generated';

interface Props {
  path: string;
  redirectTo?: string;
  children: React.ReactNode;
}

const RequireMenuPath: React.FC<Props> = ({ path, redirectTo = '/', children }) => {
  const { hasPermission, isLoading } = usePermissionsContext();
  if (isLoading) return null;
  const req = ACCESS_ROUTES[path];
  if (!req) return <Navigate to={redirectTo} replace />;
  if (!req.permissions.every(p => hasPermission(p)))
    return <Navigate to={redirectTo} replace />;
  return <>{children}</>;
};

export default RequireMenuPath;
```

Run: `npm test -- --testPathPattern="RequireMenuPath" --watchAll=false`. Expected: 4 passed.
Commit: `git commit -m "feat(authz): add RequireMenuPath route guard"`

**Task 18 — Swap `RequireAccess` → `RequireMenuPath` in App.tsx**

For each `<RequireAccess requiredRole={AccessRoles.X}>` in App.tsx, replace with `<RequireMenuPath path="<route-path>">`.
Delete `frontend/src/components/auth/RequireAccess.tsx` and its test if present.
Build + all frontend tests must pass.
Commit: `git commit -m "refactor(authz): replace RequireAccess with RequireMenuPath everywhere"`

**Task 19 — `EveryMenuItem_RegisteredInAccessRoutes` Sidebar test**

Add `data-menu-key={item.key}` to external `<button>` items in Sidebar.tsx.
Add to `Sidebar.test.tsx`:
```tsx
it('every rendered menu item key exists in ACCESS_ROUTES', () => {
  // render with isSuperUser=true, collect link hrefs + button data-menu-key,
  // assert each is in Object.keys(ACCESS_ROUTES)
});
```
See plan lines 1675–1728 for full test code.
Commit: `git commit -m "test(authz): assert every menu item key is registered in ACCESS_ROUTES"`

**Task 20 — Deferred TODO** (optional/tiny)

Add comment to bottom of `Sidebar.test.tsx`:
```ts
// TODO(authz): consider scraping App.tsx for <Route path="..."> to validate
// every non-virtual MenuPath.Key resolves to a real React route.
```

---

#### Phase 7 — Persistence

**Task 21 — Update AuthorizationSeeder** (`backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationSeeder.cs`)

Replace the seeder body with reconcile logic (see plan lines 1766–1809). Key points:
- Adds missing permissions to existing groups
- Removes stale permissions from existing groups
- Deletes any orphan `GroupPermissions` rows not in the matrix
- Upserts groups that don't exist yet

Build: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`. 0 errors.
Commit: `git commit -m "feat(authz): seeder reconciles existing group permissions to matrix"`

**Task 22 — EF migration** (`RenamePermissionStrings`)

```bash
cd backend/src/Anela.Heblo.Persistence && \
dotnet ef migrations add RenamePermissionStrings \
  --startup-project ../Anela.Heblo.API \
  --context ApplicationDbContext
```

Replace the generated `Up` body with SQL UPDATE statements mapping old `feature.level` → new `module.feature.level` format. See plan lines 1848–1903 for the full mapping (29 features × read/write/admin levels). `Down` is the mirror.

Build persistence: `dotnet build backend/src/Anela.Heblo.Persistence/... 2>&1 | tail -3`. 0 errors.
Commit: `git commit -m "feat(authz): migration to rename permission strings to module.feature.level"`

---

#### Phase 8 — Final verification

**Task 23**
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -10
cd frontend && npm run build 2>&1 | tail -5
cd frontend && npm test -- --watchAll=false 2>&1 | tail -10
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```
Commit any format changes: `git commit -m "style(authz): dotnet format"`

---

## Important notes for the next context

- **Branch:** `feature/fix-last-login-display` (do not create a new branch)
- **Worktree:** Already in an isolated worktree at `/Users/pajgrtondrej/orca/workspaces/Anela.Heblo/cuttlefish`
- **Build gate:** Always use `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` (the build target regenerates `AccessRoles.generated.cs` and the TS file automatically)
- **Test gate:** The GateConsistencyTests (Tasks 11–14) will catch any future [GateOn]/[Authorize] drift
- **Frontend:** `accessMatrix.generated.ts` already has the new `MenuRequirement` shape — Tasks 16–19 wire the frontend to use it
- **Phase 9 (Deploy):** Manual — do not implement; just ensure the branch is push-ready after Task 23

## Continuing with subagent-driven development

Run: `/superpowers:subagent-driven-development docs/superpowers/plans/2026-06-08-permission-source-of-truth.md`

The skill will re-read the plan. Tasks 1–11 are done (committed). Start from Task 12.
