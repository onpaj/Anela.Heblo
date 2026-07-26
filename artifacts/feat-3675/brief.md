## Module
FileStorage

## Finding
`DownloadFromUrlHandler` (`backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`, lines 199–221) contains a 22-line `IsValidContainerName` method that implements Azure Blob Storage naming constraints verbatim (3–63 chars, lowercase, alphanumeric + single hyphens, start/end alphanumeric):

```csharp
private static bool IsValidContainerName(string containerName)
{
    if (string.IsNullOrEmpty(containerName) || containerName.Length < 3 || containerName.Length > 63)
        return false;
    if (containerName != containerName.ToLowerInvariant())
        return false;
    ...
}
```

These are Azure-specific infrastructure naming rules. They live in the Application layer, which should be storage-provider-agnostic.

## Why it matters
Embedding Azure naming rules in the Application handler violates the dependency rule: Application must not depend on infrastructure-specific knowledge. If the blob storage provider changes (or a second provider is added), the handler must be modified — a change in infrastructure bleeds through to business logic. It also bypasses the project's standard FluentValidation `Validators/` pattern; the handler is simultaneously doing validation and orchestration, violating SRP.

## Suggested fix
Remove `IsValidContainerName` from the handler. Move the validation to a `DownloadFromUrlRequestValidator` (FluentValidation, in a new `FileStorage/Validators/` folder) and keep the same rules there. If the concern is early failure before touching storage, the validator runs in the MediatR pipeline before the handler is invoked — same result, correct separation. The handler (lines 61–74) that calls it and returns `ErrorCodes.InvalidContainerName` can then be deleted.

---
_Filed by daily arch-review routine on 2026-07-17._
