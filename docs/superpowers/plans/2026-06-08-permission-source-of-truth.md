# Permission Source of Truth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify menu, frontend route guards, and backend controller authorization under one declarative source (`AccessMatrix.cs`) so menu visibility, route gates, and `[Authorize]` attributes can never drift.

**Architecture:** A `Feature` enum + `MenuPath` registry in `AccessMatrix.cs` becomes the canonical model. A `[GateOn(Feature)]` attribute links each controller to its feature; `AccessRoles` constants and frontend `ACCESS_ROUTES` are both generated from the matrix. Validator tests assert that controllers, menu items, and matrix all reference the same permissions.

**Tech Stack:** C# .NET 8, ASP.NET Core authorization, EF Core migrations, React/TypeScript, Jest, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-06-08-permission-source-of-truth-design.md`

---

## File Map

**Backend domain types** (new):
- `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.cs` — `Feature` enum (module-prefixed).
- `backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureDefinition.cs` — feature metadata record.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/FeaturePermission.cs` — `(Feature, AccessLevel)` pair record.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/MenuPath.cs` — menu key → required permissions record.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/GateOnAttribute.cs` — controller/method attribute.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/PermissionString.cs` — small helper turning `(Feature, Level)` ↔ wire string.

**Backend domain types** (modify):
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.cs` — rebuild around the new model (drop old `AccessFeature` shape).
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessModels.cs` — drop old `AccessFeature` record.

**Backend generated** (new file, written by generator):
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` — constants emitted from the matrix.

**Backend hand-written** (delete after migration):
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs` — old hand-written constants + legacy alias.

**Backend generator** (modify):
- `backend/tools/Anela.Heblo.AccessMatrixGen/Program.cs` — emit `AccessRoles.generated.cs`, new `ACCESS_ROUTES` shape, `access-matrix.generated.json`.

**Backend controllers** (sweep ~30 files):
- `backend/src/Anela.Heblo.API/Controllers/*.cs` — add `[GateOn(Feature.X)]`; change `AccessRoles.OldConst` to new generated constant names.

**Backend persistence** (new migration):
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_RenamePermissionStrings.cs` + Designer — rewrite `GroupPermissions.PermissionValue` from old format to new.
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationSeeder.cs` — make seeder also update existing groups' permissions when matrix changes (so future renames don't require manual migrations).

**Backend tests** (new):
- `backend/test/Anela.Heblo.Tests/Authorization/GateConsistencyTests.cs` — new validators.
- `backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixTests.cs` — extend with menu-path-resolves checks.

**Frontend generated** (overwritten by generator):
- `frontend/src/auth/accessMatrix.generated.ts` — new `ACCESS_ROUTES: Record<string, MenuRequirement>` shape.

**Frontend** (modify):
- `frontend/src/components/Layout/Sidebar.tsx` — consume new `ACCESS_ROUTES` shape, replace `requiredRole`/`href` mix with single `key`.
- `frontend/src/components/auth/RequireMenuPath.tsx` — new component (replaces `RequireAccess`).
- `frontend/src/components/auth/RequireAccess.tsx` — delete after migration.
- `frontend/src/App.tsx` — swap `RequireAccess` for `RequireMenuPath`.

**Frontend tests**:
- `frontend/src/components/Layout/__tests__/Sidebar.test.tsx` — add menu-registration coverage.
- `frontend/src/components/auth/__tests__/RequireMenuPath.test.tsx` — new.

---

## Phase 1 — Additive backend types

### Task 1: `Feature` enum

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// Module-prefixed feature identifier. Each value generates one or more
/// permission strings (feature.read / .write / .admin) based on the feature
/// definition in <see cref="AccessMatrix"/>.
/// </summary>
public enum Feature
{
    // Finance
    Finance_FinancialOverview,
    Finance_MarginAnalysis,

    // Products
    Products_ProductMargins,
    Products_Catalog,
    Products_Journal,

    // Customer
    Customer_BankStatements,
    Customer_KnowledgeBase,
    Customer_Smartsupp,

    // Purchase
    Purchase_PurchaseOrders,
    Purchase_PurchaseStock,

    // Manufacture
    Manufacture_ManufactureOrders,
    Manufacture_BatchPlanning,
    Manufacture_ManufactureOutput,
    Manufacture_ManufactureStock,
    Manufacture_MaterialInventory,
    Manufacture_ProductInventory,
    Manufacture_MaterialContainers,

    // Warehouse (Czech: Sklad)
    Warehouse_Logistics,
    Warehouse_Expedition,
    Warehouse_Packaging,
    Warehouse_StockUp,

    // Marketing
    Marketing_Article,
    Marketing_Leaflet,
    Marketing_Photobank,
    Marketing_MarketingCalendar,

    // Anela
    Anela_Meetings,
    Anela_OrgChart,

