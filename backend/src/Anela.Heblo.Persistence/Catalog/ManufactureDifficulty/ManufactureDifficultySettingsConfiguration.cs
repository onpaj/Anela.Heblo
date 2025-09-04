using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.ManufactureDifficulty;

public class ManufactureDifficultySettingsConfiguration : IEntityTypeConfiguration<ManufactureDifficultySetting>
{
    public void Configure(EntityTypeBuilder<ManufactureDifficultySetting> builder)
    {
        builder.ToTable("ManufactureDifficultySettings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProductCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.DifficultyValue)
            .IsRequired();

        builder.Property(x => x.ValidFrom)
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.ValidTo)
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.CreatedBy)
            .IsRequired()
            .HasMaxLength(100);

        // Indexes for better query performance
        builder.HasIndex(x => x.ProductCode)
            .HasDatabaseName("IX_ManufactureDifficultySettings_ProductCode");

        builder.HasIndex(x => new { x.ProductCode, x.ValidFrom, x.ValidTo })
            .HasDatabaseName("IX_ManufactureDifficultySettings_ProductCode_Validity");

        // Check constraints for business rules
        builder.HasCheckConstraint("CK_ManufactureDifficultySettings_ValidDates",
            "\"ValidFrom\" IS NULL OR \"ValidTo\" IS NULL OR \"ValidFrom\" < \"ValidTo\"");
    }
}