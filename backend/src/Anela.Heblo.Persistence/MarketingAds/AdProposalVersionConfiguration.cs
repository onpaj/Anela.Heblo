using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdProposalVersionConfiguration : IEntityTypeConfiguration<AdProposalVersion>
{
    public void Configure(EntityTypeBuilder<AdProposalVersion> builder)
    {
        builder.ToTable("AdProposalVersions", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ActionsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Reasoning).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
        builder.HasIndex(x => new { x.ProposalId, x.Version }).IsUnique();
    }
}
