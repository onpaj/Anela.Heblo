using Anela.Heblo.Domain.Features.PackingMaterials;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.PackingMaterials;

public class PackingMaterialDailyRunConfiguration : IEntityTypeConfiguration<PackingMaterialDailyRun>
{
    public void Configure(EntityTypeBuilder<PackingMaterialDailyRun> builder)
    {
        builder.ToTable("PackingMaterialDailyRuns", "public");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Date)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(e => e.ProcessedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.MaterialsProcessed)
            .IsRequired()
            .HasDefaultValue(0);

        builder.HasIndex(e => e.Date)
            .HasDatabaseName("IX_PackingMaterialDailyRuns_Date")
            .IsUnique();
    }
}
