using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Marketing
{
    public class MarketingActionConfiguration : IEntityTypeConfiguration<MarketingAction>
    {
        public void Configure(EntityTypeBuilder<MarketingAction> builder)
        {
            builder.ToTable("MarketingActions", "public");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(x => x.Description)
                .HasMaxLength(5000)
                .IsRequired(false);

            builder.Property(x => x.ActionType)
                .IsRequired();

            builder.Property(x => x.StartDate)
                .IsRequired()
                .AsUtcTimestamp();

            builder.Property(x => x.EndDate)
                .IsRequired(false)
                .AsUtcTimestamp();

            builder.Property(x => x.CreatedAt)
                .IsRequired()
                .AsUtcTimestamp();

            builder.Property(x => x.ModifiedAt)
                .IsRequired()
                .AsUtcTimestamp();

            builder.Property(x => x.CreatedByUserId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.CreatedByUsername)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(x => x.ModifiedByUserId)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(x => x.ModifiedByUsername)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(x => x.DeletedAt)
                .IsRequired(false)
                .AsUtcTimestamp();

            builder.Property(x => x.DeletedByUserId)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(x => x.DeletedByUsername)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(x => x.OutlookEventId)
                .HasMaxLength(500)
                .IsRequired(false);

            builder.Property(x => x.OutlookSyncedAt)
                .IsRequired(false)
                .AsUtcTimestamp();

            builder.Property(x => x.OutlookSyncStatus)
                .IsRequired()
                .HasConversion<string>();

            builder.Property(x => x.OutlookSyncError)
                .HasMaxLength(1000)
                .IsRequired(false);

            // Soft delete filter
            builder.HasQueryFilter(x => !x.IsDeleted);

            // Indexes for performance
            builder.HasIndex(x => x.StartDate)
                .HasDatabaseName("IX_MarketingActions_StartDate");

            builder.HasIndex(x => x.EndDate)
                .HasDatabaseName("IX_MarketingActions_EndDate");

            builder.HasIndex(x => new { x.IsDeleted, x.StartDate, x.EndDate })
                .HasDatabaseName("IX_MarketingActions_IsDeleted_StartDate_EndDate");

            builder.HasIndex(x => x.ActionType)
                .HasDatabaseName("IX_MarketingActions_ActionType");

            builder.HasIndex(x => x.OutlookEventId)
                .IsUnique()
                .HasFilter("\"OutlookEventId\" IS NOT NULL")
                .HasDatabaseName("IX_MarketingActions_OutlookEventId");

            builder.HasIndex(x => x.OutlookSyncStatus)
                .HasDatabaseName("IX_MarketingActions_OutlookSyncStatus");

            // Navigation properties
            builder.HasMany(x => x.ProductAssociations)
                .WithOne(x => x.MarketingAction)
                .HasForeignKey(x => x.MarketingActionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(x => x.FolderLinks)
                .WithOne(x => x.MarketingAction)
                .HasForeignKey(x => x.MarketingActionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
