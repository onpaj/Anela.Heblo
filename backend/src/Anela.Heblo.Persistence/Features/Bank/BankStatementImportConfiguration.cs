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

        builder.Property(e => e.StatementDate)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ImportDate)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.Account)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.ItemCount)
            .IsRequired();

        builder.Property(e => e.ImportResult)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.ExtraProperties)
            .HasMaxLength(4000);

        builder.Property(e => e.ConcurrencyStamp)
            .HasMaxLength(50);

        builder.Property(e => e.CreationTime)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.CreatorId)
            .HasMaxLength(100);

        builder.Property(e => e.LastModificationTime)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.LastModifierId)
            .HasMaxLength(100);

        // Create indexes for efficient queries
        builder.HasIndex(e => e.ImportDate)
            .HasDatabaseName("IX_BankStatements_ImportDate");

        builder.HasIndex(e => e.StatementDate)
            .HasDatabaseName("IX_BankStatements_StatementDate");

        builder.HasIndex(e => e.Account)
            .HasDatabaseName("IX_BankStatements_Account");
    }
}