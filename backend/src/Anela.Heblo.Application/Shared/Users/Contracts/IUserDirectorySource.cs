namespace Anela.Heblo.Application.Shared.Users.Contracts;

/// <summary>
/// Shared/Users-owned abstraction over "give me every user in the directory". Implemented by
/// the Authorization module via an adapter (see development_guidelines.md, "Cross-Module
/// Communication Example"). No other module namespace may be referenced from this file.
/// </summary>
public interface IUserDirectorySource
{
    Task<IReadOnlyList<UserDirectoryEntry>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>A minimal, provider-agnostic projection of a directory user.</summary>
public sealed class UserDirectoryEntry
{
    public string? EntraObjectId { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
}
