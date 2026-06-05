namespace Anela.Heblo.Domain.Features.Authorization;

public static class AccessMatrix
{
    public const string BaseRole = "heblo_user";

    public static readonly IReadOnlyList<AccessFeature> Features = new[]
    {
        // Finance section
        new AccessFeature("financial_overview", "Finanční přehled", "Finance", "/finance/overview"),
        new AccessFeature("margin_analysis", "Analýza marží", "Finance", "/analytics/product-margin-summary"),

        // Produkty section
        new AccessFeature("product_margins", "Marže produktů", "Produkty", "/products/margins"),
        new AccessFeature("catalog", "Katalog", "Produkty", "/catalog", HasWrite: true),
        new AccessFeature("journal", "Žurnál", "Produkty", "/journal", HasWrite: true),

        // Zákaznické section
        new AccessFeature("bank_statements", "Bankovní výpisy", "Zákaznické", "/customer/bank-statements-overview"),
        new AccessFeature("knowledge_base", "Znalostní báze", "Zákaznické", "/knowledge-base", HasWrite: true),
        new AccessFeature("smartsupp", "Smartsupp", "Zákaznické", "/customer/smartsupp"),

        // Nákup section
        new AccessFeature("purchase_orders", "Nákupní objednávky", "Nákup", "/purchase/orders", HasWrite: true),
        new AccessFeature("purchase_stock", "Analýza skladu (nákup)", "Nákup", "/purchase/stock-analysis"),

        // Výroba section
        new AccessFeature("manufacture_orders", "Výrobní objednávky", "Výroba", "/manufacturing/orders", HasWrite: true),
        new AccessFeature("batch_planning", "Plánování šarží", "Výroba", "/manufacturing/batch-planning", HasWrite: true),
        new AccessFeature("manufacture_output", "Výstup výroby", "Výroba", "/manufacturing/output"),
        new AccessFeature("manufacture_stock", "Analýza skladu (výroba)", "Výroba", "/manufacturing/stock-analysis"),
        new AccessFeature("material_inventory", "Inventura materiálu", "Výroba", "/manufacturing/inventory", HasWrite: true),
        new AccessFeature("product_inventory", "Inventura produktů", "Sklad", "/manufacturing/product-inventory", HasWrite: true),
        new AccessFeature("material_containers", "Kontejnery materiálu", "Výroba", "/manufacturing/material-containers", HasWrite: true),

        // Sklad section
        new AccessFeature("logistics", "Logistika", "Sklad", "/logistics/inventory", HasWrite: true),
        new AccessFeature("expedition", "Expedice", "Sklad", "/logistics/expedition-archive", HasWrite: true),
        new AccessFeature("packaging", "Balení", "Baleni", "/baleni", HasWrite: true),
        new AccessFeature("stock_up", "Naskladnění", "Administrace", "/stock-up-operations", HasWrite: true),

        // Marketing section
        new AccessFeature("article", "Články", "Marketing", "/articles", HasWrite: true),
        new AccessFeature("leaflet", "Generátor letáků", "Marketing", "/leaflet-generator", HasWrite: true),
        new AccessFeature("photobank", "Fotobanka", "Marketing", "/marketing/photobank", HasWrite: true, HasAdmin: true),
        new AccessFeature("marketing_calendar", "Marketingový kalendář", "Marketing", "/marketing/calendar", HasWrite: true),

        // Anela section
        new AccessFeature("meetings", "Schůzky", "Anela", "/automation/meeting-tasks", HasWrite: true),
        new AccessFeature("org_chart", "Organigram", "Anela", null),

        // Administrace section
        new AccessFeature("data_quality", "Kvalita dat", "Administrace", "/automation/data-quality", HasWrite: true),
        new AccessFeature("administration", "Administrace", "Administrace", "/admin/feature-flags", HasWrite: true),
        new AccessFeature("feature_flags", "Feature příznaky", "Administrace", "/admin/feature-flags", HasWrite: true),
    };

    public static readonly IReadOnlyList<AccessGroup> Groups = new[]
    {
        new AccessGroup("Spravce", AllRoleValues().ToArray()),
        new AccessGroup("Vedeni", AllRoleValues().Where(r => r.EndsWith(".read")).ToArray()),
        new AccessGroup("Ucetni", new[] { "financial_overview.read", "margin_analysis.read", "product_margins.read", "bank_statements.read", "journal.read", "journal.write", "smartsupp.read" }),
        new AccessGroup("Marketer", new[] { "marketing_calendar.read", "marketing_calendar.write", "photobank.read", "photobank.write", "leaflet.read", "leaflet.write", "article.read", "article.write" }),
        new AccessGroup("Nakupci", new[] { "purchase_orders.read", "purchase_orders.write", "purchase_stock.read" }),
        new AccessGroup("Vedouci_vyroby", new[] { "manufacture_orders.read", "manufacture_orders.write", "manufacture_output.read", "manufacture_stock.read", "batch_planning.read", "batch_planning.write", "material_inventory.read", "material_inventory.write", "material_containers.read", "material_containers.write", "product_inventory.read", "product_inventory.write" }),
        new AccessGroup("Pracovnik_vyroby", new[] { "manufacture_orders.read", "material_inventory.read", "material_inventory.write", "product_inventory.read", "material_containers.read" }),
        new AccessGroup("Vedouci_skladu", new[] { "logistics.read", "logistics.write", "expedition.read", "expedition.write", "packaging.read", "packaging.write", "stock_up.read", "stock_up.write" }),
        new AccessGroup("Skladnik", new[] { "logistics.read", "expedition.read", "packaging.read", "packaging.write" }),
        new AccessGroup("Poradenstvi", new[] { "knowledge_base.read", "knowledge_base.write" }),
    };

    public static IEnumerable<AccessRoleDefinition> Roles()
    {
        foreach (var f in Features)
        {
            yield return new AccessRoleDefinition($"{f.Key}.read", f.Key, AccessLevel.Read);
            if (f.HasWrite) yield return new AccessRoleDefinition($"{f.Key}.write", f.Key, AccessLevel.Write);
            if (f.HasAdmin) yield return new AccessRoleDefinition($"{f.Key}.admin", f.Key, AccessLevel.Admin);
        }
    }

    public static IEnumerable<string> AllRoleValues() => Roles().Select(r => r.Value);
}
