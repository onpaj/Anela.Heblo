using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.PackingMaterials;

public class PackingMaterialConfiguration : IEntityTypeConfiguration<PackingMaterial>
{
    public void Configure(EntityTypeBuilder<PackingMaterial> builder)
    {
        builder.ToTable("PackingMaterial", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.ConsumptionRate)
            .IsRequired()
            .HasColumnType("decimal(18,6)");

        builder.Property(e => e.ConsumptionType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.CurrentQuantity)
            .IsRequired()
            .HasColumnType("decimal(18,6)");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.UpdatedAt)
            .IsRequired(false)
            .HasColumnType("timestamp without time zone");

        // Index for efficient name searches
        builder.HasIndex(e => e.Name)
            .HasDatabaseName("IX_PackingMaterial_Name");
    }
}