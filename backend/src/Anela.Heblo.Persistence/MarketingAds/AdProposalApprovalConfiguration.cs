using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdProposalApprovalConfiguration : IEntityTypeConfiguration<AdProposalApproval>
{
    public void Configure(EntityTypeBuilder<AdProposalApproval> builder)
    {
        builder.ToTable("AdProposalApprovals", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Principal).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(2000);
        builder.Property(x => x.At).IsRequired().AsUtcTimestamp();
        // One decision per principal per version.
        builder.HasIndex(x => new { x.ProposalId, x.Version, x.Principal }).IsUnique();
    }
}
