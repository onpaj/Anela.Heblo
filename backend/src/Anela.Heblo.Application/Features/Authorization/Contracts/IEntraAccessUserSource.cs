namespace Anela.Heblo.Application.Features.Authorization.Contracts;

public interface IEntraAccessUserSource
{
    Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct);
}

public sealed record EntraAccessUserRecord(string Id, string Email, string DisplayName);
