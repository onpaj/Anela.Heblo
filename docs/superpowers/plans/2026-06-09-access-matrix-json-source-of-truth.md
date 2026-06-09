# Access Matrix: JSON Source of Truth + On-Demand Seeder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `access-matrix.json` the single hand-edited source of truth for the feature catalog, menu paths, and bootstrap groups; generate `Feature` enum + `AccessMatrix` static data + TS/Entra artifacts from it; replace the startup seeder with an on-demand console tool + shell script (STG / PROD); drop the "system vs. custom" group distinction.

**Architecture:** Invert `Anela.Heblo.AccessMatrixGen` so the JSON drives the C# enum/data class generation instead of the other way around. Groups exist only as DB rows; the JSON's `seedGroups` is consumed exclusively by an on-demand seeder that runs `insert-if-missing` by default and supports `--reset-group <Name>` for targeted restores. Connection strings flow from Azure Key Vault (`kv-heblo-stg`, `kv-heblo-prod`) via `DefaultAzureCredential`.

**Tech Stack:** .NET 8 / EF Core 8 / Npgsql + pgvector / Azure Key Vault (`Azure.Identity`, `Azure.Extensions.AspNetCore.Configuration.Secrets`) / xUnit + FluentAssertions / Vitest.

---

## File Structure

**New (canonical source):**
- `access-matrix.json` — hand-edited, repo root.

**New (Domain — JSON deserialization DTOs):**
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrixManifest.cs` — records for deserialization, used by generator, seeder tool, and tests.

**Generated (replaces hand-written):**
- `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs` — emitted by generator; replaces `Feature.cs`.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs` — emitted by generator; replaces `AccessMatrix.cs`. **Does not contain `Groups`.**

**Generated (unchanged target, regenerated from JSON):**
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs`
- `frontend/src/auth/accessMatrix.generated.ts`
- `access-matrix-entra.generated.json` (renamed from `access-matrix.generated.json`)

**New (seeder tool):**
- `backend/tools/Anela.Heblo.AuthorizationSeeder/Anela.Heblo.AuthorizationSeeder.csproj`
- `backend/tools/Anela.Heblo.AuthorizationSeeder/Program.cs`
- `backend/tools/Anela.Heblo.AuthorizationSeeder/appsettings.json`
- `backend/tools/Anela.Heblo.AuthorizationSeeder/appsettings.Staging.json`
- `backend/tools/Anela.Heblo.AuthorizationSeeder/appsettings.Production.json`

**New (Persistence — replaces destructive seeder):**
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/JsonGroupSeeder.cs`

**New (shell wrapper):**
- `scripts/seed-authorization.sh`

**New (tests):**
- `backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixJsonTests.cs`
- `backend/test/Anela.Heblo.Tests/Authorization/JsonGroupSeederTests.cs`
- `frontend/src/auth/__tests__/accessMatrixConsistency.test.ts`

**Modify:**
- `backend/tools/Anela.Heblo.AccessMatrixGen/Program.cs` — read JSON instead of `AccessMatrix` static class; emit two more files.
- `backend/tools/Anela.Heblo.AccessMatrixGen/Anela.Heblo.AccessMatrixGen.csproj` — depend on Domain only for record types.
- `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs:325` — remove startup seed call.
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPermissionCatalogue/GetPermissionCatalogueResponse.cs` — remove `SystemGroups` field.
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPermissionCatalogue/GetPermissionCatalogueHandler.cs` — drop the `SystemGroups = ...` line.
- `backend/test/Anela.Heblo.Tests/Authorization/GetPermissionCatalogueHandlerTests.cs` — drop `SystemGroups` assertion.
- `frontend/src/pages/__tests__/GroupDetailPage.test.tsx` — drop the `systemGroups: []` field (after API client regen).
- `scripts/sync-entra-access.sh` — point at new manifest filename.
- `scripts/cleanup-entra.sh` — point at new manifest filename.

**Delete:**
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.cs`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.cs`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessModels.cs` — drop `AccessGroup` record (unused after `AccessMatrix.Groups` removal); keep `AccessRoleDefinition` by moving it to `AccessMatrixManifest.cs` (or its own file).
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationSeeder.cs`
- `backend/test/Anela.Heblo.Tests/Authorization/AuthorizationSeederTests.cs`
- `access-matrix.generated.json` (renamed; old name removed).

---

## Phase 0: Bootstrap canonical JSON

### Task 0.1: Add `AccessMatrixManifest` DTOs in Domain

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrixManifest.cs`

- [ ] **Step 1: Create the manifest DTOs**

Create `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrixManifest.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// Deserialization shape for <c>access-matrix.json</c>. The JSON is the single
/// hand-edited source of truth for the feature catalog, menu paths, and the
/// bootstrap group list consumed by the on-demand seeder. The runtime
/// <see cref="AccessMatrix"/> static class is generated from this manifest by
/// <c>Anela.Heblo.AccessMatrixGen</c>.
/// </summary>
public sealed record AccessMatrixManifest(
    string BaseRole,
    IReadOnlyList<FeatureEntry> Features,
    IReadOnlyList<MenuPathEntry> MenuPaths,
    IReadOnlyList<SeedGroupEntry> SeedGroups);

public sealed record FeatureEntry(
    string Key,
    string Label,
    bool HasWrite = false,
    bool HasAdmin = false);

public sealed record MenuPathEntry(
    string Path,
    IReadOnlyList<MenuPathRequirementEntry> Requires);

public sealed record MenuPathRequirementEntry(
    string Feature,
    string Level);

public sealed record SeedGroupEntry(
    string Name,
    IReadOnlyList<string> Roles);
```

- [ ] **Step 2: Verify build**

Run: `cd backend && dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj 2>&1 | tail -5`
Expected: `Build succeeded` (zero errors).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrixManifest.cs
git commit -m "feat(authz): add AccessMatrixManifest DTOs for JSON source of truth"
```

---

### Task 0.2: Write the canonical `access-matrix.json`

**Files:**
- Create: `access-matrix.json` (repo root)

- [ ] **Step 1: Create `access-matrix.json` at the repo root**

The content is a direct port of today's `AccessMatrix.cs` (52 features, 47 menu paths, 11 groups). Use this exact content:

