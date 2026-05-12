using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Marketing
{
    public class MarketingActionFolderLinkConfiguration : IEntityTypeConfiguration<MarketingActionFolderLink>
    {
        public void Configure(EntityTypeBuilder<MarketingActionFolderLink> builder)
        {
            builder.ToTable("MarketingActionFolderLinks", "public");

            // Composite primary key
            builder.HasKey(x => new { x.MarketingActionId, x.FolderKey });

            builder.Property(x => x.FolderKey)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.FolderType)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired()
                .AsUtcTimestamp();

            // Index for marketing action lookups
            builder.HasIndex(x => x.MarketingActionId)
                .HasDatabaseName("IX_MarketingActionFolderLinks_MarketingActionId");
        }
    }
}
