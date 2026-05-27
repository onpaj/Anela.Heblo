using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.FeatureFlags;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Xcc.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence;

/// <summary>
/// Main application database context
/// Phase 1: Single DbContext for all modules
/// Phase 2: Will be split into module-specific contexts
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    //public DbSet<ScheduledTask> Tasks { get; set; }
    public DbSet<IssuedInvoice> IssuedInvoices { get; set; } = null!;
    public DbSet<IssuedInvoiceSyncData> IssuedInvoiceSyncData { get; set; } = null!;
    public DbSet<BankStatementImport> BankStatements { get; set; } = null!;
    //public DbSet<RecurringJob> Jobs { get; set; }
    public DbSet<TransportBox> TransportBoxes { get; set; }
    public DbSet<StockTakingRecord> StockTakings { get; set; }
    public DbSet<StockUpOperation> StockUpOperations { get; set; } = null!;
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
    public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; } = null!;
    public DbSet<PurchaseOrderHistory> PurchaseOrderHistory { get; set; } = null!;
    public DbSet<Package> Packages { get; set; } = null!;

    // Catalog module
    public DbSet<ManufactureDifficultySetting> ManufactureDifficultySettings { get; set; } = null!;
    public DbSet<ProductIngredientOrder> ProductIngredientOrders { get; set; } = null!;

    // Journal module
    public DbSet<JournalEntry> JournalEntries { get; set; } = null!;
    public DbSet<JournalEntryProduct> JournalEntryProducts { get; set; } = null!;
    public DbSet<JournalEntryTag> JournalEntryTags { get; set; } = null!;
    public DbSet<JournalEntryTagAssignment> JournalEntryTagAssignments { get; set; } = null!;

    // Gift Package Manufacturing module
    public DbSet<GiftPackageManufactureLog> GiftPackageManufactureLogs { get; set; } = null!;
    public DbSet<GiftPackageManufactureItem> GiftPackageManufactureItems { get; set; } = null!;

    // Manufacture Order Management module
    public DbSet<ManufactureOrder> ManufactureOrders { get; set; } = null!;
    public DbSet<ManufactureOrderSemiProduct> ManufactureOrderSemiProducts { get; set; } = null!;
    public DbSet<ManufactureOrderProduct> ManufactureOrderProducts { get; set; } = null!;
    public DbSet<ManufactureOrderNote> ManufactureOrderNotes { get; set; } = null!;

    // Manufactured Product Inventory module
    public DbSet<ManufacturedProductInventoryItem> ManufacturedProductInventoryItems { get; set; } = null!;
    public DbSet<ManufacturedProductInventoryLog> ManufacturedProductInventoryLogs { get; set; } = null!;

    // Dashboard module
    public DbSet<UserDashboardSettings> UserDashboardSettings { get; set; } = null!;
    public DbSet<UserDashboardTile> UserDashboardTiles { get; set; } = null!;

    // Invoice Classification module
    public DbSet<ClassificationRule> ClassificationRules { get; set; } = null!;
    public DbSet<ClassificationHistory> ClassificationHistory { get; set; } = null!;

    // Packing Materials module
    public DbSet<PackingMaterial> PackingMaterials { get; set; } = null!;
    public DbSet<PackingMaterialLog> PackingMaterialLogs { get; set; } = null!;
    public DbSet<PackingMaterialDailyRun> PackingMaterialDailyRuns { get; set; } = null!;

    // Carrier Cooling module
    public DbSet<CarrierCoolingSetting> CarrierCoolingSettings { get; set; } = null!;

    // Gift Settings module
    public DbSet<GiftSetting> GiftSettings { get; set; } = null!;

    // Background Jobs module
    public DbSet<RecurringJobConfiguration> RecurringJobConfigurations { get; set; } = null!;

    // Knowledge Base module
    public DbSet<KnowledgeBaseDocument> KnowledgeBaseDocuments { get; set; } = null!;
    public DbSet<KnowledgeBaseChunk> KnowledgeBaseChunks { get; set; } = null!;
    public DbSet<KnowledgeBaseQuestionLog> KnowledgeBaseQuestionLogs { get; set; } = null!;

    // Leaflet module
    public DbSet<LeafletDocument> LeafletDocuments { get; set; } = null!;
    public DbSet<LeafletChunk> LeafletChunks { get; set; } = null!;
    public DbSet<LeafletGeneration> LeafletGenerations { get; set; } = null!;

    // Article module
    public DbSet<Article> Articles { get; set; } = null!;
    public DbSet<ArticleSource> ArticleSources { get; set; } = null!;
    public DbSet<ArticleGenerationStep> ArticleGenerationSteps { get; set; } = null!;

    // Grid Layouts module
    public DbSet<GridLayout> GridLayouts { get; set; } = null!;

    // Marketing Invoices module
    public DbSet<ImportedMarketingTransaction> ImportedMarketingTransactions { get; set; } = null!;

    // Meeting Tasks module
    public DbSet<MeetingTranscript> MeetingTranscripts { get; set; } = null!;
    public DbSet<ProposedTask> ProposedTasks { get; set; } = null!;
    public DbSet<MeetingAccessGrant> MeetingAccessGrants { get; set; } = null!;

    // Data Quality module
    public DbSet<DqtRun> DqtRuns { get; set; } = null!;
    public DbSet<InvoiceDqtResult> InvoiceDqtResults { get; set; } = null!;
    public DbSet<DqtDriftResult> DqtDriftResults { get; set; } = null!;

    // Photobank
    public DbSet<PhotobankIndexRoot> PhotobankIndexRoots { get; set; } = null!;
    public DbSet<Photo> Photos { get; set; } = null!;
    public DbSet<Tag> PhotobankTags { get; set; } = null!;
    public DbSet<PhotoTag> PhotoTags { get; set; } = null!;
    public DbSet<TagRule> PhotobankTagRules { get; set; } = null!;

    // Marketing Calendar module
    public DbSet<MarketingAction> MarketingActions { get; set; } = null!;
    public DbSet<MarketingActionProduct> MarketingActionProducts { get; set; } = null!;
    public DbSet<MarketingActionFolderLink> MarketingActionFolderLinks { get; set; } = null!;

    // Smartsupp module
    public DbSet<SmartsuppConversation> SmartsuppConversations { get; set; } = null!;
    public DbSet<SmartsuppMessage> SmartsuppMessages { get; set; } = null!;
    public DbSet<SmartsuppContact> SmartsuppContacts { get; set; } = null!;
    public DbSet<SmartsuppWebhookAuditEntry> SmartsuppWebhookAuditEntries { get; set; } = null!;

    // Inventory module
    public DbSet<Lot> Lots { get; set; } = null!;
    public DbSet<Ean> Eans { get; set; } = null!;

    // Feature Flags module
    public DbSet<FeatureFlagOverride> FeatureFlagOverrides { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Only set up PostgreSQL extensions if using PostgreSQL provider
        if (Database.IsNpgsql())
        {
            modelBuilder.HasPostgresExtension("vector");
        }

        // Apply configurations from current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Handle DateTime conversion for PostgreSQL "timestamp without time zone"
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    // Convert UTC to Unspecified for PostgreSQL, and Unspecified to UTC when reading
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                            v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified), // Convert to Unspecified for PostgreSQL
                            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)          // Treat as UTC when reading
                        ));
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
                            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Unspecified) : null, // Convert to Unspecified for PostgreSQL
                            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null          // Treat as UTC when reading
                        ));
                    }
                }
            }
        }

        //modelBuilder.ConfigureScheduledTasks();
        //modelBuilder.ConfigureIssuedInvoices();
        //modelBuilder.ConfigureRecurringJobs();
        //modelBuilder.ConfigureTransportBoxes();
        //modelBuilder.ConfigureStockTaking();
    }
}