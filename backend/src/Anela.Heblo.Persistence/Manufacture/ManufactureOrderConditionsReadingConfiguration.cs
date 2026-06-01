using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture;

public class ManufactureOrderConditionsReadingConfiguration : IEntityTypeConfiguration<ManufactureOrderConditionsReading>
{
    public void Configure(EntityTypeBuilder<ManufactureOrderConditionsReading> builder)
    {
        builder.ToTable("ManufactureOrderConditionsReadings", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Stage)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.InnerTemperature)
            .HasColumnType("numeric(5,2)")
            .IsRequired(false);

        builder.Property(x => x.InnerHumidity)
            .HasColumnType("numeric(5,2)")
            .IsRequired(false);

        builder.Property(x => x.OuterTemperature)
            .HasColumnType("numeric(5,2)")
            .IsRequired(false);

        builder.Property(x => x.OuterHumidity)
            .HasColumnType("numeric(5,2)")
            .IsRequired(false);

        builder.Property(x => x.RecordedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.Source)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(x => x.ManufactureOrderId)
            .HasDatabaseName("IX_ManufactureOrderConditionsReadings_ManufactureOrderId");

        builder.HasIndex(x => new { x.ManufactureOrderId, x.Stage })
            .IsUnique()
            .HasDatabaseName("IX_ManufactureOrderConditionsReadings_ManufactureOrderId_Stage");
    }
}
