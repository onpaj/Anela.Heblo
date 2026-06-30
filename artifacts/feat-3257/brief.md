## Module
KnowledgeBase

## Finding

`DocumentSummary` is a shared DTO used by two separate use cases, but it is defined inside the `GetDocuments` use-case folder instead of the module-level `Contracts/` directory.

**Definition site:**
`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsRequest.cs:26–35`

**Consumed outside the defining use case:**
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentResponse.cs:1,8`
  — `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs:3,59–68`
  — uses `DocumentSummary` in `MapToSummary()` return type

This creates an intra-module coupling: `UploadDocument` depends on the namespace of the `GetDocuments` use case rather than on the module's own `Contracts/` namespace.

## Why it matters

`filesystem.md` states:
> **Features/{Feature}/Contracts/**: Shared DTOs across use cases

`development_guidelines.md` states:
> DTOs live in `contracts/` of the specific module

Placing a shared DTO inside a specific use-case folder means any consumer of that DTO acquires an implicit dependency on an unrelated use case. If `GetDocuments` is refactored or renamed, every consumer of `DocumentSummary` breaks — even those that have nothing to do with listing documents.

Additionally, there is currently no `KnowledgeBase/Contracts/` folder at all, which means any future shared DTO has no canonical home.

## Suggested fix

1. Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs` with namespace `Anela.Heblo.Application.Features.KnowledgeBase.Contracts`.
2. Remove `DocumentSummary` from `GetDocumentsRequest.cs`.
3. Update `using` directives in:
   - `GetDocumentsRequest.cs` (response still references it)
   - `GetDocumentsHandler.cs`
   - `UploadDocumentResponse.cs`
   - `UploadDocumentHandler.cs`

No logic changes required — move only.

---
_Filed by daily arch-review routine on 2026-06-21._
