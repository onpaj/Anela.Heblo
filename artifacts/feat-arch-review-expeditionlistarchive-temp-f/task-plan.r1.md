# Fix Temp File Leak in ReprintExpeditionListHandler — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the orphaned `tmpXXXX.tmp` file that `ReprintExpeditionListHandler` leaks into the system temp directory on every reprint invocation, by switching from `Path.GetTempFileName() + ".pdf"` to a pure in-memory GUID-based path.

**Architecture:** Single-line production change inside the `ReprintExpeditionList` vertical slice (`backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs:29`). No new files, no contract changes, no DI changes — the existing `DeleteTempFile` helper remains the single cleanup point. Two regression tests are added that capture the path passed into `IPrintQueueSink.SendAsync` via Moq `Callback` and assert `File.Exists(path) == false` after the handler returns (success path) or throws (failure path).

**Tech Stack:** .NET 8, C# 12, xUnit, Moq, MediatR. No new packages.

---

## File Structure

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs:29` — production fix.
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` — add two regression tests (one success path, one failure path) verifying no temp file is left behind and the path handed to the sink ends in `.pdf` and is rooted under `Path.GetTempPath()`.

**Create:** none.

**Do not touch:**
- `IPrintQueueSink`, `IBlobStorageService`, `BlobPathValidator`, `PrintPickingListOptions`, `ReprintExpeditionListRequest`/`Response`, DI registrations, or sibling adapters (`PlaudCliClient.cs` is explicitly out of scope per spec).

---

