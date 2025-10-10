namespace Anela.Heblo.Application.Features.Dashboard.Contracts;

public class UserDashboardTileDto
{
    public string TileId { get; set; } = string.Empty;
    public bool IsVisible { get; set; }
    public int DisplayOrder { get; set; }
}