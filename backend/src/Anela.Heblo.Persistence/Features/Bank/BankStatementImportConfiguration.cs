using Anela.Heblo.Domain.Features.Bank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Bank;

/// <summary>
/// EF Core configuration for BankStatementImport entity
/// </summary>
public class BankStatementImportConfiguration : IEntityTypeConfiguration<BankStatementImport>
{
    public void Configure(EntityTypeBuilder<BankStatementImport> builder)
    {
        builder.ToTable("BankStatements", "dbo");

        builder.HasKey(e => e.Id);

        // Explicitly map Id column
        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .HasColumnType("integer");

        builder.Property(e => e.TransferId)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("TransferId")
            .HasColumnType("character varying(100)");

        builder.Property(e => e.StatementDate)
            .IsRequired()
            .HasColumnName("StatementDate")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ImportDate)
            .IsRequired()
            .HasColumnName("ImportDate")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.Account)
            .IsRequired()
            .HasColumnName("Account")
            .HasColumnType("text");

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasColumnName("Currency")
            .HasColumnType("integer")
            .HasConversion<int>();

        builder.Property(e => e.ItemCount)
            .IsRequired()
            .HasColumnName("ItemCount")
            .HasColumnType("integer");

        builder.Property(e => e.ImportResult)
            .IsRequired()
            .HasColumnName("ImportResult")
            .HasColumnType("text");

        // Create indexes for efficient queries
        builder.HasIndex(e => e.ImportDate)
            .HasDatabaseName("IX_BankStatements_ImportDate");

        builder.HasIndex(e => e.StatementDate)
            .HasDatabaseName("IX_BankStatements_StatementDate");

        builder.HasIndex(e => e.Account)
            .HasDatabaseName("IX_BankStatements_Account");

        builder.HasIndex(e => e.TransferId)
            .IsUnique()
            .HasDatabaseName("IX_BankStatements_TransferId");
    }
}