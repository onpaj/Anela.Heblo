# Specification: Server-Side Virtual Directory Listing for Expedition Date Sidebar

## Summary
Replace the full-container blob enumeration currently used by `GetExpeditionDatesHandler` with a hierarchical (virtual directory) listing that returns only the distinct top-level prefixes. This keeps the date sidebar latency and memory footprint bounded by the number of *dates* in the container, not the total number of blobs (which grows linearly with usage). The change introduces a single new method on `IBlobStorageService` and migrates the one current consumer.

## Background
The expedition archive stores one PDF per expedition list, keyed by date and list id (`{yyyy-MM-dd}/{listId}.pdf`). Each working day adds roughly one to many blobs; after two years of daily use the container already holds 500+ blobs and grows linearly.

The current `GetExpeditionDatesHandler.cs:21` calls `ListBlobsAsync(_containerName, prefix: null, …)`, which paginates every blob in the container, then computes distinct dates and applies skip/take in memory. The Azure SDK supports a hierarchical listing (`GetBlobsByHierarchyAsync` with delimiter `/`) that returns only the distinct virtual directories — the exact data the date sidebar needs — but the existing `IBlobStorageService` abstraction does not surface it. As a result the date sidebar is doing O(N) work over all blobs for every page load instead of O(D) work over distinct dates.

The sibling handler `GetExpeditionListsByDateHandler.cs:27` already uses the prefix parameter correctly; only the date sidebar is affected.

## Functional Requirements

### FR-1: Add hierarchical listing to the blob storage abstraction
Add a new method on `IBlobStorageService` that returns only the distinct top-level virtual directory prefixes within a container. The contract:

```csharp
/// <summary>
/// Lists distinct top-level virtual directory prefixes ("folders") within a container,
/// using the "/" hierarchy delimiter. The returned prefixes do NOT include the trailing slash.
/// </summary>
Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(
    string containerName,
    CancellationToken cancellationToken = default);
```

**Acceptance criteria:**
- The interface change is additive — no existing signature is altered.
- Returned strings are the prefix segments **without** the trailing `/` (e.g. `"2026-03-24"`, not `"2026-03-24/"`).
- The list is de-duplicated by the provider; the consumer does not need to call `.Distinct()`.
- Ordering is not guaranteed by the contract; consumers must order client-side.
- Loose top-level blobs (i.e. blob names containing no `/`) are **not** returned — only true virtual directories.
- A `CancellationToken` is honoured and propagated to the underlying SDK.
- Errors are logged with structured properties (`containerName`) and rethrown — same pattern as the existing methods on `AzureBlobStorageService`.

### FR-2: Implement `ListVirtualDirectoriesAsync` in `AzureBlobStorageService`
Implement the new method in `AzureBlobStorageService.cs` using `BlobContainerClient.GetBlobsByHierarchyAsync(prefix: null, delimiter: "/")`.

**Acceptance criteria:**
- Iterate the hierarchy enumeration and collect only items where `IsPrefix == true`.
- Strip the trailing `/` from each prefix before adding it to the result.
- Return an `IReadOnlyList<string>` (consistent with `ListBlobsAsync`).
- Log errors with the same structure as `ListBlobsAsync` and rethrow.
- Container creation is **not** triggered by this method (mirrors `ListBlobsAsync`, which does not call `GetOrCreateContainerAsync`).

### FR-3: Migrate `GetExpeditionDatesHandler` to use the new method
Replace the full enumeration in `GetExpeditionDatesHandler.cs:21–34` with a call to `ListVirtualDirectoriesAsync`. Preserve the existing request/response contract and behaviour:

```csharp
var prefixes = await _blobStorageService.ListVirtualDirectoriesAsync(_containerName, cancellationToken);

var dates = prefixes
    .Where(IsValidDatePrefix)
    .OrderByDescending(d => d, StringComparer.Ordinal)
    .ToList();

var totalCount = dates.Count;
var pagedDates = dates
    .Skip((request.Page - 1) * request.PageSize)
    .Take(request.PageSize)
    .ToList();
```

**Acceptance criteria:**
- `GetExpeditionDatesRequest` / `GetExpeditionDatesResponse` are unchanged.
- The handler no longer calls `ListBlobsAsync` at all.
- Date filtering still uses `DateOnly.TryParseExact(prefix, "yyyy-MM-dd", out _)` — non-date virtual directories (if any ever appear) are silently dropped, matching today's behaviour.
- Sort order is still descending by date string (which equals chronological for ISO 8601).
- Pagination math (`(Page - 1) * PageSize`, etc.) is identical to today's behaviour, so existing callers see no change.
- `TotalCount` continues to represent the total number of valid date prefixes — not the page size.

### FR-4: Tests
Update and add xUnit + Moq tests covering the new behaviour.

