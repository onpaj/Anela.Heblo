# Specification: Type-Safe ArticleStatus in Response DTOs

## Summary
Two Article module response DTOs (`GetArticleResponse` and `ArticleListItemDto`) declare their `Status` property as `string` instead of the `ArticleStatus` enum, causing the generated TypeScript client to emit `status?: string` for these responses while `GenerateArticleResponse` correctly emits `status?: ArticleStatus`. This forces unsafe `as ArticleStatus` casts in the frontend. The fix aligns all three DTOs on the enum type, removes the unsafe casts, and restores end-to-end type safety.

## Background
The Article module exposes three response contracts that carry the article status:

- `GenerateArticleResponse.Status` — typed as `ArticleStatus` (correct).
- `GetArticleResponse.Status` — typed as `string`, populated via `article.Status.ToString()`.
- `ArticleListItemDto.Status` — typed as `string`, populated via `a.Status.ToString()`.

The global `JsonStringEnumConverter` registered in `backend/src/Anela.Heblo.API/Program.cs:121` already serializes `ArticleStatus` values to the same wire format (`"Generated"`, `"Queued"`, etc.), so the JSON payload is identical either way. The discrepancy is only visible in the generated client:

- `frontend/src/api/generated/api-client.ts:12745` — `GenerateArticleResponse.status?: ArticleStatus` ✅
- `frontend/src/api/generated/api-client.ts:12872` — `GetArticleResponse.status?: string` ❌
- `frontend/src/api/generated/api-client.ts:13210` — `ArticleListItemDto.status?: string` ❌

To compensate, `frontend/src/api/hooks/useArticles.ts` performs unchecked casts:

- Line 162 (`useGetArticleQuery`): `status: (response.status as ArticleStatus) ?? ArticleStatus.Queued`
- Line 139 (`useListArticlesQuery`): `status: (item.status as ArticleStatus) ?? ArticleStatus.Queued`

These casts bypass TypeScript's type checking. Polling logic such as `IN_PROGRESS_STATUSES.has(status)` relies on `status` carrying the exact enum string value — any future backend serialization drift would silently break polling with no compile-time signal. The inconsistency across three DTOs also forces readers to verify each contract individually.

## Functional Requirements

### FR-1: Type `GetArticleResponse.Status` as `ArticleStatus`
Change the `Status` property on `GetArticleResponse` from `string` (with default `string.Empty`) to the `ArticleStatus` enum type. Update the handler to assign the enum value directly rather than calling `.ToString()`.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/Articles/UseCases/GetArticle/GetArticleResponse.cs:14` declares `public ArticleStatus Status { get; set; }` (no `string.Empty` initializer).
- `backend/src/Anela.Heblo.Application/Features/Articles/UseCases/GetArticle/GetArticleHandler.cs:38` assigns `Status = article.Status` (no `.ToString()`).
- `using` directive for the namespace containing `ArticleStatus` is added to `GetArticleResponse.cs` if not already present.
- Backend builds (`dotnet build`) without warnings or errors.
- The `/api/articles/{id}` endpoint continues to return `"status": "<EnumName>"` in the JSON body for every existing `ArticleStatus` value (regression-tested for at least `Queued`, `Generating`, `Generated`, `Failed`, or whatever values exist in the enum).

### FR-2: Type `ArticleListItemDto.Status` as `ArticleStatus`
Change the `Status` property on `ArticleListItemDto` from `string` (with default `string.Empty`) to the `ArticleStatus` enum type. Update the projection in the list handler to assign the enum value directly.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/Articles/UseCases/ListArticles/ArticleListItemDto.cs:8` declares `public ArticleStatus Status { get; set; }` (no `string.Empty` initializer).
- `backend/src/Anela.Heblo.Application/Features/Articles/UseCases/ListArticles/ListArticlesHandler.cs:32` assigns `Status = a.Status` (no `.ToString()`).
- `using` directive for the namespace containing `ArticleStatus` is added to `ArticleListItemDto.cs` if not already present.
- Backend builds (`dotnet build`) without warnings or errors.
- The `/api/articles` list endpoint continues to return `"status": "<EnumName>"` for each item in the JSON body, identical to the prior wire format.

### FR-3: Regenerate TypeScript API client
Trigger the OpenAPI client regeneration so that `GetArticleResponse.status` and `ArticleListItemDto.status` are emitted as `ArticleStatus` in `frontend/src/api/generated/api-client.ts`.

**Acceptance criteria:**
- After running the standard build that regenerates the client (per `docs/development/api-client-generation.md`), `frontend/src/api/generated/api-client.ts` declares `GetArticleResponse.status?: ArticleStatus` and `ArticleListItemDto.status?: ArticleStatus`.
- `GenerateArticleResponse.status?: ArticleStatus` remains unchanged.
- The generated file is committed alongside the backend changes (regeneration is part of build per project rules).

