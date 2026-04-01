using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

internal class GraphFolderResolver
{
    private readonly HttpClient _client;
    private readonly string _token;
    private readonly IMemoryCache _cache;
    private readonly ILogger _logger;
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    internal GraphFolderResolver(HttpClient client, string token, IMemoryCache cache, ILogger logger)
    {
        _client = client;
        _token = token;
        _cache = cache;
        _logger = logger;
    }

    internal async Task<string> GetOrCreateFolderIdAsync(string driveId, string folderPath, CancellationToken ct)
    {
        var cacheKey = $"graph:folder-id:{driveId}:{folderPath}";
        if (_cache.TryGetValue(cacheKey, out string? cached))
            return cached!;

        var id = await ResolveOrCreateAsync(driveId, folderPath.Trim('/'), ct);
        _cache.Set(cacheKey, id, TimeSpan.FromMinutes(60));
        return id;
    }

    private async Task<string> ResolveOrCreateAsync(string driveId, string folderPath, CancellationToken ct)
    {
        var encodedPath = GraphApiHelpers.EncodePath(folderPath);
        var getUrl = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/root:/{encodedPath}:";
        var getResponse = await _client.SendAsync(GraphApiHelpers.CreateRequest(HttpMethod.Get, getUrl, _token), ct);

        if (getResponse.IsSuccessStatusCode)
        {
            var existing = await GraphApiHelpers.DeserializeAsync<GraphDriveItem>(getResponse, ct);
            return existing.Id;
        }

        if ((int)getResponse.StatusCode != 404)
            getResponse.EnsureSuccessStatusCode();

        _logger.LogInformation("Folder {FolderPath} not found in drive {DriveId}, creating it", folderPath, driveId);

        var lastSlash = folderPath.LastIndexOf('/');
        var folderName = lastSlash >= 0 ? folderPath[(lastSlash + 1)..] : folderPath;
        string parentId;

        if (lastSlash <= 0)
        {
            parentId = await GetRootIdAsync(driveId, ct);
        }
        else
        {
            var parentPath = folderPath[..lastSlash];
            parentId = await ResolveOrCreateAsync(driveId, parentPath, ct);
        }

        var body = JsonSerializer.Serialize(new
        {
            name = folderName,
            folder = new { },
        });

        var createUrl = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{parentId}/children";
        var createRequest = GraphApiHelpers.CreateRequest(HttpMethod.Post, createUrl, _token);
        createRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var createResponse = await _client.SendAsync(createRequest, ct);
        createResponse.EnsureSuccessStatusCode();

        var created = await GraphApiHelpers.DeserializeAsync<GraphDriveItem>(createResponse, ct);
        _logger.LogInformation("Created folder {FolderName} (id={Id}) in drive {DriveId}", folderName, created.Id, driveId);
        return created.Id;
    }

    private async Task<string> GetRootIdAsync(string driveId, CancellationToken ct)
    {
        var cacheKey = $"graph:root-id:{driveId}";
        if (_cache.TryGetValue(cacheKey, out string? cached))
            return cached!;

        var url = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/root";
        var response = await _client.SendAsync(GraphApiHelpers.CreateRequest(HttpMethod.Get, url, _token), ct);
        response.EnsureSuccessStatusCode();

        var root = await GraphApiHelpers.DeserializeAsync<GraphDriveItem>(response, ct);
        _cache.Set(cacheKey, root.Id, TimeSpan.FromMinutes(60));
        return root.Id;
    }
}
