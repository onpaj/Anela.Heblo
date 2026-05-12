using Anela.Heblo.Domain.Features.PackingMaterials;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.PackingMaterials;

public class PackingMaterialConsumptionConfiguration : IEntityTypeConfiguration<PackingMaterialConsumption>
{
    public void Configure(EntityTypeBuilder<PackingMaterialConsumption> builder)
    {
        builder.ToTable("PackingMaterialConsumptions", "public");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.PackingMaterialId)
            .IsRequired();

        builder.Property(e => e.Date)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(e => e.ConsumptionType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.InvoiceId)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(e => e.ProductCode)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(e => e.ProductQuantity)
            .IsRequired(false)
            .HasColumnType("decimal(18,6)");

        builder.Property(e => e.Amount)
            .IsRequired()
            .HasColumnType("decimal(18,6)");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.HasOne<PackingMaterial>()
            .WithMany()
            .HasForeignKey(c => c.PackingMaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.PackingMaterialId)
            .HasDatabaseName("IX_PackingMaterialConsumptions_PackingMaterialId");

        builder.HasIndex(e => new { e.Date, e.PackingMaterialId })
            .HasDatabaseName("IX_PackingMaterialConsumptions_Date_MaterialId");

        builder.HasIndex(e => new { e.Date, e.InvoiceId })
            .HasDatabaseName("IX_PackingMaterialConsumptions_Date_InvoiceId");

        builder.HasIndex(e => new { e.Date, e.ProductCode })
            .HasDatabaseName("IX_PackingMaterialConsumptions_Date_ProductCode");
    }
}
