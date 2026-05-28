The sandbox is blocking all writes despite the path being inside the allowed worktree. Per the pipeline note ("plan file content will be captured automatically as the artifact"), I'll emit the plan content as the response — the harness handles persistence.

---

# Remove Private API Client Access in `useSubmitArticleFeedbackMutation` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the private-internals (`(apiClient as any).http` / `.baseUrl`) access in `useSubmitArticleFeedbackMutation` with a typed call to the generated `articles_SubmitFeedback` method, preserving the externally observable mutation result (including the 409 "already submitted" case).

**Architecture:** Per the architecture review, this is a single-hook refactor. We replace the hand-rolled `fetch` inside `useSubmitArticleFeedbackMutation` with `apiClient.articles_SubmitFeedback(articleId, new SubmitArticleFeedbackRequest({…}))` and detect 409 via `SwaggerException.isSwaggerException(err) && err.status === 409`. The generated client routes through the same `authenticatedHttp.fetch` used by every other hook, so the auth header path is unchanged. We pass `getAuthenticatedApiClient(false)` to suppress the global "Chyba API (409)" toast for this expected business-conflict status. No new helpers (`getApiBaseUrl`, `getAuthHeaders`) are introduced — Spec FR-3 is dropped and FR-5 is reframed per the arch-review amendments. Scope is strictly `useArticles.ts` and its test file; sibling hooks (`useKnowledgeBase`, `useLeaflet`), the gotcha docs, and the NSwag template are explicitly out of scope and rolled into the FR-6 follow-up issue.

**Tech Stack:** TypeScript, React Query (`@tanstack/react-query`), Jest + React Testing Library, NSwag-generated `AnelaHebloApiClient`, MSAL (via `authenticatedHttp`).

---

## File Structure

**Modified files:**
- `frontend/src/api/hooks/useArticles.ts` — rewrite the body of `useSubmitArticleFeedbackMutation`; update imports; remove the `TODO(arch-review 2026-05-25)` block.
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — extend the existing mock of `getAuthenticatedApiClient` and add the three required test cases (2xx, 409, other error).

**Not modified (out of scope per arch-review Decisions 3 and 4):**
- `frontend/src/api/client.ts` — no new `getApiBaseUrl` / `getAuthHeaders` helpers.
- `frontend/src/api/generated/api-client.ts` — generated; never hand-edited.
- `frontend/src/api/hooks/useKnowledgeBase.ts`, `useLeaflet.ts` — siblings using the same pattern; covered by the FR-6 follow-up issue.
- `docs/development/api-client-generation.md`, `memory/gotchas/api-client-must-use-absolute-urls.md` — left alone for now (would otherwise contradict 34 still-extant hook files).

**Out-of-tree artifact:**
- A new GitHub issue filed via `gh` (FR-6) listing the three follow-up items from arch-review's "Specification Amendments" §5.

---

## Background You Need Before Starting

1. The generated client (`frontend/src/api/generated/api-client.ts`) already exports everything we need:
   - Method on `ApiClient`: `articles_SubmitFeedback(id: string, request: SubmitArticleFeedbackRequest): Promise<SubmitArticleFeedbackResponse>` (around line 560).
   - `class SubmitArticleFeedbackRequest` — constructor accepts a partial `{ articleId?, precisionScore?, styleScore?, comment? }`.
   - `class SubmitArticleFeedbackResponse` — fields `{ precisionScore?, styleScore?, feedbackComment? }` (response also includes `articleId`).
   - `class SwaggerException` with `status: number` and `static isSwaggerException(obj): obj is SwaggerException` (around line 37959). Use the static guard — `instanceof` is unreliable across the test/SUT module-resolution boundary.

2. The internal HTTP layer (`frontend/src/api/client.ts`) exposes `getAuthenticatedApiClient(showErrorToasts: boolean = true)`. Passing `false` suppresses the global "Chyba API (…)" toast — required here because 409 is a normal business outcome that the consumer renders inline, not via a global toast.

