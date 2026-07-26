# Implementation: code-review-fixes

## What was implemented
Fixed the validation-precedence regression identified in code review: `DownloadFromUrlRequestValidator` now
validates `FileUrl` format in addition to `ContainerName`. The `FileUrl` rule is declared *before* the
`ContainerName` rule so that when both fields are invalid, `ValidationResultBehavior`'s `failures.First()`
picks the URL-format failure — restoring the pre-refactor precedence where `ErrorCodes.InvalidUrlFormat`
wins over `ErrorCodes.InvalidContainerName`. The new rule reproduces `DownloadFromUrlHandler.Handle`'s exact
URL check (`Uri.TryCreate(..., UriKind.Absolute, ...)` plus http/https scheme check), the same error code
(`ErrorCodes.InvalidUrlFormat`), and the same `WithState` shape (`{ "fileUrl": ..., "cause": "validation" }`).
`DownloadFromUrlHandler`'s own inline `FileUrl` check was left untouched as a harmless defense-in-depth
duplicate, per the recommended fix.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs` — added `RuleFor(x => x.FileUrl)` (declared before the `ContainerName` rule) with a new `IsValidFileUrl` predicate mirroring the handler's URL-format check, `WithErrorCode(((int)ErrorCodes.InvalidUrlFormat).ToString())`, and matching `WithState`.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs` — added `Send_InvalidFileUrlAndInvalidContainerName_ReturnsInvalidUrlFormat`, sending a request with both `FileUrl = "not-a-url"` and `ContainerName = "AB"` through the full MediatR pipeline and asserting `ErrorCode == ErrorCodes.InvalidUrlFormat` (proving the fix), plus that the blob storage service is never invoked.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs` — added `FileUrl_Invalid_ShouldHaveValidationError` (theory: `"not-a-url"`, `""`, `"ftp://example.com/file.txt"`, `"example.com/file.txt"`, asserting error message, error code, and custom state) and `FileUrl_Valid_ShouldNotHaveValidationError` (theory: `http`/`https` URLs) for validator-level coverage of the new rule.

## Tests
Ran `dotnet test .../Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~FileStorage"`:
**127 passed, 0 failed, 0 skipped** — includes the new dual-invalid pipeline test and the new `FileUrl`
validator theory cases, plus all previously-passing FileStorage tests (handler, pipeline, validator,
controller, adapter, resilience, etc.) unaffected.

## How to verify
1. `dotnet build /home/user/worktrees/feature-3675-Arch-Review-Filestorage-Azure-Container-Naming-Rul/Anela.Heblo.sln` — 0 errors.
2. `dotnet format /home/user/worktrees/feature-3675-Arch-Review-Filestorage-Azure-Container-Naming-Rul/Anela.Heblo.sln --verify-no-changes` — passes with no changes needed.
3. `dotnet test /home/user/worktrees/feature-3675-Arch-Review-Filestorage-Azure-Container-Naming-Rul/backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~FileStorage"` — 127/127 pass.
4. Inspect the new pipeline test `Send_InvalidFileUrlAndInvalidContainerName_ReturnsInvalidUrlFormat` in `FileStorageValidationPipelineTests.cs` to confirm it asserts `ErrorCodes.InvalidUrlFormat` for a request with both an invalid `FileUrl` and invalid `ContainerName`.

## Notes
No deviations from the recommended fix. The advisory (non-blocking) item about the missing
`LogWarning` for invalid container names was left as-is — addressing it would require changes to the
shared `ValidationResultBehavior` infrastructure, which is explicitly out of scope per the task context.

## Status
DONE
