using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Anela.Heblo.Domain.Features.Journal;

namespace Anela.Heblo.Persistence.Configurations.Features.Journal
{
    public class JournalEntryProductFamilyConfiguration : IEntityTypeConfiguration<JournalEntryProductFamily>
    {
        public void Configure(EntityTypeBuilder<JournalEntryProductFamily> builder)
        {
            builder.ToTable("JournalEntryProductFamilies");

            // Composite primary key
            builder.HasKey(x => new { x.JournalEntryId, x.ProductCodePrefix });

            builder.Property(x => x.ProductCodePrefix)
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            // Index for prefix lookups
            builder.HasIndex(x => x.ProductCodePrefix)
                .HasDatabaseName("IX_JournalEntryProductFamilies_ProductCodePrefix");
        }
    }
}