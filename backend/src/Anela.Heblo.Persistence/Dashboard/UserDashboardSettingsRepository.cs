using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Dashboard;

public class UserDashboardSettingsRepository : IUserDashboardSettingsRepository
{
    private readonly ApplicationDbContext _context;

    public UserDashboardSettingsRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UserDashboardSettings?> GetByUserIdAsync(string userId)
    {
        return await _context.UserDashboardSettings
            .Include(x => x.Tiles)
            .FirstOrDefaultAsync(x => x.UserId == userId);
    }

    public async Task<UserDashboardSettings> AddAsync(UserDashboardSettings settings)
    {
        _context.UserDashboardSettings.Add(settings);
        await _context.SaveChangesAsync();
        return settings;
    }

    public async Task UpdateAsync(UserDashboardSettings settings)
    {
        _context.UserDashboardSettings.Update(settings);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(string userId)
    {
        var settings = await GetByUserIdAsync(userId);
        if (settings != null)
        {
            _context.UserDashboardSettings.Remove(settings);
            await _context.SaveChangesAsync();
        }
    }
}