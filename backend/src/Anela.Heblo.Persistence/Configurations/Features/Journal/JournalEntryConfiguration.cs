using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Anela.Heblo.Domain.Features.Journal;

namespace Anela.Heblo.Persistence.Configurations.Features.Journal
{
    public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
    {
        public void Configure(EntityTypeBuilder<JournalEntry> builder)
        {
            builder.ToTable("JournalEntries");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired(false);

            builder.Property(x => x.Content)
                .HasMaxLength(10000)
                .IsRequired();

            builder.Property(x => x.EntryDate)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.Property(x => x.ModifiedAt)
                .IsRequired();

            builder.Property(x => x.CreatedByUserId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.ModifiedByUserId)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(x => x.DeletedByUserId)
                .HasMaxLength(100)
                .IsRequired(false);

            // Soft delete filter
            builder.HasQueryFilter(x => !x.IsDeleted);

            // Indexes for performance
            builder.HasIndex(x => x.EntryDate)
                .HasDatabaseName("IX_JournalEntries_EntryDate");

            builder.HasIndex(x => x.CreatedByUserId)
                .HasDatabaseName("IX_JournalEntries_CreatedByUserId");

            builder.HasIndex(x => new { x.IsDeleted, x.EntryDate })
                .HasDatabaseName("IX_JournalEntries_IsDeleted_EntryDate");

            // Navigation properties
            builder.HasMany(x => x.ProductAssociations)
                .WithOne(x => x.JournalEntry)
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(x => x.ProductFamilyAssociations)
                .WithOne(x => x.JournalEntry)
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(x => x.TagAssignments)
                .WithOne(x => x.JournalEntry)
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}