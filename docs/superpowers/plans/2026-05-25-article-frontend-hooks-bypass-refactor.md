# Article Frontend Hooks Bypass Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor three Article React Query hooks (`useArticleTraceQuery`, `useArticleFeedbackListQuery`, `useGetArticleQuery`) to call the typed NSwag-generated client methods instead of bypassing private `baseUrl`/`http` fields via `as any` raw-fetch — restoring type safety with zero behavior change for consumers.

**Architecture:** Each affected `queryFn` is rewritten in place to (1) acquire the typed client via the existing `getAuthenticatedApiClient()` accessor, (2) call the generated method (`articles_GetTrace`, `articles_FeedbackList`, or — for `useGetArticleQuery` — the already-used `articles_GetById`), and (3) explicitly project each generated DTO field into the existing local consumer-facing interface, mirroring the pattern already established by `useListArticlesQuery` and the non-cast portion of `useGetArticleQuery`. The local interfaces (`ArticleGenerationStep`, `ArticleTrace`, `ArticleDetail`, `ArticleFeedbackListResponse`, `ArticleFeedbackSummary`, `ArticleFeedbackStats`) and all hook signatures are preserved verbatim — no consumer file changes.

**Tech Stack:** TypeScript, React, React Query (`@tanstack/react-query`), NSwag-generated typed API client.

---

## File Structure

**Modified (existing, no creation):**

| File | Responsibility | Scope of change |
|---|---|---|
| `frontend/src/api/hooks/useArticleTrace.ts` | Defines `ArticleTrace`/`ArticleGenerationStep` interfaces and `useArticleTraceQuery`. | Rewrite only the `queryFn` body of `useArticleTraceQuery` (currently lines 26–44). Interfaces and hook signature unchanged. |
| `frontend/src/api/hooks/useArticles.ts` | Defines Article DTOs and the suite of Article hooks. | Three surgical edits:<br>• Remove three `as any` casts in `useGetArticleQuery` (lines 183–188).<br>• Rewrite the `queryFn` body of `useArticleFeedbackListQuery` (currently lines 259–288).<br>• Add a single-line `// TODO` comment immediately above line 222 inside `useSubmitArticleFeedbackMutation` (no code change to the mutation itself). |

**Untouched (consumer files — refactor must preserve their behavior):**
- `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts`
- `frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts`
- `frontend/src/features/articles/ArticleDetail.tsx`
- `frontend/src/features/articles/ArticleDebugPanel.tsx`

**No new files. No new tests.** Per the spec FR-4 and arch-review, no existing unit/integration test under `frontend/src/api/hooks/__tests__/` exercises these three hooks; the only related test (`useArticleFeedbackAdapter.test.ts`) mocks `useArticleFeedbackListQuery` itself and therefore is unaffected.

**No backend changes. No client regeneration. No new npm packages.**

---

## Self-Review Checklist (already applied)