3. The existing test file already mocks `getAuthenticatedApiClient` (per arch-review). The mock currently returns `{ articles_FeedbackList: jest.fn() }`; we extend it with `articles_SubmitFeedback: jest.fn()` and add `SwaggerException` import for simulating non-2xx errors.

4. The consumer (`frontend/src/components/articles/.../ArticleFeedbackSection.tsx`) is unchanged. Confirm by reading the file after the refactor — no edit, just verification.

---

## Task 1: Add three failing tests for `useSubmitArticleFeedbackMutation`

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useArticles.test.ts`

- [ ] **Step 1: Read the existing test file end-to-end**

Run: `cat frontend/src/api/hooks/__tests__/useArticles.test.ts | head -200`

Note (a) where `jest.mock('../../client')` lives, (b) the shape of the existing `getAuthenticatedApiClient` mock factory, (c) the `renderHook` / `QueryClientProvider` wrapper helper used by the other tests in the file. You will reuse all three.

- [ ] **Step 2: Extend the `getAuthenticatedApiClient` mock to expose `articles_SubmitFeedback`**

Locate the existing `jest.mock('../../client', …)` block. The current factory returns an object that includes `articles_FeedbackList: jest.fn()`. Add `articles_SubmitFeedback: jest.fn()` alongside it. Example (adapt to the exact existing factory style; do not duplicate the whole mock if it's structured with a shared client variable):

```ts
jest.mock('../../client', () => {
  const mockClient = {
    articles_FeedbackList: jest.fn(),
    articles_SubmitFeedback: jest.fn(),
  };
  return {
    __esModule: true,
    getAuthenticatedApiClient: jest.fn(() => mockClient),
    // …preserve any other exports the existing mock already returns
  };
});
```

- [ ] **Step 3: Import the generated types needed by the new tests**

Add to the imports near the top of the test file:

```ts
import {
  SubmitArticleFeedbackResponse,
  SwaggerException,
} from '../../generated/api-client';
import { getAuthenticatedApiClient } from '../../client';
```

(If `getAuthenticatedApiClient` is already imported elsewhere in the file, do not duplicate.)

- [ ] **Step 4: Add the 2xx success test**

Add this `describe` block (or `it` inside an existing `describe('useSubmitArticleFeedbackMutation')` if one exists; create the describe block if not):

```ts
describe('useSubmitArticleFeedbackMutation', () => {
  const articleId = 'article-123';

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('resolves with mapped scores and comment on 2xx', async () => {
    const apiClient = (getAuthenticatedApiClient as jest.Mock)(false);
    const response = new SubmitArticleFeedbackResponse({
      precisionScore: 4,
      styleScore: 5,
      feedbackComment: 'Helpful.',
    });
    (apiClient.articles_SubmitFeedback as jest.Mock).mockResolvedValueOnce(response);

    const { result } = renderHook(
      () => useSubmitArticleFeedbackMutation(articleId),
      { wrapper: createWrapper() }, // use whichever wrapper helper the file already defines
    );

    const mutationResult = await result.current.mutateAsync({
      precisionScore: 4,
      styleScore: 5,
      comment: 'Helpful.',
    });

    expect(apiClient.articles_SubmitFeedback).toHaveBeenCalledTimes(1);
    expect(apiClient.articles_SubmitFeedback).toHaveBeenCalledWith(
      articleId,
      expect.objectContaining({
        articleId,
        precisionScore: 4,
        styleScore: 5,
        comment: 'Helpful.',
      }),
    );
    expect(mutationResult).toEqual({
      precisionScore: 4,
      styleScore: 5,
      feedbackComment: 'Helpful.',
    });
  });
});
```

- [ ] **Step 5: Add the 409 already-submitted test**

Inside the same `describe('useSubmitArticleFeedbackMutation')` block, add:

```ts
it('resolves with { alreadySubmitted: true } when the server returns 409', async () => {
  const apiClient = (getAuthenticatedApiClient as jest.Mock)(false);
  const conflict = new SwaggerException(
    'Already submitted',
    409,
    '',
    {},
    null,
  );
  (apiClient.articles_SubmitFeedback as jest.Mock).mockRejectedValueOnce(conflict);

  const { result } = renderHook(
    () => useSubmitArticleFeedbackMutation(articleId),
    { wrapper: createWrapper() },
  );

  const mutationResult = await result.current.mutateAsync({
    precisionScore: 3,
    styleScore: 3,
    comment: '',
  });

  expect(mutationResult).toEqual({ alreadySubmitted: true });
});
```

Note: the `SwaggerException` constructor signature in NSwag-generated code is `(message, status, response, headers, result)`. If the local generated class diverges, adjust positional args — but the `status` field must be `409` and `SwaggerException.isSwaggerException(err)` must return `true` for the instance.

- [ ] **Step 6: Add the non-409 error rejection test**

```ts
it('rejects when the server returns a non-409 error', async () => {
  const apiClient = (getAuthenticatedApiClient as jest.Mock)(false);
  const serverError = new SwaggerException(
    'Internal server error',
    500,
    '',
    {},
    null,
  );
  (apiClient.articles_SubmitFeedback as jest.Mock).mockRejectedValueOnce(serverError);

  const { result } = renderHook(
    () => useSubmitArticleFeedbackMutation(articleId),
    { wrapper: createWrapper() },
  );

  await expect(
    result.current.mutateAsync({
      precisionScore: 1,
      styleScore: 1,
      comment: '',
    }),
  ).rejects.toBe(serverError);
});
```

- [ ] **Step 7: Run the three new tests and confirm they fail**

Run:
```
cd frontend && npx jest src/api/hooks/__tests__/useArticles.test.ts -t "useSubmitArticleFeedbackMutation" --no-coverage
```

Expected: all three new tests **FAIL**. The current implementation calls `(apiClient as any).http.fetch(...)`, not `articles_SubmitFeedback`, so the mocked method is never invoked and the 2xx test fails on `toHaveBeenCalledTimes(1)`; the 409 and 500 tests fail because the rejection path is never triggered. This is the expected red.

If they accidentally **pass**, stop and read `useArticles.ts` — the implementation may already have been refactored, or the mock may shadow the wrong export.

---

## Task 2: Refactor `useSubmitArticleFeedbackMutation` to use the generated typed method

**Files:**
- Modify: `frontend/src/api/hooks/useArticles.ts` — the `useSubmitArticleFeedbackMutation` function and its imports.

- [ ] **Step 1: Read the current hook in full**

Run: `cat frontend/src/api/hooks/useArticles.ts | sed -n '1,60p'` to see imports, then locate `export const useSubmitArticleFeedbackMutation` (search for the symbol) and read the whole function plus the `TODO(arch-review 2026-05-25)` comment block immediately above or inside it.

Note the exact `payload` parameter type and the exact return-result discriminated shape the function uses today. The arch-review documents the result as `{ alreadySubmitted?, precisionScore?, styleScore?, feedbackComment? }`. Preserve that shape exactly — consumers depend on it.

- [ ] **Step 2: Update imports at the top of `useArticles.ts`**

Ensure these are imported from `../generated/api-client` (add the ones missing; do not duplicate):

```ts
import {
  SubmitArticleFeedbackRequest,
  SubmitArticleFeedbackResponse, // only if the file already references it for the return type
  SwaggerException,
} from '../generated/api-client';
```

Ensure `getAuthenticatedApiClient` is already imported from `../client`; if not, add it.

Remove any imports that become unused after the refactor (likely none — `useMutation`, `useQueryClient`, `articleKeys` all stay).

- [ ] **Step 3: Replace the body of `useSubmitArticleFeedbackMutation`**

Find the existing `mutationFn` (or equivalent) and replace it with the snippet from arch-review's Specification Amendment §3. Keep the surrounding `useMutation({...})` configuration (including `onSuccess` invalidating `articleKeys.detail(articleId)`, `onError`, etc.) byte-for-byte intact.

The replacement `mutationFn` body:

```ts
mutationFn: async (payload: SubmitArticleFeedbackPayload) => {
  // Suppress the global "Chyba API (409)" toast — 409 is an expected
  // business outcome that resolves with { alreadySubmitted: true }.
  const apiClient = getAuthenticatedApiClient(false);

  const request = new SubmitArticleFeedbackRequest({
    articleId,
    precisionScore: payload.precisionScore,
    styleScore: payload.styleScore,
    comment: payload.comment,
  });

  try {
    const r = await apiClient.articles_SubmitFeedback(articleId, request);
    return {
      precisionScore: r.precisionScore ?? null,
      styleScore: r.styleScore ?? null,
      feedbackComment: r.feedbackComment ?? null,
    };
  } catch (err) {
    if (SwaggerException.isSwaggerException(err) && err.status === 409) {
      return { alreadySubmitted: true };
    }
    throw err;
  }
},
```

Notes:
- The exact `SubmitArticleFeedbackPayload` type name above must match the type the current `mutationFn` already accepts. If the current file uses a different name (e.g. inline object type), preserve that name.
- The return-shape mapping (`r.precisionScore ?? null`, etc.) matches today's externally visible result. If the existing code uses `undefined` instead of `null`, mirror that — the rule is "consumer-visible behavior unchanged".

- [ ] **Step 4: Remove the `TODO(arch-review 2026-05-25)` block**

Delete the entire `// TODO(arch-review 2026-05-25): …` comment (it may span multiple lines). This is required by FR-1's acceptance criteria.

