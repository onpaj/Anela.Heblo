# Remove Dead URL-Validation Code From DownloadFromUrlHandler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the unreachable manual URL-format/scheme check from `DownloadFromUrlHandler.Handle()`, since `ValidationResultBehavior` + `DownloadFromUrlRequestValidator` already enforce the identical rule earlier in the MediatR pipeline and never let an invalid request reach the handler.

**Architecture:** Pure dead-code deletion, no behavior change. Two duplicate handler-level unit tests that directly instantiate `DownloadFromUrlHandler` and call `Handle()` with an invalid URL (bypassing the validator/pipeline entirely) currently pass only because the dead block still exists in the handler; they are removed first because equivalent coverage already exists at the validator level (`DownloadFromUrlRequestValidatorTests.FileUrl_Invalid_ShouldHaveValidationError`) and the pipeline level (`FileStorageValidationPipelineTests.Send_InvalidFileUrlAndInvalidContainerName_ReturnsInvalidUrlFormat`). Only after the tests are cleaned up is the dead block itself deleted from the handler.

**Tech Stack:** .NET 8, MediatR, FluentValidation, xUnit, Moq, FluentAssertions.

---

## Verified current state (read directly from the worktree before writing this plan)

`backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`, lines 38–61, currently read exactly:

```csharp
    public async Task<DownloadFromUrlResponse> Handle(DownloadFromUrlRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing file download and upload request from URL: {FileUrl} to container: {ContainerName}",
            request.FileUrl,
            request.ContainerName);

        if (!Uri.TryCreate(request.FileUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Invalid URL format or unsupported scheme: {FileUrl}", request.FileUrl);
            return new DownloadFromUrlResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidUrlFormat,
                Params = new Dictionary<string, string>
                {
                    ["fileUrl"] = request.FileUrl,
                    ["cause"] = "validation",
                },
            };
        }

        var redactedUrl = RedactUrl(request.FileUrl);
        var sw = Stopwatch.StartNew();
        int attemptCount = 0;
```

The dead block is lines 45–59 inclusive (the `if (!Uri.TryCreate(...) ...) { ... }` statement), matching the spec/arch-review's stated line numbers exactly — confirmed by reading the live file, not assumed.

`using System.Collections.Generic;` (line 2) is **not** made unused by this deletion: `DownloadFromUrlHandler.Failure(...)` (lines 151–169 of the current file) still builds a `new Dictionary<string, string> { ... }` for `Params`. `using System;` (line 1) is also still needed: `RedactUrl` uses `new UriBuilder(url)` / `ub.Uri` and `GetBlobNameFromUrl` uses `new Uri(blobUrl)`, both `System` types. **No `using` directives are removed by this change.**

`backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` contains two tests that call `BuildHandler().Handle(request, ...)` directly (bypassing MediatR and therefore the validator pipeline) with an invalid `FileUrl`, asserting `ErrorCode == ErrorCodes.InvalidUrlFormat`. Both exercise only the dead block being deleted and will fail once it is removed (with the block gone, `Handle()` will instead proceed into the try/HEAD-probe/resilience path with an unconfigured `_blobStorage` mock for those inputs, producing `ErrorCodes.FileDownloadFailed`, not `InvalidUrlFormat`):

- `Handle_InvalidUrl_ShouldReturnErrorResponse` (a `[Theory]` at lines 140–159, cases `"not-a-url"`, `""`, `"ftp://example.com/file.txt"`)
- `Handle_ValidationFailure_InvalidUrl_SetsCauseValidation` (a `[Fact]` at lines 419–436, case `"not-a-valid-url"`)

Equivalent coverage already exists and is unaffected by this change:
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs` → `FileUrl_Invalid_ShouldHaveValidationError` (`[Theory]`, cases `"not-a-url"`, `""`, `"ftp://example.com/file.txt"`, `"example.com/file.txt"`) asserts the validator itself produces `ErrorCodes.InvalidUrlFormat` with `CustomState["fileUrl"]` / `CustomState["cause"] == "validation"`.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs` → `Send_InvalidFileUrlAndInvalidContainerName_ReturnsInvalidUrlFormat` sends a `DownloadFromUrlRequest` with `FileUrl = "not-a-url"` through the real `IMediator` (validator + `ValidationResultBehavior` wired exactly as in production) and asserts `result.ErrorCode == ErrorCodes.InvalidUrlFormat`, `result.Params["cause"] == "validation"`, and that `IBlobStorageService.DownloadFromUrlAsync` is never invoked — i.e. it proves the handler is unreachable for an invalid URL, which is the exact fact this whole change relies on.

Per spec FR-2's third bullet, since equivalent validator-level and pipeline-level tests already exist, the two handler-level tests above are **removed** (not rewritten) — rewriting them to assert "via the validator/pipeline path" would just duplicate the two tests named above.

---

### task: remove-duplicate-handler-level-invalid-url-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`

This task removes the two tests that exercise the handler's own invalid-URL branch, while the branch still exists in production code (so the test suite stays green throughout this task — nothing in production code changes yet).

