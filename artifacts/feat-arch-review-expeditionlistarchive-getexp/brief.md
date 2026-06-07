## Module
ExpeditionListArchive

## Finding
`GetExpeditionDatesHandler.cs:21–34`:

```csharp
var blobs = await _blobStorageService.ListBlobsAsync(_containerName, null, cancellationToken);

var dates = blobs
    .Select(b => b.Name.Split('/')[0])
    .Where(IsValidDatePrefix)
    .Distinct()
    .OrderByDescending(d => d)
    .ToList();

var totalCount = dates.Count;
var pagedDates = dates
    .Skip((request.Page - 1) * request.PageSize)
    .Take(request.PageSize)
    .ToList();
```

`ListBlobsAsync` is called with `null` prefix, which fetches **every** blob in the container. Distinct dates and pagination are computed in-memory afterwards. The container grows by one file per expedition per working day; after two years of daily use it already contains 500+ blobs, and the trend is linear.

## Why it matters
Every page load of the archive date sidebar issues a full container listing. Latency and memory allocation grow linearly with blob count, and the approach bypasses any server-side filtering the blob storage provider could offer. The existing `prefix` parameter of `ListBlobsAsync` is already designed for this scenario — it is used correctly in `GetExpeditionListsByDateHandler` — but is unused here.

## Suggested fix
Azure Blob Storage supports listing by virtual directory prefix (the date segment is already used as a prefix delimiter). The simplest fix that stays within the current abstraction is to list only top-level virtual directories (blob names that contain exactly one `/`) rather than all blobs, or to use a blob-storage–level virtual directory enumeration if `IBlobStorageService` exposes one.

A minimal in-scope change: add a `ListVirtualDirectoriesAsync(containerName)` overload to `IBlobStorageService` that returns only the distinct top-level prefixes, avoiding the full enumeration entirely. The Archive module is the only current consumer of this pattern.

---
_Filed by daily arch-review routine on 2026-05-26._