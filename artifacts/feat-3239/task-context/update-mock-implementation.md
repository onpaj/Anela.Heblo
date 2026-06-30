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

