# Eliminate 409 Conflict Noise from `PUT stheblo` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `AzureBlobPrintQueueSink` from issuing `CreateIfNotExistsAsync` on every `SendAsync`, so App Insights no longer records ~27 false-positive `PUT stheblo` failures per day.

**Architecture:** Cache container-existence verification on the sink instance using a `SemaphoreSlim` + `bool` (double-checked, flag set only after the SDK call succeeds). Promote `IPrintQueueSink` from Scoped to Singleton so the cache spans the process lifetime. Pattern mirrors the existing `AzureBlobStorageService` precedent but hardens it against transient failures (NFR-2 — flag is set only on success, retry possible on next call).

**Tech Stack:** C# 12 / .NET 8, Azure.Storage.Blobs SDK, xUnit, Moq.

---

## File Structure

**Touched files (3):**

| File | Responsibility | Change |
|------|----------------|--------|
| `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs` | The sink itself — add caching fields, refactor `SendAsync` to call new `EnsureContainerAsync`. | Modify |
| `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` | DI registration for the sink. | Modify (line 24: `AddScoped` → `AddSingleton`) |
| `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs` | Unit tests for the sink. | Modify — add 4 tests (FR-1 sequential, FR-3 concurrent, NFR-2 retry-after-failure, FR-2 empty-list still skips). |

No new files. No new NuGet packages.

---

## Task 1: Sequential dedup test (FR-1) — write failing test first

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs`

- [ ] **Step 1: Add the failing test**

Open `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs` and add this test inside the `AzureBlobPrintQueueSinkTests` class, just after the existing `SendAsync_EmptyFilePaths_DoesNotUpload` test:

```csharp
    [Fact]
    public async Task SendAsync_CalledTwice_InvokesCreateIfNotExistsOnce()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        var sink = CreateSink();

        // Act
        await sink.SendAsync([file]);
        await sink.SendAsync([file]);

        // Assert
        _containerClient.Verify(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobContainerEncryptionScopeOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AzureBlobPrintQueueSinkTests.SendAsync_CalledTwice_InvokesCreateIfNotExistsOnce" \
  --no-restore
```

Expected: 1 failed. The failure message will say `CreateIfNotExistsAsync` was called 2 times when 1 was expected (the current code calls it on every `SendAsync`).

- [ ] **Step 3: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs
git commit -m "test: add failing test for one-time container ensure in print queue sink"
```

---

## Task 2: Implement the cache in `AzureBlobPrintQueueSink`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`

- [ ] **Step 1: Replace the file contents with the cached implementation**

Open `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs` and replace the entire file with:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Azure.Features.ExpeditionList;

public class AzureBlobPrintQueueSink : IPrintQueueSink
{
    private readonly BlobContainerClient _containerClient;
    private readonly TimeProvider _clock;
    private readonly ILogger<AzureBlobPrintQueueSink> _logger;
    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private bool _containerEnsured;

    public AzureBlobPrintQueueSink(
        BlobContainerClient containerClient,
        TimeProvider clock,
        ILogger<AzureBlobPrintQueueSink> logger)
    {
        _containerClient = containerClient;
        _clock = clock;
        _logger = logger;
    }

    public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var files = filePaths.ToList();
        if (files.Count == 0)
            return;

        await EnsureContainerAsync(cancellationToken);
        var datePrefix = _clock.GetLocalNow().ToString("yyyy-MM-dd");

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogWarning("Skipping file with invalid path: {FilePath}", filePath);
                continue;
            }

            var blobName = $"{datePrefix}/{fileName}";
            await using var fileStream = File.OpenRead(filePath);
            var blobClient = _containerClient.GetBlobClient(blobName);
            await blobClient.UploadAsync(fileStream, overwrite: true, cancellationToken: cancellationToken);
            _logger.LogDebug("Uploaded {FileName} to blob {BlobName}", fileName, blobName);
        }
    }

    // Verifies the target blob container exists at most once per process lifetime.
    // The flag is set only after CreateIfNotExistsAsync returns successfully — a transient
    // failure leaves _containerEnsured == false so the next SendAsync retries.
    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerEnsured) return;

        await _ensureGate.WaitAsync(cancellationToken);
        try
        {
            if (_containerEnsured) return;

            _logger.LogDebug("Ensuring blob container exists for print queue sink");
            await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _containerEnsured = true;
        }
        finally
        {
            _ensureGate.Release();
        }
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj --no-restore
```

Expected: Build succeeded. 0 Errors.

- [ ] **Step 3: Run the FR-1 test to confirm it now passes**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AzureBlobPrintQueueSinkTests.SendAsync_CalledTwice_InvokesCreateIfNotExistsOnce" \
  --no-restore
```

