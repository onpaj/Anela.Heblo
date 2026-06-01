# Server-Side Virtual Directory Listing for Expedition Date Sidebar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the full-container blob enumeration in `GetExpeditionDatesHandler` with a hierarchical (virtual directory) listing so the date sidebar's cost stays bounded by the number of dates, not by total blob count.

**Architecture:** Additive change to the `IBlobStorageService` abstraction — a new `ListVirtualDirectoriesAsync(containerName, ct)` method that wraps Azure SDK's `BlobContainerClient.GetBlobsByHierarchyAsync(prefix: null, delimiter: "/")`. The Azure implementation strips the trailing `/` at the abstraction boundary; the in-memory mock mirrors the same hierarchical semantics (first-path-segment grouping, distinct, no loose top-level blobs). `GetExpeditionDatesHandler` is the sole consumer that migrates; all other handlers remain on `ListBlobsAsync`.

**Tech Stack:** .NET 8, C# (nullable enabled), MediatR, xUnit + Moq, `Azure.Storage.Blobs`.

---

## Spec & Architecture References

- Spec: `artifacts/feat-arch-review-expeditionlistarchive-getexp/spec.r1.md`
- Arch review: `artifacts/feat-arch-review-expeditionlistarchive-getexp/arch-review.r1.md`

## File Map (locked in before tasks)

| File | Responsibility | Change |
|------|----------------|--------|
| `backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs` | Storage abstraction surface | + `ListVirtualDirectoriesAsync` with XML doc that fully encodes the contract |
| `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs` | Azure SDK adapter | + implementation using `GetBlobsByHierarchyAsync` (named args), strip trailing `/`, no auto-create |
| `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs` | In-memory test double | + hierarchical implementation; first-segment grouping, distinct, exclude keys without `/` |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs` | MediatR handler — sole consumer that migrates | Swap `ListBlobsAsync` → `ListVirtualDirectoriesAsync`; drop `Select(b => b.Name.Split('/')[0])` and `Distinct()`; use `StringComparer.Ordinal` |
| `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs` | Handler unit tests | Migrate three existing Moq setups; add two FR-4 tests |
| `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageServiceTests.cs` (NEW) | Direct tests of the in-memory mock's hierarchical semantics | Cover loose top-level exclusion, distinct, no trailing slash, deep paths surface only first segment |

## Conventions

- Match existing C# style in `AzureBlobStorageService` (try/catch + `_logger.LogError(ex, "...{ContainerName}...", containerName)` + rethrow; no success log).
- DTOs must be classes, not records — but this plan does not introduce DTOs; the contract surface is a single method on an interface that returns `IReadOnlyList<string>`.
- Use **named arguments** for `GetBlobsByHierarchyAsync` to avoid the SDK's `(BlobTraits, BlobStates, string delimiter, string prefix, CancellationToken)` positional trap.
- xUnit + Moq. Use `It.IsAny<CancellationToken>()` for tokens in mock setups (handler passes the request `cancellationToken`; existing tests rely on `default`).

---

## Task 1: Add `ListVirtualDirectoriesAsync` to `IBlobStorageService` + temporary stubs

**Why this task exists:** Adding the interface method without an implementation breaks compilation across both `AzureBlobStorageService` and `MockBlobStorageService`. We add stubs in the same task so the solution keeps compiling and later tasks can iterate red→green per consumer.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs`

- [ ] **Step 1: Add the method to the interface with full contract XML doc**

Edit `backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs`. After the existing `DownloadAsync` declaration (line 52), append a blank line and:

```csharp
    /// <summary>
    /// Lists distinct top-level virtual directory prefixes ("folders") within a container,
    /// using the "/" hierarchy delimiter.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// <list type="bullet">
    ///   <item>Returned strings have the trailing "/" stripped (e.g. "2026-03-24", not "2026-03-24/").</item>
    ///   <item>Loose top-level blobs (names with no "/") are NOT returned — only true virtual directories.</item>
    ///   <item>The list is de-duplicated by the provider; callers do not need to call <c>.Distinct()</c>.</item>
    ///   <item>Ordering is not guaranteed; callers sort client-side.</item>
    ///   <item>Only the first-level segment is returned; deeper paths (e.g. "2026-03-24/sub/x.pdf") still surface only "2026-03-24".</item>
    ///   <item>This method does NOT auto-create the container — same behaviour as <see cref="ListBlobsAsync"/>.</item>
    /// </list>
    /// </remarks>
    Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(
        string containerName,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Add a stub on `AzureBlobStorageService` so the project still compiles**

Edit `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`. After the `ListBlobsAsync` method body (closing brace on line 179), append:

```csharp

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        // Stub — real implementation lands in a later task to keep the diff scoped.
        throw new NotImplementedException();
    }
