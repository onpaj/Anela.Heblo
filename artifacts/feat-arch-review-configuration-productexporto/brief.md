## Module
Configuration

## Finding
`ProductExportOptions` is defined in `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs` under the Configuration module's domain folder, but it is consumed exclusively by the **FileStorage** module:

- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs:18`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs:22`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs:24`

The class models retry policy, timeouts, and download URL for the product export download job — concepts that are entirely internal to FileStorage and have no relationship to the Configuration module.

## Why it matters
This violates the Vertical Slice principle that each module owns its own contracts and types. The Configuration module's Domain layer must change whenever FileStorage's download behaviour changes, and FileStorage developers must look in a different module to find the options type for their feature. It also creates a cross-module Domain-level dependency that would be a problem if modules were ever split into separate deployable units.

## Suggested fix
Move `ProductExportOptions` to the FileStorage module — the simplest location is `backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs` (or a `Contracts/` subfolder if FileStorage uses one). Update the three consuming files' `using` statements and the DI registration in `ServiceCollectionExtensions.cs:356` accordingly. No behavioural change required.

---
_Filed by daily arch-review routine on 2026-05-29._