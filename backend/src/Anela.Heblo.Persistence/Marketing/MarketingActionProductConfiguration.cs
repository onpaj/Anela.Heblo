using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Marketing
{
    public class MarketingActionProductConfiguration : IEntityTypeConfiguration<MarketingActionProduct>
    {
        public void Configure(EntityTypeBuilder<MarketingActionProduct> builder)
        {
            builder.ToTable("MarketingActionProducts", "public");

            // Composite primary key
            builder.HasKey(x => new { x.MarketingActionId, x.ProductCodePrefix });

            builder.Property(x => x.ProductCodePrefix)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired()
                .AsUtcTimestamp();

            // Index for product code prefix lookups
            builder.HasIndex(x => x.ProductCodePrefix)
                .HasDatabaseName("IX_MarketingActionProducts_ProductCodePrefix");
        }
    }
}
