namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>The resolved authorization state for a user, as used by enforcement and /api/auth/me.</summary>
public sealed record EffectivePermissions(
    bool IsSuperUser,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<string> Groups)
{
    public static EffectivePermissions Empty { get; } =
        new(false, Array.Empty<string>(), Array.Empty<string>());
}
