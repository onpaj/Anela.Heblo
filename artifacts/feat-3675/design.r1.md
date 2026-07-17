# Design: Extract Azure Container Name Validation from `DownloadFromUrlHandler`

## Component Design

### `DownloadFromUrlRequestValidator` (new)

**Location:** `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs`
**Namespace:** `Anela.Heblo.Application.Features.FileStorage.Validators`

**Responsibility:** Sole owner of the Azure Blob container-naming rule for `DownloadFromUrlRequest`. Replaces the inline `IsValidContainerName` check + early-return block previously embedded in `DownloadFromUrlHandler.Handle`. Contains no orchestration, I/O, or logging — purely a synchronous predicate over the request's `ContainerName`.

**Interface:**

```csharp
public class DownloadFromUrlRequestValidator : AbstractValidator<DownloadFromUrlRequest>
{
    public DownloadFromUrlRequestValidator()
    {
        RuleFor(x => x.ContainerName)
            .Must(IsValidContainerName)
            .WithErrorCode(((int)ErrorCodes.InvalidContainerName).ToString())
            .WithState(x => (object)new Dictionary<string, string>
            {
                { "containerName", x.ContainerName },
                { "cause", "validation" },
            })
            .WithMessage("Invalid container name");
    }

    private static bool IsValidContainerName(string containerName)
    {
        // verbatim copy of DownloadFromUrlHandler.IsValidContainerName (lines 199–221):
        // 3–63 chars, lowercase-invariant, first/last alphanumeric,
        // body alphanumeric or single hyphens (no "--").
    }
}
```

Implements `FluentValidation.IValidator<DownloadFromUrlRequest>` (via `AbstractValidator<T>`). Not exposed outside the Application layer — no controller, MCP tool, or other module references it directly; it is resolved only through DI as `IValidator<DownloadFromUrlRequest>`.

**Pipeline placement:** Runs inside `ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>` (`Common/Behaviors/ValidationResultBehavior.cs`, unchanged), which MediatR invokes before `DownloadFromUrlHandler.Handle` for every `Send(DownloadFromUrlRequest)`:

```
IMediator.Send(DownloadFromUrlRequest)
    → ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>.Handle
        → resolves IEnumerable<IValidator<DownloadFromUrlRequest>>  (= [DownloadFromUrlRequestValidator])
        → ValidateAsync(request)
            ├─ failures.Any() == true
            │     firstFailure = failures.First()
            │     errorCode = Enum.TryParse<ErrorCodes>(firstFailure.ErrorCode, ...) ?? ErrorCodes.ValidationError
            │     return new DownloadFromUrlResponse {
            │         Success = false,
            │         ErrorCode = errorCode,
            │         Params = firstFailure.CustomState as Dictionary<string, string>
            │     }
            │     // next() is NEVER called — DownloadFromUrlHandler.Handle does not run
            │
            └─ failures.Any() == false
                  → next() → DownloadFromUrlHandler.Handle(request, ct)   // unchanged
```

