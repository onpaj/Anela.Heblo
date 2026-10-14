using Anela.Heblo.Domain.Features.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Packaging;

public class CarrierLabelOffsetConfiguration : IEntityTypeConfiguration<CarrierLabelOffset>
{
    public void Configure(EntityTypeBuilder<CarrierLabelOffset> builder)
    {
        builder.ToTable("CarrierLabelOffsets", "public");

        builder.HasKey(e => e.Carrier);

        builder.Property(e => e.Carrier)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.OffsetXMm).IsRequired();
        builder.Property(e => e.OffsetYMm).IsRequired();

        builder.Property(e => e.ModifiedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ModifiedBy)
            .IsRequired()
            .HasMaxLength(200);
    }
}
