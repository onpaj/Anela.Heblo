using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

/// <summary>
/// Microsoft Graph implementation of IOneDriveService.
/// Uses application permissions (ITokenAcquisition) to access OneDrive files,
/// consistent with the existing GraphService pattern in this codebase.
/// </summary>
public class GraphOneDriveService : IOneDriveService
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly KnowledgeBaseOptions _options;
    private readonly ILogger<GraphOneDriveService> _logger;
    private const string GraphScope = "https://graph.microsoft.com/.default";
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    public GraphOneDriveService(
        ITokenAcquisition tokenAcquisition,
        IOptions<KnowledgeBaseOptions> options,
        ILogger<GraphOneDriveService> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<OneDriveFile>> ListInboxFilesAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Listing files in OneDrive inbox: {Path}", _options.OneDriveInboxPath);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
        using var client = CreateHttpClient(token);

        var encodedPath = Uri.EscapeDataString(_options.OneDriveInboxPath.TrimStart('/'));
        var url = $"{GraphBaseUrl}/me/drive/root:/{encodedPath}:/children?$filter=file ne null";

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var files = new List<OneDriveFile>();
        if (doc.RootElement.TryGetProperty("value", out var value))
        {
            foreach (var item in value.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                var webUrl = item.TryGetProperty("webUrl", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;
                var mimeType = "application/octet-stream";

                if (item.TryGetProperty("file", out var fileProp) &&
                    fileProp.TryGetProperty("mimeType", out var mimeProp))
                {
                    mimeType = mimeProp.GetString() ?? mimeType;
                }

                files.Add(new OneDriveFile(id, name, mimeType, webUrl));
            }
        }

        return files;
    }

    public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
    {
        _logger.LogDebug("Downloading file {FileId} from OneDrive", fileId);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
        using var client = CreateHttpClient(token);

        var url = $"{GraphBaseUrl}/me/drive/items/{fileId}/content";
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task MoveToArchivedAsync(string fileId, string filename, CancellationToken ct = default)
    {
        _logger.LogDebug("Moving file {Filename} ({FileId}) to archived folder", filename, fileId);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
        using var client = CreateHttpClient(token);

        var archivedFolderPath = _options.OneDriveArchivedPath.TrimStart('/');
        var body = JsonSerializer.Serialize(new
        {
            parentReference = new
            {
                path = $"/drive/root:/{archivedFolderPath}"
            }
        });

        var url = $"{GraphBaseUrl}/me/drive/items/{fileId}";
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static HttpClient CreateHttpClient(string token)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