```json
{
  "baseRole": "heblo_user",
  "features": [
    { "key": "Finance_FinancialOverview", "label": "Finanční přehled" },
    { "key": "Finance_MarginAnalysis", "label": "Analýza marží" },
    { "key": "Products_ProductMargins", "label": "Marže produktů" },
    { "key": "Products_Catalog", "label": "Katalog", "hasWrite": true },
    { "key": "Products_Journal", "label": "Žurnál", "hasWrite": true },
    { "key": "Customer_BankStatements", "label": "Bankovní výpisy" },
    { "key": "Customer_KnowledgeBase", "label": "Znalostní báze", "hasWrite": true },
    { "key": "Customer_Smartsupp", "label": "Smartsupp" },
    { "key": "Purchase_PurchaseOrders", "label": "Nákupní objednávky", "hasWrite": true },
    { "key": "Purchase_PurchaseStock", "label": "Analýza skladu (nákup)" },
    { "key": "Manufacture_ManufactureOrders", "label": "Výrobní objednávky", "hasWrite": true },
    { "key": "Manufacture_BatchPlanning", "label": "Plánování šarží", "hasWrite": true },
    { "key": "Manufacture_ManufactureOutput", "label": "Výstup výroby" },
    { "key": "Manufacture_ManufactureStock", "label": "Analýza skladu (výroba)" },
    { "key": "Manufacture_MaterialInventory", "label": "Inventura materiálu", "hasWrite": true },
    { "key": "Manufacture_ProductInventory", "label": "Inventura produktů", "hasWrite": true },
    { "key": "Manufacture_MaterialContainers", "label": "Kontejnery materiálu", "hasWrite": true },
    { "key": "Warehouse_Logistics", "label": "Logistika", "hasWrite": true },
    { "key": "Warehouse_Expedition", "label": "Expedice", "hasWrite": true },
    { "key": "Warehouse_Packaging", "label": "Balení", "hasWrite": true },
    { "key": "Warehouse_StockUp", "label": "Naskladnění", "hasWrite": true },
    { "key": "Marketing_Article", "label": "Články", "hasWrite": true },
    { "key": "Marketing_Leaflet", "label": "Generátor letáků", "hasWrite": true },
    { "key": "Marketing_Photobank", "label": "Fotobanka", "hasWrite": true, "hasAdmin": true },
    { "key": "Marketing_MarketingCalendar", "label": "Marketingový kalendář", "hasWrite": true },
    { "key": "Anela_Meetings", "label": "Schůzky", "hasWrite": true },
    { "key": "Anela_OrgChart", "label": "Organigram" },
    { "key": "Admin_DataQuality", "label": "Kvalita dat", "hasWrite": true },
    { "key": "Admin_Administration", "label": "Administrace", "hasWrite": true },
    { "key": "Admin_FeatureFlags", "label": "Feature příznaky", "hasWrite": true }
  ],
  "menuPaths": [
    { "path": "/finance/overview", "requires": [{ "feature": "Finance_FinancialOverview", "level": "Read" }] },
    { "path": "/analytics/product-margin-summary", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
    { "path": "/products/margins", "requires": [{ "feature": "Products_ProductMargins", "level": "Read" }] },
    { "path": "/catalog", "requires": [{ "feature": "Products_Catalog", "level": "Read" }] },
    { "path": "/journal", "requires": [{ "feature": "Products_Journal", "level": "Read" }] },
    { "path": "/customer/issued-invoices", "requires": [{ "feature": "Customer_BankStatements", "level": "Read" }] },
    { "path": "/customer/bank-statements-overview", "requires": [{ "feature": "Customer_BankStatements", "level": "Read" }] },
    { "path": "/knowledge-base", "requires": [{ "feature": "Customer_KnowledgeBase", "level": "Read" }] },
    { "path": "/customer/smartsupp", "requires": [{ "feature": "Customer_Smartsupp", "level": "Read" }] },
    { "path": "/purchase/orders", "requires": [{ "feature": "Purchase_PurchaseOrders", "level": "Read" }] },
    { "path": "/purchase/invoice-classification", "requires": [{ "feature": "Purchase_PurchaseOrders", "level": "Read" }] },
    { "path": "/purchase/stock-analysis", "requires": [{ "feature": "Purchase_PurchaseStock", "level": "Read" }] },
    { "path": "/manufacturing/orders", "requires": [{ "feature": "Manufacture_ManufactureOrders", "level": "Read" }] },
    { "path": "/manufacturing/batch-planning", "requires": [{ "feature": "Manufacture_BatchPlanning", "level": "Read" }] },
    { "path": "/manufacturing/batch-calculator", "requires": [{ "feature": "Manufacture_BatchPlanning", "level": "Read" }] },
    { "path": "/manufacturing/output", "requires": [{ "feature": "Manufacture_ManufactureOutput", "level": "Read" }] },
    { "path": "/manufacturing/stock-analysis", "requires": [{ "feature": "Manufacture_ManufactureStock", "level": "Read" }] },
    { "path": "/manufacturing/inventory", "requires": [{ "feature": "Manufacture_MaterialInventory", "level": "Read" }] },
    { "path": "/manufacturing/product-inventory", "requires": [{ "feature": "Manufacture_ProductInventory", "level": "Read" }] },
    { "path": "/manufacturing/material-containers", "requires": [{ "feature": "Manufacture_MaterialContainers", "level": "Read" }] },
    { "path": "/logistics/inventory", "requires": [{ "feature": "Warehouse_Logistics", "level": "Read" }] },
    { "path": "/logistics/receive-boxes", "requires": [{ "feature": "Warehouse_Logistics", "level": "Read" }] },
    { "path": "/logistics/transport-boxes", "requires": [{ "feature": "Warehouse_Logistics", "level": "Read" }] },
    { "path": "/logistics/packing-materials", "requires": [{ "feature": "Warehouse_Logistics", "level": "Read" }] },
    { "path": "/logistics/expedition-archive", "requires": [{ "feature": "Warehouse_Expedition", "level": "Read" }] },
    { "path": "/customer/expedition-settings", "requires": [{ "feature": "Warehouse_Expedition", "level": "Read" }] },
    { "path": "/baleni", "requires": [{ "feature": "Warehouse_Packaging", "level": "Read" }] },
    { "path": "/logistics/gift-package-manufacturing", "requires": [{ "feature": "Warehouse_Packaging", "level": "Read" }] },
    { "path": "/stock-up-operations", "requires": [{ "feature": "Warehouse_StockUp", "level": "Read" }] },
    { "path": "/articles", "requires": [{ "feature": "Marketing_Article", "level": "Read" }] },
    { "path": "/marketing/feedback", "requires": [{ "feature": "Marketing_Article", "level": "Write" }] },
    { "path": "/leaflet-generator", "requires": [{ "feature": "Marketing_Leaflet", "level": "Read" }] },
    { "path": "/marketing/photobank", "requires": [{ "feature": "Marketing_Photobank", "level": "Read" }] },
    { "path": "/marketing/calendar", "requires": [{ "feature": "Marketing_MarketingCalendar", "level": "Read" }] },
    { "path": "/automation/meeting-tasks", "requires": [{ "feature": "Anela_Meetings", "level": "Read" }] },
    { "path": "#org-chart", "requires": [{ "feature": "Anela_OrgChart", "level": "Read" }] },
    { "path": "/automation/data-quality", "requires": [{ "feature": "Admin_DataQuality", "level": "Read" }] },
    { "path": "/admin/access", "requires": [{ "feature": "Admin_Administration", "level": "Read" }] },
    { "path": "/admin/access/groups/:id", "requires": [{ "feature": "Admin_Administration", "level": "Read" }] },
    { "path": "/admin/access/users/:id", "requires": [{ "feature": "Admin_Administration", "level": "Read" }] },
    { "path": "/automation/background-tasks", "requires": [{ "feature": "Admin_Administration", "level": "Read" }] },
    { "path": "/recurring-jobs", "requires": [{ "feature": "Admin_Administration", "level": "Read" }] },
    { "path": "#hangfire", "requires": [{ "feature": "Admin_Administration", "level": "Read" }] },
    { "path": "/admin/feature-flags", "requires": [{ "feature": "Admin_FeatureFlags", "level": "Read" }] },
    { "path": "#terminal", "requires": [{ "feature": "Warehouse_Logistics", "level": "Read" }] },
    { "path": "#baleni-external", "requires": [{ "feature": "Warehouse_Packaging", "level": "Read" }] }
  ],
  "seedGroups": [
    { "name": "Spravce", "roles": ["finance.financial_overview.read", "finance.margin_analysis.read", "products.product_margins.read", "products.catalog.read", "products.catalog.write", "products.journal.read", "products.journal.write", "customer.bank_statements.read", "customer.knowledge_base.read", "customer.knowledge_base.write", "customer.smartsupp.read", "purchase.purchase_orders.read", "purchase.purchase_orders.write", "purchase.purchase_stock.read", "manufacture.manufacture_orders.read", "manufacture.manufacture_orders.write", "manufacture.batch_planning.read", "manufacture.batch_planning.write", "manufacture.manufacture_output.read", "manufacture.manufacture_stock.read", "manufacture.material_inventory.read", "manufacture.material_inventory.write", "manufacture.product_inventory.read", "manufacture.product_inventory.write", "manufacture.material_containers.read", "manufacture.material_containers.write", "warehouse.logistics.read", "warehouse.logistics.write", "warehouse.expedition.read", "warehouse.expedition.write", "warehouse.packaging.read", "warehouse.packaging.write", "warehouse.stock_up.read", "warehouse.stock_up.write", "marketing.article.read", "marketing.article.write", "marketing.leaflet.read", "marketing.leaflet.write", "marketing.photobank.read", "marketing.photobank.write", "marketing.photobank.admin", "marketing.marketing_calendar.read", "marketing.marketing_calendar.write", "anela.meetings.read", "anela.meetings.write", "anela.org_chart.read", "admin.data_quality.read", "admin.data_quality.write", "admin.administration.read", "admin.administration.write", "admin.feature_flags.read", "admin.feature_flags.write"] },
    { "name": "Vedeni", "roles": ["finance.financial_overview.read", "finance.margin_analysis.read", "products.product_margins.read", "products.catalog.read", "products.journal.read", "customer.bank_statements.read", "customer.knowledge_base.read", "customer.smartsupp.read", "purchase.purchase_orders.read", "purchase.purchase_stock.read", "manufacture.manufacture_orders.read", "manufacture.batch_planning.read", "manufacture.manufacture_output.read", "manufacture.manufacture_stock.read", "manufacture.material_inventory.read", "manufacture.product_inventory.read", "manufacture.material_containers.read", "warehouse.logistics.read", "warehouse.expedition.read", "warehouse.packaging.read", "warehouse.stock_up.read", "marketing.article.read", "marketing.leaflet.read", "marketing.photobank.read", "marketing.marketing_calendar.read", "anela.meetings.read", "anela.org_chart.read", "admin.data_quality.read", "admin.administration.read", "admin.feature_flags.read"] },
    { "name": "Ucetni", "roles": ["finance.financial_overview.read", "finance.margin_analysis.read", "products.product_margins.read", "customer.bank_statements.read", "products.journal.read", "products.journal.write", "customer.smartsupp.read"] },
    { "name": "Marketer", "roles": ["marketing.marketing_calendar.read", "marketing.marketing_calendar.write", "marketing.photobank.read", "marketing.photobank.write", "marketing.leaflet.read", "marketing.leaflet.write", "marketing.article.read", "marketing.article.write"] },
    { "name": "Nakupci", "roles": ["purchase.purchase_orders.read", "purchase.purchase_orders.write", "purchase.purchase_stock.read"] },
    { "name": "Vedouci_vyroby", "roles": ["manufacture.manufacture_orders.read", "manufacture.manufacture_orders.write", "manufacture.manufacture_output.read", "manufacture.manufacture_stock.read", "manufacture.batch_planning.read", "manufacture.batch_planning.write", "manufacture.material_inventory.read", "manufacture.material_inventory.write", "manufacture.material_containers.read", "manufacture.material_containers.write", "manufacture.product_inventory.read", "manufacture.product_inventory.write"] },
    { "name": "Pracovnik_vyroby", "roles": ["manufacture.manufacture_orders.read", "manufacture.material_inventory.read", "manufacture.material_inventory.write", "manufacture.product_inventory.read", "manufacture.material_containers.read"] },
    { "name": "Vedouci_skladu", "roles": ["anela.meetings.read", "anela.meetings.write", "products.catalog.write", "products.journal.read", "products.journal.write", "warehouse.expedition.write", "warehouse.logistics.write", "warehouse.stock_up.write"] },
    { "name": "Skladnik", "roles": ["products.catalog.read", "warehouse.logistics.read", "warehouse.expedition.read", "warehouse.packaging.read", "warehouse.packaging.write", "warehouse.stock_up.read", "warehouse.stock_up.write"] },
    { "name": "Poradenstvi", "roles": ["customer.knowledge_base.read", "customer.knowledge_base.write"] },
    { "name": "AccessManager", "roles": ["admin.administration.read", "admin.administration.write"] },
    { "name": "Zamestnanec", "roles": ["anela.org_chart.read", "products.catalog.read"] }
  ]
}
```

> NOTE: This is a verbatim port of `AccessMatrix.cs`'s in-memory data. Do not edit the values here — task 1.5 verifies parity by regenerating the existing TS/CS outputs from this JSON and diffing them against the current generated files (expect no diff). Editing values would break that parity check.

- [ ] **Step 2: Commit**