Expected: Passed: 1.

- [ ] **Step 4: Run the full sink test class to confirm no regression**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AzureBlobPrintQueueSinkTests" \
  --no-restore
```

Expected: Passed: 4 (the 3 pre-existing + the new one). Failed: 0.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs
git commit -m "fix: cache container-existence check in AzureBlobPrintQueueSink"
```

---

## Task 3: Concurrent dedup test (FR-3)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs`

- [ ] **Step 1: Add the concurrent-callers test**

Add this test inside the `AzureBlobPrintQueueSinkTests` class, after the `SendAsync_CalledTwice_InvokesCreateIfNotExistsOnce` test added in Task 1:

```csharp
    [Fact]
    public async Task SendAsync_FourParallelFirstCalls_InvokesCreateIfNotExistsExactlyOnce()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        // Block CreateIfNotExistsAsync until released, so all 4 callers race to the gate
        // at the same point in time. Without this, the first caller could complete before
        // the others even enter the method and the test would not exercise the gate.
        var release = new TaskCompletionSource();
        _containerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await release.Task;
                return Mock.Of<Azure.Response<BlobContainerInfo>>();
            });

        var sink = CreateSink();

        // Act — fire 4 SendAsync calls in parallel, then release the gate
        var calls = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => sink.SendAsync([file])))
            .ToArray();

        // Give the tasks time to converge on the semaphore before releasing
        await Task.Delay(50);
        release.SetResult();

        await Task.WhenAll(calls);

        // Assert
        _containerClient.Verify(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobContainerEncryptionScopeOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run the new test**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AzureBlobPrintQueueSinkTests.SendAsync_FourParallelFirstCalls_InvokesCreateIfNotExistsExactlyOnce" \
  --no-restore
```

