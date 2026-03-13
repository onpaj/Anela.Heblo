# Knowledge Base — Master Implementation Plan

**Date**: 2026-03-08
**Status**: Ready for implementation
**Source documents**:
- `docs/features/knowledge-base-rag.md` — full feature spec
- `docs/plans/2026-03-07-knowledge-base-bug-fixes.md` — bug fix details
- `docs/plans/2026-03-07-knowledge-base-missing-features.md` — missing feature details

## Summary

26 tasks in dependency order. Each task is independently implementable. Complete in order within each phase; phases within the same group can be parallelized.

---

## Phase 1 — Critical Bug Fixes (must ship before production use)

### Task 1 — Fix GraphOneDriveService: wrong Graph endpoint for app-only token

**Why**: All three OneDrive methods use `/me/drive/...` which requires a delegated token. `GetAccessTokenForAppAsync` returns an app-only token → HTTP 403 in production. Nothing works until this is fixed.

**Files**:
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` — `OneDriveUserId` property already present
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs` — already uses `/users/{userId}/drive/` URLs
- `backend/src/Anela.Heblo.API/appsettings.json` — add `"OneDriveUserId": ""` to KnowledgeBase section

**Done when**: All three Graph API URLs use `/users/{userId}/drive/` prefix; `appsettings.json` has the config key (empty, set via env var in prod).

---

### Task 2 — Fix KnowledgeBaseRepository: double-open of DB connection

**Why**: `AddChunksAsync` and `SearchSimilarAsync` call `connection.OpenAsync()` unconditionally. If EF Core has already opened the connection in the same scope, this throws `InvalidOperationException`.

**File**: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs`

**Change**: Both methods already have the `if (connection.State != System.Data.ConnectionState.Open)` guard — verify it is applied in both places.

**Done when**: Both `AddChunksAsync` and `SearchSimilarAsync` guard the `OpenAsync` call with a state check.

---

## Phase 2 — High Priority Bug Fixes

### Task 3 — Fix N+1 queries in SearchSimilarAsync

**Why**: Current code issues a separate `FindAsync` per chunk after the vector search, causing N+1 DB round-trips for every search request.

**File**: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs`

**Change**: Rewrite `SearchSimilarAsync` to JOIN `KnowledgeBaseDocuments` inline in the raw SQL query and read all fields in a single round-trip. The query already exists in the bug-fix plan.

**Done when**: Single SQL query per `SearchSimilarAsync` call; `Chunk.Document.Filename` and `SourcePath` correctly populated from the JOIN.

---

### Task 4 — Add GetDocumentsHandler (fix MediatR bypass in controller)

**Why**: `KnowledgeBaseController.GetDocuments` currently injects `IKnowledgeBaseRepository` directly, bypassing the MediatR pattern used everywhere else.

**Files to create**:
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsHandler.cs`

**Files to modify**:
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` — remove `IKnowledgeBaseRepository` injection, use `_mediator.Send(new GetDocumentsRequest(), ct)`
- `backend/test/Anela.Heblo.Tests/MCP/Tools/` — add `GetDocumentsHandlerTests.cs`

**Done when**: Controller has no direct repository injection; `GET /api/knowledgebase/documents` still works via MediatR.

---

### Task 5 — Verify/fix AnthropicClaudeService SDK compatibility

**Why**: The code uses `new AnthropicApi()`, `api.AuthorizeUsingApiKey()`, and `response.Content.Value2` (OneOf pattern). These need to match the actual `Anthropic` v1.0.0 NuGet package API.

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/AnthropicClaudeService.cs`

**Action**: Run `dotnet build` and check for compilation errors in this service. If the `Anthropic` package API differs from what's coded, replace with the correct calls. Reference the package source at `Anthropic` v1.0.0.

**Done when**: `dotnet build` passes with 0 errors; `AskQuestionHandlerTests` pass.

---

## Phase 3 — Medium Priority Bug Fixes

These tasks have no inter-dependencies and can be done in any order.

### Task 6 — Fix namespace in KnowledgeBaseIngestionJob

**Why**: File is in `.../Application/Features/KnowledgeBase/Jobs/` but declares namespace `...Infrastructure.Jobs` (extra segment).

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Jobs/KnowledgeBaseIngestionJob.cs`