    // Admin
    Admin_DataQuality,
    Admin_Administration,
    Admin_FeatureFlags,
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.cs
git commit -m "feat(authz): add module-prefixed Feature enum"
```

### Task 2: `FeatureDefinition` + `FeaturePermission` records

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureDefinition.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/FeaturePermission.cs`

- [ ] **Step 1: Create FeatureDefinition.cs**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>Per-feature metadata: which levels exist (read is implicit).</summary>
public sealed record FeatureDefinition(
    Feature Key,
    string Label,
    bool HasWrite = false,
    bool HasAdmin = false);
```

- [ ] **Step 2: Create FeaturePermission.cs**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>A specific (feature, level) pair. Used inside MenuPath.Requires.</summary>
public sealed record FeaturePermission(Feature Feature, AccessLevel Level);
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: `0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureDefinition.cs \
        backend/src/Anela.Heblo.Domain/Features/Authorization/FeaturePermission.cs
git commit -m "feat(authz): add FeatureDefinition and FeaturePermission records"
```

### Task 3: `MenuPath` record

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/MenuPath.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// One menu entry. Key is the frontend route (e.g. "/finance/overview") or a virtual
/// identifier for external onClick items (e.g. "#hangfire"). Requires lists permissions
/// the user must hold (AND semantics) for the item to be visible.
/// </summary>
public sealed record MenuPath(
    string Key,
    IReadOnlyList<FeaturePermission> Requires);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: `0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/MenuPath.cs
git commit -m "feat(authz): add MenuPath record"
```

### Task 4: `GateOnAttribute`

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/GateOnAttribute.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/GateOnAttributeTests.cs`

- [ ] **Step 1: Write failing test**

Create `backend/test/Anela.Heblo.Tests/Authorization/GateOnAttributeTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GateOnAttributeTests
{
    [GateOn(Feature.Manufacture_BatchPlanning)]
    private class SampleController { }

    [Fact]
    public void GateOn_ExposesFeature()
    {
        var attr = (GateOnAttribute)Attribute.GetCustomAttribute(
            typeof(SampleController), typeof(GateOnAttribute))!;
        attr.Feature.Should().Be(Feature.Manufacture_BatchPlanning);
    }
}
```

- [ ] **Step 2: Run test (expect compile failure since GateOnAttribute doesn't exist yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter GateOnAttributeTests 2>&1 | tail -10`
Expected: compile error mentioning `GateOnAttribute`.

- [ ] **Step 3: Create the attribute**

`backend/src/Anela.Heblo.Domain/Features/Authorization/GateOnAttribute.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// Declares which Feature a controller class or action method belongs to.
/// A method-level attribute overrides the class-level for that method.
/// Validated by GateConsistencyTests.EveryAuthorizeRole_MatchesGateOn.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class GateOnAttribute : Attribute
{
    public Feature Feature { get; }
    public GateOnAttribute(Feature feature) => Feature = feature;
}
```

- [ ] **Step 4: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter GateOnAttributeTests`
Expected: `Passed: 1`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/GateOnAttribute.cs \
        backend/test/Anela.Heblo.Tests/Authorization/GateOnAttributeTests.cs
git commit -m "feat(authz): add [GateOn(Feature)] attribute"
```

### Task 5: `PermissionString` helper

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/PermissionString.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/PermissionStringTests.cs`

This helper centralizes the `(Feature, Level) ↔ "module.feature.level"` translation. Generator and validators both use it.

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Authorization/PermissionStringTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class PermissionStringTests
{
    [Theory]
    [InlineData(Feature.Purchase_PurchaseOrders, AccessLevel.Read, "purchase.purchase_orders.read")]
    [InlineData(Feature.Manufacture_BatchPlanning, AccessLevel.Write, "manufacture.batch_planning.write")]
    [InlineData(Feature.Marketing_Photobank, AccessLevel.Admin, "marketing.photobank.admin")]
    [InlineData(Feature.Admin_Administration, AccessLevel.Read, "admin.administration.read")]
    public void Format_BuildsWireString(Feature f, AccessLevel l, string expected)
    {
        PermissionString.Format(f, l).Should().Be(expected);
    }

    [Theory]
    [InlineData("purchase.purchase_orders.read", Feature.Purchase_PurchaseOrders, AccessLevel.Read)]
    [InlineData("manufacture.batch_planning.write", Feature.Manufacture_BatchPlanning, AccessLevel.Write)]
    [InlineData("admin.administration.read", Feature.Admin_Administration, AccessLevel.Read)]
    public void TryParse_RecognizesValidStrings(string s, Feature f, AccessLevel l)
    {
        PermissionString.TryParse(s, out var feature, out var level).Should().BeTrue();
        feature.Should().Be(f);
        level.Should().Be(l);
    }

    [Theory]
    [InlineData("heblo_user")]
    [InlineData("super_user")]
    [InlineData("nonsense")]
    [InlineData("purchase.unknown.read")]
    public void TryParse_RejectsNonMatrixStrings(string s)
    {
        PermissionString.TryParse(s, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(Feature.Purchase_PurchaseOrders, "PurchasePurchaseOrders")]
    [InlineData(Feature.Manufacture_BatchPlanning, "ManufactureBatchPlanning")]
    [InlineData(Feature.Admin_FeatureFlags, "AdminFeatureFlags")]
    public void ConstantSuffix_StripsUnderscore(Feature f, string expected)
    {
        PermissionString.ConstantSuffix(f).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run tests (expect compile failure)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter PermissionStringTests 2>&1 | tail -5`
Expected: compile error mentioning `PermissionString`.

- [ ] **Step 3: Implement PermissionString**

Create `backend/src/Anela.Heblo.Domain/Features/Authorization/PermissionString.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// Turns Feature + AccessLevel ↔ wire string ("module.feature.level").
/// Module = portion of the enum name before '_', snake_cased.
/// Feature segment = portion after '_', snake_cased.
/// </summary>
public static class PermissionString
{
    public static string Format(Feature feature, AccessLevel level)
        => $"{ModuleSegment(feature)}.{FeatureSegment(feature)}.{LevelSegment(level)}";

    public static bool TryParse(string s, out Feature feature, out AccessLevel level)
    {
        feature = default;
        level = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        var enumName = $"{ToPascalCase(parts[0])}_{ToPascalCase(parts[1])}";
        if (!Enum.TryParse(enumName, ignoreCase: false, out feature)) return false;
        return parts[2] switch
        {
            "read" => (level = AccessLevel.Read) == AccessLevel.Read,
            "write" => (level = AccessLevel.Write) == AccessLevel.Write,
            "admin" => (level = AccessLevel.Admin) == AccessLevel.Admin,
            _ => false,
        };
    }

    /// <summary>e.g. Feature.Manufacture_BatchPlanning → "ManufactureBatchPlanning".</summary>
    public static string ConstantSuffix(Feature feature)
        => feature.ToString().Replace("_", "");

    private static string ModuleSegment(Feature f) => ToSnakeCase(f.ToString().Split('_')[0]);
    private static string FeatureSegment(Feature f) => ToSnakeCase(f.ToString().Split('_', 2)[1]);
    private static string LevelSegment(AccessLevel l) => l.ToString().ToLowerInvariant();

    private static string ToSnakeCase(string pascal)
    {
        var sb = new System.Text.StringBuilder(pascal.Length * 2);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(pascal[i]));
        }
        return sb.ToString();
    }

    private static string ToPascalCase(string snake)
    {
        var sb = new System.Text.StringBuilder(snake.Length);
        var upperNext = true;
        foreach (var ch in snake)
        {
            if (ch == '_') { upperNext = true; continue; }
            sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
            upperNext = false;
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter PermissionStringTests`
Expected: `Passed: 11`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/PermissionString.cs \
        backend/test/Anela.Heblo.Tests/Authorization/PermissionStringTests.cs
git commit -m "feat(authz): add PermissionString helper for Feature↔wire conversion"
```

---

## Phase 2 — Rebuild `AccessMatrix.cs`

### Task 6: Rebuild AccessMatrix structure

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessModels.cs` (drop old `AccessFeature`)

The current `AccessMatrix.Features` returns `AccessFeature` records with `Path` / `AdditionalPaths` / `AdditionalWritePaths`. Replace with `FeatureDefinition[]` + a flat `MenuPath[]` registry.

- [ ] **Step 1: Replace AccessMatrix.cs contents**

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

public static class AccessMatrix
{
    public const string BaseRole = "heblo_user";

    public static readonly IReadOnlyList<FeatureDefinition> Features = new[]
    {
        // Finance
        new FeatureDefinition(Feature.Finance_FinancialOverview, "Finanční přehled"),
        new FeatureDefinition(Feature.Finance_MarginAnalysis, "Analýza marží"),

        // Products
        new FeatureDefinition(Feature.Products_ProductMargins, "Marže produktů"),
        new FeatureDefinition(Feature.Products_Catalog, "Katalog", HasWrite: true),
        new FeatureDefinition(Feature.Products_Journal, "Žurnál", HasWrite: true),

        // Customer
        new FeatureDefinition(Feature.Customer_BankStatements, "Bankovní výpisy"),
        new FeatureDefinition(Feature.Customer_KnowledgeBase, "Znalostní báze", HasWrite: true),
        new FeatureDefinition(Feature.Customer_Smartsupp, "Smartsupp"),

        // Purchase
        new FeatureDefinition(Feature.Purchase_PurchaseOrders, "Nákupní objednávky", HasWrite: true),
        new FeatureDefinition(Feature.Purchase_PurchaseStock, "Analýza skladu (nákup)"),

        // Manufacture
        new FeatureDefinition(Feature.Manufacture_ManufactureOrders, "Výrobní objednávky", HasWrite: true),
        new FeatureDefinition(Feature.Manufacture_BatchPlanning, "Plánování šarží", HasWrite: true),
        new FeatureDefinition(Feature.Manufacture_ManufactureOutput, "Výstup výroby"),
        new FeatureDefinition(Feature.Manufacture_ManufactureStock, "Analýza skladu (výroba)"),
        new FeatureDefinition(Feature.Manufacture_MaterialInventory, "Inventura materiálu", HasWrite: true),
        new FeatureDefinition(Feature.Manufacture_ProductInventory, "Inventura produktů", HasWrite: true),
        new FeatureDefinition(Feature.Manufacture_MaterialContainers, "Kontejnery materiálu", HasWrite: true),

        // Warehouse (Sklad)
        new FeatureDefinition(Feature.Warehouse_Logistics, "Logistika", HasWrite: true),
        new FeatureDefinition(Feature.Warehouse_Expedition, "Expedice", HasWrite: true),
        new FeatureDefinition(Feature.Warehouse_Packaging, "Balení", HasWrite: true),
        new FeatureDefinition(Feature.Warehouse_StockUp, "Naskladnění", HasWrite: true),

        // Marketing
        new FeatureDefinition(Feature.Marketing_Article, "Články", HasWrite: true),
        new FeatureDefinition(Feature.Marketing_Leaflet, "Generátor letáků", HasWrite: true),
        new FeatureDefinition(Feature.Marketing_Photobank, "Fotobanka", HasWrite: true, HasAdmin: true),
        new FeatureDefinition(Feature.Marketing_MarketingCalendar, "Marketingový kalendář", HasWrite: true),

        // Anela
        new FeatureDefinition(Feature.Anela_Meetings, "Schůzky", HasWrite: true),
        new FeatureDefinition(Feature.Anela_OrgChart, "Organigram"),

        // Admin
        new FeatureDefinition(Feature.Admin_DataQuality, "Kvalita dat", HasWrite: true),
        new FeatureDefinition(Feature.Admin_Administration, "Administrace", HasWrite: true),
        new FeatureDefinition(Feature.Admin_FeatureFlags, "Feature příznaky", HasWrite: true),
    };

    public static readonly IReadOnlyList<MenuPath> MenuPaths = new[]
    {
        // Finance
        new MenuPath("/finance/overview",
            Require((Feature.Finance_FinancialOverview, AccessLevel.Read))),
        new MenuPath("/analytics/product-margin-summary",
            Require((Feature.Finance_MarginAnalysis, AccessLevel.Read))),

        // Products
        new MenuPath("/products/margins",
            Require((Feature.Products_ProductMargins, AccessLevel.Read))),
        new MenuPath("/catalog",
            Require((Feature.Products_Catalog, AccessLevel.Read))),
        new MenuPath("/journal",
            Require((Feature.Products_Journal, AccessLevel.Read))),

        // Customer
        new MenuPath("/customer/issued-invoices",
            Require((Feature.Customer_BankStatements, AccessLevel.Read))),
        new MenuPath("/customer/bank-statements-overview",
            Require((Feature.Customer_BankStatements, AccessLevel.Read))),
        new MenuPath("/knowledge-base",
            Require((Feature.Customer_KnowledgeBase, AccessLevel.Read))),
        new MenuPath("/customer/smartsupp",
            Require((Feature.Customer_Smartsupp, AccessLevel.Read))),

        // Purchase
        new MenuPath("/purchase/orders",
            Require((Feature.Purchase_PurchaseOrders, AccessLevel.Read))),
        new MenuPath("/purchase/invoice-classification",
            Require((Feature.Purchase_PurchaseOrders, AccessLevel.Read))),
        new MenuPath("/purchase/stock-analysis",
            Require((Feature.Purchase_PurchaseStock, AccessLevel.Read))),

        // Manufacture
        new MenuPath("/manufacturing/orders",
            Require((Feature.Manufacture_ManufactureOrders, AccessLevel.Read))),
        new MenuPath("/manufacturing/batch-planning",
            Require((Feature.Manufacture_BatchPlanning, AccessLevel.Read))),
        new MenuPath("/manufacturing/batch-calculator",
            Require((Feature.Manufacture_BatchPlanning, AccessLevel.Read))),
        new MenuPath("/manufacturing/output",
            Require((Feature.Manufacture_ManufactureOutput, AccessLevel.Read))),
        new MenuPath("/manufacturing/stock-analysis",
            Require((Feature.Manufacture_ManufactureStock, AccessLevel.Read))),
        new MenuPath("/manufacturing/inventory",
            Require((Feature.Manufacture_MaterialInventory, AccessLevel.Read))),
        new MenuPath("/manufacturing/product-inventory",
            Require((Feature.Manufacture_ProductInventory, AccessLevel.Read))),
        new MenuPath("/manufacturing/material-containers",
            Require((Feature.Manufacture_MaterialContainers, AccessLevel.Read))),

        // Warehouse
        new MenuPath("/logistics/inventory",
            Require((Feature.Warehouse_Logistics, AccessLevel.Read))),
        new MenuPath("/logistics/receive-boxes",
            Require((Feature.Warehouse_Logistics, AccessLevel.Read))),
        new MenuPath("/logistics/transport-boxes",
            Require((Feature.Warehouse_Logistics, AccessLevel.Read))),
        new MenuPath("/logistics/packing-materials",
            Require((Feature.Warehouse_Logistics, AccessLevel.Read))),
        new MenuPath("/logistics/expedition-archive",
            Require((Feature.Warehouse_Expedition, AccessLevel.Read))),
        new MenuPath("/customer/expedition-settings",
            Require((Feature.Warehouse_Expedition, AccessLevel.Read))),
        new MenuPath("/baleni",
            Require((Feature.Warehouse_Packaging, AccessLevel.Read))),
        new MenuPath("/logistics/gift-package-manufacturing",
            Require((Feature.Warehouse_Packaging, AccessLevel.Read))),
        new MenuPath("/stock-up-operations",
            Require((Feature.Warehouse_StockUp, AccessLevel.Read))),

        // Marketing
        new MenuPath("/articles",
            Require((Feature.Marketing_Article, AccessLevel.Read))),
        new MenuPath("/marketing/feedback",
            Require((Feature.Marketing_Article, AccessLevel.Write))),
        new MenuPath("/leaflet-generator",
            Require((Feature.Marketing_Leaflet, AccessLevel.Read))),
        new MenuPath("/marketing/photobank",
            Require((Feature.Marketing_Photobank, AccessLevel.Read))),
        new MenuPath("/marketing/calendar",
            Require((Feature.Marketing_MarketingCalendar, AccessLevel.Read))),

        // Anela
        new MenuPath("/automation/meeting-tasks",
            Require((Feature.Anela_Meetings, AccessLevel.Read))),
        new MenuPath("#org-chart",
            Require((Feature.Anela_OrgChart, AccessLevel.Read))),

        // Admin
        new MenuPath("/automation/data-quality",
            Require((Feature.Admin_DataQuality, AccessLevel.Read))),
        new MenuPath("/admin/access",
            Require((Feature.Admin_Administration, AccessLevel.Read))),
        new MenuPath("/automation/background-tasks",
            Require((Feature.Admin_Administration, AccessLevel.Read))),
        new MenuPath("/recurring-jobs",
            Require((Feature.Admin_Administration, AccessLevel.Read))),
        new MenuPath("#hangfire",
            Require((Feature.Admin_Administration, AccessLevel.Read))),
        new MenuPath("/admin/feature-flags",
            Require((Feature.Admin_FeatureFlags, AccessLevel.Read))),

        // External menu items
        new MenuPath("#terminal",
            Require((Feature.Warehouse_Logistics, AccessLevel.Read))),
        new MenuPath("#baleni-external",
            Require((Feature.Warehouse_Packaging, AccessLevel.Read))),
    };

    public static readonly IReadOnlyList<AccessGroup> Groups = new[]
    {
        new AccessGroup("Spravce", AllPermissionStrings().ToArray()),
        new AccessGroup("Vedeni", AllPermissionStrings().Where(r => r.EndsWith(".read")).ToArray()),
        new AccessGroup("Ucetni", new[]
        {
            PermissionString.Format(Feature.Finance_FinancialOverview, AccessLevel.Read),
            PermissionString.Format(Feature.Finance_MarginAnalysis, AccessLevel.Read),
            PermissionString.Format(Feature.Products_ProductMargins, AccessLevel.Read),
            PermissionString.Format(Feature.Customer_BankStatements, AccessLevel.Read),
            PermissionString.Format(Feature.Products_Journal, AccessLevel.Read),
            PermissionString.Format(Feature.Products_Journal, AccessLevel.Write),
            PermissionString.Format(Feature.Customer_Smartsupp, AccessLevel.Read),
        }),
        new AccessGroup("Marketer", new[]
        {
            PermissionString.Format(Feature.Marketing_MarketingCalendar, AccessLevel.Read),
            PermissionString.Format(Feature.Marketing_MarketingCalendar, AccessLevel.Write),
            PermissionString.Format(Feature.Marketing_Photobank, AccessLevel.Read),
            PermissionString.Format(Feature.Marketing_Photobank, AccessLevel.Write),
            PermissionString.Format(Feature.Marketing_Leaflet, AccessLevel.Read),
            PermissionString.Format(Feature.Marketing_Leaflet, AccessLevel.Write),
            PermissionString.Format(Feature.Marketing_Article, AccessLevel.Read),
            PermissionString.Format(Feature.Marketing_Article, AccessLevel.Write),
        }),
        new AccessGroup("Nakupci", new[]
        {
            PermissionString.Format(Feature.Purchase_PurchaseOrders, AccessLevel.Read),
            PermissionString.Format(Feature.Purchase_PurchaseOrders, AccessLevel.Write),
            PermissionString.Format(Feature.Purchase_PurchaseStock, AccessLevel.Read),
        }),
        new AccessGroup("Vedouci_vyroby", new[]
        {
            PermissionString.Format(Feature.Manufacture_ManufactureOrders, AccessLevel.Read),
            PermissionString.Format(Feature.Manufacture_ManufactureOrders, AccessLevel.Write),
            PermissionString.Format(Feature.Manufacture_ManufactureOutput, AccessLevel.Read),
            PermissionString.Format(Feature.Manufacture_ManufactureStock, AccessLevel.Read),
            PermissionString.Format(Feature.Manufacture_BatchPlanning, AccessLevel.Read),
            PermissionString.Format(Feature.Manufacture_BatchPlanning, AccessLevel.Write),
            PermissionString.Format(Feature.Manufacture_MaterialInventory, AccessLevel.Read),
            PermissionString.Format(Feature.Manufacture_MaterialInventory, AccessLevel.Write),
            PermissionString.Format(Feature.Manufacture_MaterialContainers, AccessLevel.Read),
            PermissionString.Format(Feature.Manufacture_MaterialContainers, AccessLevel.Write),
            PermissionString.Format(Feature.Manufacture_ProductInventory, AccessLevel.Read),
            PermissionString.Format(Feature.Manufacture_ProductInventory, AccessLevel.Write),
        }),
        new AccessGroup("Pracovnik_vyroby", new[]
        {
            PermissionString.Format(Feature.Manufacture_ManufactureOrders, AccessLevel.Read),
            PermissionString.Format(Feature.Manufacture_MaterialInventory, AccessLevel.Read),
            PermissionString.Format(Feature.Manufacture_MaterialInventory, AccessLevel.Write),
            PermissionString.Format(Feature.Manufacture_ProductInventory, AccessLevel.Read),
            PermissionString.Format(Feature.Manufacture_MaterialContainers, AccessLevel.Read),
        }),
        new AccessGroup("Vedouci_skladu", new[]
        {
            PermissionString.Format(Feature.Warehouse_Logistics, AccessLevel.Read),
            PermissionString.Format(Feature.Warehouse_Logistics, AccessLevel.Write),
            PermissionString.Format(Feature.Warehouse_Expedition, AccessLevel.Read),
            PermissionString.Format(Feature.Warehouse_Expedition, AccessLevel.Write),
            PermissionString.Format(Feature.Warehouse_Packaging, AccessLevel.Read),
            PermissionString.Format(Feature.Warehouse_Packaging, AccessLevel.Write),
            PermissionString.Format(Feature.Warehouse_StockUp, AccessLevel.Read),
            PermissionString.Format(Feature.Warehouse_StockUp, AccessLevel.Write),
        }),
        new AccessGroup("Skladnik", new[]
        {
            PermissionString.Format(Feature.Warehouse_Logistics, AccessLevel.Read),
            PermissionString.Format(Feature.Warehouse_Expedition, AccessLevel.Read),
            PermissionString.Format(Feature.Warehouse_Packaging, AccessLevel.Read),
            PermissionString.Format(Feature.Warehouse_Packaging, AccessLevel.Write),
        }),
        new AccessGroup("Poradenstvi", new[]
        {
            PermissionString.Format(Feature.Customer_KnowledgeBase, AccessLevel.Read),
            PermissionString.Format(Feature.Customer_KnowledgeBase, AccessLevel.Write),
        }),
    };

    public static IEnumerable<AccessRoleDefinition> Roles()
    {
        foreach (var f in Features)
        {
            yield return new AccessRoleDefinition(
                PermissionString.Format(f.Key, AccessLevel.Read), f.Key.ToString(), AccessLevel.Read);
            if (f.HasWrite)
                yield return new AccessRoleDefinition(
                    PermissionString.Format(f.Key, AccessLevel.Write), f.Key.ToString(), AccessLevel.Write);
            if (f.HasAdmin)
                yield return new AccessRoleDefinition(
                    PermissionString.Format(f.Key, AccessLevel.Admin), f.Key.ToString(), AccessLevel.Admin);
        }
    }

    public static IEnumerable<string> AllPermissionStrings() => Roles().Select(r => r.Value);

    // Backward compatibility for callers that used AllRoleValues()
    public static IEnumerable<string> AllRoleValues() => AllPermissionStrings();
}
```

- [ ] **Step 2: Update AccessModels.cs to drop the old AccessFeature record**

Replace `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessModels.cs` contents with:

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>A single concrete app role (one feature × one level).</summary>
public sealed record AccessRoleDefinition(string Value, string Feature, AccessLevel Level);

/// <summary>An Entra security group representing an employee work-role.</summary>
public sealed record AccessGroup(string Name, IReadOnlyList<string> Roles);
```

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj 2>&1 | tail -5`
Expected: `0 Error(s)`.

(The dependent projects — `Anela.Heblo.API` etc. — will fail at this point because `AccessRoles.X` constants now have new names. That's expected and fixed in Phase 4.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.cs \
        backend/src/Anela.Heblo.Domain/Features/Authorization/AccessModels.cs
git commit -m "feat(authz): rebuild AccessMatrix around Feature enum and MenuPaths"
```

---

## Phase 3 — Update generator

### Task 7: Generate `AccessRoles.generated.cs`

**Files:**
- Modify: `backend/tools/Anela.Heblo.AccessMatrixGen/Program.cs`

Currently the generator only emits `accessMatrix.generated.ts` + `access-matrix.generated.json`. Extend it to also emit a C# constants file.

- [ ] **Step 1: Read the current generator file**

Read `backend/tools/Anela.Heblo.AccessMatrixGen/Program.cs` end-to-end.

- [ ] **Step 2: Replace with the new generator**

```csharp
using System.Text;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Authorization;

var tsPath = args.ElementAtOrDefault(0) ?? "frontend/src/auth/accessMatrix.generated.ts";
var jsonPath = args.ElementAtOrDefault(1) ?? "access-matrix.generated.json";
var csPath = args.ElementAtOrDefault(2)
    ?? "backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs";

var roles = AccessMatrix.Roles().ToList();

// 1. JSON manifest for the Entra provisioning script.
var featureRoles = roles.Select(r => new
{
    id = DeterministicGuid.ForRole(r.Value).ToString(),
    allowedMemberTypes = new[] { "User" },
    description = $"{r.Feature} {r.Level}",
    displayName = r.Value,
    isEnabled = true,
    value = r.Value
});
var superUserRole = new[]
{
    new
    {
        id = DeterministicGuid.ForRole("super_user").ToString(),
        allowedMemberTypes = new[] { "User" },
        description = "Break-glass: grants all permissions regardless of DB group assignments",
        displayName = "super_user",
        isEnabled = true,
        value = "super_user",
    }
};
var appRoles = superUserRole.Concat(featureRoles);
var groups = AccessMatrix.Groups.Select(g => new { name = g.Name, roles = g.Roles });
var manifest = new { appRoles, groups };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? ".");
File.WriteAllText(jsonPath,
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

// 2. TS map for the frontend: ACCESS_ROUTES with MenuRequirement shape.
var ts = new StringBuilder();
ts.AppendLine("// AUTO-GENERATED by Anela.Heblo.AccessMatrixGen. Do not edit by hand.");
ts.AppendLine("export interface MenuRequirement { permissions: string[]; }");
ts.AppendLine("export const ACCESS_ROUTES: Record<string, MenuRequirement> = {");
foreach (var menu in AccessMatrix.MenuPaths)
{
    var perms = string.Join(", ", menu.Requires.Select(r =>
        $"\"{PermissionString.Format(r.Feature, r.Level)}\""));
    ts.AppendLine($"  \"{menu.Key}\": {{ permissions: [{perms}] }},");
}
ts.AppendLine("};");
ts.AppendLine();
ts.AppendLine("export const ACCESS_ROLES = [");
foreach (var r in roles) ts.AppendLine($"  \"{r.Value}\",");
ts.AppendLine("] as const;");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tsPath)) ?? ".");
File.WriteAllText(tsPath, ts.ToString());

// 3. C# constants for [Authorize(Roles = AccessRoles.X)].
var cs = new StringBuilder();
cs.AppendLine("// AUTO-GENERATED by Anela.Heblo.AccessMatrixGen. Do not edit by hand.");
cs.AppendLine("namespace Anela.Heblo.Domain.Features.Authorization;");
cs.AppendLine();
cs.AppendLine("public static class AccessRoles");
cs.AppendLine("{");
cs.AppendLine("    public const string Base = \"heblo_user\";");
cs.AppendLine("    public const string SuperUser = \"super_user\";");
cs.AppendLine();
foreach (var r in roles)
{
    if (!Enum.TryParse<Feature>(r.Feature, out var feature)) continue;
    var levelSuffix = r.Level switch
    {
        AccessLevel.Read => "Read",
        AccessLevel.Write => "Write",
        AccessLevel.Admin => "Admin",
        _ => throw new InvalidOperationException($"Unknown level {r.Level}")
    };
    cs.AppendLine($"    public const string {PermissionString.ConstantSuffix(feature)}{levelSuffix} = \"{r.Value}\";");
}
cs.AppendLine("}");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csPath)) ?? ".");
File.WriteAllText(csPath, cs.ToString());

Console.WriteLine($"Wrote {tsPath}, {jsonPath}, and {csPath}");
```

- [ ] **Step 3: Build the generator tool**

Run: `dotnet build backend/tools/Anela.Heblo.AccessMatrixGen/Anela.Heblo.AccessMatrixGen.csproj 2>&1 | tail -5`
Expected: `0 Error(s)`.

- [ ] **Step 4: Run the generator manually**

```bash
dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen -- \
  frontend/src/auth/accessMatrix.generated.ts \
  access-matrix.generated.json \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs
```

Expected: `Wrote ... and ...`.

- [ ] **Step 5: Verify outputs**

```bash
head -5 backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs
head -5 frontend/src/auth/accessMatrix.generated.ts
```

Expected: `AccessRoles.generated.cs` starts with the AUTO-GENERATED comment; TS starts with `MenuRequirement` interface.

- [ ] **Step 6: Delete the hand-written AuthorizationConstants.cs**

```bash
rm backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs
```

(The generated `AccessRoles.generated.cs` replaces it. The legacy `AuthorizationConstants.Roles.*` aliases used by 4 files will be fixed in Task 9.)

- [ ] **Step 7: Build domain again to confirm the generated file integrates**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj 2>&1 | tail -5`
Expected: `0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add backend/tools/Anela.Heblo.AccessMatrixGen/Program.cs \
        backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs \
        frontend/src/auth/accessMatrix.generated.ts \
        access-matrix.generated.json
git add -u backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs
git commit -m "feat(authz): generate AccessRoles + new ACCESS_ROUTES from matrix"
```

### Task 8: Ensure generator runs on build

**Files:**
- Verify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` build target invokes the generator with the new 3rd argument.

- [ ] **Step 1: Read the csproj generator target**

```bash
grep -A5 "AccessMatrixGen\|Generating access matrix" backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

- [ ] **Step 2: Update the build target to pass the C# output path**

If the target currently calls the generator with 2 args, add the 3rd:

```xml
<Target Name="GenerateAccessMatrix" BeforeTargets="Build">
  <Message Text="Generating access matrix artifacts..." Importance="high" />
  <Exec Command="dotnet run --project $(MSBuildThisFileDirectory)../../tools/Anela.Heblo.AccessMatrixGen -- $(MSBuildThisFileDirectory)../../../frontend/src/auth/accessMatrix.generated.ts $(MSBuildThisFileDirectory)../../../access-matrix.generated.json $(MSBuildThisFileDirectory)../Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs" />
  <Message Text="Access matrix generation completed." Importance="high" />
</Target>
```

(Adjust paths to match the existing target's style.)

- [ ] **Step 3: Clean and rebuild to verify the target fires**

```bash
rm backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj 2>&1 | grep -E "Generating|Wrote|error"
```

Expected: sees `Generating access matrix artifacts...` and `Wrote .../AccessRoles.generated.cs`.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
git commit -m "build(authz): emit AccessRoles.generated.cs from build target"
```

---

## Phase 4 — Controller sweep

### Task 9: Replace `AuthorizationConstants.Roles.*` usages with `AccessRoles.*`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Application/MeetingTasks/MeetingAccessGuardTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingAccessGuard.cs`
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs`
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs`

These 4 files use the legacy `AuthorizationConstants.Roles.*` aliases (now deleted in Task 7).

- [ ] **Step 1: Find each usage and translate to the new constant**

For each file, replace each occurrence of `AuthorizationConstants.Roles.<X>` with `AccessRoles.<NewName>`. The legacy alias table at the bottom of the old file maps:

```
AuthorizationConstants.Roles.Base            → AccessRoles.Base
AuthorizationConstants.Roles.SuperUser       → AccessRoles.SuperUser
AuthorizationConstants.Roles.HebloUser       → AccessRoles.Base
AuthorizationConstants.Roles.MeetingManager  → AccessRoles.AnelaMeetingsWrite
AuthorizationConstants.Roles.FinanceReader   → AccessRoles.FinanceFinancialOverviewRead
AuthorizationConstants.Roles.MarketingReader → AccessRoles.MarketingArticleRead
AuthorizationConstants.Roles.MarketingWriter → AccessRoles.MarketingArticleWrite
AuthorizationConstants.Roles.OrgChartRead    → AccessRoles.AnelaOrgChartRead
AuthorizationConstants.Roles.AdministrationRead  → AccessRoles.AdminAdministrationRead
AuthorizationConstants.Roles.AdministrationWrite → AccessRoles.AdminAdministrationWrite
```

For each remaining alias (e.g. `BatchPlanningRead`), translate using the rule: `{Module}{FeatureSuffix}{Level}` — e.g. `BatchPlanningRead` → `ManufactureBatchPlanningRead`.

- [ ] **Step 2: Build the API project**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj 2>&1 | grep -E "AuthorizationConstants|error" | head -20
```

Expected: no references remaining to `AuthorizationConstants.Roles`.

- [ ] **Step 3: Commit**

```bash
git add -u backend/src/Anela.Heblo.Application backend/src/Anela.Heblo.API backend/test
git commit -m "refactor(authz): replace AuthorizationConstants.Roles aliases with AccessRoles"
```

### Task 10: Sweep controllers — `[Authorize]` role names + add `[GateOn]`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/*.cs` (~30 controller files, ~162 attributes total).

This is the bulk-edit task. Every `AccessRoles.<OldName>` in a `[Authorize(Roles = ...)]` becomes `AccessRoles.<NewName>` (rule: prepend module prefix). Every controller class gets a class-level `[GateOn(Feature.X)]`. Methods that use a different feature than their class get a method-level `[GateOn]` override.

Mapping (old constant → new constant, applied across every file):

```
FinancialOverviewRead      → FinanceFinancialOverviewRead
MarginAnalysisRead         → FinanceMarginAnalysisRead
ProductMarginsRead         → ProductsProductMarginsRead
CatalogRead                → ProductsCatalogRead
CatalogWrite               → ProductsCatalogWrite
JournalRead                → ProductsJournalRead
JournalWrite               → ProductsJournalWrite
BankStatementsRead         → CustomerBankStatementsRead
KnowledgeBaseRead          → CustomerKnowledgeBaseRead
KnowledgeBaseWrite         → CustomerKnowledgeBaseWrite
SmartsuppRead              → CustomerSmartsuppRead
PurchaseOrdersRead         → PurchasePurchaseOrdersRead
PurchaseOrdersWrite        → PurchasePurchaseOrdersWrite
PurchaseStockRead          → PurchasePurchaseStockRead
ManufactureOrdersRead      → ManufactureManufactureOrdersRead
ManufactureOrdersWrite     → ManufactureManufactureOrdersWrite
BatchPlanningRead          → ManufactureBatchPlanningRead
BatchPlanningWrite         → ManufactureBatchPlanningWrite
ManufactureOutputRead      → ManufactureManufactureOutputRead
ManufactureStockRead       → ManufactureManufactureStockRead
MaterialInventoryRead      → ManufactureMaterialInventoryRead
MaterialInventoryWrite     → ManufactureMaterialInventoryWrite
ProductInventoryRead       → ManufactureProductInventoryRead
ProductInventoryWrite      → ManufactureProductInventoryWrite
MaterialContainersRead     → ManufactureMaterialContainersRead
MaterialContainersWrite    → ManufactureMaterialContainersWrite
LogisticsRead              → WarehouseLogisticsRead
LogisticsWrite             → WarehouseLogisticsWrite
ExpeditionRead             → WarehouseExpeditionRead
ExpeditionWrite            → WarehouseExpeditionWrite
PackagingRead              → WarehousePackagingRead
PackagingWrite             → WarehousePackagingWrite
StockUpRead                → WarehouseStockUpRead
StockUpWrite               → WarehouseStockUpWrite
ArticleRead                → MarketingArticleRead
ArticleWrite               → MarketingArticleWrite
LeafletRead                → MarketingLeafletRead
LeafletWrite               → MarketingLeafletWrite
PhotobankRead              → MarketingPhotobankRead
PhotobankWrite             → MarketingPhotobankWrite
PhotobankAdmin             → MarketingPhotobankAdmin
MarketingCalendarRead      → MarketingMarketingCalendarRead
MarketingCalendarWrite     → MarketingMarketingCalendarWrite
MeetingsRead               → AnelaMeetingsRead
MeetingsWrite              → AnelaMeetingsWrite
OrgChartRead               → AnelaOrgChartRead
DataQualityRead            → AdminDataQualityRead
DataQualityWrite           → AdminDataQualityWrite
AdministrationRead         → AdminAdministrationRead
AdministrationWrite        → AdminAdministrationWrite
FeatureFlagsRead           → AdminFeatureFlagsRead
FeatureFlagsWrite          → AdminFeatureFlagsWrite
```

- [ ] **Step 1: Generate the controller→Feature map**

```bash
grep -l "AccessRoles\." backend/src/Anela.Heblo.API/Controllers/*.cs | sort > /tmp/controllers-to-sweep.txt
wc -l /tmp/controllers-to-sweep.txt
```

For each controller, look at the class-level `[Authorize(Roles = AccessRoles.X)]` and determine the Feature it maps to. Most controllers use a single feature throughout their methods.

Build a table (record in a scratch file `/tmp/controller-features.txt`):

```
ArticlesController              -> Feature.Marketing_Article
BackgroundJobsController        -> Feature.Admin_Administration
BankStatementsController        -> Feature.Customer_BankStatements
CatalogController               -> Feature.Products_Catalog
CarrierCoolingController        -> Feature.Warehouse_Expedition
DataQualityController           -> Feature.Admin_DataQuality
ExpeditionController            -> Feature.Warehouse_Expedition
ExpeditionSettingsController    -> Feature.Warehouse_Expedition
FeatureFlagsController          -> Feature.Admin_FeatureFlags
... (continue for every controller)
```

- [ ] **Step 2: For each controller file, apply the sweep**

For controller `Foo.cs`:
1. Add the using if missing: `using Anela.Heblo.Domain.Features.Authorization;`
2. Add `[GateOn(Feature.X)]` just above the class declaration (after any other class-level attributes).
3. Replace every `AccessRoles.OldName` with `AccessRoles.NewName` using the mapping table.
4. For methods whose `[Authorize(Roles = ...)]` uses a different feature than the class (e.g. `ArticlesController.BackfillRequestedBy` uses `AccessRoles.AdminAdministrationWrite`), add a method-level `[GateOn(Feature.OtherFeature)]`.

Example — `ArticlesController.cs`:

```csharp
using Anela.Heblo.Domain.Features.Authorization;

[ApiController]
[Route("api/[controller]")]
[GateOn(Feature.Marketing_Article)]
[Authorize(Roles = AccessRoles.MarketingArticleRead)]
public sealed class ArticlesController : BaseApiController
{
    // ... read-level actions inherit class auth ...

    [HttpGet("feedback/list")]
    [Authorize(Roles = AccessRoles.MarketingArticleWrite)]
    public Task<...> FeedbackList(...) { ... }

    [HttpPost("admin/backfill-requested-by")]
    [GateOn(Feature.Admin_Administration)]              // <-- method override
    [Authorize(Roles = AccessRoles.AdminAdministrationWrite)]
    public Task<...> BackfillRequestedBy(...) { ... }
}
```

- [ ] **Step 3: Build the API project**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj 2>&1 | tail -10
```

Expected: `0 Error(s)`. If any reference fails, the constant name mapping above was missed for that file — go back to Step 2 for the affected controller.

- [ ] **Step 4: Commit**

```bash
git add -u backend/src/Anela.Heblo.API/Controllers
git commit -m "refactor(authz): add [GateOn] and migrate controllers to new role constants"
```

---

## Phase 5 — Validation tests

### Task 11: `EveryAuthorizeRole_MatchesGateOn`

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Authorization/GateConsistencyTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GateConsistencyTests
{
    private static IEnumerable<Type> AllControllers()
        => typeof(Anela.Heblo.API.Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    private static IEnumerable<(MemberInfo Owner, AuthorizeAttribute Auth, GateOnAttribute? Gate)> GatedMembers(Type controller)
    {
        var classGate = controller.GetCustomAttribute<GateOnAttribute>();
        foreach (var classAuth in controller.GetCustomAttributes<AuthorizeAttribute>())
            yield return (controller, classAuth, classGate);
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var methodGate = method.GetCustomAttribute<GateOnAttribute>();
            foreach (var methodAuth in method.GetCustomAttributes<AuthorizeAttribute>())
                yield return (method, methodAuth, methodGate ?? classGate);
        }
    }

    [Fact]
    public void EveryAuthorizeRole_MatchesGateOn()
    {
        var problems = new List<string>();
        foreach (var ctl in AllControllers())
        {
            foreach (var (owner, auth, gate) in GatedMembers(ctl))
            {
                foreach (var role in (auth.Roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = role.Trim();
                    if (trimmed == "heblo_user" || trimmed == "super_user") continue;
                    if (!PermissionString.TryParse(trimmed, out var feature, out _))
                    {
                        problems.Add($"{ctl.Name}.{owner.Name}: role '{trimmed}' is not a matrix permission");
                        continue;
                    }
                    if (gate is null)
                    {
                        problems.Add($"{ctl.Name}.{owner.Name}: [Authorize(Roles={trimmed})] without [GateOn]");
                        continue;
                    }
                    if (gate.Feature != feature)
                        problems.Add($"{ctl.Name}.{owner.Name}: [Authorize(Roles={trimmed})] but [GateOn(Feature.{gate.Feature})]");
                }
            }
        }
        problems.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter EveryAuthorizeRole_MatchesGateOn
```

Expected: `Passed: 1`. If it fails, fix the offending controller from the listed problems.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/GateConsistencyTests.cs
git commit -m "test(authz): assert controllers' [Authorize] role matches [GateOn]"
```

### Task 12: `EveryGatedEndpoint_HasGateOn`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/GateConsistencyTests.cs`

- [ ] **Step 1: Append the second test inside `GateConsistencyTests`**

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

- [ ] **Step 2: Run**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter EveryGatedEndpoint_HasGateOn
```

Expected: `Passed: 1`.

- [ ] **Step 3: Commit**

```bash
git add -u backend/test/Anela.Heblo.Tests/Authorization/GateConsistencyTests.cs
git commit -m "test(authz): assert every gated endpoint declares [GateOn]"
```

### Task 13: `EveryMenuPath_PermissionsResolveToKnownRoles`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixTests.cs`

- [ ] **Step 1: Append the test in the `AccessMatrixConsistencyTests` class**

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

- [ ] **Step 2: Run**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter EveryMenuPath_PermissionsResolveToKnownRoles
```

Expected: `Passed: 1`.

- [ ] **Step 3: Commit**

```bash
git add -u backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixTests.cs
git commit -m "test(authz): assert menu path requirements are valid features+levels"
```

### Task 14: `EveryMenuPath_FeatureHasController`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/GateConsistencyTests.cs`

- [ ] **Step 1: Append**

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

- [ ] **Step 2: Run**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter EveryMenuPath_FeatureHasController
```

Expected: `Passed: 1`.

- [ ] **Step 3: Commit**

```bash
git add -u backend/test/Anela.Heblo.Tests/Authorization/GateConsistencyTests.cs
git commit -m "test(authz): assert every menu feature is backed by a controller"
```

### Task 15: Run the full backend test suite

- [ ] **Step 1: Run all authorization tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Authorization"
```

Expected: all passing. Includes existing `AccessMatrixConsistencyTests`, `ControllerAuthorizationCoverageTests`, plus the 4 new tests above.

- [ ] **Step 2: Run the whole test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -10
```

Expected: all passing. If a feature-specific test fails because a role constant changed, update that test to the new constant.

---

## Phase 6 — Frontend swap

### Task 16: Update Sidebar to consume new ACCESS_ROUTES shape

**Files:**
- Modify: `frontend/src/components/Layout/Sidebar.tsx`

Two changes:
1. `canSeeRoute` now checks `req.permissions.every(...)` instead of a single string.
2. Menu items use a single `key` field (the matrix key) instead of `href`/`requiredRole`.

- [ ] **Step 1: Update the `canSee` helper**

Replace the existing `canSeeRoute` block with:

```ts
const canSeeKey = (key: string): boolean => {
  const req = ACCESS_ROUTES[key];
  if (!req) return false;
  return req.permissions.every(p => hasPermission(p));
};
```

Remove the old `canSeeRoute` helper.

- [ ] **Step 2: Refactor menu items to use `key`**

In `allSections`, each sub-item gets a single `key` field. Internal items: `key === href`. External onClick items: `key` is the virtual key from the matrix.

Replace the relevant entries — examples:

```ts
{
  id: "struktura",
  name: "Struktura",
  href: "#",
  key: "#org-chart",
  onClick: openOrgChart,
  isExternal: true,
},
{
  id: "terminal",
  name: "Terminál",
  href: "#",
  key: "#terminal",
  onClick: openTerminal,
  isExternal: true,
},
{
  id: "baleni",
  name: "Balení",
  href: "#",
  key: "#baleni-external",
  onClick: openBaleni,
  isExternal: true,
},
{
  id: "hangfire",
  name: "Hangfire",
  href: "#",
  key: "#hangfire",
  onClick: openHangfireDashboard,
},
```

For every internal item without a `requiredRole`, add `key: <same as href>`. Remove all `requiredRole` fields.

- [ ] **Step 3: Update `canSeeItem`**

Replace with:

```ts
const canSeeItem = (item: { key: string }): boolean => canSeeKey(item.key);
```

- [ ] **Step 4: Build**

```bash
cd frontend && npm run build 2>&1 | tail -10
```

Expected: `Compiled successfully`.

- [ ] **Step 5: Commit**

```bash
git add -u frontend/src/components/Layout/Sidebar.tsx
git commit -m "feat(authz): Sidebar uses MenuRequirement[] for AND-of-permissions gating"
```

### Task 17: Add `RequireMenuPath` route guard

**Files:**
- Create: `frontend/src/components/auth/RequireMenuPath.tsx`
- Test: `frontend/src/components/auth/__tests__/RequireMenuPath.test.tsx`

- [ ] **Step 1: Write failing test**

```tsx
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import RequireMenuPath from '../RequireMenuPath';

let mockPermissions: string[] = [];
let mockIsSuperUser = false;
let mockIsLoading = false;

jest.mock('../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: mockPermissions,
    isSuperUser: mockIsSuperUser,
    groups: [],
    isLoading: mockIsLoading,
    hasPermission: (p: string) => mockIsSuperUser || mockPermissions.includes(p),
  }),
}));

