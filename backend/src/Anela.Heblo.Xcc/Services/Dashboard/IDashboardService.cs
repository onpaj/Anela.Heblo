using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Xcc.Services.Dashboard;

public interface IDashboardService
{
    Task<UserDashboardSettings> GetUserSettingsAsync(string userId);
    Task SaveUserSettingsAsync(string userId, UserDashboardSettings settings);
    Task<IEnumerable<TileData>> GetTileDataAsync(string userId, Dictionary<string, string>? tileParameters = null);
}