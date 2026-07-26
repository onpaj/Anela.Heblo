# Implementation: add-container-name-validator-and-pipeline-wiring

## What was implemented
Added `DownloadFromUrlRequestValidator`, a FluentValidation validator reproducing the exact
container-name rules previously inline in `DownloadFromUrlHandler.IsValidContainerName`, and wired
it into the MediatR pipeline via the existing (non-throwing) `ValidationResultBehavior`, mirroring
the registration pattern already used by `AnalyticsModule`. The handler's own inline check is left
in place for this commit (removed in the follow-up task), so the rule is enforced twice for the
duration of this commit — redundant but harmless, keeping every commit independently green.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs` — new `AbstractValidator<DownloadFromUrlRequest>` with a single `RuleFor(x => x.ContainerName)` rule, using `WithErrorCode`/`WithState` to reproduce the handler's existing `ErrorCodes.InvalidContainerName` / `Params` contract.
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — registers `IValidator<DownloadFromUrlRequest>` and `IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>` (`ValidationResultBehavior<,>`).
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs` — new validator-level unit tests (invalid/valid container names, error-code round-trip, `Params`/state contents).
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs` — new end-to-end DI/MediatR test proving an invalid container name short-circuits before `DownloadFromUrlHandler.Handle` runs (blob storage never invoked) and a valid one reaches the handler successfully.

## Tests
- `DownloadFromUrlRequestValidatorTests` — 8 invalid-input theory cases, 5 valid-input theory cases, error-code round-trip, and `Params`/state assertions. All pass.
- `FileStorageValidationPipelineTests` — 2 facts (invalid short-circuits, valid reaches handler). All pass.
- Existing `DownloadFromUrlHandlerTests` — unaffected (handler still has its own inline check at this point); all pass unchanged.
- Full filtered run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorage.Validators.DownloadFromUrlRequestValidatorTests|FullyQualifiedName~FileStorage.Pipeline.FileStorageValidationPipelineTests|FullyQualifiedName~FileStorage.DownloadFromUrlHandlerTests"` → **Passed: 50, Failed: 0**.
- `dotnet build Anela.Heblo.sln` from repo root → Build succeeded, 0 errors (1 pre-existing, unrelated warning from the `AccessMatrixGen` post-build tool, not caused by this change).

## How to verify
```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorage.Validators.DownloadFromUrlRequestValidatorTests|FullyQualifiedName~FileStorage.Pipeline.FileStorageValidationPipelineTests|FullyQualifiedName~FileStorage.DownloadFromUrlHandlerTests"
```

## Notes
No deviations from the plan — the task-context file already contained exact, buildable code for
every file, and it matched the real current signatures of `DownloadFromUrlRequest`,
`DownloadFromUrlResponse`, `IBlobStorageService`, and `IDownloadResilienceService`. No plan
amendments were needed.

## PR Summary
Extracted the Azure Blob container-name validation rule out of `DownloadFromUrlHandler` into a
dedicated `DownloadFromUrlRequestValidator`, wired through the existing `ValidationResultBehavior`
MediatR pipeline behavior (the same non-throwing pattern already used by `AnalyticsModule`), so the
handler will no longer need to own this Azure-specific naming logic once it's removed in the next
commit. The exact validation rules and response contract (`ErrorCode`/`Params`) are preserved
byte-for-byte, verified with new validator-level and end-to-end pipeline tests.

### Changes
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs` (new)
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` (modified)
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Validators/DownloadFromUrlRequestValidatorTests.cs` (new)
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/Pipeline/FileStorageValidationPipelineTests.cs` (new)

## Status
DONE