## Task 1: Add Regression Test — Success Path Leaves No Temp File

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs`

This task adds the failing test **first** (TDD RED), before any production change. It captures the temp path passed to `IPrintQueueSink.SendAsync` and asserts the file no longer exists once `Handle` returns. With the current buggy production code, the captured path is `tmpXXXX.tmp.pdf` (deleted successfully by `DeleteTempFile` — the test on that captured path will actually PASS for the captured `.pdf` path), so to make this test actually expose the leak we must additionally assert that the *underlying* `Path.GetTempFileName()` artifact (a sibling `.tmp` file) was not left behind. The clean way to do this without coupling to internals is to assert on the **single captured path the sink received** AND on the snapshot of any handler-attributable files created during the call.

After thinking through the failure-mode again: the leak is the `.tmp` file created as a side effect of `Path.GetTempFileName()`. The sink only sees the `.pdf` path. Asserting only on the captured `.pdf` path will not expose the leak. The deterministic way to expose it is to **snapshot files in `Path.GetTempPath()` matching a stable pattern before the call, run the handler, snapshot again, and assert the delta is exactly the empty set**.

Per the architecture review (which warned that broad temp-dir snapshots are flaky under parallel xUnit execution), we constrain the snapshot to a narrow pattern: files created during the call window, matching either `tmp*.tmp` or `tmp*.tmp.pdf` (the two filename shapes the buggy code produces), or — after the fix — 32-hex-char `.pdf` filenames. To make this robust regardless of which side of the fix we are on, we additionally capture the path the sink received and explicitly assert that path does not exist post-call.

The test:
1. Captures the temp file path passed to `IPrintQueueSink.SendAsync` via Moq `Callback`.
2. Snapshots the list of files currently under `Path.GetTempPath()` (full path strings) before invoking the handler.
3. Invokes the handler.
4. After completion, asserts: the captured path exists and ends in `.pdf` and is rooted under `Path.GetTempPath()` *during the callback* (we record this fact inside the callback because the file is deleted before the test resumes); `File.Exists(capturedPath)` is `false` after `Handle` returns; the set of files in `Path.GetTempPath()` is a subset of the pre-call snapshot (i.e., no new files attributable to this handler remain). To avoid flakiness from unrelated parallel writes, we filter the post-call delta to files whose **name** matches `tmp*.tmp` or `tmp*.tmp.pdf` — the only shapes the buggy code can produce — or any 32-hex-char `.pdf` file (the post-fix shape). Any such leftover file fails the test.

- [ ] **Step 1: Write the failing test for the success path**

Append this test inside `ReprintExpeditionListHandlerTests` (after the existing `Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob` test). Also add the `System.IO` and `System.Text.RegularExpressions` `using` directives at the top of the file if they are not already present (`System.IO` is implicit, `System.Text.RegularExpressions` is needed for the filename pattern check).

Add to the `using` block at the top of the file (only those not already present):

```csharp
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
```

Add the test method to the class:

```csharp
    [Fact]
    public async Task Handle_SuccessfulReprint_LeavesNoTempFileBehind()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-002.pdf";
        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // PDF magic bytes
        var blobStream = new MemoryStream(pdfContent);

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);

        string? capturedPath = null;
        bool capturedPathExistedDuringSinkCall = false;
        bool capturedPathEndsWithPdf = false;
        bool capturedPathRootedInTempDir = false;

        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .Callback<IEnumerable<string>, CancellationToken>((paths, _) =>
            {
                capturedPath = paths.Single();
                capturedPathExistedDuringSinkCall = File.Exists(capturedPath);
                capturedPathEndsWithPdf = capturedPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                capturedPathRootedInTempDir = capturedPath.StartsWith(Path.GetTempPath(), StringComparison.Ordinal);
            })
            .Returns(Task.CompletedTask);

        var tempDir = Path.GetTempPath();
        var preCallFiles = new HashSet<string>(Directory.EnumerateFiles(tempDir));

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedPath);
        Assert.True(capturedPathEndsWithPdf, "Path passed to sink must end in .pdf");
        Assert.True(capturedPathRootedInTempDir, "Path passed to sink must be rooted under Path.GetTempPath()");
        Assert.True(capturedPathExistedDuringSinkCall, "PDF file must exist on disk when sink is invoked");
        Assert.False(File.Exists(capturedPath), "Temp file must be deleted after handler returns");

        // Detect leaks: any *new* file in the temp dir whose name matches the
        // shapes the handler could plausibly produce (current buggy or fixed).
        var postCallFiles = Directory.EnumerateFiles(tempDir).ToList();
        var handlerArtifactPattern = new Regex(@"^(tmp[^/\\]*\.tmp(\.pdf)?|[0-9a-fA-F]{32}\.pdf)$");
        var leakedFiles = postCallFiles
            .Where(p => !preCallFiles.Contains(p))
            .Where(p => handlerArtifactPattern.IsMatch(Path.GetFileName(p)))
            .ToList();

        Assert.True(
            leakedFiles.Count == 0,
            $"Handler must not leave temp files behind. Found: {string.Join(", ", leakedFiles)}");
    }
```

- [ ] **Step 2: Run the new test and verify it fails for the right reason**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ReprintExpeditionListHandlerTests.Handle_SuccessfulReprint_LeavesNoTempFileBehind" \
  --nologo
```

Expected: FAIL. The failure must be the leak-detection assertion (`"Handler must not leave temp files behind. Found: /tmp/tmpXXXXXX.tmp"`), proving the test catches the bug currently in production. If the test fails on `capturedPathEndsWithPdf` or `capturedPathExistedDuringSinkCall` instead, stop — the test instrumentation is wrong, not the production code.

- [ ] **Step 3: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs
git commit -m "test: add regression test for ReprintExpeditionListHandler temp file leak"
```

---

## Task 2: Add Regression Test — Failure Path Leaves No Temp File

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs`

Cover the catch-block cleanup branch: when `IBlobStorageService.DownloadAsync` throws, `DeleteTempFile` is called inside the `catch` block. We assert the same invariant: after the exception bubbles out of `Handle`, no handler-attributable file remains in the temp directory.

Because the sink is never called in this scenario, we cannot capture the path via the sink. Instead, we rely solely on the temp-directory delta (filtered to handler-shaped filenames). This is the same robust pattern as Task 1's leak detection.

