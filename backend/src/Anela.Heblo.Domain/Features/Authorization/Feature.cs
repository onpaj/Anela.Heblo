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
