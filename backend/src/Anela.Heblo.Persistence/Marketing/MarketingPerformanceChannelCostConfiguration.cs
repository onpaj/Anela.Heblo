using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Marketing;

public class MarketingPerformanceChannelCostConfiguration : IEntityTypeConfiguration<MarketingPerformanceChannelCost>
{
    public void Configure(EntityTypeBuilder<MarketingPerformanceChannelCost> builder)
    {
        builder.ToTable("MarketingPerformanceChannelCosts", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnType("integer").ValueGeneratedOnAdd();
        builder.Property(e => e.ChannelCode).IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
        builder.Property(e => e.CostWithoutVat).IsRequired().HasColumnType("numeric(18,2)");
        builder.Property(e => e.InvoiceCount).IsRequired();
        builder.HasIndex(e => new { e.MonthId, e.ChannelCode }).IsUnique().HasDatabaseName("IX_MarketingPerformanceChannelCosts_MonthId_ChannelCode");
    }
}
