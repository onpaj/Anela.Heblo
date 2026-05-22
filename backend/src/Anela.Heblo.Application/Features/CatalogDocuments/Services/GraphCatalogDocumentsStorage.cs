using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Common.Graph;
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

public class GraphCatalogDocumentsStorage : ICatalogDocumentsStorage
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphCatalogDocumentsStorage> _logger;

    private const long UploadSessionThresholdBytes = 4 * 1024 * 1024; // 4 MB
    private const string DelegatedUploadScope = "https://graph.microsoft.com/Files.ReadWrite.All";

    // Single-flight delegated-token cache (service is Scoped — one instance per request).
    private Task<string>? _delegatedTokenTask;

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
        var firstUrl = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/root:/{encodedPath}:/children";

        var firstRequest = GraphApiHelpers.CreateRequest(HttpMethod.Get, firstUrl, token);
        var firstResponse = await client.SendAsync(firstRequest, ct);

        if (firstResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new FolderSearchResult { Status = FolderStatus.NotFound };

        await GraphApiHelpers.EnsureSuccessAsync(firstResponse, $"listing children of {basePath}", ct);
        var firstCollection = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItemCollection>(firstResponse, ct);

        var allFolderItems = new List<CatalogGraphDriveItem>(firstCollection.Value);
        var nextLink = firstCollection.NextLink;
        while (nextLink != null)
        {
            var pageRequest = GraphApiHelpers.CreateRequest(HttpMethod.Get, nextLink, token);
            var pageResponse = await client.SendAsync(pageRequest, ct);
            await GraphApiHelpers.EnsureSuccessAsync(pageResponse, $"listing children of {basePath}", ct);
            var page = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItemCollection>(pageResponse, ct);
            allFolderItems.AddRange(page.Value);
            nextLink = page.NextLink;
        }

        var matches = allFolderItems
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

        var allFileItems = new List<CatalogGraphDriveItem>();
        string? nextLink = $"{GraphApiHelpers.GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{folderId}/children";
        while (nextLink != null)
        {
            var request = GraphApiHelpers.CreateRequest(HttpMethod.Get, nextLink, token);
            var response = await client.SendAsync(request, ct);
            await GraphApiHelpers.EnsureSuccessAsync(response, $"listing files in folder {folderId}", ct);
            var collection = await GraphApiHelpers.DeserializeAsync<CatalogGraphDriveItemCollection>(response, ct);
            allFileItems.AddRange(collection.Value);
            nextLink = collection.NextLink;
        }

        return allFileItems
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

        var token = await GetDelegatedTokenAsync();
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
            // Fill the buffer fully before sending: Graph API requires intermediate chunks to be a multiple of 320 KiB.
            int totalRead = 0;
            while (totalRead < chunkSize)
            {
                int read = await content.ReadAsync(buffer.AsMemory(totalRead), ct);
                if (read == 0) break;
                totalRead += read;
            }
            if (totalRead == 0) break;

            var chunkContent = new ByteArrayContent(buffer, 0, totalRead);
            chunkContent.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(offset, offset + totalRead - 1, sizeBytes);
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

            offset += totalRead;
        }

        return uploadedName;
    }

    private async Task<string> GetDelegatedTokenAsync()
    {
        var t = _delegatedTokenTask ??= AcquireDelegatedTokenAsync();
        try
        {
            return await t;
        }
        catch
        {
            if (_delegatedTokenTask == t) _delegatedTokenTask = null;
            throw;
        }
    }

    private async Task<string> AcquireDelegatedTokenAsync()
    {
        try
        {
            return await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { DelegatedUploadScope });
        }
        catch (MicrosoftIdentityWebChallengeUserException ex)
        {
            _logger.LogError(ex.MsalUiRequiredException,
                "User consent required for Graph scope {Scope}. Grant admin consent in Azure Portal.",
                DelegatedUploadScope);
            throw new InvalidOperationException(
                $"Microsoft 365 consent required for scope {DelegatedUploadScope}. An admin must grant consent in Azure Portal.", ex);
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogError(ex,
                "User consent required for Graph scope {Scope}. Grant admin consent in Azure Portal.",
                DelegatedUploadScope);
            throw new InvalidOperationException(
                $"Microsoft 365 consent required for scope {DelegatedUploadScope}. An admin must grant consent in Azure Portal.", ex);
        }
    }
}