`ValidationResultBehavior` itself is existing, reused as-is (no code changes) — this is a wiring change, not a behavior change. Its early-out (`if (!_validators.Any()) return await next();`) means a forgotten registration silently disables the rule rather than failing loudly; this is the one integration risk this design must guard against via a DI-wiring test (see FR-4 in spec, not this document's concern).

### `DownloadFromUrlHandler` (modified)

**Responsibility after this change:** URL-format validation (`Uri.TryCreate` + scheme check, unchanged, stays inline) plus orchestration only — HEAD probe, resilience-wrapped blob upload, and error mapping for timeout/http-status/retry-exhausted paths. No longer performs or references container-name validation.

**Interface:** Unchanged — still `IRequestHandler<DownloadFromUrlRequest, DownloadFromUrlResponse>`, same constructor dependencies (`IBlobStorageService`, `IDownloadResilienceService`, `IHttpClientFactory`, `IOptions<FileDownloadOptions>`, `ILogger<DownloadFromUrlHandler>`).

**Removed:**
- `private static bool IsValidContainerName(string containerName)` (lines 199–221)
- The `if (!IsValidContainerName(request.ContainerName)) { ...return DownloadFromUrlResponse{ErrorCode=InvalidContainerName}... }` block (lines 61–74)

By the time `Handle` runs, `request.ContainerName` is guaranteed valid per the rule above (the pipeline short-circuits otherwise) — `Handle` performs no defensive re-check.

### `FileStorageModule` (modified)

**Responsibility:** DI composition root for the FileStorage module (`AddFileStorageModule`). Adds two registrations binding `DownloadFromUrlRequest`/`DownloadFromUrlResponse` to the new validator and to the existing `ValidationResultBehavior<,>` generic, mirroring `AnalyticsModule.AddAnalyticsModule`'s pattern for `GetMarginReportRequest`/`GetProductMarginAnalysisRequest`.

**Change (added to `AddFileStorageModule`, alongside existing service registrations):**

```csharp
services.AddScoped<IValidator<DownloadFromUrlRequest>, DownloadFromUrlRequestValidator>();
services.AddScoped<IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>,
    ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>>();
```

Requires three new `using` directives in `FileStorageModule.cs`:
- `using FluentValidation;`
- `using Anela.Heblo.Application.Common.Behaviors;`
- `using Anela.Heblo.Application.Features.FileStorage.Validators;`

No other change to `AddFileStorageModule` (HTTP client registration, resilience service registration, options binding all remain as-is).

### Unaffected components

- `FileStorageController` — unchanged; sends `DownloadFromUrlRequest` via `IMediator.Send` and maps the returned `DownloadFromUrlResponse` to an HTTP status the same way regardless of where validation happened.
- `ValidationResultBehavior<TRequest, TResponse>` — unchanged, reused as-is.
- `IBlobStorageService`, `IDownloadResilienceService` — unchanged.

## Data Schemas

No data model, database, or wire-contract changes. All shapes below are existing and remain byte-for-byte identical; they are documented here only to specify the contract the new validator must reproduce.

### `DownloadFromUrlRequest` (unchanged)

```csharp
public class DownloadFromUrlRequest : IRequest<DownloadFromUrlResponse>
{
    [Required] public string FileUrl { get; set; } = null!;
    [Required] public string ContainerName { get; set; } = null!;
    public string? BlobName { get; set; }
}
```

### `DownloadFromUrlResponse` (unchanged)

```csharp
public class DownloadFromUrlResponse : BaseResponse
{
    public string BlobUrl { get; set; } = null!;
    public string BlobName { get; set; } = null!;
    public string ContainerName { get; set; } = null!;
    public long FileSizeBytes { get; set; }
}
```

`BaseResponse` (unchanged, existing):

```csharp
public abstract class BaseResponse
{
    public bool Success { get; set; } = true;
    public ErrorCodes? ErrorCode { get; set; }
    public Dictionary<string, string>? Params { get; set; }
}
```

### Validation-failure payload shape (produced by `ValidationResultBehavior`, reconstructed via `WithErrorCode`/`WithState`)

On an invalid `ContainerName`, `ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>` builds the response entirely from the first `ValidationFailure`'s `ErrorCode` and `CustomState`, populated by the validator's `.WithErrorCode(...)` / `.WithState(...)` calls:

| FluentValidation rule builder call | Populates | Consumed by behavior as |
|---|---|---|
| `.WithErrorCode(((int)ErrorCodes.InvalidContainerName).ToString())` | `ValidationFailure.ErrorCode` = `"1802"` | `Enum.TryParse<ErrorCodes>(firstFailure.ErrorCode, out parsed)` → `ErrorCodes.InvalidContainerName` |
| `.WithState(x => (object)new Dictionary<string, string>{ {"containerName", x.ContainerName}, {"cause", "validation"} })` | `ValidationFailure.CustomState` (boxed `Dictionary<string,string>`) | `firstFailure.CustomState as Dictionary<string, string>` → assigned directly to `response.Params` |
| `.WithMessage("Invalid container name")` | `ValidationFailure.ErrorMessage` | not read by `ValidationResultBehavior` (only `ErrorCode`/`CustomState` are used); kept for parity/debuggability |

Resulting wire body (`DownloadFromUrlResponse`, HTTP 400 via `[HttpStatusCode(HttpStatusCode.BadRequest)]` on `ErrorCodes.InvalidContainerName`):

```json
{
  "success": false,
  "errorCode": "InvalidContainerName",
  "params": {
    "containerName": "<submitted value>",
    "cause": "validation"
  },
  "blobUrl": null,
  "blobName": null,
  "containerName": null,
  "fileSizeBytes": 0
}
```

This is identical to what the handler previously constructed manually (`Success = false, ErrorCode = ErrorCodes.InvalidContainerName, Params = { containerName, cause = "validation" }`) — the refactor changes only which component builds it (`ValidationResultBehavior`, driven by the validator's failure metadata, instead of `DownloadFromUrlHandler.Handle`'s inline `if` block).

### Unaffected payload shapes

- `ErrorCodes.InvalidUrlFormat` failure (`Params = { fileUrl, cause="validation" }`) — still built inline in `DownloadFromUrlHandler.Handle`, untouched.
- `ErrorCodes.FileDownloadFailed` failure (`Params = { fileUrl, cause, attemptCount, elapsedMs, error }`) — still built by the handler's `Failure(...)` helper, untouched.
- Success payload (`Success = true, BlobUrl, BlobName, ContainerName, FileSizeBytes`) — still built by the handler, untouched.