### FR-4: Remove unsafe casts in `useArticles.ts`
With the generated types now correct, remove the `as ArticleStatus` casts in the two affected hooks. The `?? ArticleStatus.Queued` fallback behavior must be re-evaluated: if the generated type is `ArticleStatus | undefined`, retain the fallback; if it is non-nullable, drop it.

**Acceptance criteria:**
- `frontend/src/api/hooks/useArticles.ts:139` (`useListArticlesQuery`) reads `item.status` directly, with no `as ArticleStatus` cast.
- `frontend/src/api/hooks/useArticles.ts:162` (`useGetArticleQuery`) reads `response.status` directly, with no `as ArticleStatus` cast.
- Fallback `?? ArticleStatus.Queued` is kept only if the generated type is optional (`ArticleStatus | undefined`); otherwise it is removed.
- `npm run build` and `npm run lint` in `frontend/` succeed with no new TypeScript errors or lint warnings.
- `IN_PROGRESS_STATUSES.has(status)` continues to type-check and behave identically to today.

### FR-5: Wire-format regression test
Add or update a backend test that locks in the JSON wire format for the `status` field on both affected endpoints, so a future change to serializer configuration cannot silently break the contract.

**Acceptance criteria:**
- A test under `backend/test/Anela.Heblo.Tests/` (or the relevant test project, matching existing test layout) asserts that serializing a `GetArticleResponse` with `Status = ArticleStatus.Generated` (or another representative value) produces `"status":"Generated"` in the JSON payload.
- An equivalent assertion exists for `ArticleListItemDto`.
- Tests fail if either DTO reverts to `string` *and* the handler stops calling `.ToString()`, or if the global `JsonStringEnumConverter` is removed.

## Non-Functional Requirements

### NFR-1: Wire-format compatibility (no breaking change)
The JSON payload returned by `/api/articles/{id}` and `/api/articles` must be byte-identical to the current production output for the `status` field. No existing frontend consumer, integration, or external client can observe a behavioral change.

### NFR-2: Type safety
After the change, no `as ArticleStatus` casts remain in `frontend/src/api/hooks/useArticles.ts`. The frontend obtains `ArticleStatus` typing directly from the generated client.

### NFR-3: Build hygiene
`dotnet build`, `dotnet format`, `npm run build`, and `npm run lint` all pass cleanly. No new warnings are introduced.

## Data Model
No domain model changes. The `ArticleStatus` enum is unchanged. Only the type annotation on two DTO properties changes:

- `GetArticleResponse.Status`: `string` → `ArticleStatus`
- `ArticleListItemDto.Status`: `string` → `ArticleStatus`

Default value `= string.Empty` is removed from both (enum default is the zero-valued member, which is the existing behavior pre-handler-assignment).

## API / Interface Design

### Affected backend files
- `backend/src/Anela.Heblo.Application/Features/Articles/UseCases/GetArticle/GetArticleResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Articles/UseCases/GetArticle/GetArticleHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Articles/UseCases/ListArticles/ArticleListItemDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Articles/UseCases/ListArticles/ListArticlesHandler.cs`

### Affected frontend files
- `frontend/src/api/generated/api-client.ts` — regenerated; no manual edits
- `frontend/src/api/hooks/useArticles.ts` — remove unsafe casts (lines 139, 162)

### API contracts
- `GET /api/articles/{id}` — request, response shape, and status codes unchanged. JSON body unchanged.
- `GET /api/articles` (list) — request, response shape, and status codes unchanged. JSON body unchanged.

The change is purely a type-system improvement in the C# and generated-TypeScript surfaces. Runtime behavior is preserved.

## Dependencies
- Global `JsonStringEnumConverter` registration in `backend/src/Anela.Heblo.API/Program.cs:121` (already in place — must not be removed).
- NSwag OpenAPI export pipeline that drives TypeScript client generation (already in place per `docs/development/api-client-generation.md`).
- React Query hooks in `frontend/src/api/hooks/useArticles.ts` that consume the generated client.

## Out of Scope
- Refactoring `GenerateArticleResponse` (already correctly typed).
- Refactoring any other DTO in the codebase that may have the same `string`-vs-enum pattern outside the Article module.
- Changes to the `ArticleStatus` enum itself (no new members, no renames).
- Changes to polling cadence, `IN_PROGRESS_STATUSES` membership, or any other Article workflow logic.
- Changes to the global `JsonSerializerOptions` configuration.
- Database schema or migrations (none are involved).
- Updates to E2E tests beyond what is necessary if they assert on the `status` field type at the type level (runtime assertions remain valid because the wire format is unchanged).

## Open Questions
None.

## Status: COMPLETE