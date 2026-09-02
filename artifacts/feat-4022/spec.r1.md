# Specification: Remove dead URL-validation code from DownloadFromUrlHandler

## Summary
`DownloadFromUrlHandler.Handle()` contains a manual URL-format/scheme check that duplicates logic already enforced by `DownloadFromUrlRequestValidator` through the MediatR `ValidationResultBehavior` pipeline. Because the pipeline behavior short-circuits and returns before the handler runs whenever validation fails, this handler-level check can never execute during normal request processing. This is a small, surgical dead-code removal: delete the unreachable block and its now-unused `System.Collections.Generic` import if it becomes unused.

## Background
`FileStorageModule.AddFileStorageModule()` registers `DownloadFromUrlRequestValidator` as an `IValidator<DownloadFromUrlRequest>` and registers `ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>` as a `IPipelineBehavior`. `ValidationResultBehavior.Handle()` runs all registered validators before invoking the next pipeline step (ultimately the handler); if any validator produces a failure, it returns a `TResponse` immediately with `Success = false`, `ErrorCode` taken from the failure's `ErrorCode`, and `Params` taken from the failure's `CustomState`, and it never calls `next()` — meaning the handler is not invoked at all.

`DownloadFromUrlRequestValidator.IsValidFileUrl()` performs `Uri.TryCreate(fileUrl, UriKind.Absolute, out uri) && (uri.Scheme == Http || uri.Scheme == Https)`, and its rule is wired with `.WithErrorCode(((int)ErrorCodes.InvalidUrlFormat).ToString())` and `.WithState(...)` producing `{ "fileUrl": x.FileUrl, "cause": "validation" }`.

`DownloadFromUrlHandler.Handle()` (lines 45–59) performs the identical `Uri.TryCreate` + scheme check and, on failure, logs a warning and returns `new DownloadFromUrlResponse { Success = false, ErrorCode = ErrorCodes.InvalidUrlFormat, Params = { ["fileUrl"] = request.FileUrl, ["cause"] = "validation" } }` — an outcome structurally identical to what the pipeline behavior already produces for the same input, before the handler is ever reached.

This was confirmed by reading the current source on the feature branch:
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` (lines 45–59)
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs` (`IsValidFileUrl`, line 33)
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` (lines 71–73)

The duplication creates a maintenance drift risk (two places to update if URL-validation rules ever change) and obscures the real control flow for anyone reading the handler in isolation. Removing it is a pure cleanup with no intended behavior change.

## Functional Requirements

### FR-1: Remove the unreachable URL-validation block from DownloadFromUrlHandler
Delete the `if (!Uri.TryCreate(...) ...)` block (current lines 45–59, inclusive of the `_logger.LogWarning` call and the early `return` of the `InvalidUrlFormat` response) from `DownloadFromUrlHandler.Handle()`. The method should proceed directly from the initial `_logger.LogInformation(...)` call to computing `redactedUrl` / starting the stopwatch, exactly as it does today for any request that already passed the validator.

**Acceptance criteria:**
- The `Uri.TryCreate` / scheme-check block is no longer present in `DownloadFromUrlHandler.Handle()`.
- No other logic in `Handle()` (HEAD probe, resilience execution, success/failure response construction, exception handling) is altered.
- `DownloadFromUrlRequestValidator` and `ValidationResultBehavior` registration in `FileStorageModule` are left unchanged — they remain the sole enforcement point for URL format/scheme validity.
- The project builds with no new warnings; if the removal leaves the `using System.Collections.Generic;` import (or any other `using`) unused in the file, remove that unused import too. No other imports, usings, or unrelated lines are touched.

### FR-2: Preserve existing behavior for all inputs
End-to-end behavior of the `DownloadFromUrl` use case must be unchanged for every input, since the removed code was unreachable.

**Acceptance criteria:**
- For a request with an invalid/malformed `FileUrl` or a non-http(s) scheme, the API response is still `Success = false`, `ErrorCode = ErrorCodes.InvalidUrlFormat`, with `Params["fileUrl"]` and `Params["cause"] = "validation"` populated — now produced entirely by `ValidationResultBehavior` via `DownloadFromUrlRequestValidator`, not by the handler.
- For a request with a valid `FileUrl` and container name, the handler's download/upload flow executes exactly as before (HEAD probe, resilience-wrapped download, success/failure response construction) with no change in outcome.
- Any existing unit/integration test that exercised the handler's own invalid-URL branch is updated to assert the same outcome via the validator/pipeline path (or removed if it becomes a duplicate of an existing validator-level test), so overall test coverage of the invalid-URL scenario is not reduced.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected; this removes a cheap, already-unreachable check. Not a target of this change.

### NFR-2: Security
No security impact. URL scheme/format enforcement (restricting downloads to `http`/`https` absolute URLs) continues to be enforced identically, only earlier in the pipeline (validator) rather than redundantly in both places.

## Data Model
Not applicable — no data model changes.

## API / Interface Design
Not applicable — no request/response contract changes. `DownloadFromUrlRequest` and `DownloadFromUrlResponse` shapes, and the `ErrorCodes.InvalidUrlFormat` response for invalid URLs, are unchanged from the caller's perspective.

## Dependencies
- `DownloadFromUrlRequestValidator` (FluentValidation) and `ValidationResultBehavior<TRequest, TResponse>` (existing MediatR pipeline behavior), both already registered in `FileStorageModule.AddFileStorageModule()`. This change does not introduce, modify, or depend on any new library or service.

## Out of Scope
- Any change to `DownloadFromUrlRequestValidator`, `ValidationResultBehavior`, or `FileStorageModule` registration.
- Any change to `ContainerName` validation (`IsValidContainerName`), which is unaffected and not flagged as dead code.
- Any refactor of the handler's HEAD-probe, resilience, or response-construction logic beyond removing the specified dead block.
- Broader dead-code audits of other handlers/validators in the FileStorage module or elsewhere.

## Open Questions
None.

## Status: COMPLETE
