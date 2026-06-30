# Implementation Plan — feat-3239
# Arch Review: PhotobankGraphService — Move to Adapters + GetThumbnailResult DU

**Feature:** Move `PhotobankGraphService` from Application layer to `Anela.Heblo.Adapters.Microsoft365`,
introduce a `GetThumbnailResult` discriminated union so `GetThumbnailHandler` has zero infrastructure
exception imports, and relocate the `MicrosoftGraph` HttpClient registration to the adapter.

**FR-6 DESCOPED:** Do NOT remove `Microsoft.Graph` / `Microsoft.Identity.Web` from `Application.csproj`.

**Build validation:** `dotnet build backend/Anela.Heblo.sln`
**Format check:** `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
**Test command:** `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Photobank"`

---

## File map

| Path | Role |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankGraphService.cs` | Interface + shared types file — add DU, update signature, delete `GraphThrottledException` |
| `backend/src/Anela.Heblo.Application/Features/Photobank/Services/MockPhotobankGraphService.cs` | Update `GetThumbnailAsync` return type |
| `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs` | Replace catch blocks with result switch |
| `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` | Remove real adapter DI binding + HttpClient reg |
| `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` | Add HttpClient + `PhotobankGraphService` registration |
| `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs` | **New file** — moved from Application layer |
| `backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs` | **Delete** after new file is in place |
| `backend/test/Anela.Heblo.Tests/Features/Photobank/GetThumbnailHandlerTests.cs` | Rewrite throw-based mocks to result-based |
| `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceThumbnailTests.cs` | Update namespace + `using` for moved type |

---

### task: introduce-getthumbnailresult-du

Add the `GetThumbnailResult` discriminated union to the interface file and update `IPhotobankGraphService.GetThumbnailAsync`'s return type. Remove `GraphThrottledException` from this file — it will no longer be part of the public contract.

**Files:**
- `backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankGraphService.cs`

**Steps:**

1. Open the file. It currently contains (in order):
   - `ThumbnailSize` enum
   - `GraphThumbnail` class
   - `GraphThrottledException` class  ← **delete this**
   - `GraphPhotoItem` class
   - `GraphDeltaResult` class
   - `IPhotobankGraphService` interface with `GetThumbnailAsync` returning `Task<GraphThumbnail?>`

2. **Delete** the `GraphThrottledException` class (lines 21–30 in the current file).

3. **Add** the `GetThumbnailResult` abstract class with its three cases immediately after `GraphThumbnail` (before `GraphPhotoItem`). Use the following exact code:

```csharp
public abstract class GetThumbnailResult
{
    private GetThumbnailResult() { }

    public sealed class Success : GetThumbnailResult
    {
        public GraphThumbnail Thumbnail { get; }
        public Success(GraphThumbnail thumbnail) => Thumbnail = thumbnail;
    }

    public sealed class NotFound : GetThumbnailResult { }

    public sealed class Throttled : GetThumbnailResult
    {
        public TimeSpan? RetryAfter { get; }
        public Throttled(TimeSpan? retryAfter) => RetryAfter = retryAfter;
    }

    public sealed class UpstreamError : GetThumbnailResult
    {
        public Exception Cause { get; }
        public UpstreamError(Exception cause) => Cause = cause;
    }

    public sealed class AuthUnavailable : GetThumbnailResult
    {
        public Exception Cause { get; }
        public AuthUnavailable(Exception cause) => Cause = cause;
    }
}
```

4. **Change** the `GetThumbnailAsync` signature in the interface from:

```csharp
Task<GraphThumbnail?> GetThumbnailAsync(string driveId, string fileId, ThumbnailSize size, CancellationToken cancellationToken = default);
```

to:

```csharp
Task<GetThumbnailResult> GetThumbnailAsync(string driveId, string fileId, ThumbnailSize size, CancellationToken cancellationToken = default);
```

5. Build to confirm the interface compiles in isolation (expect implementation errors elsewhere — that is fine at this step):

```
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

### task: update-mock-implementation

Update `MockPhotobankGraphService` to implement the new `GetThumbnailAsync` return type.

