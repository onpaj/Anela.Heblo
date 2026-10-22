### task: regenerate-client-update-hooks

**Goal:** Regenerate the TS client and update frontend hooks. Depends on both tasks above.

**Files (under `frontend/`):**
- `src/api/generated/api-client.ts` (tracked; regenerate per `docs/development/api-client-generation.md`; if the generator cannot run, hand-edit: classes `UploadDocumentResponse` -> `CatalogDocumentsUploadDocumentResponse`, `UploadDocumentResponse2` -> `KnowledgeBaseUploadDocumentResponse`, plus their `I...` interfaces, `fromJS` and method signatures)
- `src/api/hooks/useKnowledgeBase.ts` (lines ~14, ~249)
- `src/api/hooks/useCatalogDocuments.ts` (lines ~6, ~71, ~95)

**Steps:**
- [ ] Regenerate client; review diff contains only rename-related changes.
- [ ] Update hook imports and type annotations.
- [ ] `grep -rn "UploadDocumentResponse" frontend/src` shows no un-prefixed or `2` names (excluding `.artifacts` dirs).
- [ ] `npm run build` and `npm run lint` pass; run related jest tests (`npm test -- useKnowledgeBase useCatalogDocuments`).
- [ ] Commit: `refactor(frontend): use module-scoped upload response types (#4437)`
