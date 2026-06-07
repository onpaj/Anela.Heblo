Now I have enough context. Let me check the spec's scoping vs the page's handleOpen cast one more time, then produce the review.

# Architecture Review: Eliminate `as any` Casts in `useExpeditionListArchive.ts`

## Skip Design: true

Pure type-safety refactor with no UI surface changes. Markup, copy, layout, and interaction in `ExpeditionListArchivePage.tsx` are unchanged.

## Architectural Fit Assessment

The proposed refactor aligns cleanly with three existing project conventions:

1. **Canonical typed-client pattern** — `useArticles.ts:216–248` is the reference implementation: `getAuthenticatedApiClient()` → `new GeneratedRequest({...})` → typed method call → `try/catch { err.status }`. The spec's FR-3 mirrors this exactly.
2. **Escape-hatch helpers** — `getApiBaseUrl()` (`client.ts:177`) and `getAuthenticatedFetch()` (`client.ts:419`) are the sanctioned public surface for URL construction and raw fetch. They already exist; FR-5 just consumes them.
3. **Existing guardrail mechanism** — the project does **not** police this rule with ESLint; it polices it with the `MIGRATED_HOOKS` Set in `frontend/src/api/__tests__/authenticated-api-usage.test.ts:197`. FR-6 should extend this established pattern rather than introduce a parallel ESLint rule.

The four typed methods (`expeditionListArchive_GetDates`, `_GetByDate`, `_Reprint`, `expeditionList_RunFix`) all delegate to `this.http.fetch(...)`, which is the same `authenticatedHttp` instance that the current `(apiClient as any).http.fetch` already reaches — so auth headers, 401 redirect, and global error toasts are preserved by construction (NFR-5 holds automatically).

The fit is good, but the spec has three blind spots flagged below.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────┐
│ ExpeditionListArchivePage.tsx        │
│  • consumes 5 hook signatures        │
│  • consumes local ItemDto type       │  ← signatures preserved → 0 page changes
└────────────────┬─────────────────────┘
                 │
┌────────────────▼─────────────────────┐
│ useExpeditionListArchive.ts          │
│  • local interfaces (adapter layer)  │
│  • 4 hooks call typed methods        │
│  • 1 helper uses getApiBaseUrl()     │
│  • queryFn maps Date→string,         │
│    long?→number for consumer compat  │
└────────────────┬─────────────────────┘
                 │
   ┌─────────────┴──────────────┐
   │                            │
┌──▼─────────────────┐  ┌──────▼──────────────┐
│ generated/         │  │ client.ts            │
│ ApiClient methods  │  │ getApiBaseUrl()      │
│ (typed)            │  │ getAuthenticatedApi… │
└──┬─────────────────┘  └──────┬──────────────┘
   │                           │
   │   .http.fetch ────────────▶ authenticatedHttp.fetch
   │                              • auth header injection
   │                              • 401 redirect
   │                              • global toast on BaseResponse.success=false
   └────────────────────────────▶ backend
