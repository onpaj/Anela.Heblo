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
        /// Base role required for application access
        /// </summary>
        public const string HebloUser = "heblo_user";

        /// <summary>
        /// Role required for managing the Knowledge Base (upload, delete documents)
        /// </summary>
        public const string KnowledgeBaseManager = "knowledge_base_manager";
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
    }
}