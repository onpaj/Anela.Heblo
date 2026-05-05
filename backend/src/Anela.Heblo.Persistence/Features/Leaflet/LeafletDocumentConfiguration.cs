using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletDocumentConfiguration : IEntityTypeConfiguration<LeafletDocument>
{
    public void Configure(EntityTypeBuilder<LeafletDocument> builder)
    {
        builder.ToTable("LeafletDocuments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Filename).IsRequired();
        builder.Property(x => x.SourcePath).IsRequired();
        builder.Property(x => x.ContentType).IsRequired();
        builder.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.IngestedAt).IsRequired().HasColumnType("timestamp without time zone");
        builder.Property(x => x.WordCount).IsRequired();
        builder.Property(x => x.DriveId).IsRequired(false);
        builder.Property(x => x.GraphItemId).IsRequired(false);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<LeafletDocumentStatus>(v, true));

        builder.Property(x => x.IndexedAt)
            .HasColumnType("timestamp without time zone");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("IX_LeafletDocuments_Status");

        builder.HasIndex(x => x.ContentHash)
            .HasDatabaseName("IX_LeafletDocuments_ContentHash");

        builder.HasIndex(d => new { d.DriveId, d.GraphItemId })
            .IsUnique()
            .HasFilter("\"GraphItemId\" IS NOT NULL")
            .HasDatabaseName("IX_LeafletDocuments_DriveId_GraphItemId");

        builder.HasMany(x => x.Chunks)
            .WithOne(x => x.Document)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
