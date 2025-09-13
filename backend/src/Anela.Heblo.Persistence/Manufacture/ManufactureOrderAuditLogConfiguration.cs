using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture;

public class ManufactureOrderAuditLogConfiguration : IEntityTypeConfiguration<ManufactureOrderAuditLog>
{
    public void Configure(EntityTypeBuilder<ManufactureOrderAuditLog> builder)
    {
        builder.ToTable("ManufactureOrderAuditLogs", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Timestamp)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.User)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Action)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.Details)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.OldValue)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(x => x.NewValue)
            .HasMaxLength(500)
            .IsRequired(false);

        // Indexes for performance
        builder.HasIndex(x => x.ManufactureOrderId)
            .HasDatabaseName("IX_ManufactureOrderAuditLogs_ManufactureOrderId");

        builder.HasIndex(x => x.Timestamp)
            .HasDatabaseName("IX_ManufactureOrderAuditLogs_Timestamp");

        builder.HasIndex(x => x.Action)
            .HasDatabaseName("IX_ManufactureOrderAuditLogs_Action");
    }
}