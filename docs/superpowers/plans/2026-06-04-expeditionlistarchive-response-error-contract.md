# ExpeditionListArchive Response Error Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the ad-hoc `string? ErrorMessage` field from `DownloadExpeditionListResponse` and `ReprintExpeditionListResponse`, replacing it with the typed `BaseResponse.ErrorCode` channel (`ErrorCodes.InvalidBlobPath`) used everywhere else in the codebase, and propagate the change through the controller, frontend hook, and tests.

**Architecture:** Add `ErrorCodes.InvalidBlobPath = 1808` (FileStorage range, `BadRequest`). Rewrite both `Fail()` factories to parameterless methods that set `ErrorCode = ErrorCodes.InvalidBlobPath`. Switch the `Reprint` controller action to `HandleResponse(response)` (matches every other controller); keep `Download` on manual `BadRequest(response)` because its success body is a binary file stream. Update the frontend hook to consume the typed shape and localize `errorCode` → Czech string in a per-hook lookup table (pattern matches `useResetOrderShipment.ts`). Update backend handler tests and the page test mock in lockstep.

**Tech Stack:** .NET 8 / C# / xUnit / Moq / FluentAssertions (where used) — backend. React / TypeScript / Jest / React Testing Library — frontend. Single atomic PR (backend + frontend together) per NFR-1.

---

## File Structure

**Backend — Modified:**
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `InvalidBlobPath = 1808` after `UnsupportedFileType = 1807`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs` — remove `ErrorMessage`, replace `Fail(string)` with parameterless `Fail()`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs` — call parameterless `Fail()`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListResponse.cs` — remove `ErrorMessage`, replace `Fail(string)` with parameterless `Fail()`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` — call parameterless `Fail()`.
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs` — `Download` returns `BadRequest(response)`; `Reprint` returns `HandleResponse(response)`.
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs` — assert `ErrorCode == ErrorCodes.InvalidBlobPath` in the invalid-path theory.
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` — same assertion in the invalid-path fact.

**Frontend — Modified:**
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — replace `ReprintExpeditionListResponse` shape, add `REPRINT_ERROR_MESSAGES` lookup, update `useReprintExpeditionList` error-handling, keep `useRunExpeditionListPrintFix` fallback chain.
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — change mock from `{ success: true, errorMessage: null }` to `{ success: true, errorCode: null, params: null }`.

**No new files. No deletions.**

---

## Task 1: Add `InvalidBlobPath` error code (1808)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:201`

- [ ] **Step 1: Add the new enum value**

Open `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`. Find the FileStorage block:

```csharp
    // FileStorage module errors (18XX)
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidUrlFormat = 1801,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidContainerName = 1802,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    FileDownloadFailed = 1803,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    BlobUploadFailed = 1804,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    BlobNotFound = 1805,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    FileTooLarge = 1806,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    UnsupportedFileType = 1807,
```

Insert immediately after `UnsupportedFileType = 1807,`:

```csharp
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidBlobPath = 1808,
```

- [ ] **Step 2: Verify the project still compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat: add ErrorCodes.InvalidBlobPath (1808) for FileStorage module"
```

---

## Task 2: Refactor `DownloadExpeditionListResponse` — write the failing test first

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs:60-61`

- [ ] **Step 1: Update the existing failure-path test to assert the new typed error**

Open the test file. Locate the existing assertion block in `Handle_InvalidBlobPath_ReturnsFailure` (lines 60-61):

```csharp
        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Stream);

        _blobStorageServiceMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
```

Replace with:

```csharp
        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidBlobPath, result.ErrorCode);
        Assert.Null(result.Stream);

        _blobStorageServiceMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
```

Then add the missing `using` directive at the top of the file. The file currently has:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
```

Add `using Anela.Heblo.Application.Shared;` so `ErrorCodes` resolves:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
```

- [ ] **Step 2: Run the test to confirm it fails (RED)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DownloadExpeditionListHandlerTests"`

Expected: `Handle_InvalidBlobPath_ReturnsFailure` fails with the assertion `Expected: InvalidBlobPath, Actual: (null)` — the handler still calls `Fail("Invalid blob path.")` which sets `ErrorMessage` but leaves `ErrorCode` null.

---

## Task 3: Make the Download test pass — rewrite `Fail()` and the handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs:22`

- [ ] **Step 1: Rewrite `DownloadExpeditionListResponse`**

