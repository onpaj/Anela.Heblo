using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Dashboard;

public class UserDashboardTile : Entity<int>
{
    public string UserId { get; set; } = string.Empty;
    public string TileId { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    // Navigation property
    public UserDashboardSettings DashboardSettings { get; set; } = null!;
}
