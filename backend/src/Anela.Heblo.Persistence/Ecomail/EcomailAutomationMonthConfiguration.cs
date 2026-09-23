using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Ecomail;

public class EcomailAutomationMonthConfiguration : IEntityTypeConfiguration<EcomailAutomationMonth>
{
    public void Configure(EntityTypeBuilder<EcomailAutomationMonth> builder)
    {
        builder.ToTable("EcomailAutomationMonths", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ComputedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.LastError).HasMaxLength(2000).IsRequired(false);

        builder.HasIndex(x => new { x.PipelineId, x.Year, x.Month }).IsUnique();
    }
}
