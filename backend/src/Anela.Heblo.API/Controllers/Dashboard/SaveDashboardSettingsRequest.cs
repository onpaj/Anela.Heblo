namespace Anela.Heblo.API.Controllers.Dashboard;

public class SaveDashboardSettingsRequest
{
    public UserDashboardTileDto[] Tiles { get; set; } = Array.Empty<UserDashboardTileDto>();
}