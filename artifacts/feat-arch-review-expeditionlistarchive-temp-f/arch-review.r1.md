# Architecture Review: Fix Temp File Leak in ReprintExpeditionListHandler

## Skip Design: true

Backend bug fix only. No UI, no API contract, no DTO change.

## Architectural Fit Assessment

The proposed fix aligns cleanly with patterns already established in this codebase. `Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf")` is the same idiom used in:

- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs:162` — production code building temp paths with `Path.Combine(Path.GetTempPath(), fileName)`.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs:11-12` and `CupsPrintingServiceTests.cs:14` — sibling print-pipeline tests that already use `Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())`.

The handler lives in the vertical slice `Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/`. The change is localized to a single line (`ReprintExpeditionListHandler.cs:29`) inside that slice. No module boundary, no DI registration, no contract is touched. Integration points (`IBlobStorageService`, `IPrintQueueSink`, `IOptions<PrintPickingListOptions>`) are unchanged.

The spec correctly defers the same-shaped bug in `PlaudCliClient.cs:29,43` to a separate sweep — that file is in a different adapter slice and its lifecycle (single-call CLI invocation) differs.

## Proposed Architecture

### Component Overview

```
ReprintExpeditionListRequest (MediatR)
        │
        ▼
ReprintExpeditionListHandler ─────► BlobPathValidator (guard)
        │                          
        │ 1. allocate temp path  ◄── CHANGE: GUID-based, no pre-created file
        │ 2. download blob ──────► IBlobStorageService
        │ 3. write to temp file
        │ 4. hand path to print   ► IPrintQueueSink (CUPS)
        │ 5. DeleteTempFile (finally) — single cleanup point
        ▼
ReprintExpeditionListResponse
```

No structural change. Only step 1's path-allocation mechanism flips from `Path.GetTempFileName() + ".pdf"` (which creates a sibling `.tmp` file on disk) to `Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf")` (which allocates a string only).

### Key Design Decisions

#### Decision 1: GUID-based path vs. wrapping `Path.GetTempFileName` cleanup
**Options considered:**
- (a) `Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf")` — single allocation, no disk side effect.
- (b) Keep `Path.GetTempFileName()`, capture both the original `.tmp` path and the renamed `.pdf` path, delete both.
- (c) Switch to a stream-based pipeline that pipes blob → print sink without disk.

**Chosen approach:** (a).

**Rationale:** (a) matches existing codebase idiom (`ShoptetApiExpeditionListSource.cs:162`), is a one-line change, and removes one filesystem syscall per reprint. (b) doubles the cleanup surface and keeps a useless `.tmp` artifact in the success path's atomic window. (c) is the spec's "Out of Scope" item — would require redesigning `IPrintQueueSink`, which takes a path collection.

#### Decision 2: Keep `.pdf` extension in temp filename
**Chosen approach:** Preserve `.pdf` suffix.

**Rationale:** `IPrintQueueSink` implementations (CUPS sink) may dispatch on extension. The spec calls this out as an acceptance criterion (FR-1). Verified that `FileSystemPrintQueueSink` and `AzureBlobPrintQueueSink` accept arbitrary paths but propagate the filename downstream — preserving `.pdf` is the safe choice.

#### Decision 3: GUID `N` format (32 hex chars, no hyphens)
**Chosen approach:** `Guid.NewGuid():N`.

**Rationale:** Matches the spec. Avoids hyphens which some downstream tools handle inconsistently. Filename uniqueness is preserved (122 bits of entropy — no realistic collision under concurrent reprints).

## Implementation Guidance

### Directory / Module Structure
No new files. Single edit:
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs:29`

### Interfaces and Contracts
None changed. `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` shape, `IBlobStorageService.DownloadAsync`, and `IPrintQueueSink.SendAsync` signatures remain identical.

### Data Flow
Unchanged. Only the path-string source changes from "GetTempFileName side-effect + concat" to "pure in-memory construction."

### Testing Guidance
Existing tests in `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` cover success and validation-failure paths with mocked dependencies but do not assert anything about the file system. The spec's leak test must be additive:

1. **Add a third test** — `Handle_SuccessfulReprint_LeavesNoFilesInTempDirectory`. Snapshot the temp directory file count (or files matching `*.pdf`/`*.tmp`) before, run the handler, snapshot after, assert equality. Use a `MemoryStream` with PDF magic bytes as the blob (same as existing test).
2. **Add a fourth test** — `Handle_BlobDownloadFails_LeavesNoFilesInTempDirectory`. Configure `_blobStorageServiceMock.Setup(...).ThrowsAsync(new IOException(...))`, then assert the same snapshot equality after the exception bubbles up.
3. **Capture the path** passed to `IPrintQueueSink` via `Callback` and assert it ends in `.pdf` and starts with `Path.GetTempPath()`.

Do **not** introduce a private temp directory or a temp-path abstraction (`ITempFileProvider`). The spec explicitly forbids broader refactoring (Out of Scope) and the existing pattern in sibling tests uses the system temp dir directly.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| New leak test is flaky because *other* code in parallel test runs writes to `Path.GetTempPath()` | MEDIUM | Snapshot only files matching a handler-specific pattern (e.g., 32-hex-char `.pdf` filenames, or capture the path returned to the sink and assert that specific path does not exist post-call). Avoid counting all temp-dir files. |
| Hidden assumption in CUPS sink about the path being pre-existing | LOW | Code path already calls `File.OpenWrite(tempFile)` which creates the file before the sink sees it. Sink receives an existing, fully-written file in both old and new code. |
| Race with concurrent reprints producing same path | NEGLIGIBLE | 122-bit GUID entropy. Already the assumption baked into the rest of the codebase. |
| Reviewer asks "why not fix `PlaudCliClient.cs` too" | LOW | Spec explicitly defers that to a separate sweep. Cite the Out-of-Scope clause if asked. |

## Specification Amendments

The spec's Testing Strategy says "use a temp-directory snapshot (count files before/after) to verify no leak." This is fragile under parallel test execution (`xUnit` runs test classes in parallel by default). **Amend the testing approach** to: capture the path passed into `IPrintQueueSink.SendAsync` via Moq `Callback`, then assert `File.Exists(capturedPath) == false` after `Handle` returns (success path) or after the exception is caught (failure path). This is deterministic regardless of what other tests write to `Path.GetTempPath()`.

## Prerequisites

None. No migrations, config, or infrastructure changes. The fix is shippable in a single commit touching:
- `ReprintExpeditionListHandler.cs:29` (production)
- `ReprintExpeditionListHandlerTests.cs` (add two leak-regression tests)

Validation gates per `CLAUDE.md`: `dotnet build` + `dotnet format` + the two new tests pass.