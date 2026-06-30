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

