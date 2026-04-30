using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionConfiguration : IEntityTypeConfiguration<ImportedMarketingTransaction>
{
    public void Configure(EntityTypeBuilder<ImportedMarketingTransaction> builder)
    {
        builder.ToTable("ImportedMarketingTransactions", "public");

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

        builder.HasIndex(e => new { e.Platform, e.TransactionId })
            .IsUnique()
            .HasDatabaseName("IX_ImportedMarketingTransactions_Platform_TransactionId");
    }
}
