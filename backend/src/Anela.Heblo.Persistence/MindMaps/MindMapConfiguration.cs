using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MindMaps;

public class MindMapConfiguration : IEntityTypeConfiguration<MindMap>
{
    public void Configure(EntityTypeBuilder<MindMap> builder)
    {
        builder.ToTable("MindMaps", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).IsRequired(false).HasMaxLength(2000);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(20).HasConversion<string>();
        builder.Property(x => x.CurrentJson).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.LastError).IsRequired(false);
        builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.UpdatedAt).IsRequired().AsUtcTimestamp();

        builder.HasMany(x => x.Meetings)
            .WithOne(x => x.MindMap)
            .HasForeignKey(x => x.MindMapId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Versions)
            .WithOne(x => x.MindMap)
            .HasForeignKey(x => x.MindMapId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
