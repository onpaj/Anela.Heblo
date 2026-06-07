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

/// <summary>Backward compatibility alias for AuthorizationConstants used throughout the codebase.</summary>
public static class AuthorizationConstants
{
    public static class Roles
    {
        public const string Base = AccessRoles.Base;
        public const string FinancialOverviewRead = AccessRoles.FinancialOverviewRead;
        public const string MarginAnalysisRead = AccessRoles.MarginAnalysisRead;
        public const string ProductMarginsRead = AccessRoles.ProductMarginsRead;
        public const string CatalogRead = AccessRoles.CatalogRead;
        public const string CatalogWrite = AccessRoles.CatalogWrite;
        public const string JournalRead = AccessRoles.JournalRead;
        public const string JournalWrite = AccessRoles.JournalWrite;
        public const string BankStatementsRead = AccessRoles.BankStatementsRead;
        public const string KnowledgeBaseRead = AccessRoles.KnowledgeBaseRead;
        public const string KnowledgeBaseWrite = AccessRoles.KnowledgeBaseWrite;
        public const string SmartsuppRead = AccessRoles.SmartsuppRead;
        public const string PurchaseOrdersRead = AccessRoles.PurchaseOrdersRead;
        public const string PurchaseOrdersWrite = AccessRoles.PurchaseOrdersWrite;
        public const string PurchaseStockRead = AccessRoles.PurchaseStockRead;
        public const string ManufactureOrdersRead = AccessRoles.ManufactureOrdersRead;
        public const string ManufactureOrdersWrite = AccessRoles.ManufactureOrdersWrite;
        public const string BatchPlanningRead = AccessRoles.BatchPlanningRead;
        public const string BatchPlanningWrite = AccessRoles.BatchPlanningWrite;
        public const string ManufactureOutputRead = AccessRoles.ManufactureOutputRead;
        public const string ManufactureStockRead = AccessRoles.ManufactureStockRead;
        public const string MaterialInventoryRead = AccessRoles.MaterialInventoryRead;
        public const string MaterialInventoryWrite = AccessRoles.MaterialInventoryWrite;
        public const string ProductInventoryRead = AccessRoles.ProductInventoryRead;
        public const string ProductInventoryWrite = AccessRoles.ProductInventoryWrite;
        public const string MaterialContainersRead = AccessRoles.MaterialContainersRead;
        public const string MaterialContainersWrite = AccessRoles.MaterialContainersWrite;
        public const string LogisticsRead = AccessRoles.LogisticsRead;
        public const string LogisticsWrite = AccessRoles.LogisticsWrite;
        public const string ExpeditionRead = AccessRoles.ExpeditionRead;
        public const string ExpeditionWrite = AccessRoles.ExpeditionWrite;
        public const string PackagingRead = AccessRoles.PackagingRead;
        public const string PackagingWrite = AccessRoles.PackagingWrite;
        public const string StockUpRead = AccessRoles.StockUpRead;
        public const string StockUpWrite = AccessRoles.StockUpWrite;
        public const string ArticleRead = AccessRoles.ArticleRead;
        public const string ArticleWrite = AccessRoles.ArticleWrite;
        public const string LeafletRead = AccessRoles.LeafletRead;
        public const string LeafletWrite = AccessRoles.LeafletWrite;
        public const string PhotobankRead = AccessRoles.PhotobankRead;
        public const string PhotobankWrite = AccessRoles.PhotobankWrite;
        public const string PhotobankAdmin = AccessRoles.PhotobankAdmin;
        public const string MarketingCalendarRead = AccessRoles.MarketingCalendarRead;
        public const string MarketingCalendarWrite = AccessRoles.MarketingCalendarWrite;
        public const string MeetingsRead = AccessRoles.MeetingsRead;
        public const string MeetingsWrite = AccessRoles.MeetingsWrite;
        public const string OrgChartRead = AccessRoles.OrgChartRead;
        public const string DataQualityRead = AccessRoles.DataQualityRead;
        public const string DataQualityWrite = AccessRoles.DataQualityWrite;
        public const string AdministrationRead = AccessRoles.AdministrationRead;
        public const string AdministrationWrite = AccessRoles.AdministrationWrite;
        public const string FeatureFlagsRead = AccessRoles.FeatureFlagsRead;
        public const string FeatureFlagsWrite = AccessRoles.FeatureFlagsWrite;

        // Legacy aliases
        public const string MeetingManager = AccessRoles.MeetingsWrite;
        public const string FinanceReader = AccessRoles.FinancialOverviewRead;
        public const string MarketingWriter = AccessRoles.ArticleWrite; // Maps to article writing, represents general marketing write access
        public const string MarketingReader = AccessRoles.ArticleRead; // Maps to article reading, represents general marketing read access
        public const string HebloUser = AccessRoles.Base; // Base heblo_user role for all authenticated users
        public const string SuperUser = AccessRoles.AdministrationWrite; // Super user has admin write access
    }
}
