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

