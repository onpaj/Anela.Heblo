namespace Anela.Heblo.Application.Features.Dashboard.Contracts;

public class UserDashboardSettingsDto
{
    public UserDashboardTileDto[] Tiles { get; set; } = Array.Empty<UserDashboardTileDto>();
    public DateTime LastModified { get; set; }
}