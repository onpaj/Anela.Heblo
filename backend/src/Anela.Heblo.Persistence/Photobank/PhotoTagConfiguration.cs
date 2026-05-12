using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Photobank
{
    public class PhotoTagConfiguration : IEntityTypeConfiguration<PhotoTag>
    {
        public void Configure(EntityTypeBuilder<PhotoTag> builder)
        {
            builder.ToTable("PhotoTags", "public");
            builder.HasKey(x => new { x.PhotoId, x.TagId });
            builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
            builder.HasIndex(x => new { x.TagId, x.PhotoId })
                .HasDatabaseName("IX_PhotoTags_TagId_PhotoId");
            builder.HasOne(x => x.Tag)
                .WithMany(x => x.PhotoTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
