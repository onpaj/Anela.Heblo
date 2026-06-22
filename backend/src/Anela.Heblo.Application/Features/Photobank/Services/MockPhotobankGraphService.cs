namespace Anela.Heblo.Application.Features.Photobank.Services;

/// <summary>
/// No-op implementation of IPhotobankGraphService used in local development and environments
/// where real authentication (Azure AD) is not configured.
/// </summary>
public class MockPhotobankGraphService : IPhotobankGraphService
{
    private static readonly byte[] MinimalPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x00, 0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC,
        0x33, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    public Task<GraphDeltaResult> GetDeltaAsync(
        string driveId,
        string rootItemId,
        string? deltaLink,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GraphDeltaResult
        {
            Items = [],
            NewDeltaLink = string.Empty,
        });
    }

    public Task<string> ResolveItemIdAsync(string driveId, string folderPath, CancellationToken cancellationToken = default)
        => Task.FromResult("mock-item-id");

    public Task<GetThumbnailResult> GetThumbnailAsync(
        string driveId,
        string fileId,
        ThumbnailSize size,
        CancellationToken cancellationToken = default)
    {
        var thumbnail = new GraphThumbnail(
            new MemoryStream(MinimalPng),
            "image/png",
            MinimalPng.Length);
        return Task.FromResult<GetThumbnailResult>(new GetThumbnailResult.Success(thumbnail));
    }
}
