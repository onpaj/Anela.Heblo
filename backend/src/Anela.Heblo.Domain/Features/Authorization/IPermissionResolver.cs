namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>Resolves a user's effective permissions (group closure), with caching.
/// super_user is handled by the caller (claims transformation) from the token, not here.</summary>
public interface IPermissionResolver
{
    /// <summary>Resolves DB-derived effective permissions for an Entra object id.
    /// Materializes the AppUser on first call. Returns empty for inactive/unknown users.</summary>
    Task<EffectivePermissions> ResolveAsync(
        string entraObjectId, string? email, string? displayName, CancellationToken ct = default);

    /// <summary>Drops any cached entry for this Entra object id (used by admin writes).</summary>
    void InvalidateCache(string entraObjectId);
}
