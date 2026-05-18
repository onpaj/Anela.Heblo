using Anela.Heblo.Domain.Features.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Logistics.CarrierCooling;

public class CarrierCoolingSettingConfiguration : IEntityTypeConfiguration<CarrierCoolingSetting>
{
    public void Configure(EntityTypeBuilder<CarrierCoolingSetting> builder)
    {
        builder.ToTable("CarrierCoolingSettings", "public");

        builder.HasKey(e => new { e.Carrier, e.DeliveryHandling });

        builder.Property(e => e.Carrier)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.DeliveryHandling)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.Cooling)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(e => e.ModifiedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ModifiedBy)
            .IsRequired()
            .HasMaxLength(200);
    }
}
