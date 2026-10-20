using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdAuditEventConfiguration : IEntityTypeConfiguration<AdAuditEvent>
{
    public void Configure(EntityTypeBuilder<AdAuditEvent> builder)
    {
        builder.ToTable("AdAuditEvents", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.EventType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ActorPrincipal).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ActorKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.OccurredAt).IsRequired().AsUtcTimestamp();
        builder.HasOne(x => x.Proposal).WithMany().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ProposalId);
        builder.HasIndex(x => x.OccurredAt);
    }
}
