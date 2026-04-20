using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionConfiguration : IEntityTypeConfiguration<ImportedMarketingTransaction>
{
    public void Configure(EntityTypeBuilder<ImportedMarketingTransaction> builder)
    {
        builder.ToTable("imported_marketing_transactions", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .HasColumnType("integer")
            .ValueGeneratedOnAdd();

        builder.Property(e => e.TransactionId)
            .IsRequired()
            .HasMaxLength(255)
            .HasColumnName("TransactionId")
            .HasColumnType("character varying(255)");

        builder.Property(e => e.Platform)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("Platform")
            .HasColumnType("character varying(50)");

        builder.Property(e => e.Amount)
            .IsRequired()
            .HasColumnName("Amount")
            .HasColumnType("numeric(18,2)");

        builder.Property(e => e.TransactionDate)
            .IsRequired()
            .HasColumnName("TransactionDate")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ImportedAt)
            .IsRequired()
            .HasColumnName("ImportedAt")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.IsSynced)
            .IsRequired()
            .HasDefaultValue(false)
            .HasColumnName("IsSynced")
            .HasColumnType("boolean");

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("ErrorMessage")
            .HasColumnType("text");

        builder.Property(e => e.Description)
            .HasColumnName("Description")
            .HasColumnType("character varying(500)")
            .HasMaxLength(500);

        builder.Property(e => e.Currency)
            .HasColumnName("Currency")
            .HasColumnType("character varying(10)")
            .HasMaxLength(10);

        builder.Property(e => e.RawData)
            .HasColumnName("RawData")
            .HasColumnType("text");

        builder.HasIndex(e => new { e.Platform, e.TransactionId })
            .IsUnique()
            .HasDatabaseName("IX_imported_marketing_transactions_Platform_TransactionId");

        builder.HasIndex(e => e.ImportedAt)
            .HasDatabaseName("IX_imported_marketing_transactions_ImportedAt");

        builder.HasIndex(e => e.IsSynced)
            .HasFilter("\"IsSynced\" = false")
            .HasDatabaseName("IX_imported_marketing_transactions_IsSynced_False");
    }
}