```

### Key Design Decisions

#### Decision 1: Use typed generated methods, not `getAuthenticatedFetch()`
**Options considered:**
- (A) Typed `client.expeditionListArchive_*` methods.
- (B) `getApiBaseUrl()` + `getAuthenticatedFetch()` for everything.

**Chosen approach:** (A) for the four RPC endpoints; (B) only for `getExpeditionListDownloadUrl` (it builds an `<a href>` URL, not a fetch).

**Rationale:** Per `docs/development/api-client-generation.md:217–268`, the escape hatch exists for endpoints whose business outcomes require HTTP-status branching (e.g. 412 Precondition Failed not yet typed). None of these four endpoints branch on status — they all return `BaseResponse`-shaped success/failure envelopes. Typed methods are the documented default.

#### Decision 2: Keep local response interfaces; map types inside `queryFn`
**Options considered:**
- (A) Re-export generated classes (spec's preferred option).
- (B) Keep local interfaces; adapt inside the hook.

**Chosen approach:** (B). The spec's claim "re-export from the generated client where structurally identical" is **incorrect** for `ExpeditionListItemDto`:

| Field | Local (today) | Generated | Page consumer |
|---|---|---|---|
| `createdOn` | `string \| null` | `Date \| undefined` | `formatDateTime(iso: string \| null)` at `ExpeditionListArchivePage.tsx:26,253` |
| `contentLength` | `number \| null` | `number \| undefined` | `formatFileSize(bytes: number \| null)` at `ExpeditionListArchivePage.tsx:19,256` |

Backend declares `CreatedOn` as `DateTimeOffset?` (`backend/.../Contracts/ExpeditionListItemDto.cs:8`), which NSwag converts to `Date | undefined` via the `init()` method. The page's formatters were built around ISO strings and `null`-not-`undefined`. Re-exporting the generated class breaks `formatDateTime` and `formatFileSize` typing.

**Rationale:** Keeping local interfaces preserves the consumer contract (zero page changes — required by NFR-1) and confines the Date↔string boundary to one place inside `queryFn`. The local interfaces become a thin DTO-mapping layer, not duplicate types.

#### Decision 3: Reuse the generated `ReprintExpeditionListRequest` *class*, drop the local interface
**Options considered:**
- (A) Keep local interface and pass plain object literal.
- (B) Drop local interface, import generated class, instantiate with `new`.

**Chosen approach:** (B). The local interface (`useExpeditionListArchive.ts:25–27`) is consumed by the page only as `{ blobPath }` plain-object input to `mutateAsync`. Since the hook owns the mutation function signature, it can declare the input type as the generated class without breaking the call site (object-literal subtyping still works).

**Rationale:** Spec FR-3 already requires `new ReprintExpeditionListRequest({ blobPath })`; keeping a parallel local interface adds noise. `useArticles.ts:222` is the canonical reference.

#### Decision 4: Reuse existing test-based guardrail, do not add an ESLint rule
**Options considered:**
- (A) Add a new `no-restricted-syntax` rule in `.eslintrc.json` (FR-6 proposal).
- (B) Add `"useExpeditionListArchive.ts"` to `MIGRATED_HOOKS` in `authenticated-api-usage.test.ts:197`.

**Chosen approach:** (B). The project's existing convention is explicit at `authenticated-api-usage.test.ts:193`: *"This test guards against regressions in hooks that have already been migrated... Extend MIGRATED_HOOKS below as each hook is cleaned up."*

**Rationale:** Two enforcement mechanisms create drift. The Jest test catches `(apiClient as any)`, `.http`, and `.baseUrl` access in plain text — exactly the same surface the proposed ESLint rule covers. Reusing it is one line of change and matches the documented workflow.

## Implementation Guidance

### Directory / Module Structure
No new files. All changes confined to:
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — rewrite the five exports.
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts` — add `"useExpeditionListArchive.ts"` to `MIGRATED_HOOKS` Set (line 197).
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — no functional change required (test mocks the hook module wholesale at line 8); fixture shapes already match the preserved local interfaces.
- (Optional, per amendment A1 below) `frontend/src/pages/ExpeditionListArchivePage.tsx:64–78` — `handleOpen` cast.

### Interfaces and Contracts

**Keep exported (unchanged signatures, zero consumer churn):**
```typescript
export interface ExpeditionListItemDto {
  blobPath: string;
  fileName: string;
  listId: string;
  createdOn: string | null;
  contentLength: number | null;
}
export interface GetExpeditionDatesResponse { dates: string[]; totalCount: number; page: number; pageSize: number; }
export interface GetExpeditionListsByDateResponse { items: ExpeditionListItemDto[]; }
export interface ReprintExpeditionListResponse { success: boolean; errorMessage: string | null; }
```

**Drop:** `ReprintExpeditionListRequest` interface — use the generated class as the mutation input type.

**Import:**
```typescript
import {
  ReprintExpeditionListRequest,
  type ExpeditionListItemDto as GeneratedItemDto,
  // ...etc. for any response classes needed for mapping
} from "../generated/api-client";
```

### Data Flow

**`useExpeditionDates(page, pageSize)`:**
```
queryFn → client.expeditionListArchive_GetDates(page, pageSize)
       → returns class instance { dates?, totalCount?, page?, pageSize? }
       → map to GetExpeditionDatesResponse: nullish-coalesce numbers (?? 0), default dates (?? [])
       → return
```

**`useExpeditionListsByDate(date)`:**
```
queryFn → client.expeditionListArchive_GetByDate(date)   // encodes date internally
       → returns { items?: GeneratedItemDto[] }
       → map items: createdOn?.toISOString() ?? null, contentLength ?? null
       → return GetExpeditionListsByDateResponse
```

**`useReprintExpeditionList`:**
```
mutationFn(input: { blobPath: string }) →
  client.expeditionListArchive_Reprint(new ReprintExpeditionListRequest({ blobPath: input.blobPath }))
  → on resolve: map { success: response.success ?? true, errorMessage: response.errorMessage ?? null }
  → on reject (SwaggerException): rethrow — central toast at client.ts:312–360 already extracts errorMessage from BaseResponse
onSuccess → queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive })
```

**`useRunExpeditionListPrintFix`:**
```
mutationFn() → client.expeditionList_RunFix()
            → returns { totalCount?, errorMessage? }
            → map to { totalCount: response.totalCount ?? 0, errorMessage: response.errorMessage ?? null }
            → return — page reads result.totalCount at ExpeditionListArchivePage.tsx:98
```

