using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class MeetingAccessGrantConfiguration : IEntityTypeConfiguration<MeetingAccessGrant>
{
    public void Configure(EntityTypeBuilder<MeetingAccessGrant> builder)
    {
        builder.ToTable("MeetingAccessGrants", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserEmail)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(x => x.UserDisplayName)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(x => x.GrantedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.GrantedByUserEmail)
            .IsRequired()
            .HasMaxLength(320);

        builder.HasIndex(x => new { x.MeetingTranscriptId, x.UserEmail })
            .IsUnique()
            .HasDatabaseName("UX_MeetingAccessGrants_TranscriptId_UserEmail");
    }
}