**Files:**
- `backend/src/Anela.Heblo.Application/Features/Photobank/Services/MockPhotobankGraphService.cs`

**Steps:**

1. The current `GetThumbnailAsync` method returns `Task<GraphThumbnail?>`. Replace the entire method body with one that returns `Task<GetThumbnailResult>`:

Replace:
```csharp
public Task<GraphThumbnail?> GetThumbnailAsync(
    string driveId,
    string fileId,
    ThumbnailSize size,
    CancellationToken cancellationToken = default)
{
    GraphThumbnail? result = new GraphThumbnail(
        new MemoryStream(MinimalPng),
        "image/png",
        MinimalPng.Length);
    return Task.FromResult<GraphThumbnail?>(result);
}
```

With:
```csharp
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
```

2. Build Application project to confirm no compile errors remain in the mock:

```
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

### task: move-photobankgraphservice-to-adapter

Create the real Graph implementation in the adapter project, then delete the original file. The implementation itself does not change — only the namespace changes and the throttle logic now returns a `GetThumbnailResult` instead of throwing.

**Files:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs` (**create**)
- `backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs` (**delete**)

**Steps:**

1. Create the directory `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/` (it does not exist yet).

2. Create `PhotobankGraphService.cs` in that directory with the content below. Key differences from the original:
   - Namespace: `Anela.Heblo.Adapters.Microsoft365.Photobank` (not `Anela.Heblo.Application.Features.Photobank.Services`)
   - Add `using Anela.Heblo.Application.Features.Photobank.Services;` to pull in the interface and shared types
   - `GetThumbnailAsync` returns `Task<GetThumbnailResult>` and replaces the two `throw` sites with `return` of the appropriate DU case
   - Remove `GraphThrottledException` — it no longer exists
   - Catch `HttpRequestException` and `MsalException` inside the service and wrap in DU cases (these are infrastructure concerns the service owns)

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.Photobank.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Adapters.Microsoft365.Photobank;

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

        if (item.File is null || item.Folder is not null)
            return null;

        var ext = Path.GetExtension(item.Name ?? string.Empty);
        if (!AllowedExtensions.Contains(ext))
            return null;

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

    public async Task<GetThumbnailResult> GetThumbnailAsync(
        string driveId,
        string fileId,
        ThumbnailSize size,
        CancellationToken cancellationToken = default)
    {
        string token;
        try
        {
            token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
        }
        catch (MsalException ex)
        {
            return new GetThumbnailResult.AuthUnavailable(ex);
        }

        var sizeSegment = size switch
        {
            ThumbnailSize.Medium => "medium",
            ThumbnailSize.Large => "large",
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
        };
        var url = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(fileId)}/thumbnails/0/{sizeSegment}/content";

        HttpResponseMessage response;
        try
        {
            var client = _httpClientFactory.CreateClient("MicrosoftGraph");
            var request = CreateRequest(HttpMethod.Get, url, token);
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new GetThumbnailResult.UpstreamError(ex);
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new GetThumbnailResult.NotFound();

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                TimeSpan? retryAfter = null;
                if (response.Headers.TryGetValues("Retry-After", out var values))
                {
                    var headerValue = values.FirstOrDefault();
                    if (headerValue != null && int.TryParse(headerValue, out var seconds))
                        retryAfter = TimeSpan.FromSeconds(seconds);
                }

                return new GetThumbnailResult.Throttled(retryAfter);
            }

            if (!response.IsSuccessStatusCode
                && response.StatusCode is not System.Net.HttpStatusCode.NotFound
                && response.StatusCode is not System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning(
                    "Graph thumbnail request returned {StatusCode} for drive {DriveId} item {FileId}. URL: {Url}",
                    (int)response.StatusCode, driveId, fileId, url);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotAcceptable)
                return new GetThumbnailResult.NotFound();

            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                return new GetThumbnailResult.UpstreamError(ex);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var contentLength = response.Content.Headers.ContentLength;

            var ms = new MemoryStream();
            await response.Content.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            return new GetThumbnailResult.Success(new GraphThumbnail(ms, contentType, contentLength));
        }
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

    private class GraphItemWithId
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

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
```

3. Delete the old file:

```
rm backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs
```

4. Build the adapter project to confirm it compiles:

```
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj
```

---

### task: wire-adapter-di-registration

Move the `MicrosoftGraph` HttpClient and `PhotobankGraphService` DI registration out of `PhotobankModule` and into `Microsoft365AdapterServiceCollectionExtensions`. Remove the dead code from the module.

**Files:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`

