# GetExpeditionListsByDateResponse.Fail() Factory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a static `InvalidDate()` failure factory to `GetExpeditionListsByDateResponse` and have `GetExpeditionListsByDateHandler` delegate to it, matching the pattern already used by `DownloadExpeditionListResponse.Fail()` and `ReprintExpeditionListResponse.Fail()`.

**Architecture:** Pure refactor, no behavior or contract change. Move the "invalid date format" failure-response construction out of the handler and onto the response DTO as a static factory, then call that factory from the handler's existing validation branch.

**Tech Stack:** .NET 8, MediatR, xUnit, Moq.

---

### task: add-invalid-date-factory

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs`
- Test: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` (new test method, appended)

- [ ] **Step 1: Write the failing test for the new factory**

Read the current test file first:

```bash
cat backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs
```

Add a new test method to the existing `GetExpeditionListsByDateHandlerTests` class (append it after `Handle_ReturnsFailure_WhenDateIsInvalid`, before the closing `}` of the class):

```csharp
    [Fact]
    public void InvalidDate_ReturnsExpectedFailureShape()
    {
        // Act
        var result = GetExpeditionListsByDateResponse.InvalidDate();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidFormat, result.ErrorCode);
        Assert.NotNull(result.Params);
        Assert.Equal("Date", result.Params!["Field"]);
        Assert.Equal("yyyy-MM-dd", result.Params!["ExpectedFormat"]);
        Assert.Empty(result.Items);
    }
```

**Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests.InvalidDate_ReturnsExpectedFailureShape"`
Expected: FAIL with a compile error — `'GetExpeditionListsByDateResponse' does not contain a definition for 'InvalidDate'`

- [ ] **Step 3: Implement the minimal factory method**

Modify `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs` to:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateResponse : BaseResponse
{
    public List<ExpeditionListItemDto> Items { get; set; } = new();

    public static GetExpeditionListsByDateResponse InvalidDate() =>
        new()
        {
            Success = false,
            ErrorCode = ErrorCodes.InvalidFormat,
            Params = new Dictionary<string, string>
            {
                { "Field", "Date" },
                { "ExpectedFormat", "yyyy-MM-dd" }
            }
        };
}
```

(Only the `InvalidDate()` method is new; the existing `using` directives, namespace, class declaration, and `Items` property are unchanged — verify your edit leaves them exactly as they were.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests.InvalidDate_ReturnsExpectedFailureShape"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs
git commit -m "feat(expedition-list-archive): add GetExpeditionListsByDateResponse.InvalidDate() factory"
```

### task: update-handler-to-use-factory

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs:21-33`
- Test: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` (existing tests, no new test needed — behavior is unchanged)

- [ ] **Step 1: Confirm the existing behavioral test still describes the required contract**

The existing test `Handle_ReturnsFailure_WhenDateIsInvalid` (already in the test file, added before this plan) asserts:

```csharp
Assert.False(result.Success);
Assert.Equal(ErrorCodes.InvalidFormat, result.ErrorCode);
Assert.NotNull(result.Params);
Assert.Equal("Date", result.Params!["Field"]);
Assert.Equal("yyyy-MM-dd", result.Params!["ExpectedFormat"]);
Assert.Empty(result.Items);
_blobStoreMock.Verify(s => s.ListBlobsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
```

This test already fully specifies the required post-refactor behavior of the handler and needs no changes. Run it now to confirm it currently passes against the pre-refactor handler:

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests.Handle_ReturnsFailure_WhenDateIsInvalid"`
Expected: PASS (5 theory cases)

- [ ] **Step 2: Update the handler to delegate to the factory**

Modify `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs`. Replace lines 21–33:

```csharp
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
        {
            return new GetExpeditionListsByDateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidFormat,
                Params = new Dictionary<string, string>
                {
                    { "Field", "Date" },
                    { "ExpectedFormat", "yyyy-MM-dd" }
                }
            };
        }
```

with:

```csharp
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
        {
            return GetExpeditionListsByDateResponse.InvalidDate();
        }
```

The full method body after this edit:

```csharp
    public async Task<GetExpeditionListsByDateResponse> Handle(GetExpeditionListsByDateRequest request, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
        {
            return GetExpeditionListsByDateResponse.InvalidDate();
        }

        var blobs = await _blobStore.ListBlobsAsync(_containerName, request.Date, cancellationToken);

        var items = blobs
            .Where(b => b.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .Select(b => new ExpeditionListItemDto
            {
                BlobPath = b.Name,
                FileName = b.FileName,
                ListId = Path.GetFileNameWithoutExtension(b.FileName),
                CreatedOn = b.CreatedOn,
                ContentLength = b.ContentLength
            })
            .ToList();

        return new GetExpeditionListsByDateResponse { Items = items };
    }
```

- [ ] **Step 3: Build and check for an unused `using` directive**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeds. If the compiler/analyzer emits a warning that `using Anela.Heblo.Application.Shared;` (line 2 of the handler file) is now unused, remove that single `using` line from `GetExpeditionListsByDateHandler.cs`. If no such warning appears, leave the `using` directives untouched — do not remove anything speculatively.

- [ ] **Step 4: Run the full test file to verify no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests"`
Expected: PASS — all tests in the file, including `Handle_ReturnsItemsForDate`, `Handle_FiltersPdfFilesOnly`, `Handle_ReturnsFailure_WhenDateIsInvalid` (5 cases), and `InvalidDate_ReturnsExpectedFailureShape` from the previous task.

- [ ] **Step 5: Run the full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS — no other test in the solution references `GetExpeditionListsByDateResponse` construction directly, so no other file should be affected. If any other test fails, stop and investigate before proceeding (systematic-debugging) — do not paper over an unrelated failure.

- [ ] **Step 6: Format and final build check**

Run: `dotnet format backend/Anela.Heblo.sln`
Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Both succeed with no new warnings introduced by this change.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs
git commit -m "refactor(expedition-list-archive): delegate invalid-date response construction to GetExpeditionListsByDateResponse.InvalidDate()"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (add `InvalidDate()` factory with exact shape) → covered by `add-invalid-date-factory`.
- FR-2 (handler delegates to factory, behavior unchanged, blob store still not called on invalid input) → covered by `update-handler-to-use-factory` (existing test `Handle_ReturnsFailure_WhenDateIsInvalid` already asserts the `Times.Never` blob-store call, and Step 1 of that task re-confirms it pre-refactor).
- NFR-1/NFR-2 (no perf/security impact) → no task needed; nothing in either task changes I/O, auth, or allocation patterns beyond moving one object construction.
- Data Model / API design (no contract change) → verified in Step 5 of `update-handler-to-use-factory` (full suite run) and by construction — the JSON shape produced is byte-for-byte identical to today's, per `design.r1.md`.
- Out of Scope items (sibling `Fail()` methods, shared base factory, validation-rule changes) → no task touches `DownloadExpeditionListResponse.cs`, `ReprintExpeditionListResponse.cs`, or the `DateOnly.TryParseExact` call itself. Confirmed no gaps.

**Placeholder scan:** No "TBD"/"TODO"/"add appropriate error handling" language present. Every step shows complete, exact code. No gaps found.

**Type consistency:** `GetExpeditionListsByDateResponse.InvalidDate()` signature (`public static GetExpeditionListsByDateResponse InvalidDate()`) is identical between `add-invalid-date-factory` (where it's defined) and `update-handler-to-use-factory` (where it's called). `ErrorCodes.InvalidFormat` and the `Params` dictionary keys (`"Field"`, `"ExpectedFormat"`) match exactly between both tasks and the existing test assertions. No mismatches found.
