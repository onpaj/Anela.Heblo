## Goal
Fix the code review findings below.

## Blocking findings from code-review.r1.md

- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs:71-73` (interacting with `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs:45-59`) — Validation order changed for requests where **both** `FileUrl` and `ContainerName` are invalid. Before this refactor, `DownloadFromUrlHandler.Handle` checked `FileUrl` format first (lines 45-59) and only reached the container-name check afterward, so a request with both fields invalid returned `ErrorCodes.InvalidUrlFormat`. Now `ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>` runs `DownloadFromUrlRequestValidator` in the MediatR pipeline *before* `Handle` executes at all; if `ContainerName` fails validation, the pipeline short-circuits and `Handle` (and its `FileUrl` check) never runs. The same dual-invalid input therefore now returns `ErrorCodes.InvalidContainerName` instead of `ErrorCodes.InvalidUrlFormat` — an observable API response change. The spec's Summary and Background explicitly require the refactor to be "byte-for-byte identical" in response/error-code behavior; this case violates that guarantee. No existing test (old or new) exercises the dual-invalid case, so this passed CI undetected.

## Recommended fix

Add a `RuleFor(x => x.FileUrl)` rule to `DownloadFromUrlRequestValidator`, reproducing the exact URL-format check currently in `DownloadFromUrlHandler.Handle` (lines 45-59: `Uri.TryCreate(request.FileUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)`), with `WithErrorCode(((int)ErrorCodes.InvalidUrlFormat).ToString())` and `WithState` producing `{ "fileUrl": request.FileUrl, "cause": "validation" }` — matching the handler's existing `Params` shape exactly.

**Ordering matters.** `ValidationResultBehavior<TRequest,TResponse>` (see `backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationResultBehavior.cs`) collects all validator failures into a list and uses `failures.First()` to build the response. FluentValidation evaluates `RuleFor` blocks within a validator in the order they are declared. To preserve the original precedence (URL-format error wins over container-name error when both are invalid), declare the `FileUrl` rule **before** the `ContainerName` rule in the validator class.

Leave `DownloadFromUrlHandler`'s own `FileUrl` check (lines 45-59) in place — do not remove it. It becomes a defense-in-depth duplicate (the pipeline will now normally catch invalid URLs before `Handle` runs), exactly like the container-name check was briefly duplicated between the two original tasks in this pipeline. Removing it is out of scope and not required to fix this bug.

## Required test coverage

Add a test proving the fix: a request with both an invalid `FileUrl` and an invalid `ContainerName`, sent through the full MediatR pipeline (in `FileStorageValidationPipelineTests.cs`, alongside the existing two tests), asserting the result's `ErrorCode` is `ErrorCodes.InvalidUrlFormat` (not `InvalidContainerName`), matching pre-refactor behavior.

## Advisory (non-blocking, fix only if trivial — do not block on this)

- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` — The removed inline container-name check used to `_logger.LogWarning("Invalid container name: {ContainerName}", ...)` before returning the error. That warning log is now gone entirely (FluentValidation validators don't log), so invalid-container-name requests are no longer logged anywhere. Not a contract break, but worth a one-line fix if easy: consider whether logging should be added somewhere reachable (e.g. this is genuinely optional — skip if it would require broader changes to `ValidationResultBehavior` itself, since that's shared infrastructure used by other modules and out of scope for this fix).

## Files likely to change
- Modify: `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs`
- Optionally modify: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs` (if you want unit-level coverage of the new `FileUrl` rule too — not strictly required since the handler-level and pipeline-level URL-format behavior is already covered elsewhere, but the *new* rule itself should have at least minimal validator-level coverage of its own if it didn't exist before).

## Acceptance criteria
- A request with an invalid `FileUrl` AND an invalid `ContainerName`, sent via `IMediator.Send`, returns `ErrorCode == ErrorCodes.InvalidUrlFormat` (matching pre-refactor handler behavior), not `InvalidContainerName`.
- All previously-passing tests still pass (`dotnet build` + relevant `dotnet test` filters green).
- `dotnet format --verify-no-changes` passes.