**Steps:**

1. Open `Microsoft365AdapterServiceCollectionExtensions.cs`. Add the following `using` at the top:

```csharp
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Adapters.Microsoft365.Photobank;
```

2. Inside the `if (!useMockAuth && !bypassJwt)` block, after the existing `OutlookCalendarSyncService` line, add:

```csharp
services.AddHttpClient("MicrosoftGraph", _ => { })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
    });
services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
```

The full method should now look like:

```csharp
public static IServiceCollection AddMicrosoft365Adapter(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
    var bypassJwt = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

    if (!useMockAuth && !bypassJwt)
    {
        services.AddScoped<IOutlookCalendarSync, OutlookCalendarSyncService>();
        services.AddHttpClient("MicrosoftGraph", _ => { })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
            });
        services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
    }

    return services;
}
```

3. Open `PhotobankModule.cs`. Locate and remove the entire `if (!useMockAuth && !bypassJwtValidation)` / `else` block (lines 43–57 in the current file). This block registered `MicrosoftGraph` HttpClient and `PhotobankGraphService`/`MockPhotobankGraphService`.

   **Keep only the mock fallback** in the module — registered unconditionally, because it is the application-layer default when no real adapter has wired up the service. The adapter registration (added above) will override it at runtime via DI because it is registered last (adapters are registered after modules in `Program.cs`). However, to avoid double-registration issues, register the mock **only when** in mock/bypass mode:

   Replace the entire block:
   ```csharp
   var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
   var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

   if (!useMockAuth && !bypassJwtValidation)
   {
       services.AddHttpClient("MicrosoftGraph", _ => { })
       .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
       {
           AllowAutoRedirect = true,
       });
       services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
   }
   else
   {
       services.AddScoped<IPhotobankGraphService, MockPhotobankGraphService>();
   }
   ```

   With:
   ```csharp
   var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
   var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

   if (useMockAuth || bypassJwtValidation)
   {
       services.AddScoped<IPhotobankGraphService, MockPhotobankGraphService>();
   }
   ```

   Also remove the now-unused `using` import for `PhotobankGraphService` if one was added (check: the original file does not import it explicitly because it is in the same namespace — so no `using` needs removing).

4. Build the full solution:

```
dotnet build backend/Anela.Heblo.sln
```

---

### task: rewrite-getthumbnailhandler

Replace the three `catch` blocks in `GetThumbnailHandler` with a result switch. After this task the handler will have no `using` directives for `System.Net.Http`, `Microsoft.Identity.Client`, or `GraphThrottledException`.

**Files:**
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs`

**Steps:**

1. Remove the following `using` directives from the top of the file (they will no longer be needed):
   - `using System.Net.Http;`
   - `using Microsoft.Identity.Client;`

2. Replace the entire `try/catch` block and the `if (rawThumbnail is null)` check that follows it with a result switch. The current structure is:

```csharp
GraphThumbnail? rawThumbnail;
try
{
    rawThumbnail = await _graphService.GetThumbnailAsync(
        locator.DriveId, locator.SharePointFileId, request.Size, cancellationToken);
}
catch (GraphThrottledException ex)
{
    _logger.LogWarning("Microsoft Graph thumbnail request throttled for photo {PhotoId}. RetryAfter: {RetryAfter}",
        request.Id, ex.RetryAfter);
    return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailThrottled)
    {
        RetryAfterSeconds = ex.RetryAfter.HasValue
            ? (int)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds)
            : null,
    };
}
catch (HttpRequestException ex)
{
    _logger.LogWarning(ex, "Upstream HTTP error fetching thumbnail for photo {PhotoId}", request.Id);
    return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailUpstream);
}
catch (MsalException ex)
{
    _logger.LogError(ex, "Token acquisition failed for thumbnail {PhotoId}. MSAL error: {ErrorCode}", request.Id, ex.ErrorCode);
    return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailAuthUnavailable);
}

