using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletChunkConfiguration : IEntityTypeConfiguration<LeafletChunk>
{
    public void Configure(EntityTypeBuilder<LeafletChunk> builder)
    {
        builder.ToTable("LeafletChunks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DocumentId).IsRequired();
        builder.Property(x => x.ChunkIndex).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.Summary).IsRequired();
        builder.Property(x => x.WordCount).IsRequired();
        builder.Ignore(x => x.Embedding);

        builder.HasIndex(x => x.DocumentId)
            .HasDatabaseName("IX_LeafletChunks_DocumentId");
    }
}
