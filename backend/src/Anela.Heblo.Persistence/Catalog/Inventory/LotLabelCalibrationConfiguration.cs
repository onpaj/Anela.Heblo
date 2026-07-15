using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class LotLabelCalibrationConfiguration : IEntityTypeConfiguration<LotLabelCalibration>
{
    public void Configure(EntityTypeBuilder<LotLabelCalibration> builder)
    {
        builder.ToTable("LotLabelCalibrations", "public");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.PitchDots).IsRequired();

        builder.Property(e => e.DriftDotsPer100Labels)
            .IsRequired()
            .HasDefaultValue(LotLabelCalibration.DefaultDriftDotsPer100Labels);

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.ModifiedBy).HasMaxLength(256);
    }
}
