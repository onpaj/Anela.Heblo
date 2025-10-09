using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Xcc.Services.Dashboard;

public interface IUserDashboardSettingsRepository
{
    Task<UserDashboardSettings?> GetByUserIdAsync(string userId);
    Task<UserDashboardSettings> AddAsync(UserDashboardSettings settings);
    Task UpdateAsync(UserDashboardSettings settings);
    Task DeleteAsync(string userId);
}