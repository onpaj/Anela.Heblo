using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Journal
{
    public class JournalEntryTagConfiguration : IEntityTypeConfiguration<JournalEntryTag>
    {
        public void Configure(EntityTypeBuilder<JournalEntryTag> builder)
        {
            builder.ToTable("JournalEntryTags", "public");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.Color)
                .HasMaxLength(7)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired()
                .AsUtcTimestamp();

            builder.Property(x => x.CreatedByUserId)
                .HasMaxLength(100)
                .IsRequired();

            // Unique index on name
            builder.HasIndex(x => x.Name)
                .IsUnique()
                .HasDatabaseName("IX_JournalEntryTags_Name");

            // Navigation properties
            builder.HasMany(x => x.TagAssignments)
                .WithOne(x => x.Tag)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}