Replace the full contents of `DownloadExpeditionListResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListResponse : BaseResponse
{
    public Stream? Stream { get; set; }
    public string ContentType { get; set; } = "application/pdf";
    public string FileName { get; set; } = string.Empty;

    public static DownloadExpeditionListResponse Fail() =>
        new() { Success = false, ErrorCode = ErrorCodes.InvalidBlobPath };
}
```

This removes the `ErrorMessage` property and replaces `Fail(string message)` with a parameterless `Fail()` that sets the typed error code.

- [ ] **Step 2: Update `DownloadExpeditionListHandler` to call parameterless `Fail()`**

Open `DownloadExpeditionListHandler.cs`. Line 22 currently reads:

```csharp
            return DownloadExpeditionListResponse.Fail("Invalid blob path.");
```

Change to:

```csharp
            return DownloadExpeditionListResponse.Fail();
```

The full method body around it should be:

```csharp
    public async Task<DownloadExpeditionListResponse> Handle(DownloadExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return DownloadExpeditionListResponse.Fail();
        }

        var stream = await _blobStorageService.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
        var fileName = Path.GetFileName(request.BlobPath);

        return new DownloadExpeditionListResponse
        {
            Success = true,
            Stream = stream,
            ContentType = "application/pdf",
            FileName = fileName
        };
    }
```

- [ ] **Step 3: Verify project compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded` with 0 errors. (The controller still references the response object — that's fine because we only removed `ErrorMessage`, which the controller reads in Task 5.)

Note: the build of the API project may fail at this point because `ExpeditionListArchiveController.Download` still reads `response.ErrorMessage`. That is fixed in Task 5.

- [ ] **Step 4: Run the Download handler test — confirm it now passes (GREEN)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DownloadExpeditionListHandlerTests"`

Expected: `Handle_InvalidBlobPath_ReturnsFailure` and `Handle_ValidBlobPath_ReturnsBlobStream` both pass.

Note: if `dotnet test` builds the API project (whole solution) it may fail compilation in the controller — that's expected and gets resolved by Task 5. If so, run with `--no-build` after building only the test project:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~DownloadExpeditionListHandlerTests"
```

- [ ] **Step 5: Commit (combined Download response + handler + test)**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs \
  backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs \
  backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs
git commit -m "refactor: switch DownloadExpeditionListResponse to typed ErrorCode

- Remove ad-hoc ErrorMessage property.
- Rewrite Fail() parameterless, sets ErrorCode = InvalidBlobPath.
- Handler calls Fail() without arguments.
- Test asserts ErrorCode == InvalidBlobPath."
```

---

## Task 4: Refactor `ReprintExpeditionListResponse` — RED, then GREEN

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs:58-74`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs:25`

- [ ] **Step 1: Update the Reprint failure-path test (RED)**

Open `ReprintExpeditionListHandlerTests.cs`. The current `Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob` body is:

```csharp
        // Assert
        Assert.False(result.Success);
        _blobStorageServiceMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
```

Replace with:

```csharp
        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidBlobPath, result.ErrorCode);
        _blobStorageServiceMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
```

Add `using Anela.Heblo.Application.Shared;` to the existing using block. The full top of the file should become:

```csharp
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
```

- [ ] **Step 2: Run the test — confirm failure**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~ReprintExpeditionListHandlerTests.Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob"`

Expected: assertion fails with `Expected: InvalidBlobPath, Actual: (null)`.

- [ ] **Step 3: Rewrite `ReprintExpeditionListResponse`**

Replace the full contents of `ReprintExpeditionListResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListResponse : BaseResponse
{
    public static ReprintExpeditionListResponse Fail() =>
        new() { Success = false, ErrorCode = ErrorCodes.InvalidBlobPath };
}
```

- [ ] **Step 4: Update `ReprintExpeditionListHandler`**

Open `ReprintExpeditionListHandler.cs`. Line 25 currently reads:

```csharp
            return ReprintExpeditionListResponse.Fail("Invalid blob path.");
```

Change to:

```csharp
            return ReprintExpeditionListResponse.Fail();
```

The surrounding method body should become:

```csharp
    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail();
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        try
        {
            await using var blobStream = await _blobStorageService.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
            await using var fileStream = File.OpenWrite(tempFile);
            await blobStream.CopyToAsync(fileStream, cancellationToken);
        }
        catch
        {
            DeleteTempFile(tempFile);
            throw;
        }

        try
        {
            await _cupsSink.SendAsync(new[] { tempFile }, cancellationToken);
            return new ReprintExpeditionListResponse { Success = true };
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }
```

