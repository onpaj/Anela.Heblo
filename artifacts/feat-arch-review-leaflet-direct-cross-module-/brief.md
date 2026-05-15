## Module
Leaflet

## Finding
Three files in the Leaflet module directly import application services from the KnowledgeBase module:

- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs` line 1:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  Uses `IDocumentTextExtractor` (injected as `IEnumerable<IDocumentTextExtractor>`, lines 12, 18).

- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` line 1:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  Uses `IDocumentTextExtractor` (injected as `IEnumerable<IDocumentTextExtractor>`, lines 11, 15).

- `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs` lines 1, 2:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  Uses both `IDocumentTextExtractor` and `IOneDriveService`.

`IDocumentTextExtractor` is defined in `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentTextExtractor.cs` and `IOneDriveService` in `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs` — both owned by KnowledgeBase.

## Why it matters
`development_guidelines.md` mandates: *"Communication between modules exclusively through contracts/ (e.g. IProductQueryService)"*. Both `IDocumentTextExtractor` and `IOneDriveService` are infrastructure capabilities that are not conceptually owned by KnowledgeBase — they are used by at least two modules. Placing them in `KnowledgeBase.Services` and importing them directly from there makes Leaflet depend on the KnowledgeBase application layer, preventing independent compilation, testing, or deployment of either module.

## Suggested fix
Move `IDocumentTextExtractor` and `IOneDriveService` (and their implementations) out of the KnowledgeBase module into a shared infrastructure abstraction — e.g. `Application/Shared/Documents/IDocumentTextExtractor.cs` and `Application/Shared/Storage/IOneDriveService.cs`. Both KnowledgeBase and Leaflet then depend on the shared abstraction. No module-to-module import is needed.

---
_Filed by daily arch-review routine on 2026-05-14._