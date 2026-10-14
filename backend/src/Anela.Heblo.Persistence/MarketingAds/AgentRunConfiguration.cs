using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("AgentRuns", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AgentName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Principal).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.FinishedAt).AsUtcTimestamp();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Summary).HasColumnType("text");
        builder.Property(x => x.DataScopesReadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CostCzk).HasColumnType("numeric(18,2)");
        builder.HasIndex(x => x.StartedAt);
    }
}
