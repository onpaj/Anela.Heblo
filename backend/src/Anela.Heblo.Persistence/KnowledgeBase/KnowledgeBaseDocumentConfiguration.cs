using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.KnowledgeBase;

public class KnowledgeBaseDocumentConfiguration : IEntityTypeConfiguration<KnowledgeBaseDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseDocument> builder)
    {
        builder.ToTable("KnowledgeBaseDocuments", "public");

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

        builder.Property(e => e.DriveId).IsRequired(false);
        builder.Property(e => e.GraphItemId).IsRequired(false);

        builder.HasIndex(e => e.ContentHash)
            .IsUnique();

        builder.HasMany(e => e.Chunks)
            .WithOne(e => e.Document)
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.DriveId, e.GraphItemId })
            .IsUnique()
            .HasFilter("\"GraphItemId\" IS NOT NULL")
            .HasDatabaseName("IX_KnowledgeBaseDocuments_DriveId_GraphItemId");

        builder.HasIndex(e => e.Status);

        builder.HasIndex(e => e.ContentType)
            .HasDatabaseName("ix_knowledgebase_documents_contenttype");
    }
}
