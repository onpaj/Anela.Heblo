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
