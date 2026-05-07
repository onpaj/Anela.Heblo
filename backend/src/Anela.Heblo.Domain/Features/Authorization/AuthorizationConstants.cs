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
        /// Role for administrative functions
        /// </summary>
        public const string Administrator = "administrator";

        /// <summary>
        /// Role for super user functions (photobank settings, etc.)
        /// </summary>
        public const string SuperUser = "super_user";

        /// <summary>
        /// Base role required for application access
        /// </summary>
        public const string HebloUser = "heblo_user";

        /// <summary>
        /// Role required for managing the Knowledge Base (upload, delete documents)
        /// </summary>
        public const string KnowledgeBaseManager = "knowledge_base_manager";

        /// <summary>
        /// Role required for generating leaflets and articles (GenAI features)
        /// </summary>
        public const string GenAiUser = "genai_user";

        /// <summary>
        /// Role for tagging photos in the photobank
        /// </summary>
        public const string MarketingWriter = "marketing_writer";
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
        public const string GenAiUser = "GenAiUser";
    }
}