if (rawThumbnail is null)
{
    return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailNotFound);
}

// NFR-3: transfer stream ownership to the response. Do NOT dispose rawThumbnail
// (GraphThumbnail.Dispose() closes the underlying Stream); FileStreamResult disposes it after writing.
return new GetThumbnailResponse
{
    Content = rawThumbnail.Content,
    ContentType = rawThumbnail.ContentType,
    ContentLength = rawThumbnail.ContentLength,
};
```

Replace with:

```csharp
var thumbnailResult = await _graphService.GetThumbnailAsync(
    locator.DriveId, locator.SharePointFileId, request.Size, cancellationToken);

return thumbnailResult switch
{
    GetThumbnailResult.Success ok =>
        new GetThumbnailResponse
        {
            Content = ok.Thumbnail.Content,
            ContentType = ok.Thumbnail.ContentType,
            ContentLength = ok.Thumbnail.ContentLength,
        },

    GetThumbnailResult.NotFound =>
        new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailNotFound),

    GetThumbnailResult.Throttled throttled =>
        LogAndReturn(
            () => _logger.LogWarning(
                "Microsoft Graph thumbnail request throttled for photo {PhotoId}. RetryAfter: {RetryAfter}",
                request.Id, throttled.RetryAfter),
            new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailThrottled)
            {
                RetryAfterSeconds = throttled.RetryAfter.HasValue
                    ? (int)Math.Ceiling(throttled.RetryAfter.Value.TotalSeconds)
                    : null,
            }),

    GetThumbnailResult.UpstreamError upstream =>
        LogAndReturn(
            () => _logger.LogWarning(upstream.Cause,
                "Upstream HTTP error fetching thumbnail for photo {PhotoId}", request.Id),
            new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailUpstream)),

    GetThumbnailResult.AuthUnavailable auth =>
        LogAndReturn(
            () => _logger.LogError(auth.Cause,
                "Token acquisition failed for thumbnail {PhotoId}", request.Id),
            new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailAuthUnavailable)),

    _ => throw new InvalidOperationException($"Unhandled GetThumbnailResult: {thumbnailResult.GetType().Name}"),
};
```

3. Add the private helper method `LogAndReturn` to the handler class (after the `Handle` method):

```csharp
private static GetThumbnailResponse LogAndReturn(Action log, GetThumbnailResponse response)
{
    log();
    return response;
}
```

4. Build the solution:

```
dotnet build backend/Anela.Heblo.sln
```

---

### task: update-handler-tests

Rewrite the throw-based mock setups in `GetThumbnailHandlerTests` to return DU values. Remove infrastructure `using` directives that are no longer needed.

**Files:**
- `backend/test/Anela.Heblo.Tests/Features/Photobank/GetThumbnailHandlerTests.cs`

**Steps:**

1. Remove the following `using` directives that will no longer be needed:
   - `using Microsoft.Identity.Client;`
   - `using System.Net.Http;`

2. The test `Handle_ReturnsNotFound_WhenGraphReturnsNull` currently mocks the service to return `(GraphThumbnail?)null`. Change the setup to return `GetThumbnailResult.NotFound`:

   Replace:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ReturnsAsync((GraphThumbnail?)null);
   ```
   With:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ReturnsAsync(new GetThumbnailResult.NotFound());
   ```

3. The test `Handle_ReturnsThrottledWithRoundedRetryAfter_WhenGraphThrottles` currently throws `GraphThrottledException`. Change to return `GetThumbnailResult.Throttled`:

   Replace:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ThrowsAsync(new GraphThrottledException(TimeSpan.FromSeconds(29.3)));
   ```
   With:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ReturnsAsync(new GetThumbnailResult.Throttled(TimeSpan.FromSeconds(29.3)));
   ```

4. The test `Handle_ReturnsThrottledWithoutRetryAfter_WhenRetryAfterNull` currently throws `GraphThrottledException(null)`. Change to return `GetThumbnailResult.Throttled`:

   Replace:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ThrowsAsync(new GraphThrottledException(null));
   ```
   With:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ReturnsAsync(new GetThumbnailResult.Throttled(null));
   ```

5. The test `Handle_ReturnsUpstream_WhenHttpRequestExceptionThrown` currently throws `HttpRequestException`. Change to return `GetThumbnailResult.UpstreamError`:

   Replace:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ThrowsAsync(new HttpRequestException("upstream error"));
   ```
   With:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ReturnsAsync(new GetThumbnailResult.UpstreamError(new HttpRequestException("upstream error")));
   ```

6. The test `Handle_ReturnsAuthUnavailable_WhenMsalExceptionThrown` currently throws `MsalServiceException`. Change to return `GetThumbnailResult.AuthUnavailable`:

   Replace:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ThrowsAsync(new MsalServiceException("invalid_client", "AADSTS7000215: Invalid client secret"));
   ```
   With:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ReturnsAsync(new GetThumbnailResult.AuthUnavailable(
           new MsalServiceException("invalid_client", "AADSTS7000215: Invalid client secret")));
   ```
   Note: you may keep `using Microsoft.Identity.Client;` in this test file to construct `MsalServiceException` for the `AuthUnavailable.Cause` — the point is the handler itself no longer imports it.

7. The test `Handle_ReturnsSuccessWithSameStream_WhenThumbnailReturned` currently mocks the service to return a `GraphThumbnail`. Change to return `GetThumbnailResult.Success`:

   Replace:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ReturnsAsync(thumbnail);
   ```
   With:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
       .ReturnsAsync(new GetThumbnailResult.Success(thumbnail));
   ```

8. The test `Handle_PassesCancellationTokenThrough` also sets up `GetThumbnailAsync` to return `(GraphThumbnail?)null`. Change to return `GetThumbnailResult.NotFound`:

   Replace:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, cts.Token))
       .ReturnsAsync((GraphThumbnail?)null);
   ```
   With:
   ```csharp
   _graphServiceMock
       .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, cts.Token))
       .ReturnsAsync(new GetThumbnailResult.NotFound());
   ```

