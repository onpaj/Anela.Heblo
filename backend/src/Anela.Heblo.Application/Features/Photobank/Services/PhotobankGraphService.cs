using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.Photobank.Services;

/// <summary>
/// Microsoft Graph implementation of IPhotobankGraphService.
/// Uses the Drive Items delta API to efficiently sync SharePoint photo libraries.
/// </summary>
public class PhotobankGraphService : IPhotobankGraphService
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PhotobankGraphService> _logger;
    private const string GraphScope = "https://graph.microsoft.com/.default";
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".tiff",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public PhotobankGraphService(
        ITokenAcquisition tokenAcquisition,
        IHttpClientFactory httpClientFactory,
        ILogger<PhotobankGraphService> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GraphDeltaResult> GetDeltaAsync(
        string driveId,
        string rootItemId,
        string? deltaLink,
        CancellationToken cancellationToken = default)
    {
        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        var items = new List<GraphPhotoItem>();
        string newDeltaLink = string.Empty;

        // First request: use deltaLink if available, otherwise start a fresh delta query
        string nextUrl = string.IsNullOrEmpty(deltaLink)
            ? $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(rootItemId)}/delta"
            : deltaLink;

        while (!string.IsNullOrEmpty(nextUrl))
        {
            var request = CreateRequest(HttpMethod.Get, nextUrl, token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var page = await DeserializeAsync<GraphDeltaPage>(response, cancellationToken);

            foreach (var item in page.Value)
            {
                var photoItem = MapItem(item, driveId);
                if (photoItem != null)
                {
                    items.Add(photoItem);
                }
            }

            if (!string.IsNullOrEmpty(page.ODataDeltaLink))
            {
                newDeltaLink = page.ODataDeltaLink;
                nextUrl = string.Empty;
            }
            else
            {
                nextUrl = page.ODataNextLink ?? string.Empty;
            }
        }

        _logger.LogDebug("Delta query for drive {DriveId}/item {RootItemId} returned {Count} relevant items", driveId, rootItemId, items.Count);

        return new GraphDeltaResult
        {
            Items = items,
            NewDeltaLink = newDeltaLink,
        };
    }

    private static GraphPhotoItem? MapItem(GraphDeltaItem item, string driveId)
    {
        // Deleted items — include them so the job can remove from DB
        if (item.Deleted != null)
        {
            return new GraphPhotoItem
            {
                ItemId = item.Id,
                Name = item.Name ?? string.Empty,
                FolderPath = string.Empty,
                DriveId = driveId,
                IsDeleted = true,
            };
        }

        // Skip folders (file facet absent or folder facet present)
        if (item.File is null || item.Folder is not null)
            return null;

        // Only index allowed image extensions
        var ext = Path.GetExtension(item.Name ?? string.Empty);
        if (!AllowedExtensions.Contains(ext))
            return null;

        // Extract folder path from parentReference.path
        // Graph path looks like: /drives/{driveId}/root:/Fotky/Produkty
        var folderPath = string.Empty;
        if (!string.IsNullOrEmpty(item.ParentReference?.Path))
        {
            var path = item.ParentReference.Path;
            var rootMarker = "/root:/";
            var idx = path.IndexOf(rootMarker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                folderPath = path[(idx + rootMarker.Length)..];
            }
        }

        return new GraphPhotoItem
        {
            ItemId = item.Id,
            Name = item.Name ?? string.Empty,
            FolderPath = folderPath,
            WebUrl = item.WebUrl,
            FileSizeBytes = item.Size,
            LastModifiedAt = item.LastModifiedDateTime,
            DriveId = item.ParentReference?.DriveId ?? driveId,
            IsDeleted = false,
        };
    }

    public async Task<GraphThumbnail?> GetThumbnailAsync(
        string driveId,
        string fileId,
        ThumbnailSize size,
        CancellationToken cancellationToken = default)
    {
        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
        var sizeSegment = size switch
        {
            ThumbnailSize.Medium => "medium",
            ThumbnailSize.Large => "large",
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
        };
        var url = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(fileId)}/thumbnails/0/{sizeSegment}/content";

        var client = _httpClientFactory.CreateClient("MicrosoftGraph");
        var request = CreateRequest(HttpMethod.Get, url, token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            TimeSpan? retryAfter = null;
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                var headerValue = values.FirstOrDefault();
                if (headerValue != null && int.TryParse(headerValue, out var seconds))
                    retryAfter = TimeSpan.FromSeconds(seconds);
            }

            throw new GraphThrottledException(retryAfter);
        }

        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var contentLength = response.Content.Headers.ContentLength;

        var ms = new MemoryStream();
        await response.Content.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        return new GraphThumbnail(ms, contentType, contentLength);
    }

    public async Task<string> ResolveItemIdAsync(string driveId, string folderPath, CancellationToken cancellationToken = default)
    {
        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
        using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        var encodedSegments = folderPath.Trim('/').Split('/').Select(Uri.EscapeDataString);
        var encodedPath = string.Join("/", encodedSegments);
        var url = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/root:/{encodedPath}:";

        var request = CreateRequest(HttpMethod.Get, url, token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var item = await DeserializeAsync<GraphItemWithId>(response, cancellationToken);
        return item.Id;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException($"Graph response deserialised to null for {typeof(T).Name}.");
    }

    // Internal DTOs for Graph API response deserialization
    private class GraphItemWithId
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    // Internal DTOs for Graph delta API response deserialization
    private class GraphDeltaPage
    {
        [JsonPropertyName("value")]
        public List<GraphDeltaItem> Value { get; set; } = [];

        [JsonPropertyName("@odata.nextLink")]
        public string? ODataNextLink { get; set; }

        [JsonPropertyName("@odata.deltaLink")]
        public string? ODataDeltaLink { get; set; }
    }

    private class GraphDeltaItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("webUrl")]
        public string? WebUrl { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("lastModifiedDateTime")]
        public DateTime? LastModifiedDateTime { get; set; }

        [JsonPropertyName("parentReference")]
        public GraphParentReference? ParentReference { get; set; }

        [JsonPropertyName("file")]
        public GraphFileFacet? File { get; set; }

        [JsonPropertyName("folder")]
        public GraphFolderFacet? Folder { get; set; }

        [JsonPropertyName("deleted")]
        public GraphDeletedFacet? Deleted { get; set; }
    }

    private class GraphParentReference
    {
        [JsonPropertyName("driveId")]
        public string? DriveId { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }
    }

    private class GraphFileFacet
    {
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }
    }

    private class GraphFolderFacet
    {
        [JsonPropertyName("childCount")]
        public int? ChildCount { get; set; }
    }

    private class GraphDeletedFacet
    {
        [JsonPropertyName("state")]
        public string? State { get; set; }
    }
}