- [ ] **Step 5: Run all Reprint handler tests — confirm GREEN**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~ReprintExpeditionListHandlerTests"`

Expected: all four `ReprintExpeditionListHandlerTests` pass.

- [ ] **Step 6: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListResponse.cs \
  backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs \
  backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs
git commit -m "refactor: switch ReprintExpeditionListResponse to typed ErrorCode

- Remove ad-hoc ErrorMessage property.
- Rewrite Fail() parameterless, sets ErrorCode = InvalidBlobPath.
- Handler calls Fail() without arguments.
- Test asserts ErrorCode == InvalidBlobPath."
```

---

## Task 5: Update `ExpeditionListArchiveController` — Download uses `BadRequest(response)`, Reprint uses `HandleResponse(response)`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs:42-66`

- [ ] **Step 1: Rewrite the `Download` and `Reprint` actions**

Open `ExpeditionListArchiveController.cs`. The current `Download` action (lines 41-53) reads:

```csharp
    [HttpGet("download/{*blobPath}")]
    public async Task<ActionResult> Download(string blobPath)
    {
        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };
        var response = await _mediator.Send(request);

        if (!response.Success || response.Stream == null)
        {
            return BadRequest(response.ErrorMessage);
        }

        return File(response.Stream, response.ContentType, response.FileName);
    }
```

Replace with:

```csharp
    [HttpGet("download/{*blobPath}")]
    public async Task<ActionResult> Download(string blobPath)
    {
        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };
        var response = await _mediator.Send(request);

        if (!response.Success || response.Stream == null)
        {
            return BadRequest(response);
        }

        return File(response.Stream, response.ContentType, response.FileName);
    }
```

The current `Reprint` action (lines 55-66) reads:

```csharp
    [HttpPost("reprint")]
    public async Task<ActionResult<ReprintExpeditionListResponse>> Reprint([FromBody] ReprintExpeditionListRequest request)
    {
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
```

Replace with:

```csharp
    [HttpPost("reprint")]
    public async Task<ActionResult<ReprintExpeditionListResponse>> Reprint([FromBody] ReprintExpeditionListRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
```

`HandleResponse<T>` is provided by `BaseApiController` (already inherited) and routes the response through the `[HttpStatusCode]` attribute on `ErrorCodes.InvalidBlobPath` → `BadRequest(response)`.

- [ ] **Step 2: Build the full backend solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: `Build succeeded` with 0 errors and no warnings about `ErrorMessage`.

- [ ] **Step 3: Run all ExpeditionListArchive tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListArchive"`
Expected: all tests pass.

- [ ] **Step 4: Run dotnet format on the changed files**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include \
  backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
  backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs \
  backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs \
  backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListResponse.cs \
  backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs \
  backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs \
  backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs
```
Expected: clean exit, no changes reported.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs
git commit -m "refactor: route ExpeditionListArchive failures through BaseResponse

- Download returns BadRequest(response) (binary success body, manual mapping kept).
- Reprint delegates to BaseApiController.HandleResponse(response).
- Removes the last reader of the deleted ErrorMessage property."
```

---

## Task 6: Update the frontend hook — typed shape + Czech localization for Reprint

**Files:**
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts:29-32,100-128`

- [ ] **Step 1: Rewrite the `ReprintExpeditionListResponse` interface and add the error-message lookup**

Open `frontend/src/api/hooks/useExpeditionListArchive.ts`. The current interface (lines 29-32) reads:

```typescript
export interface ReprintExpeditionListResponse {
  success: boolean;
  errorMessage: string | null;
}
```

Replace with:

```typescript
export interface ReprintExpeditionListResponse {
  success: boolean;
  errorCode: string | null;
  params: Record<string, string> | null;
}
```

Immediately below the interface block (before the `// --- Query Keys ---` comment around line 34), insert the localization lookup:

```typescript
const REPRINT_ERROR_MESSAGES: Partial<Record<string, string>> = {
  InvalidBlobPath: "Neplatná cesta k souboru.",
};
const GENERIC_REPRINT_ERROR = "Nepodařilo se odeslat na tisk.";
```

- [ ] **Step 2: Replace the `useReprintExpeditionList` error handler**

Find `useReprintExpeditionList` (starts around line 100). The current failure branch (lines 115-120) reads:

