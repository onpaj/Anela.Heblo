using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Anela.Heblo.Domain.Features.Journal;

namespace Anela.Heblo.Persistence.Configurations.Features.Journal
{
    public class JournalEntryTagConfiguration : IEntityTypeConfiguration<JournalEntryTag>
    {
        public void Configure(EntityTypeBuilder<JournalEntryTag> builder)
        {
            builder.ToTable("JournalEntryTags");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.Color)
                .HasMaxLength(7)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired();

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