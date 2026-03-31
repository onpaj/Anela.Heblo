using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.KnowledgeBase;

public class KnowledgeBaseChunkConfiguration : IEntityTypeConfiguration<KnowledgeBaseChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseChunk> builder)
    {
        builder.ToTable("KnowledgeBaseChunks", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Content)
            .IsRequired();

        builder.Property(e => e.Summary)
            .IsRequired()
            .HasDefaultValue(string.Empty);

        builder.Property(e => e.DocumentType)
            .IsRequired()
            .HasDefaultValue(DocumentType.KnowledgeBase)
            .HasConversion<int>();

        builder.Property(e => e.ChunkIndex)
            .IsRequired();

        // Embedding column is managed via raw SQL migration (vector(3072) type)
        builder.Ignore(e => e.Embedding);

        builder.HasIndex(e => e.DocumentId);
    }
}