```typescript
      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        throw new Error(
          errorData?.errorMessage ?? `HTTP error! status: ${response.status}`
        );
      }
```

Replace with:

```typescript
      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        const errorCode: string | undefined = errorData?.errorCode ?? undefined;
        const message =
          (errorCode && REPRINT_ERROR_MESSAGES[errorCode]) ?? GENERIC_REPRINT_ERROR;
        throw new Error(message);
      }
```

This matches the pattern used by `useResetOrderShipment.ts` / `useScanPackingOrder.ts` — the user-facing string stays Czech (per `ExpeditionListArchivePage.tsx:116` which renders `err.message` via `showError("Chyba tisku", msg)`), and unknown codes fall through to a generic Czech message.

- [ ] **Step 3: Verify the page still receives a localized error**

Read `frontend/src/pages/ExpeditionListArchivePage.tsx:104-120` to confirm the catch-block reads `err.message`. No change needed there — the new hook produces `"Neplatná cesta k souboru."` instead of the old `"Invalid blob path."`.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useExpeditionListArchive.ts
git commit -m "refactor(frontend): consume typed errorCode in useReprintExpeditionList

- ReprintExpeditionListResponse now exposes errorCode + params.
- Hook maps errorCode to Czech string via REPRINT_ERROR_MESSAGES lookup.
- Falls back to GENERIC_REPRINT_ERROR for unknown codes."
```

---

## Task 7: Update `useRunExpeditionListPrintFix` — prefer `errorCode`, keep `errorMessage` fallback

**Files:**
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts:130-152`

The backing endpoint `/api/expedition-list/run-fix` lives on a different, out-of-scope controller (per spec Out-of-Scope and arch-review Decision 4). We do not know whether it returns `errorMessage` or `errorCode`. Keep a defensive fallback chain.

- [ ] **Step 1: Update the failure branch in `useRunExpeditionListPrintFix`**

Find `useRunExpeditionListPrintFix` (around line 130). The current failure branch (lines 142-147) reads:

```typescript
      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        throw new Error(
          errorData?.errorMessage ?? `HTTP error! status: ${response.status}`
        );
      }
```

Replace with:

```typescript
      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        // The /api/expedition-list/run-fix endpoint is out of scope for this change
        // and may still return the legacy errorMessage. Prefer errorCode if present.
        const message =
          (errorData?.errorCode as string | undefined) ??
          (errorData?.errorMessage as string | undefined) ??
          `HTTP error! status: ${response.status}`;
        throw new Error(message);
      }
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/api/hooks/useExpeditionListArchive.ts
git commit -m "refactor(frontend): prefer errorCode in useRunExpeditionListPrintFix

Defensive fallback to errorMessage retained because the underlying
/api/expedition-list/run-fix controller is out of scope for this change."
```

---

## Task 8: Update the page test mock to match the new shape

**Files:**
- Modify: `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx:73-76`

- [ ] **Step 1: Replace the mock body**

Open `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx`. Locate the mock (lines 73-76):

```typescript
    (useReprintExpeditionList as jest.Mock).mockReturnValue({
      mutateAsync: jest.fn().mockResolvedValue({ success: true, errorMessage: null }),
      isPending: false,
    });
```

Replace with:

```typescript
    (useReprintExpeditionList as jest.Mock).mockReturnValue({
      mutateAsync: jest.fn().mockResolvedValue({ success: true, errorCode: null, params: null }),
      isPending: false,
    });
```

- [ ] **Step 2: Run the page test**

