using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Analytics;

public class AnalyticsDbContext : DbContext
{
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<AccountingTemplate> AccountingTemplates => Set<AccountingTemplate>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("flexi_raw");

        builder.Entity<LedgerEntry>(e =>
        {
            e.ToTable("ledger_entry");
            e.HasKey(x => x.FlexiId);
            e.Property(x => x.FlexiId).HasColumnName("flexi_id").ValueGeneratedNever();
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.EntryDate).HasColumnName("entry_date");
            e.Property(x => x.AccountDebit).HasColumnName("account_debit");
            e.Property(x => x.AccountCredit).HasColumnName("account_credit");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.Currency).HasColumnName("currency");
            e.Property(x => x.CostCenter).HasColumnName("cost_center");
            e.Property(x => x.Period).HasColumnName("period");
            e.Property(x => x.DocumentType).HasColumnName("document_type");
            e.Property(x => x.Contact).HasColumnName("contact");
            e.Property(x => x.AccountingTemplate).HasColumnName("accounting_template");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.LastModified).HasColumnName("last_modified");
            e.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasIndex(x => x.EntryDate).HasDatabaseName("ix_ledger_entry_entry_date");
            e.HasIndex(x => x.CostCenter).HasDatabaseName("ix_ledger_entry_cost_center");
            e.HasIndex(x => x.AccountDebit).HasDatabaseName("ix_ledger_entry_account_debit");
            e.HasIndex(x => x.AccountCredit).HasDatabaseName("ix_ledger_entry_account_credit");
            e.HasIndex(x => x.LastModified).HasDatabaseName("ix_ledger_entry_last_modified");
        });

        builder.Entity<Department>(e =>
        {
            e.ToTable("department");
            e.HasKey(x => x.FlexiId);
            e.Property(x => x.FlexiId).HasColumnName("flexi_id").ValueGeneratedNever();
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.LastModified).HasColumnName("last_modified");
            e.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
        });

        builder.Entity<AccountingTemplate>(e =>
        {
            e.ToTable("accounting_template");
            e.HasKey(x => x.FlexiId);
            e.Property(x => x.FlexiId).HasColumnName("flexi_id").ValueGeneratedNever();
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.LastModified).HasColumnName("last_modified");
            e.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
        });

        builder.Entity<Contact>(e =>
        {
            e.ToTable("contact");
            e.HasKey(x => x.FlexiId);
            e.Property(x => x.FlexiId).HasColumnName("flexi_id").ValueGeneratedNever();
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Cin).HasColumnName("cin");
            e.Property(x => x.Vatin).HasColumnName("vatin");
            e.Property(x => x.LastModified).HasColumnName("last_modified");
            e.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
        });

        builder.Entity<SyncState>(e =>
        {
            e.ToTable("sync_state");
            e.HasKey(x => x.EntityName);
            e.Property(x => x.EntityName).HasColumnName("entity_name");
            e.Property(x => x.Watermark).HasColumnName("watermark");
            e.Property(x => x.LastRunStartedAt).HasColumnName("last_run_started_at");
            e.Property(x => x.LastRunFinishedAt).HasColumnName("last_run_finished_at");
            e.Property(x => x.LastRunStatus).HasColumnName("last_run_status");
            e.Property(x => x.LastRunRowsFetched).HasColumnName("last_run_rows_fetched");
            e.Property(x => x.LastRunRowsUpserted).HasColumnName("last_run_rows_upserted");
            e.Property(x => x.LastErrorMessage).HasColumnName("last_error_message");
        });
    }
}
