using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdProposalConfiguration : IEntityTypeConfiguration<AdProposal>
{
    public void Configure(EntityTypeBuilder<AdProposal> builder)
    {
        builder.ToTable("AdProposals", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AccountExternalId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActionType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedByPrincipal).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedByKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.ExpiresAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.UpdatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.ExecutionStartedAt).AsUtcTimestamp();
        builder.Property(x => x.ExecutionFinishedAt).AsUtcTimestamp();
        builder.Property(x => x.RevertedAt).AsUtcTimestamp();
        // App-managed token (not xmin): portable to the InMemory provider used by handler tests.
        builder.Property(x => x.Revision).IsConcurrencyToken();

        builder.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.ProposalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Approvals).WithOne().HasForeignKey(a => a.ProposalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Executions).WithOne().HasForeignKey(e => e.ProposalId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.Status, x.ExpiresAt });
        builder.HasIndex(x => x.CreatedByPrincipal);
        builder.HasIndex(x => x.AgentRunId);
    }
}
