using Anela.Heblo.Domain.Features.Bank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Bank;

/// <summary>
/// EF Core configuration for the BankImportState watermark entity.
/// PascalCase columns to match the model snapshot / BankStatementImportConfiguration.
/// </summary>
public class BankImportStateConfiguration : IEntityTypeConfiguration<BankImportState>
{
    public void Configure(EntityTypeBuilder<BankImportState> builder)
    {
        builder.ToTable("BankImportStates", "public");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .HasColumnType("character varying(100)");

        builder.Property(e => e.Account)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("Account")
            .HasColumnType("character varying(100)");

        builder.Property(e => e.LastValidImportDate)
            .HasColumnName("LastValidImportDate")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.LastRunStartedAt)
            .HasColumnName("LastRunStartedAt")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.LastRunFinishedAt)
            .HasColumnName("LastRunFinishedAt")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.LastRunStatus)
            .HasColumnName("LastRunStatus")
            .HasColumnType("character varying(20)");

        builder.Property(e => e.LastErrorMessage)
            .HasColumnName("LastErrorMessage")
            .HasColumnType("character varying(2000)");

        builder.Property(e => e.ConsecutiveFailureCount)
            .IsRequired()
            .HasColumnName("ConsecutiveFailureCount")
            .HasColumnType("integer");

        builder.HasIndex(e => e.Account)
            .IsUnique()
            .HasDatabaseName("IX_BankImportStates_Account");
    }
}
