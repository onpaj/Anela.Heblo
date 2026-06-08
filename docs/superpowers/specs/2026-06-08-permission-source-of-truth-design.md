# Single Source of Truth for Permissions

**Status:** Design approved 2026-06-08
**Owner:** ondra@anela.cz
**Context:** RBAC drift between menu visibility, frontend route guards, and backend controller authorization

## Problem

Today, three places declare authorization independently:

1. **`AccessMatrix.cs`** — features with a primary frontend `Path` (+ optional `AdditionalPaths`, `AdditionalWritePaths`). Generates `ACCESS_ROUTES` for the frontend menu and the Entra app-roles manifest.
2. **Controllers** — each method/class carries `[Authorize(Roles = AccessRoles.X)]` referencing a hand-written `AuthorizationConstants.cs`.
3. **Sidebar.tsx + `RequireAccess`** — read `ACCESS_ROUTES`.

There is no enforced link between (2) and (1)/(3). Examples of resulting drift:

- `/marketing/feedback` declared in the matrix as `article.read`, but the underlying `GET /api/articles/feedback/list` is `[Authorize(Roles = AccessRoles.ArticleWrite)]`. Users with `article.read` see the menu and get 403s.
- `pracovnik_vyroby` sees "Inventura materiálu" but gets 403 because the page calls endpoints outside `material_inventory` (e.g. catalog or manufacture) that the role bundle doesn't cover. The single-permission gate on the matrix entry can't express this.

A "new menu item without matrix entry" is also undetected today — no test scans `Sidebar.tsx` against `ACCESS_ROUTES`.

## Goal

One declarative source — a single C# class — defining: features, menu paths (with the permissions they require, possibly multiple), and group bundles. Controllers self-declare their feature with a typed attribute. Tests assert no drift between the matrix, controllers, and the rendered Sidebar.

## Non-goals

- Changing the resolver / DB-backed RBAC mechanism. `PermissionClaimsTransformation` and the `/me` endpoint stay as they are.
- Replacing `heblo_user` / `super_user`. These remain the only true Entra app **roles**. Everything `.read`/`.write` is a *permission*, resolved server-side.
- Per-endpoint enumeration in the matrix. Backend endpoints declare their feature via `[GateOn]`; the matrix lists frontend menu paths only.

## Design

### Model

`backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.cs` is rebuilt around four types:

```csharp
public enum AccessLevel { Read, Write, Admin }

public enum Feature
{
    Finance_FinancialOverview, Finance_MarginAnalysis,
    Products_ProductMargins, Products_Catalog, Products_Journal,
    Customer_BankStatements, Customer_KnowledgeBase, Customer_Smartsupp,
    Purchase_PurchaseOrders, Purchase_PurchaseStock,
    Manufacture_ManufactureOrders, Manufacture_BatchPlanning,
    Manufacture_ManufactureOutput, Manufacture_ManufactureStock,
    Manufacture_MaterialInventory, Manufacture_ProductInventory,
    Manufacture_MaterialContainers,
    Warehouse_Logistics, Warehouse_Expedition, Warehouse_Packaging, Warehouse_StockUp,
    Marketing_Article, Marketing_Leaflet, Marketing_Photobank, Marketing_MarketingCalendar,
    Anela_Meetings, Anela_OrgChart,
    Admin_DataQuality, Admin_Administration, Admin_FeatureFlags,
}

public sealed record FeatureDefinition(
    Feature Key,
    string Label,
    bool HasWrite = false,
    bool HasAdmin = false);

public sealed record FeaturePermission(Feature Feature, AccessLevel Level);

public sealed record MenuPath(
    string Key,                                  // FE route ("/foo") or virtual ("#bar")
    IReadOnlyList<FeaturePermission> Requires);  // AND across entries
```

Permission strings on the wire use the convention `{module}.{feature}.{level}`:

- `purchase.purchase_orders.read` / `.write`
- `manufacture.batch_planning.write`
- `marketing.article.write`
- `admin.administration.read`

The `module` segment derives from the enum prefix before `_`.

### Registry

