## Module
Leaflet

## Finding
Two files in the Leaflet module directly import from the KnowledgeBase **domain** layer:

- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` line 3:
  ```csharp
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  ```
  This brings in `IKnowledgeBaseRepository`, which is injected as a constructor parameter (line 14, 23) and used to perform `SearchSimilarAsync` vector lookups.

- `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs` line 4:
  ```csharp
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  ```
  This brings in the `DocumentType` enum used to filter `OneDriveFolderMappings` (line 62).

## Why it matters
`development_guidelines.md` forbids direct cross-module references: *"No direct references between feature modules — communication only through contracts/interfaces."* The Leaflet module is importing **domain** types from KnowledgeBase, making both modules tightly coupled at the lowest architectural level. Any rename or restructure of `IKnowledgeBaseRepository` or `DocumentType` in the KnowledgeBase domain breaks the Leaflet module. It also prevents independent testing and future extraction of either module.

## Suggested fix
- **`IKnowledgeBaseRepository`**: define a narrow read-only contract in a shared or Leaflet-owned contracts location, e.g. `ILeafletKnowledgeSource` with only the `SearchSimilarAsync` signature the handler needs. KnowledgeBase implements it; Leaflet depends on the abstraction.
- **`DocumentType`**: if this is a configuration concept used across multiple modules, it belongs in a shared configuration type, not in the KnowledgeBase domain. Move it to `Application/Shared/` or to `LeafletOptions` as a plain string/enum owned by Leaflet.

---
_Filed by daily arch-review routine on 2026-05-14._