9. Run the Photobank test filter to confirm all handler tests pass:

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Photobank"
```

---

### task: update-graph-service-tests

Update `PhotobankGraphServiceThumbnailTests` to reference the moved type (`PhotobankGraphService` is now in `Anela.Heblo.Adapters.Microsoft365.Photobank`) and assert DU return values instead of exceptions/nulls.

**Files:**
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceThumbnailTests.cs`

**Steps:**

The test project references `Anela.Heblo.Application`. Check whether it also references the adapter project. Run:

```
grep -r "Anela.Heblo.Adapters.Microsoft365" backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

If **not** present, add the project reference to `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`:

```xml
<ProjectReference Include="..\..\..\..\src\Adapters\Anela.Heblo.Adapters.Microsoft365\Anela.Heblo.Adapters.Microsoft365.csproj" />
```

2. In `PhotobankGraphServiceThumbnailTests.cs`, add a `using` for the adapter namespace:

```csharp
using Anela.Heblo.Adapters.Microsoft365.Photobank;
```

3. The `CreateService` factory method currently constructs `PhotobankGraphService` from `Anela.Heblo.Application.Features.Photobank.Services`. After the move the unqualified name still resolves — but confirm the `using Anela.Heblo.Application.Features.Photobank.Services;` is kept (for `ThumbnailSize`, `GraphThumbnail`, etc. which stay in Application). The `PhotobankGraphService` type is now in `Anela.Heblo.Adapters.Microsoft365.Photobank` — if there is a name collision, qualify the constructor call explicitly:

```csharp
return new Anela.Heblo.Adapters.Microsoft365.Photobank.PhotobankGraphService(
    tokenMock.Object,
    factoryMock.Object,
    NullLogger<Anela.Heblo.Adapters.Microsoft365.Photobank.PhotobankGraphService>.Instance);
