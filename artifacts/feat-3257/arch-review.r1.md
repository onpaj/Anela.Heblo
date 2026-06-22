# Architecture Review: Move DocumentSummary DTO to KnowledgeBase Contracts

## Skip Design: false

## Architectural Fit Assessment

The finding is correct and the fix is straightforward. `DocumentSummary` is a shared DTO that is consumed by two separate use cases (`GetDocuments` and `UploadDocument`) but is defined inside the `GetDocuments` use-case folder. This directly violates the documented rule in `filesystem.md`:

> **Features/{Feature}/Contracts/**: Shared DTOs across use cases

and in `development_guidelines.md`:

> DTO objects for API (Request, Response) live in `contracts/` of the specific module

The violation is mechanical — no logic is misplaced, no business rule is at the wrong layer. The only problem is namespace topology: `UploadDocumentResponse` and `UploadDocumentHandler` both carry a `using` directive pointing at `...UseCases.GetDocuments`, meaning `UploadDocument` has an implicit compile-time dependency on an unrelated use-case namespace.

The fix is a pure file move with namespace update. Zero logic changes.

**Scope boundary confirmed:** A grep across the entire backend confirms that `DocumentSummary` (without the `Leaflet` prefix) is referenced only in four files:
- `GetDocuments/GetDocumentsRequest.cs` (definition + usage in response)
- `GetDocuments/GetDocumentsHandler.cs` (usage)
- `UploadDocument/UploadDocumentResponse.cs` (usage)
- `UploadDocument/UploadDocumentHandler.cs` (usage)
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs` (usage in test assertions)

The test file is a fifth consumer that the spec does not mention. It imports `DocumentSummary` via `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;` and will break at compile time if that `using` is removed without an update.

**Parallel finding (out of scope for this task):** The `Leaflet` module has the identical structural problem — `LeafletDocumentSummary` is defined inside `GetLeafletDocuments/GetLeafletDocumentsRequest.cs` and consumed by `UploadLeaflet`. This is not in scope here and should be filed as a separate arch-review finding.

## Proposed Architecture

### Component Overview

After the move, the KnowledgeBase module will have its canonical `Contracts/` directory, consistent with every other complex feature module in the codebase (Analytics, Article, BackgroundJobs, Bank, Catalog, CarrierCooling, DataQuality, Dashboard, etc.):

```
Features/KnowledgeBase/
├── Contracts/
│   └── DocumentSummary.cs          <-- new canonical location
├── UseCases/
│   ├── GetDocuments/
│   │   ├── GetDocumentsHandler.cs  <-- update using directive
│   │   └── GetDocumentsRequest.cs  <-- remove class definition, add using
│   └── UploadDocument/
│       ├── UploadDocumentHandler.cs   <-- replace using directive
│       └── UploadDocumentResponse.cs  <-- replace using directive
└── ...
```

The test file also requires a using directive update:

```
test/Anela.Heblo.Tests/KnowledgeBase/Controllers/
└── KnowledgeBaseControllerTests.cs  <-- add/replace using directive
```

### Key Design Decisions

#### Decision 1: Single file per DTO in Contracts/

**Options considered:**
- (A) One file per DTO class, e.g. `DocumentSummary.cs`
- (B) Group multiple DTOs into a single file, e.g. `KnowledgeBaseContracts.cs`

**Chosen approach:** Option A — one file, one class, `DocumentSummary.cs`.

**Rationale:** Every existing `Contracts/` file in this codebase follows the one-class-one-file convention (e.g. `Analytics/Contracts/TopProductDto.cs`, `Bank/Contracts/BankAccountDto.cs`). Deviating would be inconsistent and makes future moves harder.

#### Decision 2: Class, not record

**Options considered:**
- (A) Keep as `public class`
- (B) Convert to `public record`

**Chosen approach:** Option A — keep as `public class`.

**Rationale:** The project-level CLAUDE.md rule is unambiguous: "DTOs are classes, never C# records. OpenAPI client generators mishandle record parameter order." No deviation is warranted for a pure move.

#### Decision 3: Namespace

**Options considered:**
- (A) `Anela.Heblo.Application.Features.KnowledgeBase.Contracts`
- (B) Keep the existing `...UseCases.GetDocuments` namespace on the moved file

**Chosen approach:** Option A — the namespace must match the new file's directory.

**Rationale:** The namespace must reflect the canonical location. Using the old namespace on a file in a new directory would preserve the broken coupling that this task is fixing.

## Implementation Guidance

### Directory / Module Structure

Create directory: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/`

Create file: `DocumentSummary.cs`

### Interfaces and Contracts

`DocumentSummary` is a plain DTO with no interface. Its definition must be identical to the current one — property names, types, and default values unchanged. This is enforced by the existing test assertions in `KnowledgeBaseControllerTests`.

```csharp
// backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs
namespace Anela.Heblo.Application.Features.KnowledgeBase.Contracts;

public class DocumentSummary
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
    public Guid? FirstChunkId { get; set; }
}
```

### Data Flow

No data flow changes. The DTO is already used as a read projection in both use cases. This is a namespace topology fix only.

### Files to change (exhaustive list)

1. **Create** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs`
   - New file with namespace `Anela.Heblo.Application.Features.KnowledgeBase.Contracts`
   - Class body copied verbatim from lines 26–35 of `GetDocumentsRequest.cs`

2. **Edit** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsRequest.cs`
   - Delete lines 26–35 (the `DocumentSummary` class definition)
   - Add `using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;`

3. **Edit** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsHandler.cs`
   - Add `using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;`

4. **Edit** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentResponse.cs`
   - Replace `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;`
   - With `using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;`

5. **Edit** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`
   - Replace `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;`
   - With `using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;`

6. **Edit** `backend/test/Anela.Heblo.Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs`
   - Replace `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;` (the one that was pulling in `DocumentSummary`) with `using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;`
   - Note: the file also imports `...UseCases.GetDocuments` for `GetDocumentsRequest` and `GetDocumentsResponse` — that import must be retained. Only the import that brought in `DocumentSummary` needs to change. In practice a single `using` directive currently covers all three types; splitting it into two directives (one for Contracts, one for GetDocuments) is the correct outcome.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Test file not updated, breaking the build | High | Step 6 above is mandatory; `dotnet build` will catch this immediately |
| Leaflet module left with the same structural violation | Low | Out of scope — file a separate arch-review finding for `LeafletDocumentSummary` |
| Accidentally changing property types or defaults during the move | Low | Copy verbatim; verify with `dotnet build` — any divergence causes a test failure |
| Future use cases in other modules referencing `DocumentSummary` across module boundaries | Low | `DocumentSummary` is a KnowledgeBase-internal DTO; cross-module access would violate module boundary rules and be caught by the architecture tests in `ModuleBoundariesTests.cs` |

## Specification Amendments

The spec (FR-3 through FR-6) omits the test file `KnowledgeBaseControllerTests.cs`. That file has a `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;` directive that currently resolves `DocumentSummary`. After the class is removed from that namespace, the file will fail to compile unless updated.

**Amendment:** Add FR-7: Update `using` directives in `KnowledgeBaseControllerTests.cs` — add `using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;` and retain the existing `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;` for the request/response types.

## Prerequisites

None. The `KnowledgeBase/Contracts/` directory does not yet exist and must be created as part of this task. No migrations, no DI changes, no API changes required.