**Change**: `namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.Jobs;` → `namespace Anela.Heblo.Application.Features.KnowledgeBase.Jobs;`

**Done when**: `dotnet build` and `dotnet format` pass with 0 errors.

---

### Task 7 — Fix appsettings.json OpenAI placeholder values

**Why**: `"ApiKey": "xxxxxx"` and `"Organization": "xxxxxx"` are placeholder strings that look like committed credentials.

**File**: `backend/src/Anela.Heblo.API/appsettings.json`

**Change**: Replace `"xxxxxx"` values with `""` in the `OpenAI` section.

**Done when**: No placeholder strings remain in committed config files.

---

### Task 8 — Add input validation to request DTOs

**Why**: `SearchDocumentsRequest.Query` and `AskQuestionRequest.Question` accept empty strings; `TopK` accepts 0 or negative values.

**Files**:
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionRequest.cs`

**Change**: Add `[Required, MinLength(1), MaxLength(2000)]` on `Query`/`Question`; `[Range(1, 20)]` on `TopK`. Both already have these attributes — verify they are present and correct.

**Done when**: `POST /api/knowledgebase/search` with empty body returns HTTP 400.

---

### Task 9 — Handle SourcePath conflict in ingestion job

**Why**: If a file at the same OneDrive path is replaced with different content (new hash), the `INSERT` fails on the UNIQUE constraint for `SourcePath`.

**Files**:
- `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs` — add `GetDocumentBySourcePathAsync` and `DeleteDocumentAsync` if not already present
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` — implement both methods
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Jobs/KnowledgeBaseIngestionJob.cs` — add path-conflict check after hash-check (already present per code review — verify)

**Done when**: Re-uploading a changed file at the same path succeeds; old document and chunks are deleted before re-indexing.

---

### Task 10 — Migrate AnthropicClaudeService to IOptions

**Why**: Service reads model/tokens config via `IConfiguration["KnowledgeBase:*"]` directly instead of the typed `IOptions<KnowledgeBaseOptions>`.

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/AnthropicClaudeService.cs`

**Change**: Remove `IConfiguration` reads for `ClaudeModel` / `ClaudeMaxTokens`; use `IOptions<KnowledgeBaseOptions>` (already injected). Keep `IConfiguration` only for `Anthropic:ApiKey` secret.

**Done when**: No `_configuration["KnowledgeBase:*"]` reads remain; unit tests pass.

---

### Task 11 — Fix OpenAiEmbeddingService: create EmbeddingClient once in constructor

**Why**: Current code (if it creates a new `EmbeddingClient` per call) wastes allocations. The corrected version in the bug-fix plan creates it once in the constructor.

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/OpenAiEmbeddingService.cs`

**Change**: Verify `EmbeddingClient` is created in constructor using `IOptions<KnowledgeBaseOptions>.Value.EmbeddingModel` — not per-call.

**Done when**: `EmbeddingClient` created once per service instance; `dotnet build` passes.

---

### Task 12 — Fix GraphOneDriveService: replace per-call HttpClient with IHttpClientFactory

**Why**: If `CreateHttpClient()` instantiates a `new HttpClient()` per call, this risks socket exhaustion under load.

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs`

**Change**: Verify `IHttpClientFactory` is injected and used (`_httpClientFactory.CreateClient("MicrosoftGraph")`). Authorization header should be set per-request on `HttpRequestMessage`, not on `DefaultRequestHeaders`. Named client `"MicrosoftGraph"` must be registered in `KnowledgeBaseModule.cs`.

**Done when**: No `new HttpClient()` in `GraphOneDriveService`; `dotnet build` passes.

---

## Phase 4 — Missing Feature: Document Delete

### Task 13 — Add DeleteDocument use case and API endpoint

**Why**: There is no way to remove a document from the knowledge base. Cascade delete is already configured in DB.

