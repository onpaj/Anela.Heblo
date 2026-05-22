using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Logistics.GiftSettings;

public class GiftSettingConfiguration : IEntityTypeConfiguration<GiftSetting>
{
    public void Configure(EntityTypeBuilder<GiftSetting> builder)
    {
        builder.ToTable("GiftSettings", "public");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.IsEnabled).IsRequired();

        builder.Property(e => e.ThresholdCzk)
            .IsRequired()
            .HasColumnType("numeric(18,2)");

        builder.Property(e => e.Text)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.ModifiedBy).HasMaxLength(256);
    }
}
