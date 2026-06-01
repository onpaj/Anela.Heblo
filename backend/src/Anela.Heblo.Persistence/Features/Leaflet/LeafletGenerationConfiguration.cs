using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletGenerationConfiguration : IEntityTypeConfiguration<LeafletGeneration>
{
    public void Configure(EntityTypeBuilder<LeafletGeneration> builder)
    {
        builder.ToTable("LeafletGenerations");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Topic).HasMaxLength(200).IsRequired();
        builder.Property(g => g.Audience).HasMaxLength(50).IsRequired();
        builder.Property(g => g.Length).HasMaxLength(50).IsRequired();
        builder.Property(g => g.FinalMarkdown).IsRequired();
        builder.Property(g => g.UserId).HasMaxLength(200);
        builder.Property(g => g.FeedbackComment).HasColumnType("text");
        builder.HasIndex(g => g.CreatedAt);
        builder.HasIndex(g => g.UserId);
        builder.HasIndex(g => g.PrecisionScore)
            .HasFilter("\"PrecisionScore\" IS NOT NULL");
    }
}
