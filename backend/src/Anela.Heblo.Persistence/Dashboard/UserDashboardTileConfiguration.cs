using Anela.Heblo.Xcc.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Anela.Heblo.Persistence.Extensions;

namespace Anela.Heblo.Persistence.Dashboard;

public class UserDashboardTileConfiguration : IEntityTypeConfiguration<UserDashboardTile>
{
    public void Configure(EntityTypeBuilder<UserDashboardTile> builder)
    {
        builder.ToTable("UserDashboardTiles");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.UserId)
            .HasMaxLength(255)
            .IsRequired();
            
        builder.Property(x => x.TileId)
            .HasMaxLength(100)
            .IsRequired();
            
        builder.Property(x => x.IsVisible)
            .IsRequired();
            
        builder.Property(x => x.DisplayOrder)
            .IsRequired();
            
        builder.Property(x => x.LastModified)
            .AsUtcTimestamp()
            .IsRequired();
            
        // Composite index for efficient queries
        builder.HasIndex(x => new { x.UserId, x.TileId })
            .IsUnique();
            
        // Index for ordering tiles
        builder.HasIndex(x => new { x.UserId, x.DisplayOrder });
        
        // Foreign key relationship
        builder.HasOne(x => x.DashboardSettings)
            .WithMany(x => x.Tiles)
            .HasForeignKey(x => x.UserId)
            .HasPrincipalKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}