using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Logistics.StockTaking;

public class StockTakingConfiguration : IEntityTypeConfiguration<StockTakingRecord>
{
    public void Configure(EntityTypeBuilder<StockTakingRecord> builder)
    {
        builder.ToTable("StockTakingResults", "dbo");
        builder.HasKey(p => p.Id);

        // Configure Date column to use 'timestamp without time zone' for PostgreSQL
        builder.Property(p => p.Date)
            .HasColumnType("timestamp");
    }
}