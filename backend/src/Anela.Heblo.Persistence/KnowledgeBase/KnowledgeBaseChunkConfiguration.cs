using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;

namespace Anela.Heblo.Persistence.KnowledgeBase;

public class KnowledgeBaseChunkConfiguration : IEntityTypeConfiguration<KnowledgeBaseChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseChunk> builder)
    {
        builder.ToTable("KnowledgeBaseChunks", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Content)
            .IsRequired();

        builder.Property(e => e.ChunkIndex)
            .IsRequired();

        // Map float[] in domain to Vector (pgvector) in database
        builder.Property(e => e.Embedding)
            .HasColumnType("vector(1536)")
            .HasConversion(
                v => new Vector(v),
                v => v.Memory.ToArray());

        builder.HasIndex(e => e.DocumentId);
    }
}