- [ ] **Step 1: Write the failing test for the failure path**

Append this test to `ReprintExpeditionListHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_BlobDownloadFails_LeavesNoTempFileBehind()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-003.pdf";

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ThrowsAsync(new IOException("blob unavailable"));

        var tempDir = Path.GetTempPath();
        var preCallFiles = new HashSet<string>(Directory.EnumerateFiles(tempDir));

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act
        await Assert.ThrowsAsync<IOException>(() => _handler.Handle(request, default));

        // Assert: same leak detection as the success-path test.
        var postCallFiles = Directory.EnumerateFiles(tempDir).ToList();
        var handlerArtifactPattern = new Regex(@"^(tmp[^/\\]*\.tmp(\.pdf)?|[0-9a-fA-F]{32}\.pdf)$");
        var leakedFiles = postCallFiles
            .Where(p => !preCallFiles.Contains(p))
            .Where(p => handlerArtifactPattern.IsMatch(Path.GetFileName(p)))
            .ToList();

        Assert.True(
            leakedFiles.Count == 0,
            $"Handler must not leave temp files behind on failure. Found: {string.Join(", ", leakedFiles)}");

        _cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the new test and verify it fails for the right reason**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ReprintExpeditionListHandlerTests.Handle_BlobDownloadFails_LeavesNoTempFileBehind" \
  --nologo
```

Expected: FAIL with the leak-detection assertion message, listing a `tmpXXXXXX.tmp` file under `/tmp` (the artifact left by `Path.GetTempFileName()` — the `catch` block deletes the `.pdf` path but not the underlying `.tmp` file). If instead it fails because the `IOException` was not thrown, or because `SendAsync` was unexpectedly called, the test instrumentation is wrong — stop and fix.

- [ ] **Step 3: Commit the second failing test**

```bash
git add backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs
git commit -m "test: add regression test for ReprintExpeditionListHandler temp file leak on failure"
```

---

## Task 3: Apply the Production Fix

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs:29`

Replace the buggy path construction. This is the minimal change required to make both failing tests pass without altering any other handler behavior.

- [ ] **Step 1: Edit the temp-path allocation line**

In `ReprintExpeditionListHandler.cs`, change line 29 from:

```csharp
        var tempFile = Path.GetTempFileName() + ".pdf";
```

to:

```csharp
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
```

No other lines in the file change. The two `try` blocks, `DeleteTempFile` calls, and exception flow stay exactly as they are.

- [ ] **Step 2: Re-run both regression tests and verify they pass**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ReprintExpeditionListHandlerTests" \
  --nologo
```

Expected: PASS for all four tests:
- `Handle_ValidBlobPath_DownloadsAndSendsToCupsSink` (pre-existing)
- `Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob` (pre-existing)
- `Handle_SuccessfulReprint_LeavesNoTempFileBehind` (new — Task 1)
- `Handle_BlobDownloadFails_LeavesNoTempFileBehind` (new — Task 2)

- [ ] **Step 3: Run the full backend test suite to confirm no collateral damage**

Run:

```bash
dotnet test backend/Anela.Heblo.sln --nologo
```

Expected: all tests pass. If any unrelated test fails, stop and investigate before continuing — the change is one line and should not affect any other handler, but a passing full suite is the gate per `CLAUDE.md`.

- [ ] **Step 4: Run build and format gates**

Per `CLAUDE.md` "Validation before completion":