```

- [ ] **Step 3: Add a stub on `MockBlobStorageService` so the test assembly still compiles**

Edit `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs`. After the `DownloadAsync` method (closing brace on line 170), append:

```csharp

    public Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken = default)
    {
        // Stub — real implementation lands in a later task.
        throw new NotImplementedException();
    }
```

- [ ] **Step 4: Verify the solution builds**

Run from the repo root:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds. (Tests may fail later — that's expected and handled in subsequent tasks.)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs \
        backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs \
        backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs
git commit -m "feat(filestorage): add ListVirtualDirectoriesAsync abstraction"
```

---

## Task 2: Migrate existing handler tests to the new mock setup (RED)

**Why this task exists:** Before changing the handler, drive the swap from the tests. The three existing tests currently set up `ListBlobsAsync`; we move them to `ListVirtualDirectoriesAsync`. The handler still calls the old method, so all three tests will fail — that's the RED step.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs`

- [ ] **Step 1: Rewrite the three existing tests against the new method**

Replace the contents of `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class GetExpeditionDatesHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly GetExpeditionDatesHandler _handler;
    private const string ContainerName = "expedition-lists";

    public GetExpeditionDatesHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _handler = new GetExpeditionDatesHandler(_blobStorageServiceMock.Object, Options.Create(new PrintPickingListOptions()));
    }

    [Fact]
    public async Task Handle_ReturnsDatesSortedDescending()
    {
        // Arrange
        var prefixes = new List<string> { "2026-03-24", "2026-03-25", "2026-03-23" };
        _blobStorageServiceMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Dates.Count);
        Assert.Equal("2026-03-25", result.Dates[0]);
        Assert.Equal("2026-03-24", result.Dates[1]);
        Assert.Equal("2026-03-23", result.Dates[2]);
    }

    [Fact]
    public async Task Handle_PaginatesCorrectly()
    {
        // Arrange
        var prefixes = new List<string>();
        for (int i = 1; i <= 25; i++)
        {
            prefixes.Add($"2026-01-{i:D2}");
        }

        _blobStorageServiceMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 2, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(5, result.Dates.Count); // page 2 of 20: items 21-25
    }

    [Fact]
    public async Task Handle_EmptyContainer_ReturnsEmptyList()
    {
        // Arrange
        _blobStorageServiceMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>().AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Dates);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter FullyQualifiedName~GetExpeditionDatesHandlerTests
```

Expected: all three tests fail (the handler still calls `ListBlobsAsync`, which is no longer set up on the mock — it returns the Moq default of `null` and the handler throws). This is the RED state.

- [ ] **Step 3: Commit the failing tests**

```bash
git add backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs
git commit -m "test(expedition-archive): retarget handler tests at ListVirtualDirectoriesAsync"
```

---

## Task 3: Migrate the handler to use `ListVirtualDirectoriesAsync` (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs`

- [ ] **Step 1: Replace the listing + projection logic in `Handle`**

Open `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs`. Replace the entire body of `Handle` (lines 19–43) with:

```csharp
    public async Task<GetExpeditionDatesResponse> Handle(GetExpeditionDatesRequest request, CancellationToken cancellationToken)
    {
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

        return new GetExpeditionDatesResponse
        {
            Dates = pagedDates,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
```

Notes on the diff vs. today:
- `ListBlobsAsync(_containerName, null, …)` → `ListVirtualDirectoriesAsync(_containerName, …)`
- Removed `.Select(b => b.Name.Split('/')[0])` — the new method already returns prefix strings.
- Removed `.Distinct()` — the provider de-duplicates per contract.
- `OrderByDescending(d => d)` → `OrderByDescending(d => d, StringComparer.Ordinal)` (byte-identical output for ISO 8601, locale-independent, faster).

Leave `IsValidDatePrefix` unchanged.

- [ ] **Step 2: Run the handler tests and verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter FullyQualifiedName~GetExpeditionDatesHandlerTests
```

Expected: all three migrated tests pass (GREEN).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs
git commit -m "refactor(expedition-archive): use hierarchical listing for date sidebar"
```