- [ ] **Step 5: Verify no `as any` or `.http` remains in `useSubmitArticleFeedbackMutation`**

Run from `frontend/`:
```
grep -n "as any\|\.http\." src/api/hooks/useArticles.ts | grep -i "submit\|feedback" || echo "clean"
```

If the file has other functions that legitimately use `(apiClient as any).baseUrl` (sibling hooks like `useArticleFeedbackQuery` may still use it — those are out of scope per arch-review Decision 3), the grep above filters by name; expect `clean`. For the broader file-level sanity check (not blocking):
```
grep -c "as any" src/api/hooks/useArticles.ts
```
The count should be **strictly less** than before the refactor.

- [ ] **Step 6: Run the three new tests and confirm they now pass**

Run:
```
cd frontend && npx jest src/api/hooks/__tests__/useArticles.test.ts -t "useSubmitArticleFeedbackMutation" --no-coverage
```

Expected: all three pass.

If the 409 test fails with `SwaggerException.isSwaggerException` returning `false`, double-check the test instantiates the **same** `SwaggerException` class that the hook imports (both from `'../../generated/api-client'` — relative paths resolved from their respective files). If the project uses a `setupTests.ts` that resets modules, the class identity may differ; fix by importing once at the top of the test file (the import added in Task 1 Step 3).