**Files to create**:
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/DeleteDocument/DeleteDocumentRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/DeleteDocument/DeleteDocumentHandler.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/DeleteDocumentHandlerTests.cs`

**Files to modify**:
- `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs` — ensure `DeleteDocumentAsync` is declared (may already be added in Task 9)
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` — ensure implementation exists
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` — add `DELETE /api/knowledgebase/documents/{id}` → 204 No Content

**Done when**: `DELETE /api/knowledgebase/documents/{id}` returns 204; DB record and all chunks are removed.

---

## Phase 5 — Missing Feature: Additional Document Formats

### Task 14 — Refactor IDocumentTextExtractor DI to support multiple extractors

**Why**: Currently a single `IDocumentTextExtractor` is registered. To support multiple formats, `IndexDocumentHandler` needs to resolve by content type from a list.

**Files to modify**:
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — register multiple `IDocumentTextExtractor` implementations
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` — inject `IEnumerable<IDocumentTextExtractor>`; use `.FirstOrDefault(e => e.CanHandle(contentType))`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs` — update constructor to pass `new[] { _extractor.Object }`

**Done when**: Existing PDF ingestion still works; `dotnet test` green.

---

### Task 15 — Add WordDocumentExtractor (.docx support)

**Depends on**: Task 14

**Files**:
- Add `DocumentFormat.OpenXml` NuGet to `Anela.Heblo.Application.csproj`
- Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/WordDocumentExtractor.cs`
- Register in `KnowledgeBaseModule.cs`

**Done when**: `.docx` files ingested without error; text extracted correctly.

---

### Task 16 — Add PlainTextExtractor (.txt / .md support)

