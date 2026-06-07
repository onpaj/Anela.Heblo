## Module
ExpeditionListArchive

## Finding
`DownloadExpeditionListResponse` and `ReprintExpeditionListResponse` each declare their own `string? ErrorMessage` property outside the structured error mechanism established in `BaseResponse`:

```csharp
// DownloadExpeditionListResponse.cs, line 11
public string? ErrorMessage { get; set; }

// ReprintExpeditionListResponse.cs, line 7
public string? ErrorMessage { get; set; }
```

`BaseResponse` already provides a structured error contract:
- `ErrorCode` (typed enum) 
- `Params` (dictionary for localisation parameters)
- `FullError()` for serialising the combined error

These two classes bypass that mechanism entirely, inventing a parallel freeform-string error channel. The frontend reads the ad-hoc field directly: `errorData?.errorMessage` (`useExpeditionListArchive.ts`, lines 117, 143).

Other modules set `ErrorCode + Params` through the provided `BaseResponse(ErrorCodes, ...)` constructor. This module's serialised error shape is therefore different from every other module, which affects the TypeScript client's error-handling contract and makes OpenAPI-generated clients inconsistent.

## Why it matters
- **API surface inconsistency**: clients that generically handle errors by inspecting `errorCode` will see `null` here and miss the error details.
- **Maintainability**: two parallel error mechanisms require readers to know which modules use which path.
- **OpenAPI client**: the generated TypeScript client serialises `errorMessage` as a property that does not exist on the shared response base type used elsewhere.

## Suggested fix
Remove the ad-hoc `ErrorMessage` properties and use the `BaseResponse` error path. Add an appropriate `ErrorCodes` member (e.g. `InvalidBlobPath`) if one doesn't already exist, then replace the `Fail` factories:

```csharp
// DownloadExpeditionListResponse.cs
public static DownloadExpeditionListResponse Fail() =>
    new() { Success = false, ErrorCode = ErrorCodes.InvalidBlobPath };

// ReprintExpeditionListResponse.cs
public static ReprintExpeditionListResponse Fail() =>
    new() { Success = false, ErrorCode = ErrorCodes.InvalidBlobPath };
```

The controller already returns `BadRequest(response)` on failure; the error detail travels via the typed `ErrorCode` field, consistent with all other modules.

---
_Filed by daily arch-review routine on 2026-06-04._