- [ ] **Step 7: Run the full `useArticles.test.ts` suite to catch regressions**

Run:
```
cd frontend && npx jest src/api/hooks/__tests__/useArticles.test.ts --no-coverage
```

Expected: every test in the file passes, including the pre-existing `articles_FeedbackList`-based tests. If a pre-existing test broke because the shared `mockClient` object now exposes extra methods, restore the original mock object literal style (per-method `jest.fn()` properties) — adding a property never breaks reads on other properties.

- [ ] **Step 8: Run the project TypeScript compile to catch type drift**

Run:
```
cd frontend && npx tsc --noEmit
```

Expected: exit code 0. The strict-mode build (which the project already enforces) ensures `SubmitArticleFeedbackRequest` field renames after a future NSwag regeneration will surface here at compile time — satisfying NFR-3.

- [ ] **Step 9: Run the project linter on the touched files**

Run:
```
cd frontend && npx eslint src/api/hooks/useArticles.ts src/api/hooks/__tests__/useArticles.test.ts
```

Expected: no errors. Fix any introduced warnings (unused imports are the most likely).

- [ ] **Step 10: Commit**

```
git add frontend/src/api/hooks/useArticles.ts frontend/src/api/hooks/__tests__/useArticles.test.ts
git commit -m "refactor(articles): use generated typed feedback submission, drop as-any

Replace (apiClient as any).http.fetch + hand-rolled URL/body in
useSubmitArticleFeedbackMutation with a typed call to
apiClient.articles_SubmitFeedback(...). Detect HTTP 409 via
SwaggerException.isSwaggerException(err) && err.status === 409,
keeping the externally visible mutation result unchanged. Suppress
the global error toast for this mutation only (expected business
409). Drops the arch-review 2026-05-25 TODO.

Refs: arch-review 2026-05-25"
```

