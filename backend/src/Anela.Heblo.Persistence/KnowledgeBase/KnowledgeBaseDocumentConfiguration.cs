using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.KnowledgeBase;

public class KnowledgeBaseDocumentConfiguration : IEntityTypeConfiguration<KnowledgeBaseDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseDocument> builder)
    {
        builder.ToTable("KnowledgeBaseDocuments", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Filename)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.SourcePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<DocumentStatus>(v, true));

        builder.Property(e => e.DocumentType)
            .IsRequired()
            .HasDefaultValue(DocumentType.KnowledgeBase)
            .HasConversion<int>();

        builder.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.IndexedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ContentHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(e => e.ContentHash)
            .IsUnique();

        builder.HasMany(e => e.Chunks)
            .WithOne(e => e.Document)
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.SourcePath)
            .IsUnique();

        builder.HasIndex(e => e.Status);

        builder.HasIndex(e => e.ContentType)
            .HasDatabaseName("ix_knowledgebase_documents_contenttype");
    }
}
