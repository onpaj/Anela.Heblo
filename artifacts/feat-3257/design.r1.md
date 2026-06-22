# Design: Move DocumentSummary DTO to KnowledgeBase Contracts

## Component Boundaries

### DocumentSummary (Contracts)
**Responsibility:** Shared DTO representing a summary view of a knowledge-base document, consumed by both the GetDocuments and UploadDocument use cases.
**Inputs:** N/A — passive data container.
**Outputs:** N/A — passive data container.

### KnowledgeBase/Contracts/ (new directory)
**Responsibility:** Canonical location for DTOs shared across two or more use cases within the KnowledgeBase feature module, per `filesystem.md` and `development_guidelines.md`.
**Inputs:** N/A.
**Outputs:** Types imported by use-case handlers, request/response objects, and test files within the KnowledgeBase module.

### GetDocuments use case
**Responsibility:** Unchanged. Continues to reference `DocumentSummary` via the Contracts namespace instead of defining it locally.
**Inputs:** No change.
**Outputs:** No change.

### UploadDocument use case
**Responsibility:** Unchanged. Replaces its cross-use-case `using` on the GetDocuments namespace with the Contracts namespace.
**Inputs:** No change.
**Outputs:** No change.

## Data Schema

### DocumentSummary
Identical to the class currently defined in `GetDocumentsRequest.cs`. No fields, types, or constraints change — only the namespace and file location move.

**New namespace:** `Anela.Heblo.Application.Features.KnowledgeBase.Contracts`
**New file:** `src/Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs`

## API Shape

No API changes. This refactor is entirely internal to the application layer. No endpoints are added, removed, or modified. No request or response shapes change.
