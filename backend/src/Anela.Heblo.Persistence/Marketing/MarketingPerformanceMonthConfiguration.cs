using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Marketing;

public class MarketingPerformanceMonthConfiguration : IEntityTypeConfiguration<MarketingPerformanceMonth>
{
    public void Configure(EntityTypeBuilder<MarketingPerformanceMonth> builder)
    {
        builder.ToTable("MarketingPerformanceMonths", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnType("integer").ValueGeneratedOnAdd();
        builder.Property(e => e.Year).IsRequired();
        builder.Property(e => e.Month).IsRequired();
        builder.Property(e => e.RetailOrderCount).IsRequired();
        builder.Property(e => e.RetailRevenueWithVat).IsRequired().HasColumnType("numeric(18,2)");
        builder.Property(e => e.WholesaleOrderCount).IsRequired();
        builder.Property(e => e.WholesaleRevenueWithVat).IsRequired().HasColumnType("numeric(18,2)");
        builder.Property(e => e.SkippedEurInvoiceCount).IsRequired();
        builder.Property(e => e.IsLocked).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.RevenueComputedAt).HasColumnType("timestamp with time zone");
        builder.Property(e => e.CostsComputedAt).HasColumnType("timestamp with time zone");
        builder.Property(e => e.LastError).HasColumnType("text");
        builder.Ignore(e => e.Key);
        builder.HasIndex(e => new { e.Year, e.Month }).IsUnique().HasDatabaseName("IX_MarketingPerformanceMonths_Year_Month");
        builder.HasMany(e => e.ChannelCosts).WithOne(c => c.Month).HasForeignKey(c => c.MonthId).OnDelete(DeleteBehavior.Cascade);
    }
}
