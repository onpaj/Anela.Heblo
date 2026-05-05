namespace Anela.Heblo.Application.Features.Photobank.Services;

/// <summary>
/// No-op implementation of IPhotobankGraphService used in local development and environments
/// where real authentication (Azure AD) is not configured.
/// </summary>
public class MockPhotobankGraphService : IPhotobankGraphService
{
    public Task<GraphDeltaResult> GetDeltaAsync(
        string driveId,
        string rootItemId,
        string? deltaLink,
        CancellationToken ct = default)
    {
        return Task.FromResult(new GraphDeltaResult
        {
            Items = [],
            NewDeltaLink = string.Empty,
        });
    }

    public Task<string> ResolveItemIdAsync(string driveId, string folderPath, CancellationToken ct = default)
        => Task.FromResult("mock-item-id");
}
