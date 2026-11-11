# Implementation: regenerate-client-update-hooks

## What was implemented
NSwag could not complete in this environment (tool restored, but generation exited 255), so the generated client was hand-edited per the task fallback: `UploadDocumentResponse` -> `CatalogDocumentsUploadDocumentResponse`, `UploadDocumentResponse2` -> `KnowledgeBaseUploadDocumentResponse`, including `I...` interfaces, `fromJS`, and method signatures. Hook imports/annotations updated.

## Files created/modified
- `frontend/src/api/generated/api-client.ts` - renames only
- `frontend/src/api/hooks/useKnowledgeBase.ts` - import + return type
- `frontend/src/api/hooks/useCatalogDocuments.ts` - import + two return types

## Tests
`CI=true npm test -- useKnowledgeBase useCatalogDocuments`: 2 suites, 22 tests pass.

## How to verify
grep for UploadDocumentResponse in frontend/src (excluding .artifacts) shows only prefixed names; eslint on the hooks is clean; tsc reports no errors under src (node_modules typings errors are environmental).

## Notes
Full `npm run build` not run (prebuild regenerates client and nswag fails here); tsc and eslint used instead.

## PR Summary
Frontend now uses the module-scoped upload response type names matching the renamed backend DTOs.

### Changes
- `frontend/src/api/generated/api-client.ts` - renamed upload response classes/interfaces
- `frontend/src/api/hooks/useKnowledgeBase.ts` - updated type
- `frontend/src/api/hooks/useCatalogDocuments.ts` - updated type

## Status
DONE_WITH_CONCERNS