Expected: Passed: 1. (The semaphore implementation from Task 2 already enforces this — the test asserts the existing implementation, not new code.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs
git commit -m "test: verify single CreateIfNotExistsAsync under parallel first-time SendAsync"
```

---

## Task 4: Retry-after-failure test (NFR-2)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs`

- [ ] **Step 1: Add the retry-after-failure test**

Add this test inside the `AzureBlobPrintQueueSinkTests` class, after the parallel-callers test added in Task 3:

```csharp
    [Fact]
    public async Task SendAsync_FirstCreateIfNotExistsThrows_RetriesOnNextCall()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        // First call to CreateIfNotExistsAsync throws; subsequent calls succeed.
        var attempt = 0;
        _containerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempt++;
                if (attempt == 1)
                {
                    throw new InvalidOperationException("transient failure");
                }
                return Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>());
            });

        var sink = CreateSink();

        // Act + Assert — first call throws
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.SendAsync([file]));

        // Second call succeeds and triggers a retry of CreateIfNotExistsAsync
        await sink.SendAsync([file]);

        // Third call should NOT re-invoke CreateIfNotExistsAsync (cache is now hot)
        await sink.SendAsync([file]);

        // Assert — exactly 2 invocations total: the failed first, the successful second
        _containerClient.Verify(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobContainerEncryptionScopeOptions>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
```

- [ ] **Step 2: Run the new test**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AzureBlobPrintQueueSinkTests.SendAsync_FirstCreateIfNotExistsThrows_RetriesOnNextCall" \
  --no-restore
```

Expected: Passed: 1. The implementation from Task 2 sets `_containerEnsured = true` *after* the `await` returns, so a throw leaves the flag false and the next call retries.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs
git commit -m "test: verify EnsureContainer retries after transient first-call failure"
```

---

## Task 5: Promote `IPrintQueueSink` to Singleton in DI

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs:24`

- [ ] **Step 1: Change the lifetime**

Open `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`. Locate line 24:

```csharp
        services.AddScoped<IPrintQueueSink, AzureBlobPrintQueueSink>();
```

Replace with:

```csharp
        services.AddSingleton<IPrintQueueSink, AzureBlobPrintQueueSink>();
```

- [ ] **Step 2: Build the backend solution**

```bash
cd backend && dotnet build --no-restore
```

Expected: Build succeeded. 0 Errors. 0 Warnings related to this change.

If a captive-dependency warning appears (e.g. `ILogger<T>`, `BlobContainerClient`, `TimeProvider` resolved as scoped/transient into the singleton), STOP — investigate. Per the architecture review, all three are singleton-safe in the current codebase. Any failure here indicates an upstream change that needs separate analysis.

- [ ] **Step 3: Run all tests touched by this fix**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AzureBlobPrintQueueSinkTests" \
  --no-restore
```

Expected: Passed: 6 (3 original + 3 added in Tasks 1, 3, 4). Failed: 0.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs
git commit -m "fix: register IPrintQueueSink as singleton to preserve container cache across requests"
```

---

## Task 6: Full backend validation

**Files:** None modified; verification only.

- [ ] **Step 1: Build the whole backend**

```bash
cd backend && dotnet build
```

Expected: Build succeeded. 0 Errors.

- [ ] **Step 2: Run `dotnet format` and verify it produces no changes**

```bash
cd backend && dotnet format --verify-no-changes
```

Expected: Exit code 0. If formatting differences are reported, run `dotnet format` (without `--verify-no-changes`), inspect the diff, commit the formatting changes as a separate `chore: dotnet format` commit, and re-run the verify step.

- [ ] **Step 3: Run the full backend test suite**

```bash
cd backend && dotnet test
```

Expected: All test projects pass. No previously-passing test regresses.

If a test fails outside `AzureBlobPrintQueueSinkTests`, investigate — the singleton lifetime change could surface a hidden captive-dependency bug. Do not attempt to "fix" the failure by reverting to Scoped; instead, identify the offending dependency and bring it to the user.

- [ ] **Step 4: Stage and review final state**

```bash
git status
git log --oneline main..HEAD
```

Expected: 5 new commits on the branch, all related to this fix. No uncommitted changes.

---

## Task 7: Update PR description with manual staging validation step (FR-4)

**Files:** No source files. PR description / commit log only.

- [ ] **Step 1: Note the manual verification step for the PR**

When the PR is opened (separately, via `finishfeature` or the user's workflow), include the following checklist item in the PR description under **Test plan**:

> - [ ] Deploy to Staging. Trigger ≥ 3 expedition list print operations. Open Application Insights → Failures → Dependencies, filter `Target = stheblo.blob.core.windows.net` and `Name = PUT stheblo`. Confirm only one `PUT {container}?restype=container` dependency entry is recorded per app-instance lifetime (i.e. the entry appears on the first print operation after restart, then disappears for subsequent ones).

No code change for this task — only a reminder that the spec requires this manual check (FR-4) before declaring the bug resolved in production. If the validation reveals further `PUT stheblo` failures with a different signature (e.g. real upload conflicts), they are a separate bug per the spec's Out of Scope clause.

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Covered by |
|------------------|------------|
| FR-1 (one CreateIfNotExistsAsync per process) | Task 2 implementation + Task 1 sequential test |
| FR-2 (preserve send behavior — naming, overwrite, empty-list skip, warning on invalid filename) | Task 2 keeps existing behavior verbatim; existing tests `SendAsync_ValidFiles_UploadsEachFileToBlob`, `SendAsync_ValidFile_UsesBlobNameWithDatePrefix`, `SendAsync_EmptyFilePaths_DoesNotUpload` continue to pass — verified in Task 2 Step 4 and Task 6 Step 3 |
| FR-3 (thread-safe, exactly-once under concurrency) | Task 2 `SemaphoreSlim` + double-checked flag; Task 3 parallel test |
| FR-4 (staging telemetry validation) | Task 7 PR checklist note |
| NFR-1 (lock-free steady-state path) | Task 2: `if (_containerEnsured) return;` is the fast path before any semaphore acquisition |
| NFR-2 (retry-after-failure; flag set only after success) | Task 2 sets `_containerEnsured = true` inside `try` after the `await`; Task 4 retry test |
| NFR-3 (no new Info-level logs in steady state) | Task 2 uses `LogDebug` only inside `EnsureContainerAsync`, and only on the first call (after the `_containerEnsured` short-circuit) |
| NFR-4 (no auth model / config change) | No code in Tasks 1–7 touches configuration, secrets, or auth |
| Arch amendment: DI lifetime change to Singleton | Task 5 |
| Arch amendment: parallel-call test with ≥ 4 callers | Task 3 (uses 4) |
| Arch amendment: retry-after-failure test | Task 4 |

All spec/arch-review items are covered.

**Placeholder scan:** No "TBD", "TODO", "implement later", "similar to Task N", or "add appropriate error handling" placeholders. Every code step shows the full code. Every command shows the expected output. ✓

**Type / name consistency:** Field names (`_containerEnsured`, `_ensureGate`), method name (`EnsureContainerAsync`), and the `Microsoft.Extensions.DependencyInjection` registration call (`AddSingleton<IPrintQueueSink, AzureBlobPrintQueueSink>()`) are identical across all tasks. ✓
