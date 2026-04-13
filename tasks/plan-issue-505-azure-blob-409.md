# Implementation Plan: Fix Azure Blob 409 Dependency Failures (Issue #505)

## Problem
`AzureBlobStorageService.GetOrCreateContainerAsync` calls `CreateIfNotExistsAsync` on **every** `UploadAsync` invocation. This issues a `PUT {container}?restype=container` HTTP call every time. When the container already exists, Azure returns HTTP 409. The SDK handles the 409 internally without throwing, but App Insights traces it as a failed dependency — producing 20+ noise failures per day.

## Solution
Cache whether each container has been confirmed to exist using a `ConcurrentDictionary<string, bool>`. Call `CreateIfNotExistsAsync` at most once per container per service lifetime.

## Branch
`fix/issue-505-azure-blob-container-cache`

## Steps

### Step 1: Create git worktree
```bash
git worktree add .worktrees/fix-issue-505-azure-blob-container-cache -b fix/issue-505-azure-blob-container-cache
```

### Step 2: Modify `AzureBlobStorageService`
File: `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`

Add field:
```csharp
private readonly ConcurrentDictionary<string, bool> _containerExists = new();
```

Add `using System.Collections.Concurrent;` import.

Modify `GetOrCreateContainerAsync`:
```csharp
private async Task<BlobContainerClient> GetOrCreateContainerAsync(string containerName, CancellationToken cancellationToken = default)
{
    var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
    if (_containerExists.TryAdd(containerName, true))
    {
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
    }
    return containerClient;
}
```

### Step 3: Add tests to `AzureBlobStorageServiceTests`
File: `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

Add two tests:
1. `UploadAsync_CalledMultipleTimes_ShouldCallCreateIfNotExistsOnlyOnce` — verifies `CreateIfNotExistsAsync` is called exactly once when `UploadAsync` is called 3 times for the same container
2. `UploadAsync_DifferentContainers_ShouldCallCreateIfNotExistsOncePerContainer` — verifies each container gets its own `CreateIfNotExistsAsync` call exactly once

**Note:** These tests need a fresh `AzureBlobStorageService` instance (not the shared `_service` from constructor) because the `_containerExists` dictionary is per-instance. Create local instances in those tests.

### Step 4: Run tests and format
```bash
cd backend
dotnet test --configuration Release --no-build 2>&1 | tail -20
dotnet format --verify-no-changes
```

If `dotnet format` finds violations, run `dotnet format` (without `--verify-no-changes`) to fix them.

### Step 5: Build validation
```bash
cd backend && dotnet build --configuration Release
cd frontend && npm run build
```

### Step 6: Commit with conventional commit
```bash
git add backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs
git add backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs
git commit -m "fix(storage): cache container existence to eliminate 409 dependency noise

CreateIfNotExistsAsync was called on every UploadAsync invocation, causing
Azure to return HTTP 409 for existing containers. App Insights traced these
as failed dependencies (20+/day). Fix: per-container lazy flag using
ConcurrentDictionary ensures CreateIfNotExistsAsync is called at most once
per container per service lifetime.

Closes #505

@claude"
```

### Step 7: Push and create PR
```bash
git push -u origin fix/issue-505-azure-blob-container-cache
```

Then create PR targeting `main`.

## Acceptance Criteria
- [ ] `CreateIfNotExistsAsync` called at most once per container per service instance
- [ ] All existing tests still pass
- [ ] New tests verify the caching behavior
- [ ] `dotnet format --verify-no-changes` passes
- [ ] PR created targeting main, closes #505
