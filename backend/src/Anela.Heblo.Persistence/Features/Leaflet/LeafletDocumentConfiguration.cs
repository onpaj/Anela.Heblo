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

        builder.HasIndex(x => x.ContentHash)
            .HasDatabaseName("IX_LeafletDocuments_ContentHash");

        builder.HasIndex(x => x.SourcePath)
            .IsUnique()
            .HasDatabaseName("IX_LeafletDocuments_SourcePath");

        builder.HasMany(x => x.Chunks)
            .WithOne(x => x.Document)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
