using Anela.Heblo.Domain.Features.Journal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Journal
{
    public class JournalEntryTagAssignmentConfiguration : IEntityTypeConfiguration<JournalEntryTagAssignment>
    {
        public void Configure(EntityTypeBuilder<JournalEntryTagAssignment> builder)
        {
            builder.ToTable("JournalEntryTagAssignments", "public");

            // Composite primary key
            builder.HasKey(x => new { x.JournalEntryId, x.TagId });

            builder.Property(x => x.CreatedAt)
                .IsRequired();
        }
    }
}