```bash
dotnet build backend/Anela.Heblo.sln --nologo
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: both commands exit 0. If `dotnet format --verify-no-changes` reports diffs, run `dotnet format backend/Anela.Heblo.sln` and re-run the verify command.

- [ ] **Step 5: Commit the production fix**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs
git commit -m "fix: prevent temp file leak in ReprintExpeditionListHandler

Path.GetTempFileName() creates a zero-byte file on disk as a side effect.
The handler appended '.pdf' to that path, producing a different path that
referenced a non-existent file, then wrote and deleted that .pdf path —
leaving the original .tmp file orphaned in the system temp directory on
every reprint, regardless of success or failure.

Replace with Path.Combine(Path.GetTempPath(), \$\"{Guid.NewGuid():N}.pdf\"),
which produces a unique path with no filesystem side effect and preserves
the .pdf extension the CUPS sink relies on."
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Covered by |
|---|---|
| FR-1: Eliminate orphaned temp file (success path) | Task 1 test + Task 3 fix |
| FR-1: Eliminate orphaned temp file (failure path) | Task 2 test + Task 3 fix |
| FR-1: Path ends in `.pdf` | Task 1 `capturedPathEndsWithPdf` assertion |
| FR-1: Path uniqueness (no collisions) | Guaranteed by 122-bit `Guid.NewGuid()` entropy; no test needed |
| FR-2: `DeleteTempFile` remains the single cleanup point | Task 3 changes only line 29; lines 30–56 untouched |
| FR-2: No new `try/finally` scaffolding | Same — verified by surgical edit scope |
| FR-3: Identical PDF payload at temp path | Existing `Handle_ValidBlobPath_DownloadsAndSendsToCupsSink` still passes (Task 3 Step 2) |
| FR-3: Sink receives `.pdf` path | Task 1 `capturedPathEndsWithPdf` assertion |
| NFR-1: Performance | One fewer syscall per reprint — automatic consequence of the fix |
| NFR-2: Security (no user input in path) | Path components are `Path.GetTempPath()` + GUID; verified by inspection |
| NFR-3: Reliability (no new failure modes) | Existing tests cover success/validation-failure; new tests cover I/O-failure cleanup |

**Spec amendment from arch review (use Moq `Callback` to capture path, assert `File.Exists` deterministically rather than counting all temp-dir files):** Honored. Task 1 captures the sink path via `Callback` and asserts `File.Exists(capturedPath) == false`. The supplementary directory-delta check is filtered to a narrow regex of handler-shaped filenames (`tmp*.tmp`, `tmp*.tmp.pdf`, or 32-hex-char `.pdf`) to remain deterministic under parallel xUnit execution while still catching the underlying-`.tmp`-file leak that the sink-path assertion alone cannot see.

**Placeholder scan:** No TBD/TODO/"add appropriate error handling"/"similar to Task N"/undefined-reference items found. Every code block is complete. Every command shows exact arguments and expected output.

**Type consistency:** All identifiers used in test code (`_blobStorageServiceMock`, `_cupsSinkMock`, `_handler`, `ContainerName`, `ReprintExpeditionListRequest`, `IBlobStorageService.DownloadAsync`, `IPrintQueueSink.SendAsync`) match the fields and types established in the existing `ReprintExpeditionListHandlerTests` constructor and the production handler. Method signature `SendAsync(IEnumerable<string>, CancellationToken)` matches the existing test's `Setup`. `ReprintExpeditionListResponse.Success` matches the existing assertion. The regex pattern is consistent between Task 1 and Task 2.

**Out-of-scope items confirmed untouched:** No edits to `PlaudCliClient.cs`, `DeleteTempFile`, `IPrintQueueSink`, stream-based pipeline, or any background cleanup of pre-existing orphans. Confirmed by limiting Task 3 to a single-line edit at line 29.

---

## Manual Verification (post-merge, optional per spec)

The spec includes a manual verification step. This is not part of the automated plan but is captured here for the deploying engineer:

1. Deploy to dev environment.
2. Note current file count in `/tmp` matching `tmp*.tmp` and `[0-9a-f]{32}.pdf`.
3. Trigger a reprint end-to-end via the UI.
4. Confirm the PDF prints on the CUPS-attached printer.
5. Re-list `/tmp` — the file count for both patterns should match the pre-reprint snapshot. If a single `.pdf` briefly appears mid-call, that is expected; it must be gone within ~1 second of the reprint completing.
