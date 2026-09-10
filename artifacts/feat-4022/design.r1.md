# Design: Remove dead URL-validation code from DownloadFromUrlHandler

## Component Design
No new or restructured components. This change deletes an unreachable code block from one existing MediatR handler; the component boundaries and responsibilities in the `DownloadFromUrl` use case are unchanged.

- **`DownloadFromUrlRequestValidator`** (`FluentValidation`) — unchanged. Remains the sole enforcer of URL format/scheme validity via `IsValidFileUrl()`, wired with `ErrorCodes.InvalidUrlFormat` and `{ "fileUrl": x.FileUrl, "cause": "validation" }` state.
- **`ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>`** — unchanged. Continues to run all registered validators ahead of the handler and short-circuits with the `InvalidUrlFormat` response before `next()` is called whenever validation fails.
- **`DownloadFromUrlHandler.Handle()`** — loses its dead `Uri.TryCreate` / scheme-check block (current lines 45–59) and the `_logger.LogWarning` + early `return` inside it. All other responsibilities (redacting the URL for logging, HEAD probe, resilience-wrapped download, success/failure response construction, exception handling) are unchanged; the method now proceeds directly from its initial `_logger.LogInformation(...)` call into that existing logic, exactly as it already does today for any request that has passed the validator. The `using System.Collections.Generic;` import is removed only if the compiler/analyzer confirms it is unused after the deletion (the `Failure(...)` helper still builds a `Dictionary<string, string>`, so it is expected to remain needed).
- **`FileStorageModule.AddFileStorageModule()`** — unchanged. Validator and pipeline-behavior registrations are out of scope.

No component's public interface, constructor, or DI registration changes.

## Data Schemas
No schema changes. `DownloadFromUrlRequest` and `DownloadFromUrlResponse` shapes are unchanged, and the `InvalidUrlFormat` failure shape is unchanged from the caller's perspective — it is now produced only by `ValidationResultBehavior` (via the validator's `WithErrorCode`/`WithState`) instead of redundantly by both the pipeline behavior and the handler:

```
Success = false
ErrorCode = ErrorCodes.InvalidUrlFormat
Params = { "fileUrl": <original FileUrl>, "cause": "validation" }
```

No database, event, or wire-contract changes are introduced by this change.
