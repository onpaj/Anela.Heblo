# GraphFolderResolver Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract folder management and shared HTTP utilities out of `GraphOneDriveService` into two new internal classes — `GraphApiHelpers` and `GraphFolderResolver` — without changing any public behavior.

**Architecture:** Three files replace one. `GraphApiHelpers` holds static HTTP/JSON utilities and Graph model classes. `GraphFolderResolver` owns all folder path resolution and creation logic, constructed per-call by `GraphOneDriveService` with the request's `HttpClient` and token. `GraphOneDriveService` is slimmed down to file operations only.

**Tech Stack:** .NET 8, C#, `System.Text.Json`, `Microsoft.Extensions.Caching.Memory`, `Microsoft.Identity.Web`

---

### Task 1: Create `GraphApiHelpers.cs`

Move shared HTTP utilities and Graph model classes out of `GraphOneDriveService` into a new `internal static class`.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphApiHelpers.cs`

- [ ] **Step 1: Create the file**

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

internal static class GraphApiHelpers
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static string EncodePath(string path) =>
        string.Join("/", path.TrimStart('/').Split('/').Select(Uri.EscapeDataString));

    internal static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    internal static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException($"Graph response deserialised to null for {typeof(T).Name}.");
    }
}

internal class GraphDriveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("webUrl")]
    public string WebUrl { get; set; } = string.Empty;

    [JsonPropertyName("file")]
    public GraphFileFacet? File { get; set; }
}

internal class GraphFileFacet
{
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/octet-stream";
}

internal class GraphDriveItemCollection
{
    [JsonPropertyName("value")]
    public List<GraphDriveItem> Value { get; set; } = [];
}
```

- [ ] **Step 2: Verify it builds**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeded, 0 errors. (Duplicate class warnings are expected at this stage — they'll be resolved in Task 3.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphApiHelpers.cs
git commit -m "refactor(kb): extract GraphApiHelpers with shared HTTP utilities and Graph models"
```

---

### Task 2: Create `GraphFolderResolver.cs`

Move all folder path resolution logic out of `GraphOneDriveService` into a new `internal class`.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphFolderResolver.cs`

- [ ] **Step 1: Create the file**

```csharp
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
```

- [ ] **Step 2: Verify it builds**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeded, 0 errors. (Duplicate class warnings still expected — resolved in Task 3.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphFolderResolver.cs
git commit -m "refactor(kb): add GraphFolderResolver for OneDrive folder path management"
```

---

### Task 3: Refactor `GraphOneDriveService.cs`

Replace the full contents of `GraphOneDriveService.cs`. All extracted code is removed; the service delegates to `GraphApiHelpers` and `GraphFolderResolver`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs`

- [ ] **Step 1: Replace the full file contents**

```csharp
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
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

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
        var url = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/root:/{encodedPath}:/children";

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

        var url = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{fileId}/content";
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

        var url = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{fileId}";
        var request = GraphApiHelpers.CreateRequest(new HttpMethod("PATCH"), url, token);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 2: Verify it builds cleanly (no warnings)**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeded, 0 errors, 0 warnings about duplicate types.

- [ ] **Step 3: Run existing tests to verify no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~KnowledgeBase"
```

Expected output (all pass):
```
Passed: KnowledgeBaseIngestionJobTests.ExecuteAsync_IndexesFileFromKnowledgeBaseFolder_WithCorrectDocumentType
Passed: KnowledgeBaseIngestionJobTests.ExecuteAsync_IndexesFileFromConversationFolder_WithCorrectDocumentType
Passed: KnowledgeBaseIngestionJobTests.ExecuteAsync_SkipsAlreadyIndexedFileByHash
```

- [ ] **Step 4: Run dotnet format to verify formatting**

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes
```

Expected: Exit code 0 (no formatting issues).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs
git commit -m "refactor(kb): slim down GraphOneDriveService to delegate folder ops and HTTP helpers"
```
