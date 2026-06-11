using Anela.Heblo.Domain.Features.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Shared.Users;

/// <summary>
/// Resolves user identifiers to display names by looking up the AppUser directory.
/// The directory is small and changes rarely, so the whole identifier→name lookup is
/// cached briefly to avoid a full-table scan on every feedback page load.
/// </summary>
public sealed class UserDisplayNameResolver : IUserDisplayNameResolver
{
    private const string CacheKey = "UserDisplayNameResolver:Lookup";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IAuthorizationRepository _repository;
    private readonly IMemoryCache _cache;

    public UserDisplayNameResolver(IAuthorizationRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<IReadOnlyDictionary<string, string?>> ResolveAsync(
        IEnumerable<string> identifiers,
        CancellationToken cancellationToken = default)
    {
        var distinct = identifiers
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        var lookup = await GetLookupAsync(cancellationToken);

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in distinct)
        {
            result[id] = lookup.TryGetValue(id, out var name) ? name : null;
        }

        return result;
    }

    /// <summary>Cached identifier→display name map, keyed by both Entra object id and email.</summary>
    private async Task<IReadOnlyDictionary<string, string>> GetLookupAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, string>? cached) && cached is not null)
        {
            return cached;
        }

        var users = await _repository.GetAllUsersAsync(cancellationToken);

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            var displayName = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.Email;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(user.EntraObjectId))
            {
                lookup[user.EntraObjectId] = displayName;
            }

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                lookup[user.Email] = displayName;
            }
        }

        _cache.Set(CacheKey, (IReadOnlyDictionary<string, string>)lookup, CacheTtl);
        return lookup;
    }
}