```csharp
public static class AccessMatrix
{
    public const string BaseRole = "heblo_user";

    public static readonly IReadOnlyList<FeatureDefinition> Features = new[]
    {
        new FeatureDefinition(Feature.Finance_FinancialOverview, "Finanční přehled"),
        new FeatureDefinition(Feature.Products_Catalog, "Katalog", HasWrite: true),
        new FeatureDefinition(Feature.Manufacture_MaterialInventory, "Inventura materiálu", HasWrite: true),
        new FeatureDefinition(Feature.Marketing_Article, "Články", HasWrite: true),
        new FeatureDefinition(Feature.Admin_Administration, "Administrace", HasWrite: true),
        // … one entry per Feature value
    };

    public static readonly IReadOnlyList<MenuPath> MenuPaths = new[]
    {
        new MenuPath("/finance/overview",
            Require((Feature.Finance_FinancialOverview, AccessLevel.Read))),

        new MenuPath("/manufacturing/inventory",
            Require((Feature.Manufacture_MaterialInventory, AccessLevel.Read),
                    (Feature.Products_Catalog, AccessLevel.Read))),                  // multi-permission

        new MenuPath("/marketing/feedback",
            Require((Feature.Marketing_Article, AccessLevel.Write))),                // write-gated

        new MenuPath("#hangfire",
            Require((Feature.Admin_Administration, AccessLevel.Read))),
        new MenuPath("#org-chart",
            Require((Feature.Anela_OrgChart, AccessLevel.Read))),
        new MenuPath("#terminal",
            Require((Feature.Warehouse_Logistics, AccessLevel.Read))),
        new MenuPath("#baleni-external",
            Require((Feature.Warehouse_Packaging, AccessLevel.Read))),
        // … one entry per menu item (internal or external)
    };

    public static readonly IReadOnlyList<AccessGroup> Groups = /* unchanged in shape */;

    private static IReadOnlyList<FeaturePermission> Require(
        params (Feature feature, AccessLevel level)[] reqs)
        => reqs.Select(r => new FeaturePermission(r.feature, r.level)).ToArray();
}
```

Every menu item appears in `MenuPaths` exactly once. Internal items use their URL; external onClick items use a `#<id>` virtual key. A `MenuPath` lists one or more required permissions (AND semantics).

### `[GateOn]` attribute

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class GateOnAttribute : Attribute
{
    public Feature Feature { get; }
    public GateOnAttribute(Feature feature) => Feature = feature;
}
```

Every controller class gets `[GateOn(Feature.X)]`. Methods may override (e.g. `ArticlesController.BackfillRequestedBy` is class-gated by `Marketing_Article` but the method itself carries `[GateOn(Feature.Admin_Administration)]` because it requires admin-write).

The existing `[Authorize(Roles = AccessRoles.X)]` stays. `AccessRoles` constants are **generated** so they can't drift from the enum.

### Generated artifacts

`backend/tools/Anela.Heblo.AccessMatrixGen` emits three files from the matrix:

1. **`backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs`** — one constant per `(Feature, Level)` permitted by the feature's `HasWrite`/`HasAdmin` flags:
   ```csharp
   public static class AccessRoles
   {
       public const string Base = "heblo_user";
       public const string SuperUser = "super_user";
       public const string PurchasePurchaseOrdersRead  = "purchase.purchase_orders.read";
       public const string PurchasePurchaseOrdersWrite = "purchase.purchase_orders.write";
       public const string ManufactureBatchPlanningRead  = "manufacture.batch_planning.read";
       public const string ManufactureBatchPlanningWrite = "manufacture.batch_planning.write";
       // …
   }
   ```
2. **`frontend/src/auth/accessMatrix.generated.ts`** — menu path → required permissions:
   ```ts
   export interface MenuRequirement { permissions: string[]; }
   export const ACCESS_ROUTES: Record<string, MenuRequirement> = {
     "/finance/overview":        { permissions: ["finance.financial_overview.read"] },
     "/manufacturing/inventory": { permissions: ["manufacture.material_inventory.read",
                                                 "products.catalog.read"] },
     "/marketing/feedback":      { permissions: ["marketing.article.write"] },
     "#hangfire":                { permissions: ["admin.administration.read"] },
     // …
   };
   ```
3. **`access-matrix.generated.json`** — Entra app-roles + groups manifest for the provisioning script (existing shape, only the role strings change).

The hand-written `AuthorizationConstants.cs` is deleted in favor of the generated `AccessRoles.generated.cs`.

### Frontend wiring

`Sidebar.tsx` reads only `ACCESS_ROUTES`:

```ts
const canSee = (key: string): boolean => {
  const req = ACCESS_ROUTES[key];
  if (!req) return false;
  return req.permissions.every(p => hasPermission(p));
};
```

Menu items reference the matrix key — `key: "/manufacturing/inventory"` for internal routes, `key: "#hangfire"` for external onClick items. The `requiredRole` field on menu items is removed.

Route guards collapse into the same source:

```tsx
<Route path="/marketing/feedback" element={
  <RequireMenuPath path="/marketing/feedback"><MarketingFeedbackPage /></RequireMenuPath>
} />
```

`RequireMenuPath` reads `ACCESS_ROUTES[path]` and applies the same AND-of-permissions check as the Sidebar. The legacy single-role `RequireAccess` is removed.

### Validation — Tests

**Backend** (`backend/test/Anela.Heblo.Tests/Authorization/`):

| Test | Asserts | Catches |
|---|---|---|
| `EveryAuthorizeRole_MatchesGateOn` | For each method's effective `[Authorize(Roles = "m.f.l")]`, parsed `Feature` equals nearest `[GateOn]` (method overrides class). | Controller uses wrong `AccessRoles` constant. |
| `EveryGatedEndpoint_HasGateOn` | Every controller method without `[AllowAnonymous]` has a class- or method-level `[GateOn]`. | New endpoint added without feature declaration. |
| `EveryMenuPath_PermissionsResolveToKnownRoles` | Each `MenuPath.Requires` entry's `(Feature, Level)` is permitted by the feature definition (e.g. `Write` requires `HasWrite: true`). | Menu requires a level the feature does not support. |
| `EveryMenuPath_FeatureHasController` | Each `Feature` referenced by `MenuPaths` is claimed by at least one controller's `[GateOn]`. Virtual `#…` menu paths are excluded (external onClick items have no controller). | Menu references a feature with no backing endpoints. |
| `EveryFeature_BundledInAtLeastOneGroup` | (existing) | Orphan permissions nobody can be granted. |

