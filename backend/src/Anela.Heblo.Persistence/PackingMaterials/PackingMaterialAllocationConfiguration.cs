using Anela.Heblo.Domain.Features.PackingMaterials;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.PackingMaterials;

public class PackingMaterialAllocationConfiguration : IEntityTypeConfiguration<PackingMaterialAllocation>
{
    public void Configure(EntityTypeBuilder<PackingMaterialAllocation> builder)
    {
        builder.ToTable("PackingMaterialAllocations", "public");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.PackingMaterialId)
            .IsRequired();

        builder.Property(e => e.ProductCode)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.AmountPerUnit)
            .IsRequired()
            .HasColumnType("decimal(18,6)");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.UpdatedAt)
            .IsRequired(false)
            .HasColumnType("timestamp without time zone");

        builder.HasOne<PackingMaterial>()
            .WithMany(pm => pm.Allocations)
            .HasForeignKey(a => a.PackingMaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.PackingMaterialId, e.ProductCode })
            .IsUnique()
            .HasDatabaseName("IX_PackingMaterialAllocations_MaterialId_ProductCode");
    }
}
