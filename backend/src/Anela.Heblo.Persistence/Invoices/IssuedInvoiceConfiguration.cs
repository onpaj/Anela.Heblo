using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Invoices;

/// <summary>
/// EF Core configuration for IssuedInvoice entity
/// Maps to existing dbo.IssuedInvoice table
/// </summary>
public class IssuedInvoiceConfiguration : IEntityTypeConfiguration<IssuedInvoice>
{
    public void Configure(EntityTypeBuilder<IssuedInvoice> builder)
    {
        builder.ToTable("IssuedInvoice", "dbo");
        builder.HasKey(e => e.Id);

        // Primary properties
        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .IsRequired();

        builder.Property(e => e.InvoiceDate)
            .HasColumnName("InvoiceDate")
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.DueDate)
            .HasColumnName("DueDate")
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.TaxDate)
            .HasColumnName("TaxDate")
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.VarSymbol)
            .HasColumnName("VarSymbol");

        builder.Property(e => e.BillingMethod)
            .HasColumnName("BillingMethod")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.ShippingMethod)
            .HasColumnName("ShippingMethod")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.VatPayer)
            .HasColumnName("VatPayer");

        builder.Property(e => e.ItemsCount)
            .HasColumnName("ItemsCount")
            .IsRequired();

        builder.Property(e => e.Price)
            .HasColumnName("Price")
            .HasColumnType("numeric")
            .IsRequired();

        builder.Property(e => e.PriceC)
            .HasColumnName("PriceC")
            .HasColumnType("numeric")
            .IsRequired();

        builder.Property(e => e.Currency)
            .HasColumnName("Currency")
            .IsRequired();

        // Sync status fields
        builder.Property(e => e.IsSynced)
            .HasColumnName("IsSynced")
            .IsRequired();

        builder.Property(e => e.LastSyncTime)
            .HasColumnName("LastSyncTime")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("ErrorMessage");

        builder.Property(e => e.ErrorType)
            .HasColumnName("ErrorType")
            .HasConversion<int?>();

        builder.Property(e => e.CustomerName)
            .HasColumnName("CustomerName");

        builder.Property(e => e.SyncHistoryCount)
            .HasColumnName("SyncHistoryCount")
            .HasDefaultValue(0)
            .IsRequired();

        // Audit fields
        builder.Property(e => e.ExtraProperties)
            .HasColumnName("ExtraProperties");

        builder.Property(e => e.ConcurrencyStamp)
            .HasColumnName("ConcurrencyStamp")
            .HasMaxLength(40);

        builder.Property(e => e.CreationTime)
            .HasColumnName("CreationTime")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(e => e.CreatorId)
            .HasColumnName("CreatorId");

        builder.Property(e => e.LastModificationTime)
            .HasColumnName("LastModificationTime")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.LastModifierId)
            .HasColumnName("LastModifierId");

        // Relationship to sync history
        builder.HasMany(e => e.SyncHistory)
            .WithOne()
            .HasForeignKey(s => s.IssuedInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Ignore computed property
        builder.Ignore(e => e.IsCriticalError);

        // Indexes for efficient queries
        builder.HasIndex(e => e.InvoiceDate)
            .HasDatabaseName("IX_IssuedInvoice_InvoiceDate");

        builder.HasIndex(e => e.LastSyncTime)
            .HasDatabaseName("IX_IssuedInvoice_LastSyncTime");
    }
}