---

## Task 4: Add the two FR-4 handler tests (call-counts and non-date filtering)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs`

- [ ] **Step 1: Append the new tests**

Add the following two methods inside the `GetExpeditionDatesHandlerTests` class, before its closing brace:

```csharp
    [Fact]
    public async Task Handle_CallsListVirtualDirectoriesOnce_AndNeverCallsListBlobs()
    {
        // Arrange
        var prefixes = new List<string> { "2026-03-25", "2026-03-24" };
        _blobStorageServiceMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        await _handler.Handle(request, default);

        // Assert
        _blobStorageServiceMock.Verify(
            s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()),
            Times.Once);
        _blobStorageServiceMock.Verify(
            s => s.ListBlobsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_FiltersOutNonDatePrefixes()
    {
        // Arrange — mix of valid dates, structurally wrong, semantically wrong, and a sentinel folder.
        var prefixes = new List<string>
        {
            "2026-03-25",       // valid
            "miscellaneous",    // not a date
            "2026-13-99",       // structurally yyyy-MM-dd but invalid month/day
            "2026-03-24",       // valid
            "not-a-date",       // not a date
            "2025-12-31"        // valid
        };
        _blobStorageServiceMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert — only the three valid dates remain, in descending ordinal order.
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { "2026-03-25", "2026-03-24", "2025-12-31" }, result.Dates);
    }
```

- [ ] **Step 2: Run the new tests and verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter FullyQualifiedName~GetExpeditionDatesHandlerTests
```

Expected: 5 tests pass (3 existing + 2 new).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs
git commit -m "test(expedition-archive): verify call-counts and non-date filtering"
```

---

## Task 5: Implement hierarchical semantics in `MockBlobStorageService` + add direct tests

