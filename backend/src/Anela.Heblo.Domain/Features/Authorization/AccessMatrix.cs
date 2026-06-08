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

    private static IReadOnlyList<FeaturePermission> Require(
        params (Feature feature, AccessLevel level)[] reqs)
        => reqs.Select(r => new FeaturePermission(r.feature, r.level)).ToArray();
}