- ✅ Spec FR-1 → Task 1
- ✅ Spec FR-2 → Task 2 (including arch-review Amendments #2, #3, #4 — full per-item field set, `totalPages` + safe-default `stats`, rationale comment)
- ✅ Spec FR-3 → Task 3
- ✅ Spec FR-4 (preserve external behavior) → enforced by Tasks 1–3 and verified end-to-end in Task 5
- ✅ Spec FR-5 → Task 4 (single-line TODO above the existing raw-fetch call)
- ✅ Arch-review Amendment #1 — current `useArticleTraceQuery(id: string, enabled: boolean)` signature is preserved (Task 1 does not touch the signature line)
- ✅ Arch-review Amendment #5 — positional invocation only; `descending` (local) vs `sortDescending` (generated parameter name) intentionally differ
- ✅ NFR-2 — final grep in Task 5 confirms zero `as any` and zero blanket casts in the three target hooks

---

## Task 1: Refactor `useArticleTraceQuery` to use `articles_GetTrace`

**Files:**
- Modify: `frontend/src/api/hooks/useArticleTrace.ts:23-49` (only the `queryFn` body inside the hook)

**Why this task:** Replace the raw-fetch implementation that uses `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch` with a call to the typed generated method `articles_GetTrace(id)`, then explicitly project each `ArticleGenerationStepDto` field into the local `ArticleGenerationStep` interface. Spec FR-1, FR-4; arch-review "Decision 1", Data Flow for `useArticleTraceQuery`.

- [ ] **Step 1: Read the file to confirm starting state**

Run: `cat frontend/src/api/hooks/useArticleTrace.ts`

Expected: file matches the layout described in the File Structure table above — interface block lines 4–21, hook starting at line 23, `queryFn` body lines 26–44 containing `(apiClient as any).baseUrl`, `(apiClient as any).http.fetch`, and `data.steps ?? []) as ArticleGenerationStep[]`.

- [ ] **Step 2: Replace the `queryFn` body — full updated file content**

Replace the entire `useArticleTraceQuery` definition (lines 23–49) with this exact content. Interfaces above (lines 4–21) and the `import` block (lines 1–2) are untouched.

```typescript
export const useArticleTraceQuery = (id: string, enabled: boolean) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.articleTrace, id],
    queryFn: async (): Promise<ArticleTrace> => {
      const client = getAuthenticatedApiClient();
      const data = await client.articles_GetTrace(id);
      return {
        articleId: data.articleId ?? id,
        steps: (data.steps ?? []).map((step) => ({
          id: step.id ?? '',
          stepName: step.stepName ?? '',
          sequence: step.sequence ?? 0,
          status: step.status ?? '',
          startedAt: step.startedAt?.toISOString() ?? '',
          finishedAt: step.finishedAt?.toISOString() ?? null,
          durationMs: step.durationMs ?? null,
          model: step.model ?? null,
          inputJson: step.inputJson ?? null,
          outputJson: step.outputJson ?? null,
          errorMessage: step.errorMessage ?? null,
        })),
      };
    },
    enabled,
    staleTime: 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};
```

Key invariants preserved:
- Signature `useArticleTraceQuery(id: string, enabled: boolean)` unchanged (arch-review Amendment #1).
- `queryKey: [...QUERY_KEYS.articleTrace, id]` unchanged.
- `enabled`, `staleTime`, `gcTime` unchanged.
- Return type `Promise<ArticleTrace>` unchanged.
- `articleId ?? id` fallback preserved.
- Per-step mapping uses explicit projection — **no** `as ArticleGenerationStep[]` blanket cast (NFR-2).
- `startedAt`/`finishedAt` are `Date | undefined` on the generated DTO and `string` / `string | null` on the local interface, so `?.toISOString()` is required.

- [ ] **Step 3: Verify zero `as any` remains in the file**

Run: `grep -n "as any" frontend/src/api/hooks/useArticleTrace.ts`

Expected: no output (exit code 1). If any match remains, the refactor is incomplete.

- [ ] **Step 4: Verify the file compiles under TypeScript**

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -E "useArticleTrace\.ts" || echo "clean"`

Expected: `clean` (no TS errors mention `useArticleTrace.ts`). If errors appear, fix the projection — common cause is a generated DTO field whose nullability differs from what the projection expects.

- [ ] **Step 5: Verify ESLint is clean for this file**

Run: `cd frontend && npx eslint src/api/hooks/useArticleTrace.ts`

Expected: exit code 0, no warnings.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useArticleTrace.ts
git commit -m "refactor: use generated articles_GetTrace in useArticleTraceQuery

Replace raw fetch using (apiClient as any).baseUrl/.http with a call to
the typed NSwag-generated articles_GetTrace(id). Each ArticleGenerationStepDto
is explicitly projected into the local ArticleGenerationStep shape
(dates -> ISO strings, optional fields coalesced). Hook signature, query key,
enabled/staleTime/gcTime, and return shape are unchanged."
```

---

## Task 2: Refactor `useArticleFeedbackListQuery` to use `articles_FeedbackList`

**Files:**
- Modify: `frontend/src/api/hooks/useArticles.ts:256-292` (only the `queryFn` body inside the hook)

**Why this task:** Replace the raw-fetch implementation (which builds a `URLSearchParams` string and uses `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch`, then returns the unmapped JSON) with a call to the typed `articles_FeedbackList(...)` generated method, then explicitly project the generated `GetArticleFeedbackListResponse` (whose items are `ArticleFeedbackSummary` with `hasComment` / no `feedbackComment` field, dates as `Date`) into the local `ArticleFeedbackListResponse` (whose items are local `ArticleFeedbackSummary` with `hasFeedback`, `feedbackComment: string | null`, dates as `string | null`). Spec FR-2, FR-4; arch-review Decisions 1–3 and Amendments #2–#4.

**Generated signature** (confirmed at `frontend/src/api/generated/api-client.ts:601`):

```typescript
articles_FeedbackList(
  hasFeedback:    boolean | null | undefined,
  requestedBy:    string  | null | undefined,
  sortBy:         string  | undefined,
  sortDescending: boolean | undefined,
  page:           number  | undefined,
  pageSize:       number  | undefined,
): Promise<GetArticleFeedbackListResponse>
```

(Note: generated 4th parameter is named `sortDescending`; local hook params field is `descending`. Pass positionally — arch-review Amendment #5.)

- [ ] **Step 1: Read the file to confirm starting state**

Run: `sed -n '256,292p' frontend/src/api/hooks/useArticles.ts`

Expected: file matches — `useArticleFeedbackListQuery` defined starting line 256, builds `URLSearchParams`, computes `const fullUrl = ${(apiClient as any).baseUrl}${relativeUrl}`, calls `(apiClient as any).http.fetch`, ends with `return response.json();`.

- [ ] **Step 2: Replace the `queryFn` body — full updated hook**

Replace the entire `useArticleFeedbackListQuery` definition (lines 256–292) with this exact content. The rest of the file (imports, type declarations lines 1–123, other hooks above) is untouched.

```typescript
export const useArticleFeedbackListQuery = (params: ArticleFeedbackListParams = {}) => {
  return useQuery({
    queryKey: articleKeys.feedbackList(params),
    queryFn: async (): Promise<ArticleFeedbackListResponse> => {
      const client = getAuthenticatedApiClient();
      const data = await client.articles_FeedbackList(
        params.hasFeedback ?? null,
        params.requestedBy ?? null,
        params.sortBy,
        params.descending,
        params.page,
        params.pageSize,
      );
      return {
        articles: (data.items ?? []).map((item) => ({
          id: item.id ?? '',
          topic: item.topic ?? '',
          title: item.title ?? null,
          requestedBy: item.requestedBy ?? '',
          generatedAt: item.createdAt?.toISOString() ?? null,
          precisionScore: item.precisionScore ?? null,
          styleScore: item.styleScore ?? null,
          // Backend list endpoint never emits a per-item feedback comment
          // (only the boolean hasComment), so projecting null here is exact
          // behavior preservation, not data loss.
          feedbackComment: null,
          hasFeedback: item.hasComment ?? false,
        })),
        totalCount: data.totalCount ?? 0,
        page: data.page ?? params.page ?? 1,
        pageSize: data.pageSize ?? params.pageSize ?? 20,
        totalPages: data.totalPages ?? 0,
        stats: data.stats
          ? {
              totalArticles: data.stats.totalArticles ?? 0,
              totalWithFeedback: data.stats.totalWithFeedback ?? 0,
              avgPrecisionScore: data.stats.avgPrecisionScore ?? null,
              avgStyleScore: data.stats.avgStyleScore ?? null,
            }
          : {
              totalArticles: 0,
              totalWithFeedback: 0,
              avgPrecisionScore: null,
              avgStyleScore: null,
            },
      };
    },
    staleTime: 30_000,
    gcTime: 5 * 60 * 1000,
  });
};
```

Key invariants and design decisions preserved:
- Hook signature `useArticleFeedbackListQuery(params: ArticleFeedbackListParams = {})` unchanged.
- `queryKey: articleKeys.feedbackList(params)` unchanged.
- `staleTime: 30_000` and `gcTime: 5 * 60 * 1000` unchanged.
- Return type `Promise<ArticleFeedbackListResponse>` unchanged.
- Per-item field set (8 fields) matches the local `ArticleFeedbackSummary` interface at lines 78–88 verbatim — arch-review Amendment #2.
- `totalPages` projected at the response level — arch-review Amendment #3.
- `stats` coalesced to a safe default object when the generated DTO returns `undefined` — arch-review Decision 2 and Amendment #3. Local interface requires `stats: ArticleFeedbackStats` (not optional), and `useArticleFeedbackAdapter.ts:27-34` reads `query.data?.stats` directly; defensive default keeps the consumer contract intact.
- `feedbackComment: null` with explanatory comment — arch-review Decision 3 and Amendment #4.
- `descending` (local) is passed as the `sortDescending` (generated) positional argument — arch-review Amendment #5.
- **No** blanket cast (`as ArticleFeedbackListResponse`) — explicit per-field projection only (NFR-2).
- **No** `(apiClient as any)` references (NFR-2).
- **No** manual URL construction or `URLSearchParams` — the generated method handles encoding.

- [ ] **Step 3: Verify zero `as any` remains on apiClient in this hook**

Run: `sed -n '256,310p' frontend/src/api/hooks/useArticles.ts | grep -E "apiClient as any|http\.fetch|baseUrl\}"`

Expected: no output. If any match remains, the refactor is incomplete.

- [ ] **Step 4: Confirm the rest of the file is unchanged**

Run: `git diff --stat frontend/src/api/hooks/useArticles.ts`

Expected: one file changed; insertions and deletions are localized to the `useArticleFeedbackListQuery` region (no spurious edits to interfaces, query-key factory, or other hooks).

- [ ] **Step 5: Verify the file compiles under TypeScript**

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -E "useArticles\.ts" || echo "clean"`

Expected: `clean`. Common failure mode if not clean: the local `ArticleFeedbackSummary` interface (lines 78–88) requires fields that the generated DTO doesn't have or has under a different name — the projection above already handles every such case, so the most likely cause of an error here would be a stale/regenerated client. Inspect and adjust the projection only — do not modify the local interface.

- [ ] **Step 6: Verify ESLint is clean**

Run: `cd frontend && npx eslint src/api/hooks/useArticles.ts`

Expected: exit code 0, no warnings.

- [ ] **Step 7: Verify the adapter test still passes (regression check)**

Run: `cd frontend && npx jest src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts`

Expected: all tests pass. (This test mocks `useArticleFeedbackListQuery` directly — it doesn't exercise the new generated-client path, but a green run confirms the hook's public shape is still what the adapter expects.)

- [ ] **Step 8: Commit**

```bash
git add frontend/src/api/hooks/useArticles.ts
git commit -m "refactor: use generated articles_FeedbackList in useArticleFeedbackListQuery

Replace raw fetch using (apiClient as any).baseUrl/.http and manual URL
construction with a call to the typed NSwag-generated articles_FeedbackList.
Generated DTO is explicitly projected into the local
ArticleFeedbackListResponse: items->articles, createdAt->generatedAt (ISO),
hasComment->hasFeedback, feedbackComment->null (backend list endpoint omits
it). stats is coalesced to a safe default when absent. Hook signature, query
key, staleTime/gcTime, and return shape are unchanged."
```

---

## Task 3: Remove stale `as any` casts in `useGetArticleQuery`

**Files:**
- Modify: `frontend/src/api/hooks/useArticles.ts:183-188` (three lines only)

**Why this task:** The generated `GetArticleResponse` already declares `precisionScore`, `styleScore`, and `feedbackComment` (api-client.ts:12879-12881). The three `as any` casts at lines 184/186/188 are stale and suppress type checking unnecessarily. Spec FR-3.

- [ ] **Step 1: Read the file to confirm starting state**

Run: `sed -n '183,189p' frontend/src/api/hooks/useArticles.ts`

Expected output:

```
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        precisionScore: ((response as any).precisionScore as number | null) ?? null,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        styleScore: ((response as any).styleScore as number | null) ?? null,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        feedbackComment: ((response as any).feedbackComment as string | null) ?? null,
```

- [ ] **Step 2: Replace the three field lines (and their eslint-disable comments)**

Use Edit to replace this exact block (the six lines from `// eslint-disable-next-line` above `precisionScore` through `feedbackComment` line):

Old:
```typescript
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        precisionScore: ((response as any).precisionScore as number | null) ?? null,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        styleScore: ((response as any).styleScore as number | null) ?? null,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        feedbackComment: ((response as any).feedbackComment as string | null) ?? null,
```

New:
```typescript
        precisionScore: response.precisionScore ?? null,
        styleScore: response.styleScore ?? null,
        feedbackComment: response.feedbackComment ?? null,
```

The three `eslint-disable-next-line` directives are also removed (they were paired with the casts). Nothing else in the hook changes.

Do **not** touch the `sources` block immediately above (lines 170–182) — that block uses its own `as any` on individual `s` elements because the generated `ArticleSourceDto` shape may legitimately differ. That cleanup is out of scope per the spec ("Out of Scope: Adding new functionality to any of the three refactored hooks").

- [ ] **Step 3: Verify the three `(response as any)` patterns are gone**

Run: `grep -n "response as any" frontend/src/api/hooks/useArticles.ts`

Expected: no output. (Only `s as any` for `sources` should remain in this file — that's expected and out of scope.)

- [ ] **Step 4: Verify TypeScript and ESLint**

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -E "useArticles\.ts" || echo "clean" && npx eslint src/api/hooks/useArticles.ts`

Expected: `clean` from the tsc grep, and exit code 0 from eslint with no warnings.

If tsc reports that `response.precisionScore` / `response.styleScore` / `response.feedbackComment` does not exist — the generated client has fallen behind the backend. **Do not re-add the casts.** Regenerate the client (`npm run generate-api` or equivalent — see `docs/development/api-client-generation.md`) and rerun this step. The point of removing the casts is precisely so TypeScript can catch this drift.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useArticles.ts
git commit -m "refactor: drop stale (response as any) casts in useGetArticleQuery

GetArticleResponse already declares precisionScore, styleScore, and
feedbackComment (generated/api-client.ts:12879-12881), so the three casts
and their paired eslint-disable directives were unnecessary. Read the
fields directly from the typed response with the existing ?? null fallback."
```

---

## Task 4: Add dated TODO above the residual raw-fetch in `useSubmitArticleFeedbackMutation`

**Files:**
- Modify: `frontend/src/api/hooks/useArticles.ts:222` (insert a single comment line above)

**Why this task:** `useSubmitArticleFeedbackMutation` is deliberately out of scope for this refactor because its 409 branch is a non-exceptional return value (not a thrown error), and migrating to `articles_FeedbackSubmit` would force catch-and-rethrow logic to translate `ApiException` back into `{ alreadySubmitted: true }`. Per Spec FR-5 and arch-review Decision 4, a single dated `// TODO` makes the residual fragility visible at the call site without opening a separate GitHub issue (solo-dev workspace per `CLAUDE.md`).

- [ ] **Step 1: Read the surrounding lines to confirm position**

Run: `sed -n '219,225p' frontend/src/api/hooks/useArticles.ts`

Expected:
```typescript
  return useMutation({
    mutationFn: async (payload: SubmitArticleFeedbackPayload): Promise<SubmitArticleFeedbackResult> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/articles/${articleId}/feedback`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
```

- [ ] **Step 2: Insert the TODO comment immediately above the `const fullUrl = ...` line**

Use Edit to replace:

Old:
```typescript
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/articles/${articleId}/feedback`;
```

New:
```typescript
      const apiClient = getAuthenticatedApiClient();
      // TODO(arch-review 2026-05-25): Uses private apiClient internals (baseUrl/http) via `as any` — same fragility as the hooks refactored in this PR. Keep raw fetch only for 409 branch; revisit when generated client exposes typed-mutation 409 handling.
      const fullUrl = `${(apiClient as any).baseUrl}/api/articles/${articleId}/feedback`;
```

No other change to the mutation. The body of `mutationFn`, the 409 short-circuit, the error throw, the JSON parsing of the success branch, and `onSuccess` are all untouched.

- [ ] **Step 3: Verify the mutation's behavior is unchanged by diff**

Run: `git diff frontend/src/api/hooks/useArticles.ts | grep -E "^[-+]" | grep -vE "^\+\+\+|^---" | head -40`

Expected: exactly one added line (the TODO). No removed lines for this task. (If you batched Task 3 and Task 4 into the same working state, the diff will include Task 3's removals too — that is fine; verify the mutation block itself adds only the one comment line.)

- [ ] **Step 4: Verify ESLint is still clean (TODO comments must not trigger warnings)**

Run: `cd frontend && npx eslint src/api/hooks/useArticles.ts`

Expected: exit code 0.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useArticles.ts
git commit -m "chore: flag useSubmitArticleFeedbackMutation raw fetch with dated TODO

The mutation keeps raw fetch because its 409 branch is a non-exceptional
business outcome that consumers read off the return value (not the thrown
error). Migrating to articles_FeedbackSubmit would require catch-and-rethrow
to translate ApiException 409 back into { alreadySubmitted: true }. Mark
the residual fragility with a dated TODO instead of expanding scope or
opening a separate issue (solo-dev workspace per CLAUDE.md)."
```

---

## Task 5: Final end-to-end validation

**Files:** none (verification only)

**Why this task:** Lock in NFR-2 (zero `as any` / blanket casts in the three refactored hooks), confirm the full frontend still builds, and confirm related tests pass. This is the hard gate before declaring the feature done.

- [ ] **Step 1: Verify NFR-2 globally for the three target hooks**

Run each command; all must produce no output.

```bash
grep -n "apiClient as any" frontend/src/api/hooks/useArticleTrace.ts
grep -nE "\(response as any\)" frontend/src/api/hooks/useArticles.ts
grep -nE "as ArticleGenerationStep\[\]|as ArticleFeedbackListResponse" frontend/src/api/hooks/useArticleTrace.ts frontend/src/api/hooks/useArticles.ts
```

Expected: each command exits with no matches (`grep` exit code 1).

Acceptable remaining matches in `frontend/src/api/hooks/useArticles.ts`:
- `s as any` inside `useGetArticleQuery`'s `sources` projection (lines ~170–182) — pre-existing, **out of scope**.
- `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch` inside `useSubmitArticleFeedbackMutation` (lines ~222–225) — preserved per Spec FR-5 with TODO above.

- [ ] **Step 2: Full frontend type-check**

Run: `cd frontend && npm run build`

Expected: build succeeds. If it fails with a TypeScript error in either modified file, fix the projection in the corresponding task — do **not** re-introduce `as any`.

- [ ] **Step 3: Frontend lint**

Run: `cd frontend && npm run lint`

Expected: exit code 0, no errors. Warnings unrelated to the changed files are pre-existing and out of scope.

- [ ] **Step 4: Run the adapter regression test**

Run: `cd frontend && npx jest src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts`

Expected: all tests pass. The test mocks `useArticleFeedbackListQuery` directly, so a pass confirms the hook's public return shape remains exactly what the adapter consumes.

- [ ] **Step 5: Run the broader hooks test suite (regression check)**

Run: `cd frontend && npx jest src/api/hooks/__tests__/`

Expected: all tests pass. None of these tests exercise the three refactored hooks directly (no `useArticles.test.ts` or `useArticleTrace.test.ts` exists), so this is a smoke check that nothing in the shared `getAuthenticatedApiClient()` path was disturbed.

- [ ] **Step 6: Spot-check the consumer components for type errors**

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -E "ArticleDebugPanel\.tsx|ArticleDetail\.tsx|useArticleFeedbackAdapter\.ts" || echo "clean"`

Expected: `clean`. The hooks' return shapes are unchanged, so consumers must still compile without modification (Spec FR-4).

- [ ] **Step 7: Confirm commits**

Run: `git log --oneline main..HEAD`

Expected: at least four commits (one per Task 1–4). If Tasks 3 and 4 were combined into a single commit, that is also acceptable — but the per-task commits are preferred for review.

- [ ] **Step 8: Done — no further commit unless validation surfaced an issue**

If steps 1–7 all pass, the feature is implementation-complete. Any remaining work (PR creation, etc.) is handled by the pipeline downstream of plan execution and is not part of this plan.
