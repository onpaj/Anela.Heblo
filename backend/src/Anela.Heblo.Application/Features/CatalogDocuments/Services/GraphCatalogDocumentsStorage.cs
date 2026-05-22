using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Common.Graph;
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

public class GraphCatalogDocumentsStorage : ICatalogDocumentsStorage
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphCatalogDocumentsStorage> _logger;

    private const long UploadSessionThresholdBytes = 4 * 1024 * 1024; // 4 MB

    public GraphCatalogDocumentsStorage(
        ITokenAcquisition tokenAcquisition,
        IHttpClientFactory httpClientFactory,
        ILogger<GraphCatalogDocumentsStorage> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<FolderSearchResult> FindFolderAsync(
        string driveId, string basePath, string prefix, bool allowMultiple,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Searching for folder prefix {Prefix} in {BasePath} on drive {DriveId}", prefix, basePath, driveId);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        var encodedPath = GraphApiHelpers.EncodePath(basePath.TrimStart('/'));
        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/root:/{encodedPath}:/children";

        var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
        var response = await client.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new FolderSearchResult { Status = FolderStatus.NotFound };

        await GraphApiHelpers.EnsureSuccessAsync(response, $"listing children of {basePath}", ct);

        var collection = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItemCollection>(response, ct);

        var matches = collection.Value
            .Where(i => i.Folder is not null && i.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return new FolderSearchResult { Status = FolderStatus.NotFound };

        if (matches.Count > 1 && !allowMultiple)
        {
            _logger.LogWarning("Multiple folders matching prefix {Prefix} found in {BasePath} — data issue", prefix, basePath);
            return new FolderSearchResult { Status = FolderStatus.MultipleMatches };
        }

        var chosen = matches.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).First();

        if (matches.Count > 1)
            _logger.LogInformation("Multiple PIF folders match prefix {Prefix}; using first alphabetically: {Name}", prefix, chosen.Name);

        return new FolderSearchResult
        {
            Status = FolderStatus.Found,
            FolderId = chosen.Id,
            FolderName = chosen.Name,
        };
    }

    public async Task<List<CatalogDocumentDto>> ListFilesAsync(
        string driveId, string folderId, CancellationToken ct = default)
    {
        _logger.LogDebug("Listing files in folder {FolderId} on drive {DriveId}", folderId, driveId);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{folderId}/children";
        var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, url, token);
        var response = await client.SendAsync(request, ct);
        await GraphApiHelpers.EnsureSuccessAsync(response, $"listing files in folder {folderId}", ct);

        var collection = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItemCollection>(response, ct);

        return collection.Value
            .Where(i => i.File is not null)
            .Select(i => new CatalogDocumentDto
            {
                Name = i.Name,
                WebUrl = i.WebUrl,
                SizeBytes = i.Size,
                ModifiedAt = i.LastModifiedDateTime,
            })
            .ToList();
    }

    public async Task<string> UploadFileAsync(
        string driveId, string folderId, string filename,
        Stream content, string contentType, long sizeBytes,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Uploading {Filename} ({SizeBytes} bytes) to folder {FolderId} on drive {DriveId}",
            filename, sizeBytes, folderId, driveId);

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        if (sizeBytes <= UploadSessionThresholdBytes)
            return await UploadSmallFileAsync(client, token, driveId, folderId, filename, content, contentType, ct);

        return await UploadLargeFileAsync(client, token, driveId, folderId, filename, content, sizeBytes, contentType, ct);
    }

    private static async Task<string> UploadSmallFileAsync(
        HttpClient client, string token, string driveId, string folderId,
        string filename, Stream content, string contentType, CancellationToken ct)
    {
        var encodedName = Uri.EscapeDataString(filename);
        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{folderId}:/{encodedName}:/content?@microsoft.graph.conflictBehavior=rename";

        var request = GraphApiHelpers.CreateRequest(HttpMethod.Put, url, token);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var response = await client.SendAsync(request, ct);
        await GraphApiHelpers.EnsureSuccessAsync(response, $"uploading {filename}", ct);

        var item = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItem>(response, ct);
        return item.Name;
    }

    private static async Task<string> UploadLargeFileAsync(
        HttpClient client, string token, string driveId, string folderId,
        string filename, Stream content, long sizeBytes, string contentType, CancellationToken ct)
    {
        // Step 1: Create upload session
        var encodedName = Uri.EscapeDataString(filename);
        var sessionUrl = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{folderId}:/{encodedName}:/createUploadSession";

        var bodyJson = JsonSerializer.Serialize(new
        {
            item = new Dictionary<string, string>
            {
                ["@microsoft.graph.conflictBehavior"] = "rename",
                ["name"] = filename,
            }
        });

        var sessionRequest = GraphApiHelpers.CreateRequest(HttpMethod.Post, sessionUrl, token);
        sessionRequest.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        var sessionResponse = await client.SendAsync(sessionRequest, ct);
        await GraphApiHelpers.EnsureSuccessAsync(sessionResponse, $"creating upload session for {filename}", ct);

        var session = await GraphApiHelpers.DeserializeAsync<CatalogGraphUploadSession>(sessionResponse, ct);

        // Step 2: Upload in chunks
        const int chunkSize = 10 * 1024 * 1024; // 10 MB chunks
        var buffer = new byte[chunkSize];
        long offset = 0;
        string uploadedName = filename;

        while (offset < sizeBytes)
        {
            var bytesRead = await content.ReadAsync(buffer, ct);
            if (bytesRead == 0) break;

            var chunkContent = new ByteArrayContent(buffer, 0, bytesRead);
            chunkContent.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(offset, offset + bytesRead - 1, sizeBytes);
            chunkContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            var chunkRequest = new HttpRequestMessage(HttpMethod.Put, session.UploadUrl)
            {
                Content = chunkContent
            };

            var chunkResponse = await client.SendAsync(chunkRequest, ct);
            if (chunkResponse.StatusCode == System.Net.HttpStatusCode.OK ||
                chunkResponse.StatusCode == System.Net.HttpStatusCode.Created)
            {
                var item = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItem>(chunkResponse, ct);
                uploadedName = item.Name;
            }
            else if ((int)chunkResponse.StatusCode != 202)
            {
                await GraphApiHelpers.EnsureSuccessAsync(chunkResponse, $"uploading chunk at offset {offset}", ct);
            }

            offset += bytesRead;
        }

        return uploadedName;
    }
}