**Why this task exists:** The mock must mirror Azure's `GetBlobsByHierarchyAsync(delimiter: "/")` semantics — not the existing `StartsWith` flat semantics. Without this, downstream tests that consume `MockBlobStorageService` would silently pass while production behaves differently (arch-review Risk #1).

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageServiceTests.cs`

- [ ] **Step 1: Write the failing direct tests for the mock**

Create `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageServiceTests.cs` with:

```csharp
using System.Text;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class MockBlobStorageServiceTests
{
    private const string ContainerName = "expedition-lists";

    private static MemoryStream Bytes(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task ListVirtualDirectoriesAsync_ReturnsEmpty_WhenContainerDoesNotExist()
    {
        // Arrange
        var service = new MockBlobStorageService();

        // Act
        var result = await service.ListVirtualDirectoriesAsync(ContainerName);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListVirtualDirectoriesAsync_ReturnsFirstSegments_WithoutTrailingSlash()
    {
        // Arrange
        var service = new MockBlobStorageService();
        await service.UploadAsync(Bytes("a"), ContainerName, "2026-03-24/list-001.pdf", "application/pdf");
        await service.UploadAsync(Bytes("b"), ContainerName, "2026-03-25/list-002.pdf", "application/pdf");

        // Act
        var result = await service.ListVirtualDirectoriesAsync(ContainerName);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("2026-03-24", result);
        Assert.Contains("2026-03-25", result);
        Assert.All(result, p => Assert.False(p.EndsWith('/')));
    }

    [Fact]
    public async Task ListVirtualDirectoriesAsync_DeduplicatesPrefixes()
    {
        // Arrange
        var service = new MockBlobStorageService();
        await service.UploadAsync(Bytes("a"), ContainerName, "2026-03-24/list-001.pdf", "application/pdf");
        await service.UploadAsync(Bytes("b"), ContainerName, "2026-03-24/list-002.pdf", "application/pdf");
        await service.UploadAsync(Bytes("c"), ContainerName, "2026-03-24/list-003.pdf", "application/pdf");

        // Act
        var result = await service.ListVirtualDirectoriesAsync(ContainerName);

        // Assert
        Assert.Single(result);
        Assert.Equal("2026-03-24", result[0]);
    }

    [Fact]
    public async Task ListVirtualDirectoriesAsync_ExcludesLooseTopLevelBlobs()
    {
        // Arrange — mix loose top-level blobs and nested blobs.
        var service = new MockBlobStorageService();
        await service.UploadAsync(Bytes("a"), ContainerName, "readme.txt", "text/plain");
        await service.UploadAsync(Bytes("b"), ContainerName, "loose.pdf", "application/pdf");
        await service.UploadAsync(Bytes("c"), ContainerName, "2026-03-24/list.pdf", "application/pdf");

        // Act
        var result = await service.ListVirtualDirectoriesAsync(ContainerName);

        // Assert — only the nested blob contributes a prefix.
        Assert.Single(result);
        Assert.Equal("2026-03-24", result[0]);
    }

    [Fact]
    public async Task ListVirtualDirectoriesAsync_ReturnsOnlyFirstSegmentForDeepPaths()
    {
        // Arrange — deeper paths still only surface the first segment.
        var service = new MockBlobStorageService();
        await service.UploadAsync(Bytes("a"), ContainerName, "2026-03-24/archive/old/list.pdf", "application/pdf");
        await service.UploadAsync(Bytes("b"), ContainerName, "2026-03-24/list.pdf", "application/pdf");

        // Act
        var result = await service.ListVirtualDirectoriesAsync(ContainerName);

        // Assert
        Assert.Single(result);
        Assert.Equal("2026-03-24", result[0]);
    }
}
```

- [ ] **Step 2: Run the new tests and verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter FullyQualifiedName~MockBlobStorageServiceTests
```

Expected: all five tests throw `NotImplementedException` from the Task 1 stub. RED.

- [ ] **Step 3: Replace the stub with the real hierarchical mock impl**

In `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs`, replace the stub `ListVirtualDirectoriesAsync` added in Task 1 with:

```csharp
    public Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken = default)
    {
        if (!_containers.ContainsKey(containerName))
        {
            IReadOnlyList<string> empty = new List<string>().AsReadOnly();
            return Task.FromResult(empty);
        }

        // Mirror Azure's GetBlobsByHierarchyAsync(delimiter: "/") semantics:
        //   - skip keys that contain no "/" (loose top-level blobs)
        //   - take the substring before the first "/" (first-level segment only)
        //   - distinct
        IReadOnlyList<string> prefixes = _containers[containerName]
            .Keys
            .Select(name =>
            {
                var slash = name.IndexOf('/');
                return slash < 0 ? null : name[..slash];
            })
            .Where(prefix => prefix is not null)
            .Select(prefix => prefix!)
            .Distinct()
            .ToList()
            .AsReadOnly();

        return Task.FromResult(prefixes);
    }
```

- [ ] **Step 4: Run the mock tests and verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter FullyQualifiedName~MockBlobStorageServiceTests
```

Expected: all five tests pass (GREEN).

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs \
        backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageServiceTests.cs
git commit -m "test(filestorage): implement hierarchical semantics in mock blob storage"
```

---

## Task 6: Implement `AzureBlobStorageService.ListVirtualDirectoriesAsync`

**Why this task exists:** Production code path. Unit tests via mocking `BlobServiceClient` for an async hierarchy enumerable are heavy and brittle (per arch-review: handler + mock coverage is sufficient). We implement carefully using named arguments and the same try/catch/log pattern as `ListBlobsAsync`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`

- [ ] **Step 1: Replace the stub with the real implementation**

In `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`, replace the stub `ListVirtualDirectoriesAsync` added in Task 1 with:

```csharp
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var prefixes = new List<string>();

            // Named arguments avoid the SDK's positional ordering trap:
            //   GetBlobsByHierarchyAsync(BlobTraits, BlobStates, string delimiter, string prefix, CancellationToken).
            // We want the FIRST level only (delimiter "/") and the WHOLE container (prefix null).
            // No GetOrCreateContainerAsync — matches ListBlobsAsync behaviour.
            await foreach (var item in containerClient.GetBlobsByHierarchyAsync(
                prefix: null,
                delimiter: "/",
                cancellationToken: cancellationToken))
            {
                if (item.IsPrefix)
                {
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

- [ ] **Step 2: Verify the whole solution still builds**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with no new warnings.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs
git commit -m "feat(filestorage): implement Azure hierarchical listing"
```

---

## Task 7: Final validation (build, format, full test run)

**Files:** none (verification only)

- [ ] **Step 1: Run `dotnet format` and amend if it changed anything**

```bash
dotnet format backend/Anela.Heblo.sln
```

Then:

```bash
git status
```

If files changed:

```bash
git add -u
git commit -m "chore: dotnet format"
```

If nothing changed, proceed.

- [ ] **Step 2: Run the full backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln
```

Expected: all tests pass. Pay particular attention to:
- `GetExpeditionDatesHandlerTests` (5 tests)
- `MockBlobStorageServiceTests` (5 tests)
- Any other test that uses `MockBlobStorageService` — they should be unaffected because we only added a method.

- [ ] **Step 3: Sanity-check the consumer surface**

Run a final grep to confirm no other handler accidentally still relies on the old shape we removed:

```bash
grep -rn "ListBlobsAsync" backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/
```

Expected output: only `GetExpeditionListsByDateHandler.cs` references `ListBlobsAsync` (it has always done so with a real prefix and is unchanged by this work). `GetExpeditionDatesHandler.cs` no longer references `ListBlobsAsync`.

- [ ] **Step 4: Verify no remnants of the old projection logic**

```bash
grep -n "b\.Name\.Split" backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs
```

Expected: no matches (the `Split('/')` and `Distinct()` projection are gone).

- [ ] **Step 5: Done — no further commit needed unless `dotnet format` produced changes.**

---

## Out-of-Scope Reminders (do NOT do)

- Do **not** push pagination into the storage layer (continuation tokens). YAGNI — domain growth is ~250 prefixes/year, two orders of magnitude under Azure's 5000-prefix cap. The handler keeps `Skip`/`Take` in memory.
- Do **not** auto-create the container in `ListVirtualDirectoriesAsync` — match `ListBlobsAsync` behaviour. The XML doc states this contract; future maintainers must not "helpfully" add it.
- Do **not** touch `ListBlobsAsync` or any of its callers (`GetExpeditionListsByDateHandler`, `DownloadExpeditionListHandler`, `ReprintExpeditionListHandler`, `DownloadFromUrlHandler`).
- Do **not** add a cache layer, a frontend change, an OpenAPI contract change, or a blob-layout migration.
- Do **not** add a positional call to `GetBlobsByHierarchyAsync` — the SDK signature is `(BlobTraits, BlobStates, string delimiter, string prefix, CancellationToken)` and positional calls swap `delimiter` and `prefix`. Always use named arguments.

## Spec Coverage Self-Check

| Spec item | Implemented in |
|-----------|---------------|
| FR-1: New interface method with full XML contract | Task 1 / Step 1 |
| FR-1: XML doc states "does NOT auto-create container" | Task 1 / Step 1 (in `<remarks>`) |
| FR-2: `AzureBlobStorageService` impl using `GetBlobsByHierarchyAsync` | Task 6 |
| FR-2: Strip trailing `/`, return `IReadOnlyList<string>`, structured log, rethrow | Task 6 |
| FR-2: No `GetOrCreateContainerAsync` call | Task 6 (code + comment) |
| FR-3: Handler migrated to new method | Task 3 |
| FR-3: `DateOnly.TryParseExact("yyyy-MM-dd")` filter retained | Task 3 (unchanged `IsValidDatePrefix`) |
| FR-3: `OrderByDescending(StringComparer.Ordinal)` | Task 3 |
| FR-3: Pagination math unchanged | Task 3 |
| FR-4: Existing 3 handler tests migrated to new method | Task 2 |
| FR-4: New test — new method called exactly once, old method never | Task 4 / Step 1 (`Handle_CallsListVirtualDirectoriesOnce_AndNeverCallsListBlobs`) |
| FR-4: New test — non-date prefixes filtered | Task 4 / Step 1 (`Handle_FiltersOutNonDatePrefixes`) |
| FR-4: `MockBlobStorageService` hierarchical impl | Task 5 / Step 3 |
| FR-4: Mock-level coverage of strip-slash + exclude-loose-top-level | Task 5 / Step 1 (5 tests) |
| FR-4: `dotnet test` passes | Task 7 / Step 2 |
| NFR-2 (security): no new authn surface, secrets, or external calls | Inherent — no new dependencies added |
| NFR-3 (compatibility): contract additive; no other handler touched | Task 1 (interface only adds); Task 7 / Step 3 (grep verification) |
| NFR-4 (observability): structured log on error; no success log | Task 6 (mirrors `ListBlobsAsync` pattern) |
| Arch-review amendment 1: explicit mock hierarchical semantics requirement | Task 5 / Step 3 (impl) + Task 5 / Step 1 (tests) |
| Arch-review amendment 2: no-auto-create contract in interface XML doc | Task 1 / Step 1 |