jest.mock('../../../auth/accessMatrix.generated', () => ({
  __esModule: true,
  ACCESS_ROUTES: {
    '/x': { permissions: ['a.b.read', 'c.d.read'] },
  },
}));

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/x" element={
          <RequireMenuPath path="/x"><div data-testid="ok">ok</div></RequireMenuPath>
        } />
        <Route path="/" element={<div data-testid="home">home</div>} />
      </Routes>
    </MemoryRouter>
  );
}

describe('RequireMenuPath', () => {
  afterEach(() => {
    mockPermissions = [];
    mockIsSuperUser = false;
    mockIsLoading = false;
  });

  it('renders children when all permissions held', () => {
    mockPermissions = ['a.b.read', 'c.d.read'];
    renderAt('/x');
    expect(screen.getByTestId('ok')).toBeInTheDocument();
  });

  it('redirects to dashboard when any permission missing', () => {
    mockPermissions = ['a.b.read'];
    renderAt('/x');
    expect(screen.queryByTestId('ok')).not.toBeInTheDocument();
    expect(screen.getByTestId('home')).toBeInTheDocument();
  });

  it('renders for super_user even without explicit permissions', () => {
    mockIsSuperUser = true;
    renderAt('/x');
    expect(screen.getByTestId('ok')).toBeInTheDocument();
  });

  it('renders nothing while permissions are loading', () => {
    mockIsLoading = true;
    renderAt('/x');
    expect(screen.queryByTestId('ok')).not.toBeInTheDocument();
    expect(screen.queryByTestId('home')).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run (expect failure)**

```bash
cd frontend && npm test -- --testPathPattern="RequireMenuPath" --watchAll=false 2>&1 | tail -5
```

Expected: failure ("Cannot find module").

- [ ] **Step 3: Create RequireMenuPath**

```tsx
import React from 'react';
import { Navigate } from 'react-router-dom';
import { usePermissionsContext } from '../../auth/PermissionsContext';
import { ACCESS_ROUTES } from '../../auth/accessMatrix.generated';

interface Props {
  path: string;          // must match a key in ACCESS_ROUTES
  redirectTo?: string;   // default: dashboard
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

- [ ] **Step 4: Run tests**

```bash
cd frontend && npm test -- --testPathPattern="RequireMenuPath" --watchAll=false
```

Expected: `Tests: 4 passed`.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/auth/RequireMenuPath.tsx \
        frontend/src/components/auth/__tests__/RequireMenuPath.test.tsx
git commit -m "feat(authz): add RequireMenuPath route guard"
```

### Task 18: Swap `RequireAccess` for `RequireMenuPath` in App.tsx

**Files:**
- Modify: `frontend/src/App.tsx`
- Delete: `frontend/src/components/auth/RequireAccess.tsx`
- Delete: `frontend/src/components/auth/__tests__/RequireAccess.test.tsx` (if present)

- [ ] **Step 1: List existing usages of RequireAccess**

```bash
grep -n "RequireAccess" frontend/src/App.tsx
```

- [ ] **Step 2: Replace each usage**

For each route using `<RequireAccess requiredRole="x.y.z">`, replace with `<RequireMenuPath path="<route-path>">`. Example:

```tsx
// Before
<Route path="/marketing/feedback" element={
  <RequireAccess requiredRole={AccessRoles.MarketingArticleWrite}>
    <MarketingFeedbackPage />
  </RequireAccess>
} />

// After
<Route path="/marketing/feedback" element={
  <RequireMenuPath path="/marketing/feedback">
    <MarketingFeedbackPage />
  </RequireMenuPath>
} />
```

Update the import:

```tsx
import RequireMenuPath from "./components/auth/RequireMenuPath";
```

Remove the old `import RequireAccess from "./components/auth/RequireAccess";`.

- [ ] **Step 3: Delete `RequireAccess.tsx` and its test (if present)**

```bash
rm -f frontend/src/components/auth/RequireAccess.tsx \
      frontend/src/components/auth/__tests__/RequireAccess.test.tsx
```

- [ ] **Step 4: Build and run all frontend tests**

```bash
cd frontend && npm run build 2>&1 | tail -5 && npm test -- --watchAll=false 2>&1 | tail -10
```

Expected: build succeeds, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -u frontend/src/App.tsx frontend/src/components/auth/
git commit -m "refactor(authz): replace RequireAccess with RequireMenuPath everywhere"
```

### Task 19: `EveryMenuItem_RegisteredInAccessRoutes` frontend test

**Files:**
- Modify: `frontend/src/components/Layout/__tests__/Sidebar.test.tsx`

- [ ] **Step 1: Add the test**

```tsx
it('every rendered menu item key exists in ACCESS_ROUTES', () => {
  mockIsSuperUser = true;
  renderSidebar();

  // Collect every key from rendered menu items. Internal items have a Link with
  // a real href; external items have a button and we identify them by data-key.
  // Easiest: assert against the component's static navigation by walking the DOM
  // for <a href> and reading data-menu-key on buttons (added in Sidebar.tsx).
  const links = screen.getAllByRole('link');
  const buttons = screen.getAllByRole('button');

  const internalKeys = links
    .map(l => l.getAttribute('href'))
    .filter((h): h is string => !!h && h !== '/');

  const externalKeys = buttons
    .map(b => b.getAttribute('data-menu-key'))
    .filter((k): k is string => !!k && k.startsWith('#'));

  const allKeys = [...internalKeys, ...externalKeys];
  expect(allKeys.length).toBeGreaterThan(0);
  const matrixKeys = Object.keys(ACCESS_ROUTES);
  for (const k of allKeys) {
    expect(matrixKeys).toContain(k);
  }
});
```

Add the import at the top:

```ts
import { ACCESS_ROUTES } from '../../../auth/accessMatrix.generated';
```

Remove any stub mock of `accessMatrix.generated` to ensure the real generated map is used.

- [ ] **Step 2: Annotate external-item buttons in Sidebar.tsx**

Add `data-menu-key={item.key}` to the `<button>` rendered for external onClick items so the test can collect them.

- [ ] **Step 3: Run**

```bash
cd frontend && npm test -- --testPathPattern="Sidebar" --watchAll=false
```

Expected: all Sidebar tests pass, new one included.

- [ ] **Step 4: Commit**

```bash
git add -u frontend/src/components/Layout
git commit -m "test(authz): assert every menu item key is registered in ACCESS_ROUTES"
```

### Task 20: `EveryMenuItem_RouteExistsInApp` (smoke check)

This is harder to test mechanically (App.tsx is large). The simpler check that catches drift: a build-time test asserting every non-virtual `MenuPath.Key` is present in `App.tsx`'s source. Since brittleness > value here, defer to manual spot-check or skip.

- [ ] **Step 1: Mark as deferred**

Add a TODO note at the bottom of `Sidebar.test.tsx`:

```ts
// TODO(authz): consider scraping App.tsx for <Route path="..."> to validate
// every non-virtual MenuPath.Key resolves to a real React route. Manual review
// suffices for now.
```

(Skip if you'd rather omit. Not load-bearing.)

- [ ] **Step 2: Commit (optional)**

```bash
git add -u frontend/src/components/Layout/__tests__/Sidebar.test.tsx
git commit -m "docs(authz): note potential route-existence test"
```

---

## Phase 7 — Persistence migration

### Task 21: Update `AuthorizationSeeder` to reconcile existing groups

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationSeeder.cs`

Today the seeder only creates new groups; if a group already exists, its permissions stay as they are. After the rename, existing rows hold stale strings. Make the seeder also reconcile existing groups against the matrix.

- [ ] **Step 1: Replace seeder body**

```csharp
public static async Task SeedAsync(ApplicationDbContext db, CancellationToken ct)
{
    var validPermissions = AccessMatrix.AllPermissionStrings().ToHashSet();
    var matrixGroups = AccessMatrix.Groups.ToDictionary(g => g.Name);

    var existing = await db.PermissionGroups
        .Include(g => g.Permissions)
        .ToListAsync(ct);
    var existingByName = existing.ToDictionary(g => g.Name);

    foreach (var matrixGroup in AccessMatrix.Groups)
    {
        if (!existingByName.TryGetValue(matrixGroup.Name, out var group))
        {
            group = new PermissionGroup
            {
                Id = Guid.NewGuid(),
                Name = matrixGroup.Name,
                Description = matrixGroup.Name,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "system",
            };
            db.PermissionGroups.Add(group);
        }

        var desired = matrixGroup.Roles.Where(validPermissions.Contains).ToHashSet();
        var current = group.Permissions.Select(p => p.PermissionValue).ToHashSet();

        foreach (var add in desired.Except(current))
            group.Permissions.Add(new GroupPermission { GroupId = group.Id, PermissionValue = add });

        foreach (var remove in group.Permissions.Where(p => !desired.Contains(p.PermissionValue)).ToList())
            group.Permissions.Remove(remove);
    }

    var orphans = await db.GroupPermissions
        .Where(p => !validPermissions.Contains(p.PermissionValue))
        .ToListAsync(ct);
    db.GroupPermissions.RemoveRange(orphans);

    await db.SaveChangesAsync(ct);
}
```

- [ ] **Step 2: Build**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj 2>&1 | tail -5
```

Expected: `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add -u backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationSeeder.cs
git commit -m "feat(authz): seeder reconciles existing group permissions to matrix"
```

### Task 22: EF migration to rename existing permission strings

This is a safety net so the first deploy doesn't depend solely on the seeder. The migration rewrites old `feature.level` strings into the new `module.feature.level` format.

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_RenamePermissionStrings.cs` (+ Designer)

- [ ] **Step 1: Generate the migration scaffolding**

```bash
cd backend/src/Anela.Heblo.Persistence && \
dotnet ef migrations add RenamePermissionStrings \
  --startup-project ../Anela.Heblo.API \
  --context ApplicationDbContext
```

- [ ] **Step 2: Replace the migration `Up` body**

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Old format ("feature.level") → new format ("module.feature.level").
    // Mappings mirror Feature enum prefixes.
    var mappings = new (string Old, string New)[]
    {
        ("financial_overview", "finance.financial_overview"),
        ("margin_analysis",    "finance.margin_analysis"),
        ("product_margins",    "products.product_margins"),
        ("catalog",            "products.catalog"),
        ("journal",            "products.journal"),
        ("bank_statements",    "customer.bank_statements"),
        ("knowledge_base",     "customer.knowledge_base"),
        ("smartsupp",          "customer.smartsupp"),
        ("purchase_orders",    "purchase.purchase_orders"),
        ("purchase_stock",     "purchase.purchase_stock"),
        ("manufacture_orders", "manufacture.manufacture_orders"),
        ("batch_planning",     "manufacture.batch_planning"),
        ("manufacture_output", "manufacture.manufacture_output"),
        ("manufacture_stock",  "manufacture.manufacture_stock"),
        ("material_inventory", "manufacture.material_inventory"),
        ("product_inventory",  "manufacture.product_inventory"),
        ("material_containers","manufacture.material_containers"),
        ("logistics",          "warehouse.logistics"),
        ("expedition",         "warehouse.expedition"),
        ("packaging",          "warehouse.packaging"),
        ("stock_up",           "warehouse.stock_up"),
        ("article",            "marketing.article"),
        ("leaflet",            "marketing.leaflet"),
        ("photobank",          "marketing.photobank"),
        ("marketing_calendar", "marketing.marketing_calendar"),
        ("meetings",           "anela.meetings"),
        ("org_chart",          "anela.org_chart"),
        ("data_quality",       "admin.data_quality"),
        ("administration",     "admin.administration"),
        ("feature_flags",      "admin.feature_flags"),
    };

    foreach (var (old, @new) in mappings)
    {
        foreach (var level in new[] { "read", "write", "admin" })
        {
            migrationBuilder.Sql(
                $"UPDATE \"GroupPermissions\" SET \"PermissionValue\" = '{@new}.{level}' WHERE \"PermissionValue\" = '{old}.{level}';");
        }
    }
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    // Reverse mapping
    var mappings = new (string Old, string New)[]
    {
        // (same list as Up, reversed in the SQL)
    };
    // For each pair (old, new), UPDATE WHERE PermissionValue = new.<level> SET = old.<level>.
    // Implementation mirror of Up; copy and reverse.
}
```

(For brevity the `Down` is a mirror of `Up` with old/new swapped — fill in the same mapping list.)

- [ ] **Step 3: Run the migration locally against the staging DB**

The CLAUDE.md says migrations are manual. So just confirm the migration compiles:

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj 2>&1 | tail -5
```

Expected: `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/*RenamePermissionStrings*
git commit -m "feat(authz): migration to rename permission strings to module.feature.level"
```

---

## Phase 8 — Run the whole suite

### Task 23: Backend tests, frontend tests, frontend build, format

- [ ] **Step 1: Backend tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -10
```

Expected: all tests passing.

- [ ] **Step 2: Frontend build**

```bash
cd frontend && npm run build 2>&1 | tail -5
```

Expected: `Compiled successfully`.

- [ ] **Step 3: Frontend tests**

```bash
cd frontend && npm test -- --watchAll=false 2>&1 | tail -10
```

Expected: all tests passing.

- [ ] **Step 4: dotnet format**

```bash
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj 2>&1 | tail -3
dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj 2>&1 | tail -3
```

Expected: no output / no changes (already formatted).

- [ ] **Step 5: If anything was reformatted, commit**

```bash
git add -u
git commit -m "style(authz): dotnet format"
```

---

## Phase 9 — Deploy

These steps are manual (or run from the deploy host). They MUST happen in this order on the same deploy cycle.

- [ ] **Step 1: Push the branch**

```bash
git push -u origin <branch>
```

- [ ] **Step 2: Run the Entra sync script (staging first)**

```bash
./scripts/sync-entra-access.sh --apply
```

This pushes the new app-role catalog with the new role string values.

- [ ] **Step 3: Run the DB migration against staging**

```bash
dotnet ef database update --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API \
  --connection "$STAGING_CONN" \
  RenamePermissionStrings
```

Expected: migration applied, all stale `PermissionValue` rows rewritten.

- [ ] **Step 4: Deploy backend + frontend to staging**

Docker build + push as usual. The seeder runs at startup and reconciles any drift.

- [ ] **Step 5: Smoke test in staging**

Verify:
- Login as a known group member (e.g. `Pracovnik_vyroby`). Sidebar shows only Manufacture + Warehouse items they have permission for.
- Open `/manufacturing/inventory` — page loads without 403 (or 403s on the specific endpoint that's now mis-aligned, which is now caught by the new validators).
- Open `/marketing/feedback` as a `Marketer` (has `marketing.article.write`). Page loads.
- Open `/marketing/feedback` as a user without `.write`. Redirected to dashboard.

- [ ] **Step 6: Promote to production**

Same migration + Entra sync + deploy sequence.

---

## Self-Review

- [x] **Spec coverage:** Every section of the spec maps to a phase: model (P1), registry (P2), `[GateOn]` attribute (P1.T4), generator (P3), controller sweep (P4), validation tests (P5), Sidebar wiring (P6), `RequireMenuPath` (P6.T17), persistence (P7), deploy (P9). ✓
- [x] **Placeholder scan:** No `TBD`/`TODO`/`implement later`. One `TODO` note in Task 20 is explicit and optional. The Task 22 `Down` migration says "mirror of `Up` with old/new swapped" — the full list is in `Up`; the engineer can copy + swap. Acceptable since the data is right there, but tightened by listing the mapping table in `Up`. ✓
- [x] **Type consistency:** Throughout the plan: `Feature` enum values use `Module_Feature` underscored, generated constants use `ModuleFeature` (no underscore), wire strings use `module.feature.level` dot-separated. `MenuPath.Key` is consistent. ✓
- [x] **Scope check:** Single subsystem (authorization). One cohesive plan. ✓
