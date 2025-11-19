using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.PackingMaterials;

public class PackingMaterialLogConfiguration : IEntityTypeConfiguration<PackingMaterialLog>
{
    public void Configure(EntityTypeBuilder<PackingMaterialLog> builder)
    {
        builder.ToTable("PackingMaterialLog", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.PackingMaterialId)
            .IsRequired();

        builder.Property(e => e.Date)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(e => e.OldQuantity)
            .IsRequired()
            .HasColumnType("decimal(18,6)");

        builder.Property(e => e.NewQuantity)
            .IsRequired()
            .HasColumnType("decimal(18,6)");

        builder.Property(e => e.LogType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.UserId)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        // Computed column for ChangeAmount (not mapped as it's calculated in entity)
        builder.Ignore(e => e.ChangeAmount);

        // Index for efficient date range queries
        builder.HasIndex(e => new { e.PackingMaterialId, e.Date })
            .HasDatabaseName("IX_PackingMaterialLog_MaterialId_Date");

        // Index for log type queries
        builder.HasIndex(e => e.LogType)
            .HasDatabaseName("IX_PackingMaterialLog_LogType");

        // Foreign key relationship to PackingMaterial table
        builder.HasIndex(e => e.PackingMaterialId)
            .HasDatabaseName("IX_PackingMaterialLog_PackingMaterialId");
    }
}