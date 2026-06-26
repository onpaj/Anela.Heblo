using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Dashboard;

public class UserDashboardSettings : Entity<int>
{
    public string UserId { get; set; } = string.Empty;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    // Navigation property
    public ICollection<UserDashboardTile> Tiles { get; set; } = new List<UserDashboardTile>();
}
