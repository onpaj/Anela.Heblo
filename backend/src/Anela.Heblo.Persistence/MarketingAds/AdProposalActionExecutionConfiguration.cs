using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdProposalActionExecutionConfiguration : IEntityTypeConfiguration<AdProposalActionExecution>
{
    public void Configure(EntityTypeBuilder<AdProposalActionExecution> builder)
    {
        builder.ToTable("AdProposalActionExecutions", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AccountExternalId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActionType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TargetLevel).HasMaxLength(32).IsRequired();
        builder.Property(x => x.TargetExternalId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.BeforeValue).HasMaxLength(64);
        builder.Property(x => x.BeforeRawJson).HasColumnType("text");
        builder.Property(x => x.AfterValue).HasMaxLength(64);
        builder.Property(x => x.PlatformResourceId).HasMaxLength(200);
        builder.Property(x => x.PlatformResponseJson).HasColumnType("text");
        builder.Property(x => x.Error).HasColumnType("text");
        builder.Property(x => x.ExecutedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.RevertState).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.RevertedAt).AsUtcTimestamp();
        builder.Property(x => x.RevertResponseJson).HasColumnType("text");
        builder.Property(x => x.RevertError).HasColumnType("text");
        builder.HasIndex(x => new { x.ProposalId, x.Version, x.ActionIndex }).IsUnique();
        builder.HasIndex(x => new { x.Platform, x.AccountExternalId, x.ExecutedAt });
    }
}
