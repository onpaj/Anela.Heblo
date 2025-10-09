namespace Anela.Heblo.API.Controllers.Dashboard;

public class UserDashboardSettingsDto
{
    public UserDashboardTileDto[] Tiles { get; set; } = Array.Empty<UserDashboardTileDto>();
    public DateTime LastModified { get; set; }
}

public class UserDashboardTileDto
{
    public string TileId { get; set; } = string.Empty;
    public bool IsVisible { get; set; }
    public int DisplayOrder { get; set; }
}