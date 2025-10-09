namespace Anela.Heblo.Xcc.Domain;

public class UserDashboardSettings : Entity<int>
{
    public string UserId { get; set; } = string.Empty;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public ICollection<UserDashboardTile> Tiles { get; set; } = new List<UserDashboardTile>();
}