**Frontend** (`frontend/src/components/Layout/__tests__/Sidebar.test.tsx`):

| Test | Asserts | Catches |
|---|---|---|
| `EveryMenuItem_RegisteredInAccessRoutes` | Render Sidebar with `isSuperUser=true`, walk the tree, collect every `key` on rendered menu items, assert each appears in `ACCESS_ROUTES`. | **New menu item without a matrix entry.** |
| `EveryMenuItem_RouteExistsInApp` | Every internal `key` (not `#…`) matches a `<Route path>` in `App.tsx`. | Menu pointing at a dead route. |

By construction, `ACCESS_ROUTES` is regenerated from `AccessMatrix` on backend build, so FE and BE always read the same data.

### Migration order

Additive first, then a single mechanical sweep:

1. **Additive types.** Add `Feature` enum, `AccessLevel`, `FeatureDefinition`, `FeaturePermission`, `MenuPath`, `GateOnAttribute`. No behavior change.
2. **Add `MenuPaths` registry** alongside the current `AccessFeature.Path` / `AdditionalPaths` / `AdditionalWritePaths`. Both coexist.
3. **Generate `AccessRoles.generated.cs`** from the new model. New constants with new strings (`module.feature.level`). Old `AuthorizationConstants.cs` stays so controllers still compile.
4. **Sweep controllers.** Replace every `[Authorize(Roles = AccessRoles.Old)]` with the new generated constant + add `[GateOn(Feature.X)]`. ~162 call sites, mechanical, validated by the new tests. Single PR.
5. **Entra sync.** Update the app-role manifest and run the existing `sync-entra-access.sh` to push the new role strings to both app registrations.
6. **DB migration.** Rename permission strings stored in any `Group.Permissions` / role-assignment rows. One EF migration, idempotent string replace.
7. **Frontend swap.** Update Sidebar to consume the new `ACCESS_ROUTES` shape; add `RequireMenuPath`; delete `RequireAccess`.
8. **Delete dead code.** Remove `AuthorizationConstants.cs`, remove `Path` / `AdditionalPaths` / `AdditionalWritePaths` from `AccessFeature`. The shape consolidates to `FeatureDefinition` + `MenuPaths`.
9. **Tests.** Add the suite from §Validation.

Steps 1–3 are pure additions. Step 4 is the only risky one — it changes ~162 attributes, but the new tests fail loudly if anything is missed. Steps 5–6 require coordinated deploy because role strings on the wire change.

## Open questions

- Module prefix names — `Finance_*`, `Products_*`, `Customer_*`, `Purchase_*`, `Manufacture_*`, `Warehouse_*`, `Marketing_*`, `Anela_*`, `Admin_*`. All English. Confirm at implementation time; the only one with a non-obvious Czech mapping is `Warehouse_*` (Czech "Sklad").
- Section/grouping metadata (the old `Section` field on `AccessFeature`) — keep on `FeatureDefinition` for documentation, or drop? Suggest: drop. The Sidebar groups by its own static section structure; the matrix doesn't need to know the section name.
- Whether the existing `AccessGroup` definitions stay in `AccessMatrix.cs` or move to a sibling file. Suggest: keep alongside; they reference the same `Feature` values and benefit from co-location.

## Out of scope (deferred)

- Per-endpoint matrix enumeration (option 2 from the brainstorm). Reconsider only if `[GateOn]` proves insufficient.
- Externalizing the matrix to JSON/YAML. The class form is canonical for now; JSON is a generated artifact, not a source.
- Refactoring pages that span multiple features to depend on a single feature. The multi-permission `MenuPath` accepts the current reality; future page boundaries can simplify the matrix.
