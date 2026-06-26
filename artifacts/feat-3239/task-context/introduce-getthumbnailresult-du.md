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