```

4. The tests that currently assert throw behavior must now assert on the returned DU. Update each:

   **`GetThumbnailAsync_ThrowsGraphThrottledException_WhenGraphReturns429`** → assert `GetThumbnailResult.Throttled`:

   Replace the Act/Assert section:
   ```csharp
   // Act
   var act = async () => await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

   // Assert
   var ex = await act.Should().ThrowAsync<GraphThrottledException>();
   ex.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
   ```
   With:
   ```csharp
   // Act
   var result = await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

   // Assert
   result.Should().BeOfType<GetThumbnailResult.Throttled>();
   ((GetThumbnailResult.Throttled)result).RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
   ```

   **`GetThumbnailAsync_ThrowsGraphThrottledException_WhenGraphReturns429_WithNoRetryAfterHeader`** → assert `GetThumbnailResult.Throttled` with null RetryAfter:

   Replace:
   ```csharp
   // Act
   var act = async () => await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

   // Assert
   var ex = await act.Should().ThrowAsync<GraphThrottledException>();
   ex.Which.RetryAfter.Should().BeNull();
   ```
   With:
   ```csharp
   // Act
   var result = await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

   // Assert
   result.Should().BeOfType<GetThumbnailResult.Throttled>();
   ((GetThumbnailResult.Throttled)result).RetryAfter.Should().BeNull();
   ```

   **`GetThumbnailAsync_ThrowsHttpRequestException_WhenGraphReturns500`** → assert `GetThumbnailResult.UpstreamError`:

   Replace:
   ```csharp
   // Act
   var act = async () => await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

   // Assert
   await act.Should().ThrowAsync<HttpRequestException>();
   ```
   With:
   ```csharp
   // Act
   var result = await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

   // Assert
   result.Should().BeOfType<GetThumbnailResult.UpstreamError>();
   ```

   **`GetThumbnailAsync_ReturnsNull_WhenGraphReturns404`** → assert `GetThumbnailResult.NotFound`:

   Replace:
   ```csharp
   // Assert
   result.Should().BeNull();
   ```
   With:
   ```csharp
   // Assert
   result.Should().BeOfType<GetThumbnailResult.NotFound>();
   ```

   **`GetThumbnailAsync_ReturnsNull_WhenGraphReturns406`** → assert `GetThumbnailResult.NotFound`:

   Replace:
   ```csharp
   // Assert
   result.Should().BeNull();
   ```
   With:
   ```csharp
   // Assert
   result.Should().BeOfType<GetThumbnailResult.NotFound>();
   ```

   **`GetThumbnailAsync_ReturnsGraphThumbnail_WhenGraphReturns200`** — the existing assertions check `.ContentType`, `.ContentLength`, and `.Content` on the raw return value. After the change the return is `GetThumbnailResult.Success`. Update:

   Replace:
   ```csharp
   // Assert
   result.Should().NotBeNull();
   result!.ContentType.Should().Be("image/jpeg");
   result.ContentLength.Should().Be(imageBytes.Length);
   result.Content.Should().NotBeNull();
   ```
   With:
   ```csharp
   // Assert
   result.Should().BeOfType<GetThumbnailResult.Success>();
   var success = (GetThumbnailResult.Success)result;
   success.Thumbnail.ContentType.Should().Be("image/jpeg");
   success.Thumbnail.ContentLength.Should().Be(imageBytes.Length);
   success.Thumbnail.Content.Should().NotBeNull();
   ```

   The two URL-building tests (`GetThumbnailAsync_BuildsCorrectUrl`) do not assert the return type — leave them unchanged, but note the `result` variable is now `GetThumbnailResult`, not `GraphThumbnail?`. The test still compiles because it discards the return value.

5. Run all Photobank tests:

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Photobank"
```

---

### task: final-validation

Confirm the entire solution builds, passes format check, and all Photobank tests are green.

**Steps:**

1. Full solution build:

```
dotnet build backend/Anela.Heblo.sln
```

Expected: zero errors.

2. Format check (CI gate):

```
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

If it reports changes, run `dotnet format backend/Anela.Heblo.sln` to apply them, then re-run `--verify-no-changes`.

3. Photobank tests:

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Photobank"
```

Expected: all tests pass, zero skipped.

4. Commit with a descriptive message covering all changed files.
