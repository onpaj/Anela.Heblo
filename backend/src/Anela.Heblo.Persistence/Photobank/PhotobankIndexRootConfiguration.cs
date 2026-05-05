using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Photobank
{
    public class PhotobankIndexRootConfiguration : IEntityTypeConfiguration<PhotobankIndexRoot>
    {
        public void Configure(EntityTypeBuilder<PhotobankIndexRoot> builder)
        {
            builder.ToTable("PhotobankIndexRoots", "public");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.SharePointPath).HasMaxLength(1000).IsRequired();
            builder.Property(x => x.DisplayName).HasMaxLength(200);
            builder.Property(x => x.CreatedByUserId).HasMaxLength(100);
            builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
            builder.Property(x => x.DriveId).HasMaxLength(500);
            builder.Property(x => x.RootItemId).HasMaxLength(500);
            builder.Property(x => x.DeltaLink).HasMaxLength(2000);
            builder.Property(x => x.LastIndexedAt);
        }
    }
}
