# Code Review: Add Container Name Validator and Pipeline Wiring

## Summary
The implementation matches the task-context spec verbatim: `DownloadFromUrlRequestValidator` reproduces the handler's `IsValidContainerName` logic exactly, `FileStorageModule` registers it plus `ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>` mirroring `AnalyticsModule`, and both new test files cover the validator in isolation and the end-to-end DI/MediatR short-circuit behavior. All 18 relevant tests pass and the solution builds cleanly with no new warnings.

## Review Result: PASS

### task: add-container-name-validator-and-pipeline-wiring
**Status:** PASS

## Verification performed
- Read `DownloadFromUrlRequestValidator.cs`: the `RuleFor(x => x.ContainerName).Must(IsValidContainerName).WithErrorCode(...).WithState(...).WithMessage(...)` rule and the private `IsValidContainerName` helper are byte-for-byte identical to the task spec and to the original inline method in `DownloadFromUrlHandler.cs` (lines 199-221), confirming FR-1 acceptance criteria (length 3-63, lowercase-only, alphanumeric start/end, no consecutive hyphens).
- Confirmed `ErrorCodes.InvalidContainerName = 1802` exists in `Shared/ErrorCodes.cs`, and `WithErrorCode(((int)ErrorCodes.InvalidContainerName).ToString())` round-trips through `Enum.TryParse<ErrorCodes>` per the validator test `ContainerName_Invalid_ErrorCodeRoundTrips_ToInvalidContainerName`.
- Read `FileStorageModule.cs`: the three new `using` directives and the two `services.AddScoped<...>` registrations (`IValidator<DownloadFromUrlRequest>` → `DownloadFromUrlRequestValidator`; `IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>` → `ValidationResultBehavior<...>`) are placed exactly where specified, immediately before `return services;`, matching FR-2 and the `AnalyticsModule` precedent.
- Read `ValidationResultBehavior.cs` to confirm the pipeline's actual short-circuit behavior (`Enum.TryParse` + `CustomState as Dictionary<string,string>` reconstruction) matches what the pipeline test asserts.
- Read `DownloadFromUrlHandler.cs`, `DownloadFromUrlRequest.cs`, `DownloadFromUrlResponse.cs`: confirmed the handler's inline `IsValidContainerName` check is intentionally left in place for this commit (per the task's explicit "leave it untouched, remove in the next task" instruction), and that the property names/types used across the two new test files (`FileUrl`, `ContainerName`, `BlobName`, `Success`, `ErrorCode`, `Params`, `ContainerName` on the response) match the real classes.
- Ran `dotnet build Anela.Heblo.sln` from repo root: 0 errors, 251 warnings, all pre-existing and unrelated to this change (confirmed none originate from the four touched/created files).
- Ran the specified filtered test command: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~FileStorage.Validators.DownloadFromUrlRequestValidatorTests|FullyQualifiedName~FileStorage.Pipeline.FileStorageValidationPipelineTests"` → **Passed: 18, Failed: 0**.

## Spec compliance
- FR-1 (validator extraction): fully met — class location, rule shape, `WithErrorCode`/`WithState` contract, and verbatim `IsValidContainerName` logic all match.
- FR-2 (pipeline wiring): fully met — exact two-line registration pattern present in `FileStorageModule.cs`, proven end-to-end by `FileStorageValidationPipelineTests` (invalid name short-circuits before `IBlobStorageService.DownloadFromUrlAsync` is invoked; valid name reaches the handler and returns success).
- This task intentionally does not address FR-3 (removing the handler's inline check) or FR-4 (deleting/relocating the old handler-level tests) — the task-context file explicitly scopes those to a follow-up task and states the redundant double-check is expected and harmless for this commit. Correctly out of scope here.

## Architecture adherence
Matches `arch-review.r1.md`'s guidance precisely: `ValidationResultBehavior` (not `ValidationBehavior`) is used to preserve the `DownloadFromUrlResponse` wire contract; registration is per-request-type (not global), mirroring `AnalyticsModule`; the `IsValidContainerName` predicate stays a private, non-shared helper on the validator per Decision 3 (no premature extraction to a shared/domain helper).

## Completeness
Both required test files exist and are comprehensive: validator-level tests cover 8 invalid + 5 valid `[InlineData]` cases plus error-code round-trip and `Params`/state assertions; the pipeline test proves both the short-circuit-on-invalid and pass-through-on-valid paths via a real `ServiceCollection`/`AddMediatR` wiring, directly addressing the "missing registration silently disables the check" risk called out in the arch review.

## Correctness
No logic errors found. The validator's character-by-character loop, case check, and length bounds are an exact copy of the original method. The pipeline test's mock setup for `IDownloadResilienceService.ExecuteWithResilienceAsync` and the `IHttpClientFactory`/`StubHttpMessageHandler` HEAD-probe stub correctly avoid real network calls while allowing the valid-path test to reach and complete the handler.

## Docs to Update
None. This is an internal Application-layer refactor with no public API, DI-wiring documentation, or architecture-doc changes implied by this specific task (FR-1/FR-2 only).

## Overall Notes
Clean, minimal, spec-literal implementation. The redundant double-validation (handler + validator) introduced by this commit is intentional and explicitly scoped to be resolved by the next task in the plan; nothing here needs revision.
