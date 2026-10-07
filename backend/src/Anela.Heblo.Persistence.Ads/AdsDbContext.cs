using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Ads;

/// <summary>
/// The <c>ads</c> schema: normalised Google Ads / Meta Ads / Sklik data at management granularity
/// (ADR-008). Lives in Heblo_V3 next to the other reporting schemas (ADR-007) so Metabase can join
/// ad cost to shoptet_raw revenue. Holds no customer PII.
/// </summary>
public class AdsDbContext : DbContext
{
    public const string SchemaName = "ads";
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<AdAccount> Accounts => Set<AdAccount>();
    public DbSet<AdEntity> Entities => Set<AdEntity>();
    public DbSet<AdDailyFact> DailyFacts => Set<AdDailyFact>();
    public DbSet<AdSearchTermDaily> SearchTermsDaily => Set<AdSearchTermDaily>();
    public DbSet<AdChangeEvent> ChangeEvents => Set<AdChangeEvent>();
    public DbSet<AdSyncState> SyncStates => Set<AdSyncState>();

    public AdsDbContext(DbContextOptions<AdsDbContext> options) : base(options) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(SchemaName);
        ConfigureAccounts(builder);
        ConfigureEntities(builder);
        ConfigureDailyFacts(builder);
        ConfigureSearchTerms(builder);
        ConfigureChangeEvents(builder);
        ConfigureSyncState(builder);
    }

    private static void ConfigureAccounts(ModelBuilder builder) =>
        builder.Entity<AdAccount>(e =>
        {
            e.ToTable("ad_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.Platform).HasColumnName("platform").IsRequired();
            e.Property(x => x.ExternalId).HasColumnName("external_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            e.Property(x => x.TimeZone).HasColumnName("time_zone").IsRequired();
            e.Property(x => x.IsManaged).HasColumnName("is_managed");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.Platform, x.ExternalId })
                .IsUnique()
                .HasDatabaseName("ux_ad_accounts_platform_external_id");
        });

    private static void ConfigureEntities(ModelBuilder builder) =>
        builder.Entity<AdEntity>(e =>
        {
            e.ToTable("ad_entities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Level).HasColumnName("level").IsRequired();
            e.Property(x => x.ExternalId).HasColumnName("external_id").IsRequired();
            e.Property(x => x.ParentId).HasColumnName("parent_id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").IsRequired();
            e.Property(x => x.AttributesJson).HasColumnName("attributes").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<AdAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AdEntity>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.Level, x.ExternalId })
                .IsUnique()
                .HasDatabaseName("ux_ad_entities_account_level_external_id");
            e.HasIndex(x => x.ParentId).HasDatabaseName("ix_ad_entities_parent_id");
        });

    private static void ConfigureDailyFacts(ModelBuilder builder) =>
        builder.Entity<AdDailyFact>(e =>
        {
            e.ToTable("ad_daily_facts");
            e.HasKey(x => new { x.EntityId, x.Date });
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.Impressions).HasColumnName("impressions");
            e.Property(x => x.Clicks).HasColumnName("clicks");
            e.Property(x => x.Cost).HasColumnName("cost").HasColumnType("numeric(18,4)");
            e.Property(x => x.Conversions).HasColumnName("conversions").HasColumnType("numeric(18,4)");
            e.Property(x => x.ConversionValue).HasColumnName("conversion_value").HasColumnType("numeric(18,4)");
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasOne<AdEntity>().WithMany().HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date).HasDatabaseName("ix_ad_daily_facts_date");
        });

    private static void ConfigureSearchTerms(ModelBuilder builder) =>
        builder.Entity<AdSearchTermDaily>(e =>
        {
            e.ToTable("ad_search_term_daily");
            e.HasKey(x => new { x.AdGroupEntityId, x.Date, x.SearchTerm, x.MatchType });
            e.Property(x => x.AdGroupEntityId).HasColumnName("ad_group_entity_id");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.SearchTerm).HasColumnName("search_term");
            e.Property(x => x.MatchType).HasColumnName("match_type");
            e.Property(x => x.Impressions).HasColumnName("impressions");
            e.Property(x => x.Clicks).HasColumnName("clicks");
            e.Property(x => x.Cost).HasColumnName("cost").HasColumnType("numeric(18,4)");
            e.Property(x => x.Conversions).HasColumnName("conversions").HasColumnType("numeric(18,4)");
            e.Property(x => x.ConversionValue).HasColumnName("conversion_value").HasColumnType("numeric(18,4)");
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasOne<AdEntity>().WithMany().HasForeignKey(x => x.AdGroupEntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date).HasDatabaseName("ix_ad_search_term_daily_date");
        });

    private static void ConfigureChangeEvents(ModelBuilder builder) =>
        builder.Entity<AdChangeEvent>(e =>
        {
            e.ToTable("ad_change_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.ExternalEventId).HasColumnName("external_event_id").IsRequired();
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.Actor).HasColumnName("actor");
            e.Property(x => x.ActorKind).HasColumnName("actor_kind").IsRequired();
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.EntityExternalRef).HasColumnName("entity_external_ref");
            e.Property(x => x.ChangeType).HasColumnName("change_type").IsRequired();
            e.Property(x => x.OldValueJson).HasColumnName("old_value").HasColumnType("jsonb");
            e.Property(x => x.NewValueJson).HasColumnName("new_value").HasColumnType("jsonb");
            e.Property(x => x.Source).HasColumnName("source").IsRequired();
            e.Property(x => x.Origin).HasColumnName("origin").IsRequired();
            e.Property(x => x.MatchedExecutionId).HasColumnName("matched_execution_id");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasOne<AdAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AdEntity>().WithMany().HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.Source, x.ExternalEventId })
                .IsUnique()
                .HasDatabaseName("ux_ad_change_events_account_source_external_event_id");
            e.HasIndex(x => x.OccurredAt).HasDatabaseName("ix_ad_change_events_occurred_at");
            e.HasIndex(x => x.EntityId).HasDatabaseName("ix_ad_change_events_entity_id");
        });

    private static void ConfigureSyncState(ModelBuilder builder) =>
        builder.Entity<AdSyncState>(e =>
        {
            e.ToTable("sync_state");
            e.HasKey(x => new { x.Platform, x.AccountExternalId, x.Stream });
            e.Property(x => x.Platform).HasColumnName("platform");
            e.Property(x => x.AccountExternalId).HasColumnName("account_external_id");
            e.Property(x => x.Stream).HasColumnName("stream");
            e.Property(x => x.Watermark).HasColumnName("watermark");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.LastSuccessAt).HasColumnName("last_success_at");
            e.Property(x => x.LastError).HasColumnName("last_error");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });
}
