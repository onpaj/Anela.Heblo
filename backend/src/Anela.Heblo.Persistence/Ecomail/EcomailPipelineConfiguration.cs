using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Ecomail;

public class EcomailPipelineConfiguration : IEntityTypeConfiguration<EcomailPipeline>
{
    public void Configure(EntityTypeBuilder<EcomailPipeline> builder)
    {
        builder.ToTable("EcomailPipelines", "public");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired(false).AsUtcTimestamp();
        builder.Property(x => x.UpdatedAt).IsRequired(false).AsUtcTimestamp();
        builder.Property(x => x.SyncedAt).IsRequired().AsUtcTimestamp();
    }
}
