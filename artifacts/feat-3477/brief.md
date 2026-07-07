## Module
Leaflet

## Finding
Three Leaflet-owned types directly reference KnowledgeBase-owned service interfaces that have not been relocated to a shared namespace:

- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` (line 1) — injects `IEnumerable` from `Anela.Heblo.Application.Features.KnowledgeBase.Services`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs` (line 1) — injects `IEnumerable` from `Anela.Heblo.Application.Features.KnowledgeBase.Services`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs` (line 1) — injects `IOneDriveService` and references `OneDriveFile` from `Anela.Heblo.Application.Features.KnowledgeBase.Services`

These violations are captured in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` in `LeafletAllowlist` (lines 30–46) with the explicit comment:

> Track separately and remove these entries when `IDocumentTextExtractor` is relocated to a shared namespace.

No tracking issue has been created since the allowlist was written (2026-05-15). This is that issue.

## Why it matters
`IDocumentTextExtractor` and `IOneDriveService` are cross-cutting services used by at least two feature modules (KnowledgeBase and Leaflet). Keeping them under `KnowledgeBase.Services` forces Leaflet to take a hard compile-time dependency on the KnowledgeBase module — exactly the kind of coupling the boundary test exists to prevent. If KnowledgeBase is ever extracted or modified, Leaflet breaks silently until the test fails.

## Suggested fix
Move `IDocumentTextExtractor`, its implementations, `IOneDriveService`, and `OneDriveFile` from `Anela.Heblo.Application.Features.KnowledgeBase.Services` to a shared infrastructure namespace — the natural candidates are `Anela.Heblo.Application.Shared.Documents` (for the text extractor) and `Anela.Heblo.Application.Shared.OneDrive` (or `Anela.Heblo.Domain.Shared`) for the OneDrive service.

After relocation:
1. Update all import statements in both KnowledgeBase and Leaflet.
2. Remove the four allowlist entries from `LeafletAllowlist` in `ModuleBoundariesTests.cs`.
3. Verify the boundary test still passes.

---
_Filed by daily arch-review routine on 2026-07-04._
