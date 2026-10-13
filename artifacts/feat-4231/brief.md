## Module
FileStorage / ExpeditionListArchive

## Finding
All four `ExpeditionListArchive` handlers inject `IBlobStorageService` directly from `Anela.Heblo.Domain.Features.FileStorage`, creating a hard compile-time dependency from `ExpeditionListArchive` onto the `FileStorage` module's domain:

- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs` — uses `IBlobStorageService.DownloadAsync`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` — uses `IBlobStorageService.DownloadAsync`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` — uses `IBlobStorageService.ListBlobsAsync`; also consumes `BlobItemInfo` (`.Name`, `.FileName`, `.CreatedOn`, `.ContentLength`)
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs` — uses `IBlobStorageService.ListVirtualDirectoriesAsync`

All four files contain `using Anela.Heblo.Domain.Features.FileStorage;` and inject `IBlobStorageService`.

## Why it matters
The documented cross-module communication rule requires consumer modules to define their own narrow contract interface in their `Contracts/` folder; the provider module supplies an adapter implementation (see `ILeafletKnowledgeSource` / `KnowledgeBaseLeafletSourceAdapter` as the canonical example, and `development_guidelines.md` Cross-Module Communication section).

`IBlobStorageService` is a 7-method interface covering the full blob-storage surface. `ExpeditionListArchive` only uses three of those methods. By importing the full `IBlobStorageService`, the module couples directly to a foreign Domain type — moving `ExpeditionListArchive` to a separate service would require pulling `FileStorage` domain along, and testing handlers in isolation requires the full `IBlobStorageService` mock.

## Suggested fix
1. Define a narrow contract in `Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs` exposing only the three operations the module actually needs (`DownloadAsync`, `ListBlobsAsync`, `ListVirtualDirectoriesAsync`). Replace `BlobItemInfo` with a local `ExpeditionBlobItem` DTO owned by `ExpeditionListArchive`.
2. Add an adapter (in the Azure Adapters project or FileStorage module) that wraps `IBlobStorageService` and implements `IExpeditionListArchiveBlobStore`.
3. Register the binding in `ExpeditionListArchiveModule.cs`.
4. Replace `IBlobStorageService` injections in the four handlers with `IExpeditionListArchiveBlobStore`.

---
_Filed by daily arch-review routine on 2026-09-20._
