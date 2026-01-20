using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Stock;

public class StockUpOperationConfiguration : IEntityTypeConfiguration<StockUpOperation>
{
    public void Configure(EntityTypeBuilder<StockUpOperation> builder)
    {
        builder.ToTable("StockUpOperations", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentNumber)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ProductCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Amount)
            .IsRequired();

        builder.Property(x => x.SourceType)
            .IsRequired();

        builder.Property(x => x.SourceId)
            .IsRequired();

        builder.Property(x => x.State)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.SubmittedAt)
            .IsRequired(false)
            .AsUtcTimestamp();

        builder.Property(x => x.CompletedAt)
            .IsRequired(false)
            .AsUtcTimestamp();

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000)
            .IsRequired(false);

        // CRITICAL: UNIQUE constraint on DocumentNumber - Layer 1 protection
        builder.HasIndex(x => x.DocumentNumber)
            .IsUnique()
            .HasDatabaseName("IX_StockUpOperations_DocumentNumber_Unique");

        // Index for filtering by state
        builder.HasIndex(x => x.State)
            .HasDatabaseName("IX_StockUpOperations_State");

        // Composite index for source tracking
        builder.HasIndex(x => new { x.SourceType, x.SourceId })
            .HasDatabaseName("IX_StockUpOperations_Source");

        // Index for failed operations queries
        builder.HasIndex(x => new { x.State, x.CreatedAt })
            .HasDatabaseName("IX_StockUpOperations_State_CreatedAt");
    }
}
