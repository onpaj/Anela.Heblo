using Anela.Heblo.Domain.Features.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Packaging;

public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.ToTable("Packages", "public");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.OrderCode).IsRequired().HasMaxLength(50);
        builder.Property(p => p.CustomerName).IsRequired().HasMaxLength(255);
        builder.Property(p => p.PackageNumber).IsRequired().HasMaxLength(50);
        builder.Property(p => p.TrackingNumber).HasMaxLength(100);
        builder.Property(p => p.ShippingProviderCode).IsRequired().HasMaxLength(50);
        builder.Property(p => p.ShippingProviderName).HasMaxLength(100);
        builder.Property(p => p.ShipmentGuid).IsRequired();
        builder.Property(p => p.PackedAt).IsRequired();
        builder.Property(p => p.PackedBy).HasMaxLength(255);
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.HasIndex(p => p.OrderCode);
        builder.HasIndex(p => p.PackedAt);
        builder.HasIndex(p => new { p.OrderCode, p.PackageNumber }).IsUnique();
    }
}
