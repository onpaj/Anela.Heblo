using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Anela.Heblo.Domain.Features.Journal;

namespace Anela.Heblo.Persistence.Configurations.Features.Journal
{
    public class JournalEntryTagAssignmentConfiguration : IEntityTypeConfiguration<JournalEntryTagAssignment>
    {
        public void Configure(EntityTypeBuilder<JournalEntryTagAssignment> builder)
        {
            builder.ToTable("JournalEntryTagAssignments");

            // Composite primary key
            builder.HasKey(x => new { x.JournalEntryId, x.TagId });

            builder.Property(x => x.CreatedAt)
                .IsRequired();
        }
    }
}