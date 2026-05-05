using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletGenerationConfiguration : IEntityTypeConfiguration<LeafletGeneration>
{
    public void Configure(EntityTypeBuilder<LeafletGeneration> builder)
    {
        builder.ToTable("LeafletGenerations", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Topic).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Audience).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Length).IsRequired().HasMaxLength(50);
        builder.Property(x => x.FinalMarkdown).IsRequired();
        builder.Property(x => x.KbSourceCount).IsRequired();
        builder.Property(x => x.LeafletSourceCount).IsRequired();
        builder.Property(x => x.DurationMs).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UserId).IsRequired(false).HasMaxLength(200);
        builder.Property(x => x.PrecisionScore).IsRequired(false);
        builder.Property(x => x.StyleScore).IsRequired(false);
        builder.Property(x => x.FeedbackComment).IsRequired(false).HasColumnType("text");

        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.PrecisionScore)
            .HasFilter("\"PrecisionScore\" IS NOT NULL");
    }
}
