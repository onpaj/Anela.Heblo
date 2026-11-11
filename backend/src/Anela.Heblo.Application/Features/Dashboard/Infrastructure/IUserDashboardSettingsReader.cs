using Anela.Heblo.Application.Features.Dashboard.Contracts;

namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

public interface IUserDashboardSettingsReader
{
    /// <summary>
    /// Returns the effective dashboard settings for the user: persisted settings plus any AutoShow tiles
    /// not yet provisioned (or the full default set for a user with no settings). Strictly read-only -
    /// never writes to the repository, never mutates tracked entities and takes no lock.
    /// </summary>
    Task<UserDashboardSettingsDto> GetEffectiveSettingsAsync(string userId, CancellationToken cancellationToken = default);
}