---

## Task 3: File the FR-6 follow-up GitHub issue

**Files:**
- None local. Creates an issue in the project's GitHub repo via `gh`.

- [ ] **Step 1: Confirm `gh` is authenticated and pointed at the right repo**

Run:
```
gh repo view --json nameWithOwner
```

Expected output includes the project's `owner/repo`. If `gh` is not authenticated, run `gh auth login` and retry.

- [ ] **Step 2: File the follow-up issue**

Run (HEREDOC, exact content):

```
gh issue create \
  --title "Convert sibling feedback-submit hooks + docs to typed SwaggerException pattern (follow-up to arch-review 2026-05-25)" \
  --body "$(cat <<'EOF'
This issue tracks the broader rollout of the pattern applied in #<this-PR-number> to `useSubmitArticleFeedbackMutation`. That PR replaced `(apiClient as any).http.fetch` + hand-rolled URL/body construction with a typed call to the generated `articles_SubmitFeedback` and `SwaggerException.isSwaggerException(err) && err.status === 409` for the 409 branch.

The remaining work, per the arch-review §"Specification Amendments" §5:

1. **Convert sibling hooks** that repeat the same private-fetch + 409 pattern:
   - `frontend/src/api/hooks/useKnowledgeBase.ts` — `useSubmitFeedbackMutation` (approx. lines 387-410).
   - `frontend/src/api/hooks/useLeaflet.ts` — feedback submission (approx. lines 286-315).
   Use the same `try { await apiClient.<method>(...) } catch (err) { if (SwaggerException.isSwaggerException(err) && err.status === 409) … }` shape.

2. **Update guidance docs** that currently teach the `(apiClient as any).baseUrl / .http.fetch` pattern as recommended:
   - `docs/development/api-client-generation.md` — the "CRITICAL: URL Construction Rules" section.
   - `memory/gotchas/api-client-must-use-absolute-urls.md`.
   New guidance: prefer the generated typed methods + `SwaggerException` for status-based branching; reserve `(apiClient as any).baseUrl` only for endpoints that genuinely have no generated wrapper (e.g. file uploads streamed via raw fetch).

3. **Add a typed escape hatch** to `getAuthenticatedApiClient` (or `authenticatedHttp.fetch`) — e.g. `expectedStatuses?: number[]` — so individual mutations can opt out of the global error toast for known business-conflict status codes without disabling toasts wholesale. This removes the current need to pass the boolean `false` and replaces it with a precise opt-out.

4. **(Optional, long-term)** Change the NSwag template (or specific endpoints) so 409 is returned as a discriminated typed result for endpoints where 409 is a business outcome, removing the need for try/catch on status. This was the original framing in the spec's FR-6 but is the lowest-priority of the four.

Context:
- The current `(apiClient as any).baseUrl / .http.fetch` pattern is used by 34 hook files (hundreds of occurrences). Items (1) and (2) should land together so the codebase and docs stay consistent.
- The original audit comment that surfaced this debt was `arch-review 2026-05-25` in `frontend/src/api/hooks/useArticles.ts` (now removed by the referencing PR).
EOF
)"
```

- [ ] **Step 3: Record the issue URL for the PR description**

`gh issue create` prints the new issue URL on stdout. Save it; it must be linked from the PR that lands this plan's work (FR-6 acceptance criterion).

---

