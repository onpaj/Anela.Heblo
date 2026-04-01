using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

/// <summary>
/// Microsoft Graph implementation of IOneDriveService for SharePoint document libraries.
/// Uses application permissions (ITokenAcquisition) to access SharePoint files via drive ID.
/// Find a drive ID: GET /v1.0/sites/{siteId}/drives → copy the "id" of the target library.
/// </summary>
public class GraphOneDriveService : IOneDriveService
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GraphOneDriveService> _logger;
    private const string GraphScope = "https://graph.microsoft.com/.default";

    public GraphOneDriveService(
        ITokenAcquisition tokenAcquisition,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<GraphOneDriveService> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<OneDriveFile>> ListInboxFilesAsync(string driveId, string inboxPath, CancellationToken ct = default)
    {
        _logger.LogDebug("Listing files in SharePoint drive {DriveId} at path {Path}", driveId, inboxPath);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        var encodedPath = GraphApiHelpers.EncodePath(inboxPath);
        // Graph API does not support $filter on complex facet properties like 'file'.
        // Retrieve all children and skip folders (items without the 'file' facet) in code.
        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/root:/{encodedPath}:/children";

        var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await GraphApiHelpers.DeserializeAsync<GraphDriveItemCollection>(response, ct);

        var files = new List<OneDriveFile>();
        foreach (var item in result.Value)
        {
            // Skip folders — only items with the 'file' facet are actual files
            if (item.File is null)
                continue;

            files.Add(new OneDriveFile(item.Id, item.Name, item.File.MimeType, item.WebUrl));
        }

        return files;
    }

    public async Task<byte[]> DownloadFileAsync(string driveId, string fileId, CancellationToken ct = default)
    {
        _logger.LogDebug("Downloading file {FileId} from SharePoint drive {DriveId}", fileId, driveId);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{fileId}/content";
        var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task MoveToArchivedAsync(string driveId, string fileId, string filename, string archivedPath, CancellationToken ct = default)
    {
        _logger.LogDebug("Moving file {Filename} ({FileId}) to {ArchivedPath} in drive {DriveId}", filename, fileId, archivedPath, driveId);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        // Graph requires the destination folder's item ID — path-based parentReference is not supported for SharePoint drives.
        var resolver = new GraphFolderResolver(client, token, _cache, _logger);
        var folderItemId = await resolver.GetOrCreateFolderIdAsync(driveId, archivedPath, ct);

        var body = JsonSerializer.Serialize(new
        {
            parentReference = new
            {
                driveId,
                id = folderItemId
            }
        });

        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{fileId}";
        var request = GraphApiHelpers.CreateRequest(HttpMethod.Patch, url, token);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