**`getExpeditionListDownloadUrl(blobPath)`:**
```
return `${getApiBaseUrl()}/api/expedition-list-archive/download/${
  blobPath.split("/").map(encodeURIComponent).join("/")
}`;
```
No `getAuthenticatedApiClient()` call — instantiating the client just to read its base URL is wasted work.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Re-exporting generated types breaks page formatters (`Date` vs `string`, `undefined` vs `null`) | HIGH | Decision 2: keep local interfaces, map inside `queryFn`. Verified against `ExpeditionListArchivePage.tsx:26, 253, 256`. |
| Double error toast: central handler fires on `!response.ok`, then hook re-throws and page shows `showError("Chyba tisku", msg)` | MEDIUM | Central toast already runs on `!response.ok` *today* via `authenticatedHttp.fetch`. Today's `(apiClient as any).http.fetch` path goes through the same code. So toast count is unchanged. Document this in PR for reviewer confidence. |
| Generated class fields are optional (`?:`); destructuring `response.totalCount` yields `number \| undefined` and downstream code expects `number` | MEDIUM | All mapping uses `?? 0` / `?? null` / `?? []`. Page line 98 `result.totalCount` reads from mapped local interface, which is non-optional. |
| `expeditionListArchive_Reprint` already throws on non-2xx via `throwException(...)` — error contract differs from today's `errorData?.errorMessage` extraction | LOW | NFR-1 ("network requests identical") still holds. User-visible toast may differ in wording (status-text vs `errorMessage`) — spec FR-3 explicitly allows this: "falling back to the generated client's standard `SwaggerException` handling is acceptable". |
| Spec FR-6 introduces ESLint rule that overlaps with existing Jest guardrail | LOW | Decision 4: add to `MIGRATED_HOOKS` instead. |
| **Sixth `(apiClient as any).http.fetch` exists in `ExpeditionListArchivePage.tsx:64–66`** (page-level `handleOpen`) — spec scopes only the hook file | HIGH (scope gap) | See Spec Amendments A1. |

## Specification Amendments

**A1 — Out-of-scope cast in `ExpeditionListArchivePage.tsx`.**
`handleOpen` at lines 62–78 contains a sixth `(apiClient as any).http.fetch` use that the brief and spec both missed. It calls `getExpeditionListDownloadUrl(item.blobPath)` then fetches the URL as a blob. Two options — the spec must pick one explicitly:
- **(Recommended)** Migrate it in the same change to `getAuthenticatedFetch()(url, { method: 'GET' })`. The URL is already absolute (from `getApiBaseUrl()`), so this is a one-line edit. Keeps the "no `(apiClient as any)` in this feature area" promise honest.
- Carve it out explicitly with a sentence: "*The `handleOpen` cast in `ExpeditionListArchivePage.tsx:66` is out of scope and tracked separately.*"

Otherwise the spec's NFR-2 ("zero `as any` casts") is misleading at the feature level.

**A2 — Reframe FR-6 to extend the existing Jest guardrail.**
Replace "Add an ESLint rule…" with:
> Add `"useExpeditionListArchive.ts"` to the `MIGRATED_HOOKS` Set in `frontend/src/api/__tests__/authenticated-api-usage.test.ts:197`. Verify `npm test -- authenticated-api-usage` passes after refactor and fails if `(apiClient as any).http.fetch` is reintroduced in the file. No `.eslintrc.json` change required.

**A3 — Correct the "structurally identical" claim under "Data Model".**
Local `ExpeditionListItemDto` is **not** structurally compatible with the generated class (`createdOn: string | null` vs `Date | undefined`; `contentLength: number | null` vs `number | undefined`). Re-export is not safe; per Decision 2, the local interfaces stay as a hook-level adapter and the hook maps response shapes inside `queryFn`. Update the spec's "Decision: re-export from the generated client where structurally identical" to "Decision: keep local interfaces; map generated types inside `queryFn`."

**A4 — Drop `ReprintExpeditionListRequest` from "Keep the locally-declared interfaces".**
Per Decision 3, the request type comes from the generated client. This is the only spec-listed interface that should be removed.

**A5 — Acceptance criterion for FR-5 wording.**
Spec says "No reference to `getAuthenticatedApiClient()`." Add: "*Imports `getApiBaseUrl` from `../client` and uses only string concatenation — no client instantiation.*"

## Prerequisites

None. All required helpers (`getApiBaseUrl`, `getAuthenticatedFetch`, `getAuthenticatedApiClient`) already exist in `frontend/src/api/client.ts`. All required typed methods and DTO classes already exist in `frontend/src/api/generated/api-client.ts`. The existing guardrail test (`authenticated-api-usage.test.ts`) is in place and only needs a one-line `MIGRATED_HOOKS` addition. No backend changes, no OpenAPI regeneration, no migrations, no config.

Implementation can start immediately.