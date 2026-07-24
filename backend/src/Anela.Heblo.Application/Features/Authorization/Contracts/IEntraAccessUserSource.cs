namespace Anela.Heblo.Application.Features.Authorization.Contracts;

public interface IEntraAccessUserSource
{
    /// <exception cref="EntraAccessSourceAuthException">
    /// Thrown when the underlying identity provider auth/configuration fails.
    /// </exception>
    /// <exception cref="EntraAccessSourceException">
    /// Thrown for other unexpected failures resolving Base role members.
    /// </exception>
    Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct);
}

public sealed record EntraAccessUserRecord(string Id, string Email, string DisplayName);
