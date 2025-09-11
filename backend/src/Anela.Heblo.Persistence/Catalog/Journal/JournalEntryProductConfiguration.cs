using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Journal
{
    public class JournalEntryProductConfiguration : IEntityTypeConfiguration<JournalEntryProduct>
    {
        public void Configure(EntityTypeBuilder<JournalEntryProduct> builder)
        {
            builder.ToTable("JournalEntryProducts", "public");

            // Composite primary key
            builder.HasKey(x => new { x.JournalEntryId, ProductCode = x.ProductCodePrefix });

            builder.Property(x => x.ProductCodePrefix)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired()
                .AsUtcTimestamp();

            // Index for product code lookups
            builder.HasIndex(x => x.ProductCodePrefix)
                .HasDatabaseName("IX_JournalEntryProducts_ProductCode");
        }
    }
}