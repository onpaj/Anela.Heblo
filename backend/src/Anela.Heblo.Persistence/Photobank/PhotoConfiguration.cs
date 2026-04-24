using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Photobank
{
    public class PhotoConfiguration : IEntityTypeConfiguration<Photo>
    {
        public void Configure(EntityTypeBuilder<Photo> builder)
        {
            builder.ToTable("Photos", "public");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.SharePointFileId).HasMaxLength(500).IsRequired();
            builder.HasIndex(x => x.SharePointFileId).IsUnique().HasDatabaseName("IX_Photos_SharePointFileId");
            builder.Property(x => x.FolderPath).HasMaxLength(2000).IsRequired();
            builder.Property(x => x.FileName).HasMaxLength(200).IsRequired();
            builder.Property(x => x.SharePointWebUrl).HasMaxLength(2000);
            builder.Property(x => x.DriveId).HasMaxLength(500);
            builder.Property(x => x.MimeType).HasMaxLength(50);
            builder.Property(x => x.IndexedAt).IsRequired().AsUtcTimestamp();
            builder.Property(x => x.ModifiedAt).IsRequired().AsUtcTimestamp();
            builder.HasIndex(x => x.FolderPath).HasDatabaseName("IX_Photos_FolderPath");
            builder.HasMany(x => x.Tags)
                .WithOne(x => x.Photo)
                .HasForeignKey(x => x.PhotoId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
