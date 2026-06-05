namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>Compile-time role constants for [Authorize(Roles = …)]. Mirror of AccessMatrix.</summary>
public static class AccessRoles
{
    public const string Base = "heblo_user";

    // Finance
    public const string FinancialOverviewRead = "financial_overview.read";
    public const string MarginAnalysisRead = "margin_analysis.read";

    // Produkty
    public const string ProductMarginsRead = "product_margins.read";
    public const string CatalogRead = "catalog.read";
    public const string CatalogWrite = "catalog.write";
    public const string JournalRead = "journal.read";
    public const string JournalWrite = "journal.write";

    // Zákaznické
    public const string BankStatementsRead = "bank_statements.read";
    public const string KnowledgeBaseRead = "knowledge_base.read";
    public const string KnowledgeBaseWrite = "knowledge_base.write";
    public const string SmartsuppRead = "smartsupp.read";

    // Nákup
    public const string PurchaseOrdersRead = "purchase_orders.read";
    public const string PurchaseOrdersWrite = "purchase_orders.write";
    public const string PurchaseStockRead = "purchase_stock.read";

    // Výroba
    public const string ManufactureOrdersRead = "manufacture_orders.read";
    public const string ManufactureOrdersWrite = "manufacture_orders.write";
    public const string BatchPlanningRead = "batch_planning.read";
    public const string BatchPlanningWrite = "batch_planning.write";
    public const string ManufactureOutputRead = "manufacture_output.read";
    public const string ManufactureStockRead = "manufacture_stock.read";
    public const string MaterialInventoryRead = "material_inventory.read";
    public const string MaterialInventoryWrite = "material_inventory.write";
    public const string ProductInventoryRead = "product_inventory.read";
    public const string ProductInventoryWrite = "product_inventory.write";
    public const string MaterialContainersRead = "material_containers.read";
    public const string MaterialContainersWrite = "material_containers.write";

    // Sklad
    public const string LogisticsRead = "logistics.read";
    public const string LogisticsWrite = "logistics.write";
    public const string ExpeditionRead = "expedition.read";
    public const string ExpeditionWrite = "expedition.write";
    public const string PackagingRead = "packaging.read";
    public const string PackagingWrite = "packaging.write";
    public const string StockUpRead = "stock_up.read";
    public const string StockUpWrite = "stock_up.write";

    // Marketing
    public const string ArticleRead = "article.read";
    public const string ArticleWrite = "article.write";
    public const string LeafletRead = "leaflet.read";
    public const string LeafletWrite = "leaflet.write";
    public const string PhotobankRead = "photobank.read";
    public const string PhotobankWrite = "photobank.write";
    public const string PhotobankAdmin = "photobank.admin";
    public const string MarketingCalendarRead = "marketing_calendar.read";
    public const string MarketingCalendarWrite = "marketing_calendar.write";

    // Anela
    public const string MeetingsRead = "meetings.read";
    public const string MeetingsWrite = "meetings.write";
    public const string OrgChartRead = "org_chart.read";

    // Administrace
    public const string DataQualityRead = "data_quality.read";
    public const string DataQualityWrite = "data_quality.write";
    public const string AdministrationRead = "administration.read";
    public const string AdministrationWrite = "administration.write";
    public const string FeatureFlagsRead = "feature_flags.read";
    public const string FeatureFlagsWrite = "feature_flags.write";
}