- [ ] Open `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` and delete the `Handle_InvalidUrl_ShouldReturnErrorResponse` theory test. Find this exact block (currently lines 140–159, immediately preceded by `Handle_ValidRequestWithoutBlobName_ShouldGenerateBlobName`'s closing `}` and a blank line, and immediately followed by a blank line then the `Handle_DifferentFileTypes_ShouldExtractCorrectBlobName` theory):

  ```csharp
      [Theory]
      [InlineData("not-a-url")]
      [InlineData("")]
      [InlineData("ftp://example.com/file.txt")]
      public async Task Handle_InvalidUrl_ShouldReturnErrorResponse(string invalidUrl)
      {
          // Arrange
          var request = new DownloadFromUrlRequest
          {
              FileUrl = invalidUrl,
              ContainerName = "documents",
          };

          // Act
          var result = await BuildHandler().Handle(request, CancellationToken.None);

          // Assert
          Assert.False(result.Success);
          Assert.Equal(ErrorCodes.InvalidUrlFormat, result.ErrorCode);
      }

  ```

  Delete the entire block above (all 20 lines shown, including the trailing blank line), so `Handle_ValidRequestWithoutBlobName_ShouldGenerateBlobName`'s closing brace is followed directly by the `[Theory]` attribute of `Handle_DifferentFileTypes_ShouldExtractCorrectBlobName`.

- [ ] In the same file, delete the `Handle_ValidationFailure_InvalidUrl_SetsCauseValidation` fact test. Find this exact block (currently lines 419–436, preceded by the closing `}` of `Handle_RedactsUrl_RemovesQueryString` and a blank line, followed by a blank line then `Handle_BlobStorageThrowsHttpRequestException_ReturnsFileDownloadFailed`):

  ```csharp
      [Fact]
      public async Task Handle_ValidationFailure_InvalidUrl_SetsCauseValidation()
      {
          // Arrange
          var request = new DownloadFromUrlRequest
          {
              FileUrl = "not-a-valid-url",
              ContainerName = "exports",
          };

          // Act
          var result = await BuildHandler().Handle(request, CancellationToken.None);

          // Assert
          Assert.False(result.Success);
          Assert.Equal(ErrorCodes.InvalidUrlFormat, result.ErrorCode);
          Assert.Equal("validation", result.Params!["cause"]);
      }

  ```

  Delete the entire block above (all 17 lines shown, including the trailing blank line).

- [ ] Confirm no other reference to the deleted test methods remains in the file (they are not called from anywhere else — `grep` for the method names to be sure):

  ```bash
  grep -n "Handle_InvalidUrl_ShouldReturnErrorResponse\|Handle_ValidationFailure_InvalidUrl_SetsCauseValidation" \
    backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs
  ```

  Expected output: nothing (no matches, exit code 1).

- [ ] Build the solution to confirm the test file still compiles:

  ```bash
  dotnet build Anela.Heblo.sln
  ```

  Expected: `Build succeeded.` with 0 errors.

- [ ] Run the FileStorage test suite to confirm it is still green (production code is unchanged at this point, so all remaining tests — including the ones that still exist in `DownloadFromUrlHandlerTests.cs` — must pass exactly as before):

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.FileStorage"
  ```

  Expected: all tests pass, 0 failed. The total test count for `DownloadFromUrlHandlerTests` drops by 4 (3 `[InlineData]` cases from the removed theory + 1 removed fact) compared to before this task.

- [ ] Commit:

  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs
  git commit -m "test: remove handler-level invalid-URL tests duplicated by validator/pipeline tests"
  ```

---

### task: remove-dead-url-validation-block-from-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`

This task deletes the actual dead code, now that no test depends on it.

- [ ] Open `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` and replace this exact block (current lines 38–63):

  ```csharp
      public async Task<DownloadFromUrlResponse> Handle(DownloadFromUrlRequest request, CancellationToken cancellationToken)
      {
          _logger.LogInformation(
              "Processing file download and upload request from URL: {FileUrl} to container: {ContainerName}",
              request.FileUrl,
              request.ContainerName);

          if (!Uri.TryCreate(request.FileUrl, UriKind.Absolute, out var uri) ||
              (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
          {
              _logger.LogWarning("Invalid URL format or unsupported scheme: {FileUrl}", request.FileUrl);
              return new DownloadFromUrlResponse
              {
                  Success = false,
                  ErrorCode = ErrorCodes.InvalidUrlFormat,
                  Params = new Dictionary<string, string>
                  {
                      ["fileUrl"] = request.FileUrl,
                      ["cause"] = "validation",
                  },
              };
          }

          var redactedUrl = RedactUrl(request.FileUrl);
          var sw = Stopwatch.StartNew();
          int attemptCount = 0;
  ```

  with:

  ```csharp
      public async Task<DownloadFromUrlResponse> Handle(DownloadFromUrlRequest request, CancellationToken cancellationToken)
      {
          _logger.LogInformation(
              "Processing file download and upload request from URL: {FileUrl} to container: {ContainerName}",
              request.FileUrl,
              request.ContainerName);

          var redactedUrl = RedactUrl(request.FileUrl);
          var sw = Stopwatch.StartNew();
          int attemptCount = 0;
  ```

  (i.e. delete only the `if (!Uri.TryCreate(...) ...) { ... }` statement — lines 45–59 of the original — and the blank line immediately after it; the `_logger.LogInformation(...)` call above and the `var redactedUrl = ...` / `var sw = ...` / `int attemptCount = 0;` lines below are untouched and now sit directly adjacent).

- [ ] Do **not** remove `using System.Collections.Generic;` or `using System;` from the top of the file. Confirm both are still referenced elsewhere in the file:

  ```bash
  grep -n "Dictionary<string, string>" backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs
  grep -n "new Uri(\|new UriBuilder(" backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs
  ```

  Expected: the first command still matches inside the `Failure(...)` helper method; the second still matches inside `RedactUrl` and `GetBlobNameFromUrl`. Both `using` directives stay exactly as they are — no import changes in this task.

- [ ] Build the solution:

  ```bash
  dotnet build Anela.Heblo.sln
  ```

  Expected: `Build succeeded.` with 0 errors and no new warnings (in particular, no `CS0168`/`CS0219`/unused-`using` warnings — none are expected since both usings remain genuinely used).

- [ ] Run `dotnet format` and confirm it makes no further changes beyond what was just hand-edited (this file is expected to already be correctly formatted after the deletion):

  ```bash
  dotnet format Anela.Heblo.sln --verify-no-changes
  ```

  Expected: exits 0 with no output (no formatting violations). If it reports a violation, run `dotnet format Anela.Heblo.sln`, inspect `git diff` to confirm the only changes are whitespace/formatting in the touched file, and re-run `--verify-no-changes` to confirm.

- [ ] Run the full FileStorage test suite (handler + validator + pipeline tests) to confirm behavior is unchanged end-to-end:

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.FileStorage"
  ```

  Expected: all tests pass, 0 failed, including:
  - `DownloadFromUrlHandlerTests` (remaining tests — happy-path, HEAD-probe, retry/timeout/http-status failure, redaction, exception-mapping tests — none of which touch the deleted block)
  - `DownloadFromUrlRequestValidatorTests.FileUrl_Invalid_ShouldHaveValidationError` (still proves the validator alone rejects the same invalid URLs the deleted handler block used to check)
  - `FileStorageValidationPipelineTests.Send_InvalidFileUrlAndInvalidContainerName_ReturnsInvalidUrlFormat` and `Send_InvalidContainerName_ShortCircuits_BlobStorageNeverInvoked` (still prove `ValidationResultBehavior` short-circuits before the handler runs, and that `IBlobStorageService` is never invoked for an invalid request — i.e. the handler truly cannot be reached with an invalid URL)

- [ ] Run the full backend test suite once, to catch any unrelated regression:

  ```bash
  dotnet test Anela.Heblo.sln
  ```

  Expected: all tests pass, 0 failed.

- [ ] Review the diff to confirm the change is surgical — only the dead block is gone, nothing else in the file changed:

  ```bash
  git diff backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs
  ```

  Expected: a single contiguous deletion of the 15 lines that made up the `if (!Uri.TryCreate(...) ...) { ... }` block plus its trailing blank line (16 lines removed total); no other line in the file shows as added or removed.

- [ ] Commit:

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs
  git commit -m "refactor: remove unreachable URL-validation block from DownloadFromUrlHandler

  ValidationResultBehavior + DownloadFromUrlRequestValidator already reject
  invalid FileUrl values before the handler runs, so this in-handler check
  could never execute. No behavior change; validator/pipeline tests already
  cover the invalid-URL scenario end-to-end."
  ```

---

## Self-review against the spec

- **FR-1** (remove the unreachable block, lines 45–59): done in `remove-dead-url-validation-block-from-handler`; verified no other `Handle()` logic is touched (diff review step); `using` directives explicitly confirmed still needed and left alone; build gate included.
- **FR-2** (preserve behavior for all inputs):
  - Invalid-URL responses still `Success=false` / `ErrorCode=InvalidUrlFormat` / `Params["fileUrl"]` + `Params["cause"]="validation"`, now produced solely by `ValidationResultBehavior` — proven by `FileStorageValidationPipelineTests.Send_InvalidFileUrlAndInvalidContainerName_ReturnsInvalidUrlFormat`, which is not modified by this plan and re-run in the final task.
  - Valid-URL flow (HEAD probe, resilience-wrapped download, success/failure construction) unchanged — proven by the remaining `DownloadFromUrlHandlerTests` cases, re-run in the final task.
  - The existing handler-level test that exercised the dead invalid-URL branch is removed (task 1), since equivalent validator-level and pipeline-level coverage already exists — coverage of the invalid-URL scenario is not reduced, only de-duplicated.
- **NFR-1 / NFR-2**: no code changes target performance or security; nothing in this plan touches them.
- **Out of scope items** (validator, `ValidationResultBehavior`, `FileStorageModule` registration, `IsValidContainerName`, HEAD-probe/resilience/response-construction refactors): none are touched by either task in this plan.

No placeholders, no "TBD", no references to undefined types/methods — every step shows the exact current code, the exact resulting code, and exact runnable commands with expected output.
