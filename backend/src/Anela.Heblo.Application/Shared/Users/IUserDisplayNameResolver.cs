namespace Anela.Heblo.Application.Shared.Users;

/// <summary>
/// Resolves user identifiers (Entra object id or email, as stored by the LLM tools)
/// into human-readable display names. Cross-cutting, read-only, stateless.
/// </summary>
public interface IUserDisplayNameResolver
{
    /// <summary>
    /// Maps each supplied identifier to a display name (DisplayName, falling back to Email).
    /// Identifiers with no matching user resolve to <c>null</c>.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>> ResolveAsync(
        IEnumerable<string> identifiers,
        CancellationToken cancellationToken = default);
}