**Acceptance criteria:**
- Existing `GetExpeditionDatesHandlerTests` are updated to mock `ListVirtualDirectoriesAsync` instead of `ListBlobsAsync`. The three existing scenarios (sort descending, paginate, empty container) continue to pass.
- A new handler test asserts that `ListVirtualDirectoriesAsync` is invoked exactly once per request and `ListBlobsAsync` is never invoked.
- A new handler test asserts that non-date prefixes (e.g. `"miscellaneous"`, `"2026-13-99"`) are filtered out.
- The `MockBlobStorageService` test double (`backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs`) gets an in-memory implementation of `ListVirtualDirectoriesAsync` derived from the same fake blob set it already exposes.
- If `AzureBlobStorageService` has direct unit/integration coverage, add at least one test that the hierarchy listing correctly strips trailing slashes and excludes loose top-level blobs. (If no such integration test fixture exists today, this is satisfied by the handler-level + mock coverage.)
- All tests run via `dotnet test` and pass.

## Non-Functional Requirements

### NFR-1: Performance
- Wire latency for the date sidebar should become O(D) on number of *dates*, not O(N) on number of blobs. With 500+ blobs / ~500 working days today, this is a small absolute win; the value is in cutting the linear growth.
- Memory allocation per request should drop to roughly `(number of dates) * sizeof(string)` plus the page slice — no full `BlobItemInfo` collection is materialised any more.
- No additional round trips to Azure Blob Storage; the hierarchy listing is a single paged call just like the flat listing.

### NFR-2: Security
- No new authentication surface, no new secrets, no new external calls. The new method uses the same `BlobServiceClient` already configured for the existing methods.
- The new method only enumerates metadata (prefix names). Blob contents are never read.

### NFR-3: Compatibility
- The change is additive at the abstraction layer — `IBlobStorageService.ListBlobsAsync` and all other members remain in place, so existing handlers (`GetExpeditionListsByDateHandler`, `ReprintExpeditionListHandler`, `DownloadExpeditionListHandler`, `DownloadFromUrlHandler`) are untouched.
- API contracts (`GetExpeditionDatesRequest`, `GetExpeditionDatesResponse`) are unchanged → no frontend or OpenAPI client regeneration is forced. The TypeScript client may rebuild as part of normal CI without behavioural change.

### NFR-4: Observability
- Errors thrown from `ListVirtualDirectoriesAsync` log `containerName` via structured logging, matching the existing handlers' style.
- No success-path logging is added (consistent with `ListBlobsAsync`, which is silent on success).

## Data Model

No persisted data changes. Only an in-process contract addition.

| Type | Change |
|------|--------|
| `IBlobStorageService` | + `ListVirtualDirectoriesAsync(string containerName, CancellationToken)` |
| `AzureBlobStorageService` | + implementation of the above |
| `MockBlobStorageService` (tests) | + in-memory implementation |
| `BlobItemInfo` | no change |

Blob layout remains `{containerName}/{yyyy-MM-dd}/{listId}.pdf`.

## API / Interface Design

No public HTTP API change. The existing endpoint backing `GetExpeditionDatesHandler` continues to return the same shape; only its internal implementation changes.

Internal C# surface added:

```csharp
// Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs
Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(
    string containerName,
    CancellationToken cancellationToken = default);
```

```csharp
// Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs
public async Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(
    string containerName,
    CancellationToken cancellationToken = default)
{
    try
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var prefixes = new List<string>();

        await foreach (var item in containerClient.GetBlobsByHierarchyAsync(
            prefix: null,
            delimiter: "/",
            cancellationToken: cancellationToken))
        {
            if (item.IsPrefix)
            {
                // Azure returns "2026-03-24/" — strip the trailing delimiter.
                var name = item.Prefix;
                if (name.EndsWith('/'))
                {
                    name = name[..^1];
                }
                prefixes.Add(name);
            }
        }

        return prefixes.AsReadOnly();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error listing virtual directories in container {ContainerName}", containerName);
        throw;
    }
}
```

## Dependencies
- `Azure.Storage.Blobs` SDK — already a dependency, no version bump required. `BlobContainerClient.GetBlobsByHierarchyAsync` is available on currently referenced versions.
- No new NuGet packages.
- No infrastructure changes (no container layout migration, no Azure RBAC changes — the existing data-plane permissions cover hierarchy listing).

## Out of Scope
- **No pagination at the storage layer.** The new method returns *all* date prefixes; pagination remains in-memory in the handler. Given the workload (one prefix per working day, projected growth of ~250/year), in-memory paging is sufficient. Server-side paging via continuation tokens can be added later if the prefix count ever approaches the Azure single-page cap (5000).
- **No change to `ListBlobsAsync`**, no removal of the `null`-prefix code path, no deprecation of any existing method. Other consumers may still legitimately need full enumeration.
- **No frontend changes.** The API contract is unchanged.
- **No caching layer.** A memory or distributed cache for the date list is plausible future work but is not in scope here.
- **No change to how blobs are written** (the `{date}/{listId}.pdf` layout used by the print pipeline is unchanged).
- **No change to other Archive handlers** (`GetExpeditionListsByDate`, `Download`, `Reprint`).

## Open Questions
None.

## Status: COMPLETE