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