## Task 4: Verify consumer (`ArticleFeedbackSection.tsx`) needs no edit

**Files:**
- Read-only: `frontend/src/components/articles/ArticleFeedbackSection.tsx` (or whichever path the consumer lives at — find it via `grep -rl useSubmitArticleFeedbackMutation frontend/src`).

- [ ] **Step 1: Locate the consumer**

Run:
```
grep -rl useSubmitArticleFeedbackMutation frontend/src
```

- [ ] **Step 2: Confirm the consumer reads `alreadySubmitted` / score fields from the mutation result**

Open the file and verify the mutation result is accessed via the same field names this plan preserved (`alreadySubmitted`, `precisionScore`, `styleScore`, `feedbackComment`). If the consumer accesses a field this plan didn't preserve, **stop**: the result mapping in Task 2 Step 3 must be amended to keep that field intact. Do not change the consumer.

- [ ] **Step 3: No commit needed** (read-only verification).

---

## Task 5: Final verification before PR

- [ ] **Step 1: Re-run the full frontend test suite**

Run:
```
cd frontend && npx jest --no-coverage
```

Expected: all tests pass. If unrelated tests fail, they are pre-existing flakes — note in the PR description and confirm by running them on `main`.

- [ ] **Step 2: Confirm the working tree contains only the intended changes**

Run:
```
git status
git diff --stat HEAD~1
```

Expected: exactly two files modified — `frontend/src/api/hooks/useArticles.ts` and `frontend/src/api/hooks/__tests__/useArticles.test.ts`.

- [ ] **Step 3: Open the PR (only if the user has asked to push)**

Defer to the user. If asked to open, the PR description must:
- Link the FR-6 follow-up issue created in Task 3.
- Call out (per arch-review §Risks "deviation from documented pattern") that this PR intentionally deviates from `docs/development/api-client-generation.md` for one hook, with the doc + sibling-hook rollout tracked in the follow-up issue.
- Note that the 409 global-toast suppression fixes a latent bug in the prior implementation (where 409 fired the generic error toast before the hook could branch).

---

## Spec Coverage Cross-Check

| Spec / Arch-Review Requirement | Implemented by |
|---|---|
| FR-1: remove `as any` casts and `apiClient.http` access in this hook; remove TODO | Task 2 Steps 3, 4, 5 |
| FR-2: preserve typed 409 → `{ alreadySubmitted: true }`; 2xx → mapped scores; other → rejection | Task 1 Steps 4–6 (tests) + Task 2 Step 3 (impl) |
| FR-3: **dropped** by arch-review Amendment §1 (no `getApiBaseUrl()` needed) | N/A — explicitly out of scope |
| FR-4: typed request body via generated `SubmitArticleFeedbackRequest` | Task 2 Step 3 (`new SubmitArticleFeedbackRequest({...})`) |
| FR-5: standard auth header path | Automatically satisfied — call routes through `authenticatedHttp.fetch` via the generated client; verified by Task 2 Step 5 (no new auth code in the diff) |
| FR-6: file GitHub issue covering sibling hooks, docs, escape hatch, NSwag template | Task 3 |
| NFR-1: performance unchanged | Same single POST; verified by inspection |
| NFR-2: security — no token logging, same origin, same headers | Same `authenticatedHttp.fetch` path; verified by Task 2 Step 5 |
| NFR-3: NSwag regeneration breakage surfaces at compile time, not runtime | Task 2 Step 8 (`tsc --noEmit`) + use of typed `SubmitArticleFeedbackRequest` |
| NFR-4: tests cover 2xx, 409, other-error | Task 1 Steps 4, 5, 6 |
| Arch-review Decision 2: suppress global toast | Task 2 Step 3 (`getAuthenticatedApiClient(false)`) |
| Arch-review Decision 3: scope strictly to `useArticles.ts` + its test | File Structure section + Task list |
| Arch-review Decision 4: do not edit docs / sibling hooks | Out-of-scope note + Task 3 (rolled into follow-up) |

No spec requirement is uncovered.