# Implementation: move-documentsummary-to-contracts

## What was implemented
Moved the `DocumentSummary` DTO class from `GetDocumentsRequest.cs` (where it was co-located with the request/response types) into a dedicated `Contracts/DocumentSummary.cs` file under the `KnowledgeBase.Contracts` namespace. All files that previously referenced `DocumentSummary` via the `GetDocuments` namespace have been updated to use the new `Contracts` namespace. No logic was changed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs` — NEW: canonical home for the `DocumentSummary` class (`public class`, not record, per project rules)
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsRequest.cs` — removed inline `DocumentSummary` class definition; added `using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsHandler.cs` — added `using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentResponse.cs` — replaced `using ...GetDocuments;` with `using ...Contracts;`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs` — replaced `using ...GetDocuments;` with `using ...Contracts;`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs` — added `using ...Contracts;`; kept existing `using ...GetDocuments;` (still needed for `GetDocumentsRequest`/`GetDocumentsResponse`)

## Tests
`dotnet build` completed with 0 errors, 254 warnings (all pre-existing, none related to this change). `dotnet format` ran clean.

## How to verify
1. `cd` to worktree root and run `dotnet build` — expect 0 errors.
2. Confirm `DocumentSummary` no longer exists in `GetDocumentsRequest.cs`.
3. Confirm `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs` exists and declares `public class DocumentSummary`.

## Notes
No deviations. The `Contracts` directory was created as it did not previously exist. The `DocumentSummary` class uses `public class` (not `public record`) as required by the project DTO rule.

## PR Summary
Pure mechanical refactor: moves the `DocumentSummary` DTO out of the `GetDocuments` use-case folder into a shared `KnowledgeBase.Contracts` namespace. This makes the type available to any future use-case without taking a cross-use-case dependency.

### Changes
- `Contracts/DocumentSummary.cs` — new file, canonical DTO location
- `GetDocumentsRequest.cs` — removed inline class, added contracts using
- `GetDocumentsHandler.cs` — added contracts using
- `UploadDocumentResponse.cs` — updated using to contracts namespace
- `UploadDocumentHandler.cs` — updated using to contracts namespace
- `KnowledgeBaseControllerTests.cs` — added contracts using (kept GetDocuments using)

## Status
DONE