Run: `cd frontend && npm test -- --watchAll=false --testPathPattern=ExpeditionListArchivePage`
Expected: all four `ExpeditionListArchivePage – refresh button` tests pass.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx
git commit -m "test(frontend): align ExpeditionListArchivePage mock with new BaseResponse shape"
```

---

## Task 9: Verify there are no orphan references to `errorMessage` or `ErrorMessage` for these types

**Files:**
- Inspect (no edit unless a stray reference is found)

- [ ] **Step 1: Grep for backend references**

Run: `grep -rn "ErrorMessage" backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs backend/test/Anela.Heblo.Tests/ExpeditionListArchive`
Expected: no matches.

If a match appears, open the file and decide:
- If it is on a different DTO (e.g. `BaseResponse.FullError()` uses `"ErrorMessage"` as a Params key in `BaseResponse(Exception)`) — leave it; it is the shared error-params convention, not the property we removed.
- Otherwise remove it.

- [ ] **Step 2: Grep for frontend references in the affected files**

Run: `grep -n "errorMessage" frontend/src/api/hooks/useExpeditionListArchive.ts frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx frontend/src/pages/ExpeditionListArchivePage.tsx`
Expected matches:
- One match inside `useRunExpeditionListPrintFix` (the defensive fallback we kept in Task 7).
- No matches in the test file.
- No matches in `ExpeditionListArchivePage.tsx`.

If any other match is found, decide whether it is intentional (the run-fix fallback) or a leftover. Remove leftovers.

- [ ] **Step 3: Grep for the old `Fail(string)` signature**

Run: `grep -rn "DownloadExpeditionListResponse.Fail(\|ReprintExpeditionListResponse.Fail(" backend/`
Expected: each `Fail(` callsite is followed by `)` (no string argument).

If a match shows `Fail("..."`), the corresponding handler was not updated — go back to Task 3 / Task 4.

- [ ] **Step 4: No commit (read-only verification)**

If steps 1-3 found nothing unexpected, proceed. Otherwise fix and re-run.

---

## Task 10: Full backend build + test + format gate

**Files:**
- No edits, verification only.

- [ ] **Step 1: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 2: Full backend test suite (ExpeditionListArchive scope)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListArchive"`
Expected: all tests pass (Download: 6 cases — 1 success + 5 invalid theory rows; Reprint: 4 cases).

- [ ] **Step 3: Format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: clean exit. If files differ, run `dotnet format backend/Anela.Heblo.sln` and stage the result.

---

## Task 11: Full frontend build + lint + test gate

**Files:**
- No edits, verification only.

- [ ] **Step 1: Frontend build**

Run: `cd frontend && npm run build`
Expected: build succeeds with 0 errors.

- [ ] **Step 2: Frontend lint**

Run: `cd frontend && npm run lint`
Expected: 0 errors.

- [ ] **Step 3: Frontend unit tests (page + hooks scope)**

Run: `cd frontend && npm test -- --watchAll=false --testPathPattern="ExpeditionListArchive"`
Expected: all matching tests pass.

- [ ] **Step 4: Verify the auto-generated OpenAPI client no longer carries `errorMessage` on these types**

Run: `grep -n "errorMessage" frontend/src/api/generated/api-client.ts | head -20`
Expected: the generated file regenerated during `npm run build` (which calls the OpenAPI generator after `dotnet build`). Inspect each match — none of them should belong to `DownloadExpeditionListResponse` or `ReprintExpeditionListResponse`. (Matches on other modules' response types are fine and out of scope.)

If a match appears on either of our two response types, the backend build did not regenerate the client — re-run `npm run build` to force regeneration.

---

## Self-Review Checklist

Done — spec items mapped to tasks:

| Spec item | Task |
|---|---|
| FR-1 (remove `ErrorMessage` from both responses) | Tasks 3, 4 |
| FR-2 (add `InvalidBlobPath = 1808`, parameterless `Fail()`) | Tasks 1, 3, 4 |
| FR-3 (handlers call parameterless `Fail()`) | Tasks 3, 4 |
| FR-4 (Download → `BadRequest(response)`; Reprint → `HandleResponse(response)` per arch amendment) | Task 5 |
| FR-5 (frontend hook typed shape + Czech localization map) | Tasks 6, 7 |
| FR-6 (backend tests assert `ErrorCode == InvalidBlobPath`) | Tasks 2, 4 |
| FR-6 addition (page test mock updated) | Task 8 |
| NFR-1 (atomic PR — backend + frontend) | All tasks 1-8 in one branch |
| NFR-2 (OpenAPI client consistency) | Task 11 step 4 |
| NFR-3 (no regression: 400 + no temp file on invalid path) | Tasks 3, 4 retain `BlobPathValidator.IsValid` check ahead of any I/O; existing temp-file leak tests stay green (Task 4 step 5, Task 10 step 2) |
| NFR-4 (DTOs as classes, `dotnet format` clean) | Task 5 step 4, Task 10 step 3 |

No placeholders. No unspecified types. `Fail()` is consistently named across all tasks. `REPRINT_ERROR_MESSAGES` lookup and `GENERIC_REPRINT_ERROR` are defined in Task 6 and referenced only there. `HandleResponse` is the existing helper on `BaseApiController` (verified in `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:29`).
