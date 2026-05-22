namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// Authorization constants for roles and permissions
/// </summary>
public static class AuthorizationConstants
{
    /// <summary>
    /// Application roles
    /// </summary>
    public static class Roles
    {
        /// <summary>
        /// Role for accessing financial overview and reports
        /// </summary>
        public const string FinanceReader = "finance_reader";

        /// <summary>
        /// Role for managing catalog data and products
        /// </summary>
        public const string CatalogManager = "catalog_manager";

        /// <summary>
        /// Role for managing purchase orders
        /// </summary>
        public const string PurchaseManager = "purchase_manager";

        /// <summary>
        /// Role for super user functions (photobank settings, knowledge base management, etc.)
        /// </summary>
        public const string SuperUser = "super_user";

        /// <summary>
        /// Base role required for application access
        /// </summary>
        public const string HebloUser = "heblo_user";

        /// <summary>
        /// Role required for generating leaflets and articles (GenAI features)
        /// </summary>
        public const string MarketingReader = "marketing_reader";

        /// <summary>
        /// Role for tagging photos in the photobank
        /// </summary>
        public const string MarketingWriter = "marketing_writer";

        /// <summary>
        /// Role required for managing meeting access levels and grants
        /// </summary>
        public const string MeetingManager = "meeting_manager";
    }

    /// <summary>
    /// Authorization policy names
    /// </summary>
    public static class Policies
    {
        /// <summary>
        /// Policy required for uploading and deleting Knowledge Base documents
        /// </summary>
        public const string KnowledgeBaseUpload = "KnowledgeBaseUpload";

        /// <summary>
        /// Policy required for generating leaflets and articles
        /// </summary>
        public const string MarketingReader = "MarketingReader";

        /// <summary>
        /// Policy required for uploading catalog documents to SharePoint
        /// </summary>
        public const string CatalogDocumentsUpload = "CatalogDocumentsUpload";
    }
}