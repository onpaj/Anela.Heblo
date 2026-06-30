# Specification: Move DocumentSummary DTO to KnowledgeBase Contracts

## Summary

`DocumentSummary` is a shared DTO currently defined inside the `GetDocuments` use-case folder, but it is consumed by both `GetDocuments` and `UploadDocument`. This creates an illegitimate cross-use-case namespace dependency. This task moves `DocumentSummary` to the canonical `KnowledgeBase/Contracts/` directory and updates all references — no logic changes.

## Background

`filesystem.md` specifies that shared DTOs across use cases belong in `Features/{Feature}/Contracts/`. `development_guidelines.md` reinforces that DTOs live in the `contracts/` folder of the specific module. Currently the `KnowledgeBase` module has no `Contracts/` directory at all.

`DocumentSummary` is defined in `GetDocumentsRequest.cs` (namespace `...UseCases.GetDocuments`) but is imported and used by `UploadDocumentResponse.cs` and `UploadDocumentHandler.cs` via a `using` directive pointing at the `GetDocuments` namespace. This means any future rename or refactor of `GetDocuments` would silently break `UploadDocument`, and the dependency is invisible from the use-case boundary.

## Functional Requirements

### FR-1: Create the KnowledgeBase Contracts directory and move DocumentSummary

Create the file `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs`.

The class definition must be identical to the current one — a `public class` (not a record) with the following properties:

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `Filename` | `string` (default `string.Empty`) |
| `Status` | `string` (default `string.Empty`) |
| `ContentType` | `string` (default `string.Empty`) |
| `CreatedAt` | `DateTime` |
| `IndexedAt` | `DateTime?` |
| `FirstChunkId` | `Guid?` |

The namespace must be `Anela.Heblo.Application.Features.KnowledgeBase.Contracts`.

**Acceptance criteria:**
- File `Contracts/DocumentSummary.cs` exists with the correct namespace.
- The class is declared as `public class DocumentSummary` (not a record).
- All seven properties are present with their original types and default values.

### FR-2: Remove DocumentSummary from GetDocumentsRequest.cs

Delete lines 26–35 (the `DocumentSummary` class definition) from `GetDocumentsRequest.cs`. The file retains `GetDocumentsRequest` and `GetDocumentsResponse`.

**Acceptance criteria:**
- `DocumentSummary` is no longer declared in `GetDocumentsRequest.cs`.
- `GetDocumentsRequest` and `GetDocumentsResponse` remain intact and correct.

### FR-3: Update using directives in GetDocumentsRequest.cs

Add `using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;` to `GetDocumentsRequest.cs` so that `GetDocumentsResponse.Documents` (type `List<DocumentSummary>`) resolves from the new location.

**Acceptance criteria:**
- `GetDocumentsRequest.cs` compiles without errors after the move.
- No reference to the old `DocumentSummary` declaration site remains.

### FR-4: Update using directives in GetDocumentsHandler.cs

`GetDocumentsHandler.cs` constructs `DocumentSummary` instances inline (lines 48–57). It currently resolves `DocumentSummary` via its own namespace (`...UseCases.GetDocuments`), so after FR-2 it will break. Add `using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;` to `GetDocumentsHandler.cs`.

**Acceptance criteria:**
- `GetDocumentsHandler.cs` compiles without errors after the move.
- The `new DocumentSummary { ... }` construction in `Handle()` remains unchanged in logic.

### FR-5: Update using directives in UploadDocumentResponse.cs

Replace:
```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
```
with:
```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;
```

**Acceptance criteria:**
- `UploadDocumentResponse.cs` no longer references the `GetDocuments` namespace.
- `DocumentSummary? Document` property continues to compile correctly.

### FR-6: Update using directives in UploadDocumentHandler.cs

Replace:
```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
```
with:
```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;
```

**Acceptance criteria:**
- `UploadDocumentHandler.cs` no longer references the `GetDocuments` namespace.
- The `MapToSummary()` method return type (`DocumentSummary`) and its body remain unchanged in logic.

## Non-Functional Requirements

### NFR-1: Performance

No runtime performance impact. This is a compile-time namespace/file restructuring only.

### NFR-2: Security

No security impact. No data exposure changes.

### NFR-3: Correctness

The move must be purely mechanical — zero changes to property names, types, default values, or method logic. `dotnet build` must succeed with no warnings introduced by this change.

### NFR-4: OpenAPI / Client Generation

`DocumentSummary` is exposed in the API surface (returned by both `GetDocuments` and `UploadDocument` endpoints). Moving its namespace does not change its schema shape, so the generated TypeScript client is unaffected. Verify by confirming the OpenAPI document still emits `DocumentSummary` schema with all seven fields after the build.

## Data Model

No new entities. `DocumentSummary` is an existing read-model DTO with the shape described in FR-1.

## API / Interface Design

No API changes. The two affected endpoints remain:

- `GET /api/knowledge-base/documents` — response type `GetDocumentsResponse` (contains `List<DocumentSummary>`)
- `POST /api/knowledge-base/documents` — response type `UploadDocumentResponse` (contains `DocumentSummary?`)

Both continue to serialize identically after the move.

## Dependencies

- No external service dependencies.
- Requires `dotnet build` to pass after changes (validation gate per CLAUDE.md).
- Requires `dotnet format` to pass after changes.

## Out of Scope

- Changes to any other use case or handler not listed above.
- Changes to domain entities, repository interfaces, or infrastructure.
- Renaming `DocumentSummary` or altering its properties.
- Adding new shared DTOs to `Contracts/`.
- Frontend / TypeScript changes (OpenAPI schema shape is unchanged).
- Database migrations.

## Open Questions

None.

## Status: COMPLETE