```bash
git add access-matrix.json
git commit -m "feat(authz): add canonical access-matrix.json (1:1 port of AccessMatrix.cs)"
```

---

### Task 0.3: Add `AccessMatrixJsonTests` to validate the JSON

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixJsonTests.cs`

- [ ] **Step 1: Write the validation tests**

Create `backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixJsonTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

/// <summary>Schema-level validation of access-matrix.json (the hand-edited
/// source of truth). These tests run against the file in the repo root,
/// resolved relative to the test project.</summary>
public class AccessMatrixJsonTests
{
    private static AccessMatrixManifest LoadManifest()
    {
        // Repo root sits four levels above the test bin output:
        // bin/Debug/net8.0 → test/Anela.Heblo.Tests → test → backend → <repo root>
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "access-matrix.json"));

        path.Should().EndWith("access-matrix.json");
        File.Exists(path).Should().BeTrue($"expected access-matrix.json at {path}");

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<AccessMatrixManifest>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("Failed to deserialize access-matrix.json");
    }

    [Fact]
    public void Json_Deserializes_AndPopulatesAllSections()
    {
        var m = LoadManifest();

        m.BaseRole.Should().Be("heblo_user");
        m.Features.Should().NotBeEmpty();
        m.MenuPaths.Should().NotBeEmpty();
        m.SeedGroups.Should().NotBeEmpty();
    }

    [Fact]
    public void Json_HasNoDuplicateFeatureKeys()
    {
        var m = LoadManifest();
        var dupes = m.Features.GroupBy(f => f.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        dupes.Should().BeEmpty();
    }

    [Fact]
    public void Json_HasNoDuplicateSeedGroupNames()
    {
        var m = LoadManifest();
        var dupes = m.SeedGroups.GroupBy(g => g.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        dupes.Should().BeEmpty();
    }

    [Fact]
    public void MenuPath_Requirements_ReferenceKnownFeatures()
    {
        var m = LoadManifest();
        var knownFeatures = m.Features.Select(f => f.Key).ToHashSet();

        var unknown = m.MenuPaths
            .SelectMany(mp => mp.Requires.Select(r => (mp.Path, r.Feature)))
            .Where(x => !knownFeatures.Contains(x.Feature))
            .ToList();

        unknown.Should().BeEmpty($"menuPaths reference features not declared in features[]: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void MenuPath_Requirements_HaveValidLevels()
    {
        var m = LoadManifest();
        var validLevels = new[] { "Read", "Write", "Admin" };

        var invalid = m.MenuPaths
            .SelectMany(mp => mp.Requires.Select(r => (mp.Path, r.Level)))
            .Where(x => !validLevels.Contains(x.Level))
            .ToList();

        invalid.Should().BeEmpty();
    }

    [Fact]
    public void SeedGroup_Roles_AreValidPermissionStrings()
    {
        var m = LoadManifest();
        var knownFeatures = m.Features.ToDictionary(f => f.Key);

        foreach (var group in m.SeedGroups)
        {
            foreach (var role in group.Roles)
            {
                var parts = role.Split('.');
                parts.Length.Should().Be(3, $"role '{role}' in group '{group.Name}' is not 'module.feature.level'");

                var level = parts[2];
                level.Should().BeOneOf("read", "write", "admin");

                // Reconstruct PascalCase feature key from snake_case module + feature segments.
                var pascalModule = ToPascal(parts[0]);
                var pascalFeature = ToPascal(parts[1]);
                var featureKey = $"{pascalModule}_{pascalFeature}";

                knownFeatures.Should().ContainKey(featureKey,
                    $"role '{role}' in group '{group.Name}' references unknown feature '{featureKey}'");

                var f = knownFeatures[featureKey];
                if (level == "write") f.HasWrite.Should().BeTrue(
                    $"role '{role}' uses write level but feature '{featureKey}' is read-only");
                if (level == "admin") f.HasAdmin.Should().BeTrue(
                    $"role '{role}' uses admin level but feature '{featureKey}' lacks hasAdmin");
            }
        }

        static string ToPascal(string snake)
        {
            var sb = new System.Text.StringBuilder();
            bool upper = true;
            foreach (var c in snake)
            {
                if (c == '_') { upper = true; continue; }
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 2: Run the tests, expect them to pass against the just-written JSON**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~AccessMatrixJsonTests --no-restore 2>&1 | tail -20`
Expected: All six tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixJsonTests.cs
git commit -m "test(authz): add schema validation for access-matrix.json"
```

---

## Phase 1: Invert AccessMatrixGen (additive — keeps hand-written files temporarily)

### Task 1.1: Rewrite `AccessMatrixGen` to read from JSON

**Files:**
- Modify: `backend/tools/Anela.Heblo.AccessMatrixGen/Program.cs`
- Modify: `backend/tools/Anela.Heblo.AccessMatrixGen/Anela.Heblo.AccessMatrixGen.csproj`

The new generator reads `access-matrix.json` and emits five files:
1. `Feature.generated.cs` (the enum — will replace `Feature.cs`)
2. `AccessMatrix.generated.cs` (the data class — will replace `AccessMatrix.cs`)
3. `AccessRoles.generated.cs` (unchanged role-string constants)
4. `accessMatrix.generated.ts` (unchanged TS for the FE)
5. `access-matrix-entra.generated.json` (renamed Entra manifest)

In this task, the new generator writes files alongside (not replacing) the hand-written `Feature.cs` and `AccessMatrix.cs`. The cleanup happens in Phase 2.

- [ ] **Step 1: Replace `Program.cs` with the JSON-driven generator**

Overwrite `backend/tools/Anela.Heblo.AccessMatrixGen/Program.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Authorization;

// Args:
//   [0] = path to access-matrix.json     (default: ./access-matrix.json)
//   [1] = path to Feature.generated.cs
//   [2] = path to AccessMatrix.generated.cs
//   [3] = path to AccessRoles.generated.cs
//   [4] = path to accessMatrix.generated.ts
//   [5] = path to access-matrix-entra.generated.json
var manifestPath = args.ElementAtOrDefault(0) ?? "access-matrix.json";
var featureEnumPath = args.ElementAtOrDefault(1)
    ?? "backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs";
var matrixDataPath = args.ElementAtOrDefault(2)
    ?? "backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs";
var rolesPath = args.ElementAtOrDefault(3)
    ?? "backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs";
var tsPath = args.ElementAtOrDefault(4)
    ?? "frontend/src/auth/accessMatrix.generated.ts";
var entraPath = args.ElementAtOrDefault(5) ?? "access-matrix-entra.generated.json";

if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"access-matrix.json not found at '{manifestPath}'");
    return 1;
}

var json = File.ReadAllText(manifestPath);
var manifest = JsonSerializer.Deserialize<AccessMatrixManifest>(json, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
}) ?? throw new InvalidOperationException("Failed to deserialize access-matrix.json");

// ---------- 1. Feature.generated.cs ----------
var enumSb = new StringBuilder();
enumSb.AppendLine("// AUTO-GENERATED by Anela.Heblo.AccessMatrixGen. Do not edit by hand.");
enumSb.AppendLine("namespace Anela.Heblo.Domain.Features.Authorization;");
enumSb.AppendLine();
enumSb.AppendLine("/// <summary>Module-prefixed feature identifier. Each value generates one or more");
enumSb.AppendLine("/// permission strings (feature.read / .write / .admin) based on the feature");
enumSb.AppendLine("/// definition in <see cref=\"AccessMatrix\"/>.</summary>");
enumSb.AppendLine("public enum Feature");
enumSb.AppendLine("{");
foreach (var f in manifest.Features)
    enumSb.AppendLine($"    {f.Key},");
enumSb.AppendLine("}");
WriteFile(featureEnumPath, enumSb.ToString());

// ---------- 2. AccessMatrix.generated.cs ----------
var matrixSb = new StringBuilder();
matrixSb.AppendLine("// AUTO-GENERATED by Anela.Heblo.AccessMatrixGen. Do not edit by hand.");
matrixSb.AppendLine("namespace Anela.Heblo.Domain.Features.Authorization;");
matrixSb.AppendLine();
matrixSb.AppendLine("public static class AccessMatrix");
matrixSb.AppendLine("{");
matrixSb.AppendLine($"    public const string BaseRole = \"{manifest.BaseRole}\";");
matrixSb.AppendLine();
matrixSb.AppendLine("    public static readonly IReadOnlyList<FeatureDefinition> Features = new[]");
matrixSb.AppendLine("    {");
foreach (var f in manifest.Features)
{
    var args = new List<string> { $"Feature.{f.Key}", Escape(f.Label) };
    if (f.HasWrite) args.Add("HasWrite: true");
    if (f.HasAdmin) args.Add("HasAdmin: true");
    matrixSb.AppendLine($"        new FeatureDefinition({string.Join(", ", args)}),");
}
matrixSb.AppendLine("    };");
matrixSb.AppendLine();
matrixSb.AppendLine("    public static readonly IReadOnlyList<MenuPath> MenuPaths = new[]");
matrixSb.AppendLine("    {");
foreach (var mp in manifest.MenuPaths)
{
    var reqs = string.Join(", ", mp.Requires.Select(r =>
        $"new FeaturePermission(Feature.{r.Feature}, AccessLevel.{r.Level})"));
    matrixSb.AppendLine($"        new MenuPath({Escape(mp.Path)}, new FeaturePermission[] {{ {reqs} }}),");
}
matrixSb.AppendLine("    };");
matrixSb.AppendLine();
matrixSb.AppendLine("    public static IEnumerable<AccessRoleDefinition> Roles()");
matrixSb.AppendLine("    {");
matrixSb.AppendLine("        foreach (var f in Features)");
matrixSb.AppendLine("        {");
matrixSb.AppendLine("            yield return new AccessRoleDefinition(");
matrixSb.AppendLine("                PermissionString.Format(f.Key, AccessLevel.Read), f.Key.ToString(), AccessLevel.Read);");
matrixSb.AppendLine("            if (f.HasWrite)");
matrixSb.AppendLine("                yield return new AccessRoleDefinition(");
matrixSb.AppendLine("                    PermissionString.Format(f.Key, AccessLevel.Write), f.Key.ToString(), AccessLevel.Write);");
matrixSb.AppendLine("            if (f.HasAdmin)");
matrixSb.AppendLine("                yield return new AccessRoleDefinition(");
matrixSb.AppendLine("                    PermissionString.Format(f.Key, AccessLevel.Admin), f.Key.ToString(), AccessLevel.Admin);");
matrixSb.AppendLine("        }");
matrixSb.AppendLine("    }");
matrixSb.AppendLine();
matrixSb.AppendLine("    public static IEnumerable<string> AllPermissionStrings() => Roles().Select(r => r.Value);");
matrixSb.AppendLine("    public static IEnumerable<string> AllRoleValues() => AllPermissionStrings();");
matrixSb.AppendLine("}");
WriteFile(matrixDataPath, matrixSb.ToString());

// ---------- 3. AccessRoles.generated.cs ----------
// We must compute role values here without depending on the (regenerated) AccessMatrix.
var roles = manifest.Features.SelectMany(f => RolesFor(f)).ToList();
var rolesSb = new StringBuilder();
rolesSb.AppendLine("// AUTO-GENERATED by Anela.Heblo.AccessMatrixGen. Do not edit by hand.");
rolesSb.AppendLine("namespace Anela.Heblo.Domain.Features.Authorization;");
rolesSb.AppendLine();
rolesSb.AppendLine("public static class AccessRoles");
rolesSb.AppendLine("{");
rolesSb.AppendLine($"    public const string Base = \"{manifest.BaseRole}\";");
rolesSb.AppendLine("    public const string SuperUser = \"super_user\";");
rolesSb.AppendLine();
foreach (var (value, featureKey, level) in roles)
{
    var suffix = featureKey.Replace("_", "");
    var levelName = level;
    rolesSb.AppendLine($"    public const string {suffix}{levelName} = \"{value}\";");
}
rolesSb.AppendLine();
rolesSb.AppendLine("    public static string For(Feature feature, AccessLevel level) => (feature, level) switch");
rolesSb.AppendLine("    {");
foreach (var (value, featureKey, level) in roles)
{
    var suffix = featureKey.Replace("_", "");
    rolesSb.AppendLine($"        (Feature.{featureKey}, AccessLevel.{level}) => {suffix}{level},");
}
rolesSb.AppendLine("        _ => throw new ArgumentOutOfRangeException(nameof(feature), $\"Feature.{feature} does not support AccessLevel.{level}\")");
rolesSb.AppendLine("    };");
rolesSb.AppendLine("}");
WriteFile(rolesPath, rolesSb.ToString());

// ---------- 4. accessMatrix.generated.ts ----------
var tsSb = new StringBuilder();
tsSb.AppendLine("// AUTO-GENERATED by Anela.Heblo.AccessMatrixGen. Do not edit by hand.");
tsSb.AppendLine("export interface MenuRequirement { permissions: string[]; }");
tsSb.AppendLine("export const ACCESS_ROUTES: Record<string, MenuRequirement> = {");
foreach (var mp in manifest.MenuPaths)
{
    var perms = string.Join(", ", mp.Requires.Select(r =>
        $"\"{RoleStringFor(r.Feature, r.Level)}\""));
    tsSb.AppendLine($"  \"{mp.Path}\": {{ permissions: [{perms}] }},");
}
tsSb.AppendLine("};");
tsSb.AppendLine();
tsSb.AppendLine("export const ACCESS_ROLES = [");
foreach (var (value, _, _) in roles)
    tsSb.AppendLine($"  \"{value}\",");
tsSb.AppendLine("] as const;");
WriteFile(tsPath, tsSb.ToString());

// ---------- 5. access-matrix-entra.generated.json ----------
var featureRoles = roles.Select(r => new
{
    id = DeterministicGuid.ForRole(r.Value).ToString(),
    allowedMemberTypes = new[] { "User" },
    description = $"{r.FeatureKey} {r.Level}",
    displayName = r.Value,
    isEnabled = true,
    value = r.Value,
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
    },
};
var entraManifest = new
{
    appRoles = superUserRole.Concat(featureRoles),
    groups = manifest.SeedGroups.Select(g => new { name = g.Name, roles = g.Roles }),
};
WriteFile(entraPath, JsonSerializer.Serialize(entraManifest, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Wrote {featureEnumPath}, {matrixDataPath}, {rolesPath}, {tsPath}, {entraPath}");
return 0;

static void WriteFile(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
    File.WriteAllText(path, content);
}

static string Escape(string s) => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

static string RoleStringFor(string featureKey, string levelName)
{
    var parts = featureKey.Split('_', 2);
    var module = ToSnake(parts[0]);
    var feature = ToSnake(parts[1]);
    var level = levelName.ToLowerInvariant();
    return $"{module}.{feature}.{level}";
}

static string ToSnake(string pascal)
{
    var sb = new StringBuilder();
    for (var i = 0; i < pascal.Length; i++)
    {
        if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
        sb.Append(char.ToLowerInvariant(pascal[i]));
    }
    return sb.ToString();
}

static IEnumerable<(string Value, string FeatureKey, string Level)> RolesFor(FeatureEntry f)
{
    yield return (RoleStringFor(f.Key, "Read"), f.Key, "Read");
    if (f.HasWrite) yield return (RoleStringFor(f.Key, "Write"), f.Key, "Write");
    if (f.HasAdmin) yield return (RoleStringFor(f.Key, "Admin"), f.Key, "Admin");
}
```

- [ ] **Step 2: Verify the generator csproj already references Domain**

Read `backend/tools/Anela.Heblo.AccessMatrixGen/Anela.Heblo.AccessMatrixGen.csproj` and confirm it has `<ProjectReference Include="../../src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj" />`. It already does — no change needed.

- [ ] **Step 3: Run the generator against the new JSON**

Run from the repo root:
```bash
cd /Users/pajgrtondrej/orca/workspaces/Anela.Heblo/cuttlefish && \
dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen -- \
  access-matrix.json \
  backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs \
  frontend/src/auth/accessMatrix.generated.ts \
  access-matrix-entra.generated.json 2>&1 | tail -10
```
Expected: `Wrote ...` line, exit code 0. Six output files exist.

- [ ] **Step 4: Build, expecting compile errors**

Run: `cd backend && dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj 2>&1 | tail -10`
Expected: **errors** — the hand-written `Feature.cs` and `AccessMatrix.cs` still exist, so the `Feature` enum and `AccessMatrix` class are now declared twice. This is expected. Phase 2 deletes the hand-written files.

> Do not commit yet. The build is broken intentionally; the fix lands in Phase 2 to keep the diff coherent.

---

### Task 1.2: Verify regenerated outputs match committed FE/AccessRoles files

**Files (read-only checks):**
- `frontend/src/auth/accessMatrix.generated.ts`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs`

- [ ] **Step 1: Show the diff of the previously committed generated files**

Run: `git diff -- frontend/src/auth/accessMatrix.generated.ts backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs 2>&1 | head -40`
Expected: **no diff** (or only whitespace/blank-line diffs). The new generator's outputs should be byte-equivalent to what was already checked in. If you see semantic diffs (role values, route lists), stop and audit `access-matrix.json` against `AccessMatrix.cs` — the JSON port is wrong.

- [ ] **Step 2: Confirm the new Entra manifest matches the old one in content**

Run: `diff <(jq -S . access-matrix.generated.json) <(jq -S . access-matrix-entra.generated.json) 2>&1 | head -20`
Expected: empty (no diff). If different, audit the seedGroups in `access-matrix.json` against the old manifest's `groups[].roles`.

> No commit in this task — Phase 2 lands the full switch-over as one coherent commit.

---

## Phase 2: Switch over to generated runtime files

### Task 2.1: Delete the hand-written `Feature.cs` and `AccessMatrix.cs`

**Files:**
- Delete: `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.cs`
- Delete: `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.cs`

- [ ] **Step 1: Delete the hand-written files**

Run: `git rm backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.cs backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.cs`

- [ ] **Step 2: Build to confirm only the generated versions remain**

Run: `cd backend && dotnet build 2>&1 | tail -15`
Expected: `Build succeeded` (zero errors). The generated `Feature.generated.cs` and `AccessMatrix.generated.cs` now provide the `Feature` enum and `AccessMatrix` class.

- [ ] **Step 3: Drop the now-unused `AccessGroup` record from `AccessModels.cs`**

Read `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessModels.cs`. It contains:
```csharp
public sealed record AccessRoleDefinition(string Value, string Feature, AccessLevel Level);
public sealed record AccessGroup(string Name, IReadOnlyList<string> Roles);
```

Edit it to remove the `AccessGroup` record (no runtime code references it after Phase 2):

```csharp
namespace Anela.Heblo.Domain.Features.Authorization;

public sealed record AccessRoleDefinition(string Value, string Feature, AccessLevel Level);
```

- [ ] **Step 4: Run all authorization tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~Authorization 2>&1 | tail -25`
Expected: One or more tests fail because the existing `AuthorizationSeederTests` still references `AccessMatrix.Groups`, which the new generated `AccessMatrix` doesn't expose. Phase 4 deletes those tests. The non-seeder authorization tests (`AccessMatrixTests`, `GateConsistencyTests`, etc.) should still pass.

> Do not commit yet — Phase 3/4 finishes the cleanup before the next commit point.

---

## Phase 3: Drop `SystemGroups` from the catalog API

### Task 3.1: Remove the `SystemGroups` field

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPermissionCatalogue/GetPermissionCatalogueResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPermissionCatalogue/GetPermissionCatalogueHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/GetPermissionCatalogueHandlerTests.cs`

- [ ] **Step 1: Read the current response DTO and handler**

Run: `cat backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPermissionCatalogue/GetPermissionCatalogueResponse.cs`
Expected: A DTO with at least `Permissions`, `Features`, and `SystemGroups` fields.

- [ ] **Step 2: Remove `SystemGroups` from the response DTO**

Edit `GetPermissionCatalogueResponse.cs`. Delete the `public List<CatalogueGroupDto> SystemGroups { get; set; } = new();` line (currently `:9`). If `CatalogueGroupDto` is no longer referenced anywhere in this file or in `GetPermissionCatalogueHandler.cs`, also remove the `CatalogueGroupDto` class definition (grep first to confirm).

- [ ] **Step 3: Remove the `SystemGroups = ...` assignment in the handler**

Edit `GetPermissionCatalogueHandler.cs`. Delete the entire `SystemGroups = AccessMatrix.Groups.Select(...)` block (currently `:22-26`). The handler becomes:

```csharp
public Task<GetPermissionCatalogueResponse> Handle(GetPermissionCatalogueRequest request, CancellationToken ct)
{
    var response = new GetPermissionCatalogueResponse
    {
        Permissions = AccessMatrix.AllRoleValues().ToList(),
        Features = AccessMatrix.Features.Select(f => new CatalogueFeatureDto
        {
            Key = PermissionString.Format(f.Key, AccessLevel.Read)[..^".read".Length],
            Label = f.Label,
            Section = f.Key.ToString().Split('_')[0],
            HasWrite = f.HasWrite,
            HasAdmin = f.HasAdmin,
        }).ToList(),
    };
    return Task.FromResult(response);
}
```

- [ ] **Step 4: Update the handler test**

Read `backend/test/Anela.Heblo.Tests/Authorization/GetPermissionCatalogueHandlerTests.cs`. Find the line `result.SystemGroups.Select(g => g.Name).Should().BeEquivalentTo(AccessMatrix.Groups.Select(g => g.Name));` (currently `:18`). Delete it. Rename the test method from `Handle_ReturnsAllPermissionsAndSystemGroups` to `Handle_ReturnsAllPermissionsAndFeatures` to keep the name honest.

- [ ] **Step 5: Build and run the handler tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetPermissionCatalogue 2>&1 | tail -10`
Expected: All tests pass.

- [ ] **Step 6: Regenerate the FE OpenAPI client**

Run: `./scripts/regenerate-api-client.sh 2>&1 | tail -5`
Expected: Script completes, regenerates `frontend/src/api/generated/api-client.ts` — the `systemGroups` field disappears from `GetPermissionCatalogueResponse`.

- [ ] **Step 7: Drop the `systemGroups: []` fixture line in the FE test**

Edit `frontend/src/pages/__tests__/GroupDetailPage.test.tsx`. Find `systemGroups: [],` (currently `:33`) and delete it.

- [ ] **Step 8: Run the FE tests**

Run: `cd frontend && npm test -- --run src/pages/__tests__/GroupDetailPage.test.tsx 2>&1 | tail -10`
Expected: Test passes.

- [ ] **Step 9: Commit Phases 1–3 together**

```bash
git add access-matrix.json access-matrix-entra.generated.json \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrixManifest.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessModels.cs \
  backend/tools/Anela.Heblo.AccessMatrixGen/Program.cs \
  backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPermissionCatalogue/GetPermissionCatalogueResponse.cs \
  backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetPermissionCatalogue/GetPermissionCatalogueHandler.cs \
  backend/test/Anela.Heblo.Tests/Authorization/GetPermissionCatalogueHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Authorization/AccessMatrixJsonTests.cs \
  frontend/src/auth/accessMatrix.generated.ts \
  frontend/src/api/generated/api-client.ts \
  frontend/src/pages/__tests__/GroupDetailPage.test.tsx
git rm backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.cs \
       backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.cs \
       access-matrix.generated.json
git commit -m "refactor(authz): make access-matrix.json the single source of truth

Invert AccessMatrixGen: read access-matrix.json, emit Feature.generated.cs,
AccessMatrix.generated.cs, AccessRoles.generated.cs, accessMatrix.generated.ts,
and access-matrix-entra.generated.json. Drop hand-written Feature.cs and
AccessMatrix.cs. Remove SystemGroups from the catalogue endpoint (no longer
runtime-relevant — system/manual group distinction is going away)."
```

---

## Phase 4: Remove startup seed + delete old seeder

### Task 4.1: Delete the startup seed call

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs`

- [ ] **Step 1: Delete the seed invocation**

Open `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs`. Find `await Anela.Heblo.Persistence.Features.Authorization.AuthorizationSeeder.SeedAsync(db, default);` (currently `:325`) inside `MigrateDatabaseAsync`. Delete that line.

If no other code in this file references `Anela.Heblo.Persistence.Features.Authorization`, also remove that `using` statement.

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj 2>&1 | tail -5`
Expected: `Build succeeded`.

---

### Task 4.2: Delete the old destructive seeder + its tests

**Files:**
- Delete: `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationSeeder.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Authorization/AuthorizationSeederTests.cs`

- [ ] **Step 1: Confirm no other code references the old seeder**

Run: `grep -rn "AuthorizationSeeder" backend/src backend/test 2>&1 | grep -v "AuthorizationSeederTests.cs" | grep -v "/JsonGroupSeeder"`
Expected: empty output (or only the file we're about to delete).

- [ ] **Step 2: Delete the seeder and its tests**

Run: `git rm backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationSeeder.cs backend/test/Anela.Heblo.Tests/Authorization/AuthorizationSeederTests.cs`

- [ ] **Step 3: Build + run tests**

Run: `cd backend && dotnet build 2>&1 | tail -5 && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~Authorization 2>&1 | tail -10`
Expected: Build succeeds; all remaining authorization tests pass.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs
git rm backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationSeeder.cs \
       backend/test/Anela.Heblo.Tests/Authorization/AuthorizationSeederTests.cs
git commit -m "refactor(authz): remove startup seed and destructive AuthorizationSeeder

Replaced by on-demand JsonGroupSeeder in the next commit. API startup no longer
touches authorization data."
```

---

## Phase 5: Implement `JsonGroupSeeder` (TDD)

### Task 5.1: Write the first red test for `AddMissingGroupsAsync`

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Authorization/JsonGroupSeederTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Authorization/JsonGroupSeederTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class JsonGroupSeederTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"jsonseed_{Guid.NewGuid()}").Options);

    private static SeedGroupEntry Group(string name, params string[] roles) =>
        new(name, roles);

    [Fact]
    public async Task AddMissing_CreatesGroupsFromList_WhenDbIsEmpty()
    {
        await using var db = NewDb();
        var seedGroups = new[]
        {
            Group("Spravce", "products.catalog.read", "products.catalog.write"),
            Group("Skladnik", "warehouse.logistics.read"),
        };

        await JsonGroupSeeder.AddMissingGroupsAsync(db, seedGroups, default);

        var groups = await db.PermissionGroups.Include(g => g.Permissions).ToListAsync();
        groups.Should().HaveCount(2);
        groups.Single(g => g.Name == "Spravce").Permissions
            .Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(new[] { "products.catalog.read", "products.catalog.write" });
        groups.Single(g => g.Name == "Skladnik").Permissions
            .Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(new[] { "warehouse.logistics.read" });
    }
}
```

- [ ] **Step 2: Run the test to see it fail (compile-time)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JsonGroupSeederTests 2>&1 | tail -15`
Expected: Build fails — `JsonGroupSeeder` not defined. Good — that's the red.

---

### Task 5.2: Implement minimal `AddMissingGroupsAsync` to pass Task 5.1

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Authorization/JsonGroupSeeder.cs`

- [ ] **Step 1: Write the minimal implementation**

Create `backend/src/Anela.Heblo.Persistence/Features/Authorization/JsonGroupSeeder.cs`:

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Authorization;

/// <summary>
/// On-demand bootstrap of permission groups from access-matrix.json's seedGroups list.
/// Insert-if-missing semantics: existing groups in the DB are not mutated. Use
/// <see cref="ResetGroupAsync"/> to explicitly restore a named group to its JSON
/// definition.
/// </summary>
public static class JsonGroupSeeder
{
    public static async Task AddMissingGroupsAsync(
        ApplicationDbContext db,
        IReadOnlyList<SeedGroupEntry> seedGroups,
        CancellationToken ct)
    {
        var existingNames = (await db.PermissionGroups.Select(g => g.Name).ToListAsync(ct))
            .ToHashSet();

        foreach (var seed in seedGroups.Where(s => !existingNames.Contains(s.Name)))
        {
            var group = new PermissionGroup
            {
                Id = Guid.NewGuid(),
                Name = seed.Name,
                Description = seed.Name,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            foreach (var role in seed.Roles)
                group.Permissions.Add(new GroupPermission
                {
                    GroupId = group.Id,
                    PermissionValue = role,
                });
            db.PermissionGroups.Add(group);
        }

        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 2: Run the test, expect PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JsonGroupSeederTests.AddMissing_CreatesGroupsFromList_WhenDbIsEmpty 2>&1 | tail -10`
Expected: PASS.

---

### Task 5.3: Red test — `AddMissing` skips existing groups

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/JsonGroupSeederTests.cs`

- [ ] **Step 1: Add the test**

Append to `JsonGroupSeederTests.cs`:

```csharp
    [Fact]
    public async Task AddMissing_LeavesExistingGroupsUntouched()
    {
        await using var db = NewDb();

        // Pre-seed a "Spravce" group with permissions that do NOT match the JSON seed.
        var customId = Guid.NewGuid();
        db.PermissionGroups.Add(new PermissionGroup
        {
            Id = customId,
            Name = "Spravce",
            Description = "edited by admin",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedBy = "admin@example.com",
        });
        db.GroupPermissions.Add(new GroupPermission
        {
            GroupId = customId,
            PermissionValue = "marketing.article.read",
        });
        await db.SaveChangesAsync();

        var seedGroups = new[]
        {
            Group("Spravce", "products.catalog.read", "products.catalog.write"),
        };

        await JsonGroupSeeder.AddMissingGroupsAsync(db, seedGroups, default);

        var persisted = await db.PermissionGroups
            .Include(g => g.Permissions)
            .SingleAsync(g => g.Name == "Spravce");

        persisted.Id.Should().Be(customId);
        persisted.Description.Should().Be("edited by admin");
        persisted.CreatedBy.Should().Be("admin@example.com");
        persisted.Permissions.Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(new[] { "marketing.article.read" });
    }

    [Fact]
    public async Task AddMissing_IsIdempotent()
    {
        await using var db = NewDb();
        var seedGroups = new[]
        {
            Group("Spravce", "products.catalog.read"),
            Group("Skladnik", "warehouse.logistics.read"),
        };

        await JsonGroupSeeder.AddMissingGroupsAsync(db, seedGroups, default);
        await JsonGroupSeeder.AddMissingGroupsAsync(db, seedGroups, default);

        (await db.PermissionGroups.CountAsync()).Should().Be(2);
        (await db.GroupPermissions.CountAsync()).Should().Be(2);
    }
```

- [ ] **Step 2: Run the new tests — expect PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JsonGroupSeederTests 2>&1 | tail -10`
Expected: All three tests pass. The implementation from Task 5.2 already enforces insert-if-missing.

---

### Task 5.4: Red test — `ResetGroup` restores permissions to JSON

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/JsonGroupSeederTests.cs`

- [ ] **Step 1: Add the test**

Append to `JsonGroupSeederTests.cs`:

```csharp
    [Fact]
    public async Task ResetGroup_RestoresPermissionsToJsonDefinition_OverwritingExisting()
    {
        await using var db = NewDb();

        // Seed the group first
        var seed = Group("Spravce", "products.catalog.read", "products.catalog.write");
        await JsonGroupSeeder.AddMissingGroupsAsync(db, new[] { seed }, default);

        // Mutate it (simulate admin edit)
        var group = await db.PermissionGroups
            .Include(g => g.Permissions)
            .SingleAsync(g => g.Name == "Spravce");
        db.GroupPermissions.RemoveRange(group.Permissions);
        db.GroupPermissions.Add(new GroupPermission
        {
            GroupId = group.Id,
            PermissionValue = "drifted.permission.read",
        });
        await db.SaveChangesAsync();

        // Now reset to JSON
        await JsonGroupSeeder.ResetGroupAsync(db, seed, default);

        var reset = await db.PermissionGroups
            .Include(g => g.Permissions)
            .SingleAsync(g => g.Name == "Spravce");

        reset.Permissions.Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(new[] { "products.catalog.read", "products.catalog.write" });
    }
```

- [ ] **Step 2: Run — expect compile failure (no `ResetGroupAsync` yet)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JsonGroupSeederTests.ResetGroup_RestoresPermissionsToJsonDefinition_OverwritingExisting 2>&1 | tail -10`
Expected: Compile failure — `ResetGroupAsync` not defined.

---

### Task 5.5: Implement minimal `ResetGroupAsync`

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Authorization/JsonGroupSeeder.cs`

- [ ] **Step 1: Add `ResetGroupAsync` to the seeder**

Append to `JsonGroupSeeder.cs` (inside the `public static class JsonGroupSeeder`):

```csharp
    public static async Task ResetGroupAsync(
        ApplicationDbContext db,
        SeedGroupEntry seed,
        CancellationToken ct)
    {
        var group = await db.PermissionGroups
            .Include(g => g.Permissions)
            .FirstOrDefaultAsync(g => g.Name == seed.Name, ct);

        if (group is null)
        {
            group = new PermissionGroup
            {
                Id = Guid.NewGuid(),
                Name = seed.Name,
                Description = seed.Name,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.PermissionGroups.Add(group);
        }
        else
        {
            db.GroupPermissions.RemoveRange(group.Permissions);
            group.Permissions.Clear();
        }

        foreach (var role in seed.Roles)
            group.Permissions.Add(new GroupPermission
            {
                GroupId = group.Id,
                PermissionValue = role,
            });

        await db.SaveChangesAsync(ct);
    }
```

- [ ] **Step 2: Run the test — expect PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JsonGroupSeederTests.ResetGroup_RestoresPermissionsToJsonDefinition_OverwritingExisting 2>&1 | tail -10`
Expected: PASS.

---

### Task 5.6: Red tests — `ResetGroup` preserves memberships and parents

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/JsonGroupSeederTests.cs`

- [ ] **Step 1: Add the tests**

Append to `JsonGroupSeederTests.cs`:

```csharp
    [Fact]
    public async Task ResetGroup_PreservesUserGroupMemberships()
    {
        await using var db = NewDb();
        var seed = Group("Spravce", "products.catalog.read");
        await JsonGroupSeeder.AddMissingGroupsAsync(db, new[] { seed }, default);

        var group = await db.PermissionGroups.SingleAsync(g => g.Name == "Spravce");
        var userId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = userId,
            EntraObjectId = "abc-123",
            Email = "user@example.com",
            DisplayName = "Test User",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = group.Id });
        await db.SaveChangesAsync();

        await JsonGroupSeeder.ResetGroupAsync(db, seed, default);

        (await db.UserGroups.AnyAsync(ug => ug.UserId == userId && ug.GroupId == group.Id))
            .Should().BeTrue("reset must not delete user-group memberships");
    }

    [Fact]
    public async Task ResetGroup_PreservesParentRelationships()
    {
        await using var db = NewDb();
        var spravceSeed = Group("Spravce", "products.catalog.read");
        var vedeniSeed = Group("Vedeni", "products.catalog.read");
        await JsonGroupSeeder.AddMissingGroupsAsync(db, new[] { spravceSeed, vedeniSeed }, default);

        var spravce = await db.PermissionGroups.SingleAsync(g => g.Name == "Spravce");
        var vedeni = await db.PermissionGroups.SingleAsync(g => g.Name == "Vedeni");
        db.GroupParents.Add(new GroupParent { GroupId = vedeni.Id, ParentGroupId = spravce.Id });
        await db.SaveChangesAsync();

        await JsonGroupSeeder.ResetGroupAsync(db, vedeniSeed, default);

        (await db.GroupParents.AnyAsync(p => p.GroupId == vedeni.Id && p.ParentGroupId == spravce.Id))
            .Should().BeTrue("reset must not delete group parent relationships");
    }

    [Fact]
    public async Task ResetGroup_CreatesGroupWhenMissing()
    {
        await using var db = NewDb();
        var seed = Group("NewGroup", "products.catalog.read");

        await JsonGroupSeeder.ResetGroupAsync(db, seed, default);

        var group = await db.PermissionGroups
            .Include(g => g.Permissions)
            .SingleAsync(g => g.Name == "NewGroup");
        group.Permissions.Select(p => p.PermissionValue)
            .Should().BeEquivalentTo(new[] { "products.catalog.read" });
    }
```

- [ ] **Step 2: Run — expect all PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JsonGroupSeederTests 2>&1 | tail -10`
Expected: All seven tests pass. The implementation from Task 5.5 already preserves memberships (it only mutates the `Permissions` collection) and parents.

- [ ] **Step 3: Commit Phase 5**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Authorization/JsonGroupSeeder.cs \
        backend/test/Anela.Heblo.Tests/Authorization/JsonGroupSeederTests.cs
git commit -m "feat(authz): add JsonGroupSeeder (insert-if-missing + reset-group)"
```

---

## Phase 6: On-demand console tool

### Task 6.1: Create the seeder tool project

**Files:**
- Create: `backend/tools/Anela.Heblo.AuthorizationSeeder/Anela.Heblo.AuthorizationSeeder.csproj`
- Create: `backend/tools/Anela.Heblo.AuthorizationSeeder/appsettings.json`
- Create: `backend/tools/Anela.Heblo.AuthorizationSeeder/appsettings.Staging.json`
- Create: `backend/tools/Anela.Heblo.AuthorizationSeeder/appsettings.Production.json`

- [ ] **Step 1: Get the pinned Azure package versions used by the API**

Run: `grep -E 'Azure.(Identity|Extensions.AspNetCore.Configuration.Secrets)' backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Two `<PackageReference>` lines with version numbers. Note both versions for use in the next step.

- [ ] **Step 2: Create the tool csproj**

Create `backend/tools/Anela.Heblo.AuthorizationSeeder/Anela.Heblo.AuthorizationSeeder.csproj`. Replace the `Version="X.Y.Z"` placeholders below with the versions you read in Step 1:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.AuthorizationSeeder</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Azure.Identity" Version="X.Y.Z" />
    <PackageReference Include="Azure.Extensions.AspNetCore.Configuration.Secrets" Version="X.Y.Z" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="appsettings*.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <EmbeddedResource Include="..\..\..\access-matrix.json" Link="access-matrix.json" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create empty `appsettings.json`**

Create `backend/tools/Anela.Heblo.AuthorizationSeeder/appsettings.json`:

```json
{}
```

- [ ] **Step 4: Create `appsettings.Staging.json`**

Create `backend/tools/Anela.Heblo.AuthorizationSeeder/appsettings.Staging.json`:

```json
{
  "KeyVault": {
    "Uri": "https://kv-heblo-stg.vault.azure.net/"
  }
}
```

- [ ] **Step 5: Create `appsettings.Production.json`**

Create `backend/tools/Anela.Heblo.AuthorizationSeeder/appsettings.Production.json`:

```json
{
  "KeyVault": {
    "Uri": "https://kv-heblo-prod.vault.azure.net/"
  }
}
```

- [ ] **Step 6: Verify the project restores**

Run: `cd backend && dotnet restore tools/Anela.Heblo.AuthorizationSeeder/Anela.Heblo.AuthorizationSeeder.csproj 2>&1 | tail -5`
Expected: `Restore succeeded`.

---

### Task 6.2: Write `Program.cs` for the seeder tool

**Files:**
- Create: `backend/tools/Anela.Heblo.AuthorizationSeeder/Program.cs`

- [ ] **Step 1: Write the program**

Create `backend/tools/Anela.Heblo.AuthorizationSeeder/Program.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureKeyVault;
using Npgsql;

try
{
    return await Run(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FATAL: {ex.Message}");
    return 1;
}

static async Task<int> Run(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Usage: dotnet run -- <Staging|Production> [--reset-group <Name>]");
        return 2;
    }

    var envArg = args[0];
    string? resetGroupName = null;
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--reset-group" && i + 1 < args.Length)
        {
            resetGroupName = args[++i];
        }
        else
        {
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 2;
        }
    }

    var env = NormalizeEnv(envArg);
    if (env is null)
    {
        Console.Error.WriteLine($"Environment must be 'Staging' or 'Production', got '{envArg}'.");
        return 2;
    }

    // 1. Configuration: appsettings + KV
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{env}.json", optional: false)
        .Build();

    var keyVaultUri = configuration["KeyVault:Uri"]
        ?? throw new InvalidOperationException($"KeyVault:Uri missing in appsettings.{env}.json");

    var withKv = new ConfigurationBuilder()
        .AddConfiguration(configuration)
        .AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential())
        .Build();

    var connectionString = withKv.GetConnectionString(env)
        ?? throw new InvalidOperationException(
            $"ConnectionStrings:{env} not found via Key Vault at {keyVaultUri}.");

    // 2. Load embedded JSON manifest
    var manifest = LoadEmbeddedManifest();

    // 3. PROD safety gates
    if (env == "Production")
    {
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("  WARNING: TARGETING PRODUCTION DATABASE");
        Console.WriteLine($"  Key Vault: {keyVaultUri}");
        Console.WriteLine($"  Action:    {(resetGroupName is null ? "Bootstrap (insert-if-missing)" : $"RESET GROUP '{resetGroupName}'")}");
        Console.WriteLine("============================================================");
        Console.Write("Type PRODUCTION to continue: ");
        var input = Console.ReadLine();
        if (input != "PRODUCTION")
        {
            Console.Error.WriteLine("Confirmation failed; aborting.");
            return 3;
        }

        if (resetGroupName is not null)
        {
            Console.Write($"Confirm reset by typing the group name '{resetGroupName}': ");
            var confirm = Console.ReadLine();
            if (confirm != resetGroupName)
            {
                Console.Error.WriteLine("Group name confirmation failed; aborting.");
                return 3;
            }
        }
    }

    // 4. Build DbContext directly (no DI host needed)
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.UseVector();
    await using var dataSource = dataSourceBuilder.Build();

    var dbOpts = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(dataSource)
        .Options;
    await using var db = new ApplicationDbContext(dbOpts);

    // 5. Execute
    if (resetGroupName is not null)
    {
        var seed = manifest.SeedGroups.FirstOrDefault(
            g => string.Equals(g.Name, resetGroupName, StringComparison.Ordinal));
        if (seed is null)
        {
            Console.Error.WriteLine($"Group '{resetGroupName}' is not defined in access-matrix.json seedGroups.");
            Console.Error.WriteLine($"Known groups: {string.Join(", ", manifest.SeedGroups.Select(g => g.Name))}");
            return 4;
        }

        await JsonGroupSeeder.ResetGroupAsync(db, seed, CancellationToken.None);
        Console.WriteLine($"OK — reset group '{resetGroupName}' against {env}.");
    }
    else
    {
        await JsonGroupSeeder.AddMissingGroupsAsync(db, manifest.SeedGroups, CancellationToken.None);
        Console.WriteLine($"OK — bootstrap complete against {env} ({manifest.SeedGroups.Count} groups in JSON; insert-if-missing).");
    }

    return 0;
}

static string? NormalizeEnv(string raw) =>
    raw.Trim().ToLowerInvariant() switch
    {
        "staging" or "stg" => "Staging",
        "production" or "prod" => "Production",
        _ => null,
    };

static AccessMatrixManifest LoadEmbeddedManifest()
{
    var asm = Assembly.GetExecutingAssembly();
    var resourceName = asm.GetManifestResourceNames()
        .FirstOrDefault(n => n.EndsWith("access-matrix.json", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("Embedded resource access-matrix.json not found.");

    using var stream = asm.GetManifestResourceStream(resourceName)!;
    return JsonSerializer.Deserialize<AccessMatrixManifest>(stream, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    }) ?? throw new InvalidOperationException("Failed to deserialize embedded access-matrix.json.");
}
```

- [ ] **Step 2: Build the tool**

Run: `cd backend && dotnet build tools/Anela.Heblo.AuthorizationSeeder/Anela.Heblo.AuthorizationSeeder.csproj 2>&1 | tail -10`
Expected: `Build succeeded`.

- [ ] **Step 3: Smoke-test the tool's arg parsing (without hitting KV)**

Run: `cd backend && dotnet run --project tools/Anela.Heblo.AuthorizationSeeder -- 2>&1 | tail -5`
Expected: Usage error printed, exit code 2.

Run: `cd backend && dotnet run --project tools/Anela.Heblo.AuthorizationSeeder -- BadEnv 2>&1 | tail -5`
Expected: "Environment must be 'Staging' or 'Production'" error, exit code 2.

- [ ] **Step 4: Commit Phase 6**

```bash
git add backend/tools/Anela.Heblo.AuthorizationSeeder/
git commit -m "feat(authz): add on-demand seeder console tool

dotnet run --project backend/tools/Anela.Heblo.AuthorizationSeeder -- <Env> [--reset-group <Name>]
Reads connection string from Azure Key Vault (kv-heblo-stg / kv-heblo-prod),
embeds access-matrix.json, and applies JsonGroupSeeder.{AddMissingGroupsAsync,
ResetGroupAsync}. PROD requires typed confirmation."
```

---

## Phase 7: Shell wrapper

### Task 7.1: Write `scripts/seed-authorization.sh`

**Files:**
- Create: `scripts/seed-authorization.sh`

- [ ] **Step 1: Create the script**

Create `scripts/seed-authorization.sh`:

```bash
#!/usr/bin/env bash
# Bootstrap (or reset) authorization groups in STG / PROD.
# Reads connection string + secrets from Azure Key Vault.
# Requires: dotnet 8 SDK; `az login` (or a service principal env) for KV access.
set -euo pipefail

usage() {
  cat <<EOF
Usage: $(basename "$0") <staging|production> [--reset-group <Name>]

Default (no --reset-group): insert-if-missing across all groups defined in
access-matrix.json. Existing DB groups are left untouched.

--reset-group <Name>: locate <Name> in the JSON seedGroups, then clear and
re-add its permissions in the DB. Other groups untouched.

Examples:
  $(basename "$0") staging
  $(basename "$0") staging --reset-group Spravce
  $(basename "$0") production --reset-group AccessManager
EOF
}

if [[ $# -eq 0 || "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

ENV_ARG="$1"
shift

case "${ENV_ARG,,}" in
  staging|stg)    ENV_NAME="Staging" ;;
  production|prod) ENV_NAME="Production" ;;
  *)
    echo "ERROR: first argument must be 'staging' or 'production' (got '$ENV_ARG')." >&2
    usage >&2
    exit 2
    ;;
esac

# Warn (but don't block) if az isn't authenticated — KV calls will fail clearly later.
if ! az account show >/dev/null 2>&1; then
  echo "WARNING: 'az account show' returned non-zero. Run 'az login' if Key Vault access fails." >&2
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec dotnet run \
  --project "$REPO_ROOT/backend/tools/Anela.Heblo.AuthorizationSeeder/Anela.Heblo.AuthorizationSeeder.csproj" \
  -- "$ENV_NAME" "$@"
```

- [ ] **Step 2: Make it executable**

Run: `chmod +x scripts/seed-authorization.sh`

- [ ] **Step 3: Test the help/usage path**

Run: `./scripts/seed-authorization.sh --help 2>&1 | head -10`
Expected: Usage text printed.

Run: `./scripts/seed-authorization.sh garbage 2>&1 | tail -5`
Expected: Error "first argument must be 'staging' or 'production'", exit code 2.

- [ ] **Step 4: Commit**

```bash
git add scripts/seed-authorization.sh
git commit -m "feat(authz): add seed-authorization.sh wrapper script"
```

---

## Phase 8: Frontend menu-path consistency test

### Task 8.1: Write the Vitest

**Files:**
- Create: `frontend/src/auth/__tests__/accessMatrixConsistency.test.ts`

- [ ] **Step 1: Write the test**

Create `frontend/src/auth/__tests__/accessMatrixConsistency.test.ts`:

```typescript
import { readFileSync } from "fs";
import { resolve } from "path";
import { describe, expect, it } from "vitest";
import { ACCESS_ROUTES } from "../accessMatrix.generated";

/**
 * Bidirectional consistency check between access-matrix.json's menuPaths
 * (surfaced as ACCESS_ROUTES) and the routes actually mounted in App.tsx.
 *
 * Keys prefixed with '#' are virtual identifiers for external onClick items
 * (terminal, hangfire, baleni-external) — they intentionally have no React
 * Router route, so they are excluded from the App.tsx side of the check.
 */
describe("access matrix ↔ App.tsx consistency", () => {
  const appSource = readFileSync(
    resolve(__dirname, "../../App.tsx"),
    "utf-8",
  );

  const guardedRoutes = Array.from(
    appSource.matchAll(/guard\(\s*["']([^"']+)["']/g),
    (m) => m[1],
  );

  const matrixKeys = Object.keys(ACCESS_ROUTES);

  it("every guard() call in App.tsx has an ACCESS_ROUTES entry", () => {
    const missing = guardedRoutes.filter((r) => !(r in ACCESS_ROUTES));
    expect(missing, `App.tsx guards routes not present in access-matrix.json: ${missing.join(", ")}`)
      .toHaveLength(0);
  });

  it("every non-virtual ACCESS_ROUTES key is guarded in App.tsx", () => {
    const stale = matrixKeys
      .filter((k) => !k.startsWith("#"))
      .filter((k) => !guardedRoutes.includes(k));
    expect(stale, `access-matrix.json declares routes not guarded in App.tsx: ${stale.join(", ")}`)
      .toHaveLength(0);
  });

  it("at least one guarded route exists (regression guard)", () => {
    expect(guardedRoutes.length).toBeGreaterThan(0);
  });
});
```

- [ ] **Step 2: Run the test**

Run: `cd frontend && npm test -- --run src/auth/__tests__/accessMatrixConsistency.test.ts 2>&1 | tail -20`
Expected: All three tests pass.

If a test fails because `App.tsx` guards a route not in JSON (or vice versa), the right fix is **almost always to add/remove the entry in `access-matrix.json`** and regenerate (Phase 1 generator), not to weaken the test.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/auth/__tests__/accessMatrixConsistency.test.ts
git commit -m "test(authz): validate access-matrix.json menuPaths against App.tsx guards"
```

---

## Phase 9: Update Entra scripts for the renamed manifest

### Task 9.1: Point sync/cleanup scripts at the new filename

**Files:**
- Modify: `scripts/sync-entra-access.sh`
- Modify: `scripts/cleanup-entra.sh`

- [ ] **Step 1: Update `sync-entra-access.sh`**

Read `scripts/sync-entra-access.sh`. Find the line `MANIFEST="${MANIFEST:-access-matrix.generated.json}"`. Change to:

```bash
MANIFEST="${MANIFEST:-access-matrix-entra.generated.json}"
```

- [ ] **Step 2: Update `cleanup-entra.sh`**

Read `scripts/cleanup-entra.sh`. Find the line `MANIFEST="${MANIFEST:-access-matrix.generated.json}"`. Change to:

```bash
MANIFEST="${MANIFEST:-access-matrix-entra.generated.json}"
```

- [ ] **Step 3: Confirm no other consumers of the old filename remain**

Run: `grep -rn "access-matrix.generated.json" --include="*.sh" --include="*.md" --include="*.yml" --include="*.cs" --include="*.csproj" . 2>&1 | grep -v "node_modules\|/bin/\|/obj/"`
Expected: empty output (only the renamed Entra references remain via `access-matrix-entra.generated.json`).

- [ ] **Step 4: Smoke-test the Entra script's arg parsing (without running it)**

Run: `bash -n scripts/sync-entra-access.sh && bash -n scripts/cleanup-entra.sh && echo OK`
Expected: `OK` (syntax check passes).

- [ ] **Step 5: Commit**

```bash
git add scripts/sync-entra-access.sh scripts/cleanup-entra.sh
git commit -m "chore(authz): point Entra sync scripts at access-matrix-entra.generated.json"
```

---

## Phase 10: Final verification

### Task 10.1: Full backend build + format + tests

- [ ] **Step 1: Format**

Run: `cd backend && dotnet format 2>&1 | tail -5`
Expected: Zero changes, or only whitespace formatting on touched files.

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build 2>&1 | tail -10`
Expected: `Build succeeded` with zero errors.

- [ ] **Step 3: All tests**

Run: `cd backend && dotnet test 2>&1 | tail -10`
Expected: All tests pass.

### Task 10.2: Full frontend build + lint + tests

- [ ] **Step 1: Lint**

Run: `cd frontend && npm run lint 2>&1 | tail -5`
Expected: No errors.

- [ ] **Step 2: Build**

Run: `cd frontend && npm run build 2>&1 | tail -10`
Expected: Build completes (no TS errors).

- [ ] **Step 3: Tests**

Run: `cd frontend && npm test -- --run 2>&1 | tail -15`
Expected: All tests pass, including the new `accessMatrixConsistency.test.ts`.

### Task 10.3: Generator idempotence sanity check

- [ ] **Step 1: Regenerate and diff**

Run from repo root:
```bash
dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen 2>&1 | tail -3
git diff --stat -- backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs \
                   backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs \
                   backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs \
                   frontend/src/auth/accessMatrix.generated.ts \
                   access-matrix-entra.generated.json
```
Expected: No diff. The generator is deterministic.

### Task 10.4: STG manual verification

> Requires: `az login` against the Anela tenant. Performs real DB writes against `Heblo_TST` via `kv-heblo-stg`.

- [ ] **Step 1: Confirm Azure login**

Run: `az account show --query "user.name" -o tsv`
Expected: prints your account email.

- [ ] **Step 2: Run STG bootstrap**

Run: `./scripts/seed-authorization.sh staging 2>&1 | tail -5`
Expected: `OK — bootstrap complete against Staging (12 groups in JSON; insert-if-missing).` Exit code 0.

- [ ] **Step 3: Re-run to confirm idempotence**

Run: `./scripts/seed-authorization.sh staging 2>&1 | tail -5`
Expected: Same OK line; no change in DB.

- [ ] **Step 4: Verify DB state**

Connect to the STG DB (psql via `kv-heblo-stg` secret or whatever your normal workflow is) and run:
```sql
SELECT name, (SELECT COUNT(*) FROM "GroupPermissions" gp WHERE gp."GroupId" = pg."Id") AS perm_count
  FROM "PermissionGroups" pg
  ORDER BY name;
```
Expected: 12 rows for the JSON groups, plus any pre-existing custom groups (not modified).

- [ ] **Step 5: Test the reset flow**

In psql, manually add a junk permission to one group:
```sql
INSERT INTO "GroupPermissions" ("GroupId", "PermissionValue")
  VALUES ((SELECT "Id" FROM "PermissionGroups" WHERE name='Skladnik'), 'fake.permission.read');
```

Then run: `./scripts/seed-authorization.sh staging --reset-group Skladnik 2>&1 | tail -5`
Expected: `OK — reset group 'Skladnik' against Staging.` In DB, the `fake.permission.read` row is gone; the JSON-defined Skladnik permissions are present.

### Task 10.5: PROD confirmation gate dry test (no actual writes)

- [ ] **Step 1: Trigger PROD prompt, then abort**

Run: `./scripts/seed-authorization.sh production` and type literally `cancel` at the prompt.
Expected: Tool prints "Confirmation failed; aborting." and exits with code 3. No DB writes.

- [ ] **Step 2: Trigger PROD reset prompt, fail group-name confirmation**

Run: `./scripts/seed-authorization.sh production --reset-group Spravce`. Type `PRODUCTION` at the first prompt, then `wrong-name` at the second.
Expected: Tool prints "Group name confirmation failed; aborting." and exits with code 3. No DB writes.

> Do not perform an actual PROD bootstrap as part of plan verification — that's an operational decision separate from validating the code change. If a PROD bootstrap is genuinely needed (e.g., fresh environment), do it as a deliberate ops step with explicit approval, not as part of merging this PR.

### Task 10.6: End-of-PR smoke

- [ ] **Step 1: Boot the API locally**

Run: `./scripts/start-backend-dev.sh 2>&1 | head -30`
Expected: API starts cleanly. Search the log for "AuthorizationSeeder" — it should NOT appear (no startup seed).

- [ ] **Step 2: Hit the catalogue endpoint**

In another terminal: `curl -s -H "Authorization: ..." http://localhost:5001/api/access/permission-catalogue | jq 'keys'`
Expected: Response includes `permissions` and `features` keys; **no `systemGroups` key**.

- [ ] **Step 3: Final commit if format pass introduced any noise**

```bash
git status
# If there are auto-format diffs not yet committed:
git add -A && git commit -m "chore: dotnet format pass"
```

---

## Self-Review

**1. Spec coverage:**
- JSON as single source of truth → Phases 0–2 (Tasks 0.2, 1.1, 2.1).
- Enum generated from JSON → Task 1.1 (`Feature.generated.cs`).
- Permission UI loads from generated enum/class → Task 3.1 (catalogue handler still uses generated `AccessMatrix.AllRoleValues()` / `Features`).
- Groups loaded from DB only → Task 3.1 drops `SystemGroups`; runtime resolution was already DB-driven via `IPermissionResolver`.
- Manual creation or seeded → Task 5 implements `JsonGroupSeeder`; Task 6 wraps it in a tool; Task 7 wraps it in a script.
- No system/custom distinction → `SystemGroups` field removed (Task 3.1), `AccessGroup` record removed (Task 2.1), destructive `AuthorizationSeeder` removed (Task 4.2).
- Test validating URLs against JSON → Task 8.1 (`accessMatrixConsistency.test.ts`).
- On-demand seeder script for STG/PROD with KV → Tasks 6 + 7.
- `--reset-group` flag → Task 5.4–5.5 + Task 6.2.

**2. Placeholder scan:** Scanned for "TODO", "TBD", "implement later", "appropriate error handling", "similar to" — none present. All code blocks contain literal code.

**3. Type consistency:** `SeedGroupEntry` is defined in Task 0.1 and consumed identically in Tasks 5.x and 6.2. `AccessMatrixManifest` shape matches across Domain DTO, generator deserialization, and seeder embedded-resource load. `JsonGroupSeeder.AddMissingGroupsAsync` and `JsonGroupSeeder.ResetGroupAsync` signatures consistent between Task 5.2/5.5 (declaration) and Task 6.2 (call site).

**4. Order safety:** Phase 1 leaves the build broken intentionally (Task 1.1 Step 4 documents this). Phase 2 fixes it. The single commit at Task 3.1 Step 9 bundles the broken intermediate state with the fix — no broken commit lands on the branch.

---

**Plan complete and saved to `docs/superpowers/plans/2026-06-09-access-matrix-json-source-of-truth.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