**Depends on**: Task 14

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/PlainTextExtractor.cs`

Handles `text/*` and `application/markdown` content types. UTF-8 decode only, no parsing needed.

**Done when**: `.txt` and `.md` files ingested without error.

---

## Phase 6 — Missing Feature: Resilience (Retry Logic)

### Task 17 — Add Polly retry to OpenAiEmbeddingService

**Depends on**: Task 11

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/OpenAiEmbeddingService.cs`

**Change**: Wrap `_client.GenerateEmbeddingAsync(...)` call in a `ResiliencePipeline` with 3 retries, exponential backoff (2s base), handling `HttpRequestException`. Use `Polly` (already available in project).

**Done when**: `dotnet build` passes; unit tests pass (pipeline is transparent to mocks).

---

### Task 18 — Add Polly retry to AnthropicClaudeService

**Depends on**: Task 10, Task 5

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/AnthropicClaudeService.cs`

**Change**: Same pattern as Task 17 — wrap `api.CreateMessageAsync(...)` in a `ResiliencePipeline`.

**Done when**: `dotnet build` passes; unit tests pass.

---

## Phase 7 — Missing Feature: Frontend UI

### Task 19 — Regenerate TypeScript API client

**Why**: `KnowledgeBaseController` was added but the generated `api-client.ts` has not been regenerated.

**Commands**:
```bash
cd backend && dotnet build
cd frontend && npm run generate-api-client
```

**Done when**: `frontend/src/api/generated/api-client.ts` contains `knowledgeBase_GetDocuments`, `knowledgeBase_Search`, `knowledgeBase_Ask`.

---

### Task 20 — Create API hooks (useKnowledgeBase.ts)

**Depends on**: Task 19, Task 4 (GetDocumentsHandler must be in place)

**New file**: `frontend/src/api/hooks/useKnowledgeBase.ts`

Hooks to create:
- `useKnowledgeBaseDocumentsQuery()` — GET `/api/knowledgebase/documents`
- `useKnowledgeBaseSearchMutation()` — POST `/api/knowledgebase/search`
- `useKnowledgeBaseAskMutation()` — POST `/api/knowledgebase/ask`
- `useDeleteKnowledgeBaseDocumentMutation()` — DELETE `/api/knowledgebase/documents/{id}` (for Task 22)

All hooks must use absolute URLs (`${(apiClient as any).baseUrl}${relativeUrl}`).

**Done when**: `npm run build` passes; hooks exist and are typed correctly.

---

### Task 21 — Create KnowledgeBase page and tab components

**Depends on**: Task 20

**New files**:
- `frontend/src/pages/KnowledgeBasePage.tsx` — page with header + 3 tabs (Dokumenty / Vyhledávání / Dotaz AI)
- `frontend/src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx` — table with status badges, loading skeleton, empty state
- `frontend/src/components/knowledge-base/KnowledgeBaseSearchTab.tsx` — search input + results list with score badges
- `frontend/src/components/knowledge-base/KnowledgeBaseAskTab.tsx` — question textarea + prose answer box + sources accordion

Status badge colors: `indexed` → green, `processing` → yellow, `failed` → red.

**Done when**: `npm run build` passes; all three tabs render without errors.

---

### Task 22 — Add Delete button to Documents tab

**Depends on**: Task 13 (backend endpoint), Task 21 (Documents tab), Task 20 (delete hook)

**File**: `frontend/src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx`

**Change**: Add delete icon button per row; show confirmation dialog before calling `useDeleteKnowledgeBaseDocumentMutation`; on success invalidate `knowledgeBaseKeys.documents`.

**Done when**: Delete button appears; confirmation dialog shown; document removed from list after deletion.

---

### Task 23 — Register route and add navigation item

**Depends on**: Task 21

**Files**:
- `frontend/src/App.tsx` — add `<Route path="/knowledge-base" element={<KnowledgeBasePage />} />`
- `frontend/src/components/Layout/Sidebar.tsx` — add nav item `{ id: "knowledge-base", name: "Znalostní báze", href: "/knowledge-base" }` in the appropriate section (e.g. Automatizace or a new section)

**Done when**: `/knowledge-base` route renders; nav item appears in sidebar.

---

### Task 24 — Write unit tests for API hooks

**Depends on**: Task 20

**New file**: `frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts`

Test cases: documents query returns correct shape; search mutation sends correct POST body; ask mutation returns answer + sources.

**Done when**: `npm test -- useKnowledgeBase` passes.

---

## Phase 8 — Missing Feature: Integration Tests

### Task 25 — Add Testcontainers integration tests for KnowledgeBaseRepository

**New file**: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs`

Uses `Testcontainers.PostgreSql` (`pgvector/pgvector:pg16` image). Test cases:
- Add document + chunks → retrieve by hash
- Search returns closest chunk by cosine similarity
- Delete document cascades to chunks

Add `<PackageReference Include="Testcontainers.PostgreSql" Version="3.10.0" />` to test project.

**Done when**: `dotnet test --filter Integration` passes.

---

### Task 26 — Add DocumentChunker edge case tests

**Depends on**: nothing

**File**: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentChunkerTests.cs`

Add cases: Czech diacritics, very long document (10,000 words), exactly chunk-size words, exactly chunk-size + 1 words.

**Done when**: `dotnet test --filter DocumentChunker` passes.

---

## Dependency Graph

```
Task 1  (Graph endpoint fix)         — no dependencies
Task 2  (connection guard)           — no dependencies
Task 3  (N+1 queries)                — no dependencies
Task 4  (GetDocumentsHandler)        — no dependencies
Task 5  (Anthropic SDK verify)       — no dependencies
Task 6  (namespace fix)              — no dependencies
Task 7  (appsettings placeholder)    — no dependencies
Task 8  (input validation)           — no dependencies
Task 9  (SourcePath conflict)        — no dependencies
Task 10 (AnthropicClaudeService IOptions) — no dependencies
Task 11 (EmbeddingClient lifetime)   — no dependencies
Task 12 (IHttpClientFactory)         — no dependencies
Task 13 (DeleteDocument)             — no dependencies (builds on Task 9 repo methods)
Task 14 (multi-extractor DI)         — no dependencies
Task 15 (WordDocumentExtractor)      — Task 14
Task 16 (PlainTextExtractor)         — Task 14
Task 17 (OpenAI retry)               — Task 11
Task 18 (Anthropic retry)            — Task 10, Task 5
Task 19 (regenerate TS client)       — no dependencies (best after Task 4 is in)
Task 20 (API hooks)                  — Task 19
Task 21 (page + tab components)      — Task 20
Task 22 (delete button)              — Task 13, Task 21
Task 23 (route + nav)                — Task 21
Task 24 (hook unit tests)            — Task 20
Task 25 (integration tests)          — no dependencies
Task 26 (chunker edge cases)         — no dependencies
```

## Recommended Implementation Order

Fastest path to a working, production-safe system:

1. Tasks 1–5 (critical + high bugs — unblock production use)
2. Tasks 6–12 (medium bugs — cleanup)
3. Task 13 (delete endpoint — small, unblocks frontend)
4. Task 19 → 20 → 21 → 22 → 23 (frontend UI — visible user value)
5. Task 24 (hook tests)
6. Tasks 14 → 15 → 16 (additional formats)
7. Tasks 17–18 (resilience)
8. Tasks 25–26 (integration tests)
