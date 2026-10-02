# Architecture Review: Move `LeafletDocumentSummary` to the Leaflet module's shared `Contracts/` folder

## Skip Design: true
Backend-only, pure code-organization refactor. No UI, API contract, or DTO-shape change; nothing for a designer to review.

## Architectural Fit Assessment
This aligns exactly with the documented convention in `docs/architecture/filesystem.md`: `Features/{Feature}/Contracts/` is the designated home for "Shared DTOs across use cases," while `Features/{Feature}/UseCases/{UseCase}/` is scoped to that single use case's Handler/Request/Response. `LeafletDocumentSummary` is consumed by two use cases (`GetLeafletDocuments` and `UploadLeaflet`), which is precisely the condition the doc defines for `Contracts/` placement. The Leaflet module already has an active `Contracts/` folder (`ILeafletKnowledgeSource.cs`, `KnowledgeSearchResult.cs`), so this is not introducing a new pattern — it's applying an existing, established one. The only integration points are C# `using` directives in four backend source/test files; there is no runtime behavior, DI registration, or wire-format change anywhere in the data flow.

## Proposed Architecture

### Component Overview
```
Features/Leaflet/
├── Contracts/
│   ├── ILeafletKnowledgeSource.cs
│   ├── KnowledgeSearchResult.cs
│   └── LeafletDocumentSummary.cs        <-- NEW (moved here)
├── UseCases/
│   ├── GetLeafletDocuments/
│   │   ├── GetLeafletDocumentsRequest.cs    (LeafletDocumentSummary class removed)
│   │   └── GetLeafletDocumentsHandler.cs    (adds `using ...Contracts;`)
│   └── UploadLeaflet/
│       ├── UploadLeafletResponse.cs         (using swapped: UseCases.GetLeafletDocuments -> Contracts)
│       └── UploadLeafletHandler.cs          (using swapped: UseCases.GetLeafletDocuments -> Contracts)
```
Before: `UploadLeaflet -> UseCases.GetLeafletDocuments` (sibling use-case coupling).
After: `UploadLeaflet -> Contracts`, `GetLeafletDocuments -> Contracts` (both use cases depend downward on the shared contract layer; no sibling-to-sibling edge remains).

### Key Design Decisions

#### Decision 1: Target location for the moved type
**Options considered:**
- (a) Leave `LeafletDocumentSummary` in `GetLeafletDocuments/` and have `UploadLeaflet` keep importing it cross-use-case.
- (b) Move it to `Features/Leaflet/Contracts/LeafletDocumentSummary.cs`.
- (c) Promote it to a module-wide/shared DTO location outside `Features/Leaflet` (e.g. `Application/Shared`).

**Chosen approach:** (b) — move to `Features/Leaflet/Contracts/LeafletDocumentSummary.cs`, one class per file, matching the existing style of `KnowledgeSearchResult.cs` and `ILeafletKnowledgeSource.cs` in that same folder.

**Rationale:** (a) is the exact anti-pattern the issue flags and `filesystem.md` warns against. (c) is unwarranted scope creep — the type is shared only *within* the Leaflet module (across its own use cases), not across modules, so it belongs in that module's own `Contracts/` folder per the documented layout, not in a cross-module shared location. (b) is the minimal, doc-prescribed fix.

#### Decision 2: File granularity
**Options considered:**
- Keep `LeafletDocumentSummary` bundled in a multi-class file (as it currently is, alongside `Request`/`Response`).
- Give it its own file, `LeafletDocumentSummary.cs`, named after the class.

**Chosen approach:** Own file, named after the class, mirroring `KnowledgeSearchResult.cs` (single class per file) already in `Contracts/`.

**Rationale:** Consistency with the sibling files already in that folder; a shared DTO is easier to find by filename when it lives in its own file rather than being buried as a second class inside a use-case's request/response file.

## Implementation Guidance

### Directory / Module Structure
1. Create `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/LeafletDocumentSummary.cs`:
   ```csharp
   namespace Anela.Heblo.Application.Features.Leaflet.Contracts;

   public class LeafletDocumentSummary
   {
       public Guid Id { get; set; }
       public string Filename { get; set; } = string.Empty;
       public string Status { get; set; } = string.Empty;
       public string ContentType { get; set; } = string.Empty;
       public DateTime IngestedAt { get; set; }
       public DateTime? IndexedAt { get; set; }
       public Guid? FirstChunkId { get; set; }
   }
   ```
   (Matches `docs/development/development_guidelines.md` DTO rule: class, not record.)
2. Delete the `LeafletDocumentSummary` class block from `GetLeafletDocumentsRequest.cs`, leaving `GetLeafletDocumentsRequest` and `GetLeafletDocumentsResponse` in place.

### Interfaces and Contracts
No public interface changes — the class name, namespace-external shape, and members are identical. Only the C# namespace changes, from `Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments` to `Anela.Heblo.Application.Features.Leaflet.Contracts`. This does not affect the OpenAPI schema name (Swashbuckle/NSwag key schemas by class name, not namespace), so the generated TypeScript client's `LeafletDocumentSummary` type is expected to be unaffected — regenerate the client as a verification step, not because a diff is expected.

Files requiring a `using` change (verified via repo-wide search — this is a closed, complete list):
| File | Change |
|------|--------|
| `Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsRequest.cs` | Remove the `LeafletDocumentSummary` class; add `using Anela.Heblo.Application.Features.Leaflet.Contracts;` (needed because `GetLeafletDocumentsResponse.Documents` is typed `List<LeafletDocumentSummary>`) |
| `Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs` | Add `using Anela.Heblo.Application.Features.Leaflet.Contracts;` |
| `Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletResponse.cs` | Replace `using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;` with `using Anela.Heblo.Application.Features.Leaflet.Contracts;` |
| `Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` | Replace `using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;` with `using Anela.Heblo.Application.Features.Leaflet.Contracts;` (keep its existing `using ...UseCases.IndexLeaflet;`, untouched) |
| `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs` | Add `using Anela.Heblo.Application.Features.Leaflet.Contracts;` (keep the existing `using ...UseCases.GetLeafletDocuments;` — still needed there for `GetLeafletDocumentsRequest`/`Response` and the handler type used elsewhere in that file) |

Do not touch `frontend/src/api/generated/api-client.ts` by hand — it is regenerated by the build per `docs/development/api-client-generation.md`; confirm post-build that the diff (if any) is empty for this type.

### Data Flow
Unchanged. `GetLeafletDocumentsHandler` and `UploadLeafletHandler` both construct `LeafletDocumentSummary` instances exactly as before; only the compile-time namespace they resolve the type from changes. No runtime behavior, serialization, or persistence path is touched.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A consumer of `LeafletDocumentSummary` is missed, causing a build break | Low | The repo-wide search above found exactly 4 backend source/test files (plus the auto-generated frontend client, which doesn't need manual editing); `dotnet build` will immediately surface any missed `using` |
| OpenAPI/TypeScript client regeneration produces an unexpected diff | Low | Class name and members are unchanged, namespace is not part of the OpenAPI schema key; verify with a build + client regen and confirm no diff for `LeafletDocumentSummary` per FR-3 |
| Scope creep into unrelated Leaflet DTOs during the refactor | Low | Spec and this review both explicitly restrict the change to `LeafletDocumentSummary` only (see Out of Scope in spec) |

## Specification Amendments
None — the spec (`spec.r1.md`) already fully and correctly scopes this change; no additions or corrections are needed.

## Prerequisites
None. No migrations, config, or infrastructure changes are required before implementation can start; the target `Contracts/` folder already exists in the module.
