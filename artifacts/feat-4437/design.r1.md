# Design: Disambiguate UploadDocumentResponse DTO names

## Component Design
Rename-only change; no new components.
- `KnowledgeBaseUploadDocumentResponse : BaseResponse` with `DocumentSummary? Document` (namespace `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument`).
- `CatalogDocumentsUploadDocumentResponse : BaseResponse` with `string UploadedFilename`, parameterless ctor and `(ErrorCodes, Dictionary<string,string>?)` ctor (namespace `Anela.Heblo.Application.Features.CatalogDocuments.Contracts`).
- Requests, handlers, controllers reference the new names. Frontend hooks import `KnowledgeBaseUploadDocumentResponse` / `CatalogDocumentsUploadDocumentResponse` from the generated client.

## Data Schemas
JSON payloads unchanged:
- KB: `{ success, errorCode, params, document }`
- Catalog: `{ success, errorCode, params, uploadedFilename }`
Only OpenAPI schema names change.
