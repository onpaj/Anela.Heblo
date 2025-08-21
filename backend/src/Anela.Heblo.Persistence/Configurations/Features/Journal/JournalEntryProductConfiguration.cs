using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Anela.Heblo.Domain.Features.Journal;

namespace Anela.Heblo.Persistence.Configurations.Features.Journal
{
    public class JournalEntryProductConfiguration : IEntityTypeConfiguration<JournalEntryProduct>
    {
        public void Configure(EntityTypeBuilder<JournalEntryProduct> builder)
        {
            builder.ToTable("JournalEntryProducts");

            // Composite primary key
            builder.HasKey(x => new { x.JournalEntryId, x.ProductCode });

            builder.Property(x => x.ProductCode)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            // Index for product code lookups
            builder.HasIndex(x => x.ProductCode)
                .HasDatabaseName("IX_JournalEntryProducts_ProductCode");
        }
    }
}