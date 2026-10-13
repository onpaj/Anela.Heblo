using Anela.Heblo.Persistence.Ga4.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Ga4;

/// <summary>
/// The <c>ga4_agg</c> schema: aggregated Google Analytics 4 web traffic, pulled through the GA4
/// Data API. It lives in the same database as the rest of Heblo (ADR-007) so Metabase — which has
/// no cross-database joins in any edition — can join it to the other reporting schemas.
///
/// Aggregates only, never events: nothing in the reporting backlog needs event grain, and the
/// Postgres server is a single-vCore burstable shared with production.
/// </summary>
public class Ga4DbContext : DbContext
{
    public const string SchemaName = "ga4_agg";

    public DbSet<TrafficMonthly> TrafficMonthly => Set<TrafficMonthly>();
    public DbSet<TrafficTotalDaily> TrafficTotalDaily => Set<TrafficTotalDaily>();
    public DbSet<TrafficDaily> TrafficDaily => Set<TrafficDaily>();
    public DbSet<LandingPageDaily> LandingPageDaily => Set<LandingPageDaily>();
    public DbSet<ConversionsDaily> ConversionsDaily => Set<ConversionsDaily>();
    public DbSet<PageDaily> PageDaily => Set<PageDaily>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    public Ga4DbContext(DbContextOptions<Ga4DbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(SchemaName);

        builder.Entity<TrafficMonthly>(e =>
        {
            e.ToTable("traffic_monthly");
            e.HasKey(x => x.Month);
            e.Property(x => x.Month).HasColumnName("month");
            e.Property(x => x.Sessions).HasColumnName("sessions");
            e.Property(x => x.TotalUsers).HasColumnName("total_users");
            e.Property(x => x.NewUsers).HasColumnName("new_users");
            e.Property(x => x.ScreenPageViews).HasColumnName("screen_page_views");
            e.Property(x => x.EngagedSessions).HasColumnName("engaged_sessions");
            e.Property(x => x.UserEngagementSeconds).HasColumnName("user_engagement_seconds");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
        });

        builder.Entity<TrafficTotalDaily>(e =>
        {
            e.ToTable("traffic_total_daily");
            e.HasKey(x => x.Date);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.Sessions).HasColumnName("sessions");
            e.Property(x => x.TotalUsers).HasColumnName("total_users");
            e.Property(x => x.NewUsers).HasColumnName("new_users");
            e.Property(x => x.ScreenPageViews).HasColumnName("screen_page_views");
            e.Property(x => x.EngagedSessions).HasColumnName("engaged_sessions");
            e.Property(x => x.UserEngagementSeconds).HasColumnName("user_engagement_seconds");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
        });

        builder.Entity<TrafficDaily>(e =>
        {
            e.ToTable("traffic_daily");
            e.HasKey(x => new { x.Date, x.ChannelGroup });
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.ChannelGroup).HasColumnName("channel_group");
            e.Property(x => x.Sessions).HasColumnName("sessions");
            e.Property(x => x.TotalUsers).HasColumnName("total_users");
            e.Property(x => x.NewUsers).HasColumnName("new_users");
            e.Property(x => x.ScreenPageViews).HasColumnName("screen_page_views");
            e.Property(x => x.EngagedSessions).HasColumnName("engaged_sessions");
            e.Property(x => x.UserEngagementSeconds).HasColumnName("user_engagement_seconds");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasIndex(x => x.Date).HasDatabaseName("ix_traffic_daily_date");
        });

        builder.Entity<LandingPageDaily>(e =>
        {
            e.ToTable("landing_page_daily");
            e.HasKey(x => new { x.Date, x.LandingPage });
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.LandingPage).HasColumnName("landing_page");
            e.Property(x => x.Sessions).HasColumnName("sessions");
            e.Property(x => x.EngagedSessions).HasColumnName("engaged_sessions");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasIndex(x => x.Date).HasDatabaseName("ix_landing_page_daily_date");
        });

        builder.Entity<ConversionsDaily>(e =>
        {
            e.ToTable("conversions_daily");
            e.HasKey(x => new { x.Date, x.ChannelGroup });
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.ChannelGroup).HasColumnName("channel_group");
            e.Property(x => x.Transactions).HasColumnName("transactions");
            e.Property(x => x.PurchaseRevenue).HasColumnName("purchase_revenue").HasColumnType("numeric(18,4)");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasIndex(x => x.Date).HasDatabaseName("ix_conversions_daily_date");
        });

        builder.Entity<PageDaily>(e =>
        {
            e.ToTable("page_daily");
            e.HasKey(x => new { x.Date, x.PagePath });
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.PagePath).HasColumnName("page_path");
            e.Property(x => x.PageTitle).HasColumnName("page_title");
            e.Property(x => x.ScreenPageViews).HasColumnName("screen_page_views");
            e.Property(x => x.Sessions).HasColumnName("sessions");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasIndex(x => x.Date).HasDatabaseName("ix_page_daily_date");
        });

        builder.Entity<SyncState>(e =>
        {
            e.ToTable("sync_state");
            e.HasKey(x => x.EntityName);
            e.Property(x => x.EntityName).HasColumnName("entity_name");
            e.Property(x => x.WatermarkDate).HasColumnName("watermark_date");
            e.Property(x => x.LastRunStartedAt).HasColumnName("last_run_started_at");
            e.Property(x => x.LastRunFinishedAt).HasColumnName("last_run_finished_at");
            e.Property(x => x.LastRunStatus).HasColumnName("last_run_status");
            e.Property(x => x.LastRunRowsFetched).HasColumnName("last_run_rows_fetched");
            e.Property(x => x.LastRunRowsUpserted).HasColumnName("last_run_rows_upserted");
            e.Property(x => x.TopNPerDay).HasColumnName("top_n_per_day");
            e.Property(x => x.LastErrorMessage).HasColumnName("last_error_message");
        });
    }
}
