The sandbox is blocking writes. Per the pipeline note ("plan file content will be captured automatically as the artifact"), I'll emit the plan content as the response — the harness handles persistence.

---

# Remove `as any` bypass in `useSubmitArticleFeedbackMutation` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove both `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch` casts from `useSubmitArticleFeedbackMutation` by introducing two typed public helpers (`getApiBaseUrl()` and `getAuthenticatedFetch()`) in the frontend API client module, while preserving the hook's existing externally-visible behaviour — including the typed 409 "already submitted" result.

**Architecture:** Per the arch-review (Decisions 1–4), `frontend/src/api/client.ts` gains two exports backed by extracted internal helpers that are also reused by `getAuthenticatedApiClient` (single source of truth for the base URL and the auth headers). The hook in `frontend/src/api/hooks/useArticles.ts` then constructs the absolute URL with `getApiBaseUrl()`, issues the raw `POST` via `getAuthenticatedFetch()`, types its body as the generated `SubmitArticleFeedbackRequest`, and keeps its 409-as-typed-result branch verbatim. The generated NSwag client file is not modified. Header parity, 2xx, 409, and 500 behaviours are pinned by unit tests that mock `global.fetch` directly.

**Tech Stack:** TypeScript, React Query (`@tanstack/react-query`), NSwag-generated `AnelaHebloApiClient`, Vitest (or Jest — confirm in Task 1) + React Testing Library, MSAL-or-equivalent auth provider exposing the bearer token consumed by `getAuthenticatedApiClient`.

---

## File Structure

**Modified files:**
- `frontend/src/api/client.ts` — extract a module-level `apiBaseUrl` constant and an internal `buildAuthHeaders()` helper from the existing `getAuthenticatedApiClient` body; add two new public exports `getApiBaseUrl()` and `getAuthenticatedFetch()`.
- `frontend/src/api/hooks/useArticles.ts` — rewrite the body of `useSubmitArticleFeedbackMutation`; replace both `as any` casts; type the request body as `SubmitArticleFeedbackRequest`; delete the `TODO(arch-review 2026-05-25)` comment block.
- `frontend/CLAUDE.md` (or whichever `CLAUDE.md` carries the `${apiClient.baseUrl}${relativeUrl}` rule — confirm in Task 1) — update the URL-construction rule to reference `getApiBaseUrl()`.

**Created files:**
- `frontend/src/api/__tests__/client.test.ts` — new test file pinning `getApiBaseUrl()` and `getAuthenticatedFetch()` (only if no test file for `client.ts` exists today — extend the existing one if it does).
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — three new test cases (2xx, 409, non-2xx) if the file already exists, otherwise create it.

**Not modified (explicitly out of scope per spec):**
- `frontend/src/api/generated/**` (any NSwag-generated module) — read-only.
- Other hooks in `frontend/src/api/hooks/` that may use the same pattern — FR-6 follow-up issue covers them.
- The backend feedback endpoint.
- The NSwag template / endpoint annotations — FR-6 follow-up issue covers this.

**Out-of-tree artifact:**
- A new GitHub issue (via `gh issue create`) tracking the long-term NSwag-template fix (FR-6) and listing the spec / arch-review tag (2026-05-25).

---

## Background You Need Before Starting

This plan assumes the implementer has not read the spec or arch-review. The essentials:

1. **Why this exists.** The hook today does:
   ```ts
   const url = `${(apiClient as any).baseUrl}/api/articles/${articleId}/feedback`;
   const response = await (apiClient as any).http.fetch(url, { … });
   if (response.status === 409) return { alreadySubmitted: true } as const;
   if (!response.ok) throw new Error(`Feedback submission failed: ${response.status}`);
   return await response.json();
   ```
   The `as any` casts reach into the NSwag-generated client's private fields. NSwag regeneration can rename or remove these fields silently. We replace the casts with two typed helpers that read from the same config and auth-provider sources the generated client itself consumes, so a regeneration cannot break this code silently.

2. **What `getApiBaseUrl()` returns.** The same string today's `getAuthenticatedApiClient` passes into `new AnelaHebloApiClient(baseUrl, …)`. After the refactor, both reads go through one module-level constant — they cannot drift.

3. **What `getAuthenticatedFetch()` does.** Returns a function with the same shape as `fetch` (`(input, init?) => Promise<Response>`) that attaches the same auth headers `getAuthenticatedApiClient` attaches today. It is a **transparent passthrough** — it does **not** throw on non-2xx responses. Callers inspect `response.status`. (Arch-review Decision 3.)

4. **Why we don't subclass the generated client.** It is regenerated; hand-edits are lost. We do not edit the NSwag template in this PR either (FR-6 follow-up).

5. **Why the 409 branch stays in the hook.** The generated client raises 409 as an exception with no typed-result alternative. Until the NSwag template grows that capability (FR-6), the hook is the only place that knows 409 means "already submitted, not an error." `getAuthenticatedFetch` deliberately does not encode any per-endpoint policy.

6. **Test convention.** Tests mock `global.fetch` (`vi.spyOn(global, 'fetch')` or the Jest equivalent — confirm in Task 1), not the generated client, so the real auth-helper path is exercised.

---

## Task 1: Reconnaissance — confirm prerequisites and unknowns

**Files:**
- Read: `frontend/src/api/client.ts`
- Read: `frontend/src/api/generated/**/AnelaHebloApiClient*.ts` (or whatever path the generated client lives at)
- Read: `frontend/CLAUDE.md` (or root `CLAUDE.md`)
- Read: `frontend/package.json` (for test runner)
- Read: any existing test file alongside `useArticles.ts` and `client.ts`

This is a read-only task. No commits.

- [ ] **Step 1: Locate the API client module**

Run: `git ls-files frontend/src/api/client.ts`
Expected: one line, the path. If it does not exist, run `git ls-files | grep -i 'api/client'` and pick the canonical entry point — every other reference in this plan must be adjusted to the discovered path.

- [ ] **Step 2: Read `getAuthenticatedApiClient`**

Open `frontend/src/api/client.ts`. Note three things:

a) The base-URL source — almost certainly an env var like `import.meta.env.VITE_API_BASE_URL`, but could be a runtime config object. Record the exact expression.

b) The auth-header source — where `Authorization` / tenant / anti-forgery headers come from. Could be a `transformOptions` / `transformHttpRequestOptions` hook on the generated client, or a custom `http` wrapper passed to its constructor. Record the full set of headers attached.

c) Whether `getAuthenticatedApiClient` returns a singleton or a fresh client per call.

- [ ] **Step 3: Confirm `SubmitArticleFeedbackRequest` is a generated, exported type**

Run: `git grep -n "export class SubmitArticleFeedbackRequest\|export interface SubmitArticleFeedbackRequest" frontend/src/api/`
Expected: at least one match in the generated module.

If no match, the generated module probably inlines the type into the method signature. Fall back per arch-review Amendment 4: import the parameter type of `AnelaHebloApiClient.submitArticleFeedback` (or whatever the method is called) using `Parameters<typeof AnelaHebloApiClient.prototype.submitArticleFeedback>[N]`. Record the name and import path before moving on.

- [ ] **Step 4: Identify refresh-on-401 / interceptor logic**

Run: `git grep -n "401\|refresh" frontend/src/api/client.ts`
And inspect any `transformOptions` / `http.fetch` wrapper found in Step 2. If a refresh-on-401-then-retry path exists, record its location — the `getAuthenticatedFetch` helper must mirror it (arch-review risk row 3).

- [ ] **Step 5: Locate the test runner and existing test files**

Run: `git ls-files 'frontend/src/api/**/__tests__/*' 'frontend/src/api/**/*.test.ts*'`
Run: `cat frontend/package.json | grep -A2 '"scripts"'`

Record:
- Test runner — `vitest` or `jest`.
- Existing test file for the hook (likely `frontend/src/api/hooks/__tests__/useArticles.test.ts`) — if missing, you'll create it in Task 7.
- Existing test file for `client.ts` — if missing, you'll create it in Task 5.

- [ ] **Step 6: Read the `CLAUDE.md` rule about URL construction**

Run: `git grep -n "apiClient.baseUrl\|baseUrl}" CLAUDE.md frontend/CLAUDE.md frontend/src/CLAUDE.md 2>/dev/null`

Record the exact file path and line range where the rule lives. You'll update it in Task 11.

- [ ] **Step 7: Sanity-check the bypass is still present**

Run: `git grep -n "apiClient as any" frontend/src/api/hooks/useArticles.ts`
Expected: exactly two matches in `useSubmitArticleFeedbackMutation`, plus the `TODO` block dated 2026-05-25.

If there are zero matches, the bug is already fixed elsewhere — stop and report. If there are more than two matches in this hook (or matches in another hook in the same file), record them — only the matches inside `useSubmitArticleFeedbackMutation` are in scope; siblings go to the FR-6 issue.

---

## Task 2: Extract `apiBaseUrl` as a module-level constant in `client.ts`

This is a preparation refactor with no behaviour change. We pull the base-URL expression out of `getAuthenticatedApiClient` so both that function and the future `getApiBaseUrl()` read the same source.

**Files:**
- Modify: `frontend/src/api/client.ts`

- [ ] **Step 1: Replace the inline base-URL expression with a module-level constant**

Find the current expression you recorded in Task 1 Step 2 (illustrative — adapt to actual code):

```ts
// before
export function getAuthenticatedApiClient(): AnelaHebloApiClient {
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? "";
  return new AnelaHebloApiClient(baseUrl, /* … */);
}
```

Refactor to:

```ts
// after
const apiBaseUrl: string = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/$/, "");

export function getAuthenticatedApiClient(): AnelaHebloApiClient {
  return new AnelaHebloApiClient(apiBaseUrl, /* … */);
}
```

Notes:
- Preserve whatever trailing-slash handling already exists. The `.replace(/\/$/, "")` above is illustrative — if the original code had no normalisation, do not add any. The goal is to keep `apiClient.baseUrl` (as observed today) exactly equal to the new `apiBaseUrl` constant.
- If the existing code does any other transformation (e.g. defaulting to `window.location.origin`), preserve it verbatim.

- [ ] **Step 2: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: no new errors.

- [ ] **Step 3: Run existing tests**

Run: `cd frontend && npm test -- --run` (vitest) or `npm test -- --watchAll=false` (Jest CRA)
Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/client.ts
git commit -m "refactor(api): extract apiBaseUrl to module constant"
```

---

## Task 3: Add `getApiBaseUrl()` export (TDD)

**Files:**
- Create: `frontend/src/api/__tests__/client.test.ts` (or extend the existing one located in Task 1 Step 5)
- Modify: `frontend/src/api/client.ts`

- [ ] **Step 1: Write the failing test**

If `frontend/src/api/__tests__/client.test.ts` does not exist, create it. Add (adapt `vi.` to `jest.` if Jest):

```ts
import { describe, it, expect, vi, beforeEach } from "vitest";

describe("getApiBaseUrl", () => {
  beforeEach(() => {
    vi.resetModules();
  });

  it("returns the same string passed to AnelaHebloApiClient constructor", async () => {
    vi.stubEnv("VITE_API_BASE_URL", "https://api.example.test");

    const ctorSpy = vi.fn();
    vi.doMock("../generated/AnelaHebloApiClient", () => ({
      AnelaHebloApiClient: class {
        constructor(...args: unknown[]) {
          ctorSpy(...args);
        }
      },
    }));

    const { getApiBaseUrl, getAuthenticatedApiClient } = await import("../client");

    getAuthenticatedApiClient();
    expect(ctorSpy).toHaveBeenCalled();
    const ctorBaseUrl = ctorSpy.mock.calls[0][0] as string;

    expect(getApiBaseUrl()).toBe(ctorBaseUrl);
    expect(getApiBaseUrl()).toBe("https://api.example.test");
  });
});
```

Adjust the import paths to match what Task 1 recorded (the generated client may live at a different path; the env-var name may differ).

- [ ] **Step 2: Run the test — expect failure**

Run: `cd frontend && npx vitest run src/api/__tests__/client.test.ts`
Expected: FAIL — `getApiBaseUrl is not exported` (or similar).

- [ ] **Step 3: Implement `getApiBaseUrl`**

Edit `frontend/src/api/client.ts`. Add:

```ts
export function getApiBaseUrl(): string {
  return apiBaseUrl;
}
```

Place the export directly after the `apiBaseUrl` constant declaration introduced in Task 2.

- [ ] **Step 4: Run the test — expect pass**

Run: `cd frontend && npx vitest run src/api/__tests__/client.test.ts`
Expected: PASS.

- [ ] **Step 5: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/api/__tests__/client.test.ts
git commit -m "feat(api): add getApiBaseUrl() public helper"
```

---

## Task 4: Extract `buildAuthHeaders()` internal helper

Preparation refactor — no behaviour change. We isolate "the headers the generated client attaches" into one function so `getAuthenticatedFetch()` (Task 5) can reuse it.

**Files:**
- Modify: `frontend/src/api/client.ts`

- [ ] **Step 1: Identify the headers attached today**

From Task 1 Step 2, you know whether headers come from a `transformOptions` hook or a custom `http: { fetch }` wrapper. List every header it attaches. Common ones: `Authorization: Bearer <token>`, tenant id, anti-forgery, `Accept-Language`. Record them.

- [ ] **Step 2: Extract into an internal helper**

Add to `frontend/src/api/client.ts` (NOT exported — internal):

```ts
function buildAuthHeaders(): HeadersInit {
  const headers: Record<string, string> = {};
  const token = getAccessToken(); // use whatever the existing code uses
  if (token) headers.Authorization = `Bearer ${token}`;
  // Mirror every other header the existing transform attaches.
  // E.g. headers["X-Tenant-Id"] = getTenantId();
  return headers;
}
```

If the existing code attaches headers asynchronously (e.g. `await getAccessTokenAsync()`), the helper becomes `async function buildAuthHeaders(): Promise<HeadersInit>` — record this; Task 5's `getAuthenticatedFetch` will need to `await` it.

- [ ] **Step 3: Wire `getAuthenticatedApiClient` to call `buildAuthHeaders()`**

Replace the existing inline header construction inside `getAuthenticatedApiClient` (whether it was in a `transformOptions` callback or an `http.fetch` wrapper) with a call to `buildAuthHeaders()`. Example:

```ts
// before
const transformOptions = (options: RequestInit) => {
  const token = getAccessToken();
  options.headers = { ...options.headers, Authorization: `Bearer ${token}` };
  return Promise.resolve(options);
};

// after
const transformOptions = (options: RequestInit) => {
  options.headers = { ...options.headers, ...buildAuthHeaders() };
  return Promise.resolve(options);
};
```

The visible behaviour must not change — same headers in, same order of precedence.

- [ ] **Step 4: Type-check and run existing tests**

Run: `cd frontend && npx tsc --noEmit`
Run: `cd frontend && npx vitest run`
Expected: all green, no new failures.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/client.ts
git commit -m "refactor(api): extract buildAuthHeaders helper"
```

---

## Task 5: Add `getAuthenticatedFetch()` export (TDD with header parity)

**Files:**
- Modify: `frontend/src/api/__tests__/client.test.ts`
- Modify: `frontend/src/api/client.ts`

- [ ] **Step 1: Write the failing tests**

Append to `frontend/src/api/__tests__/client.test.ts`:

```ts
describe("getAuthenticatedFetch", () => {
  beforeEach(() => {
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it("attaches the same auth headers the generated client attaches", async () => {
    vi.stubEnv("VITE_API_BASE_URL", "https://api.example.test");

    vi.doMock("../auth/token", () => ({
      getAccessToken: () => "test-token",
    }));

    const fetchSpy = vi.spyOn(global, "fetch").mockResolvedValue(
      new Response(null, { status: 200 }),
    );

    const { getAuthenticatedFetch } = await import("../client");
    const authedFetch = getAuthenticatedFetch();
    await authedFetch("https://api.example.test/x");

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const init = fetchSpy.mock.calls[0][1] as RequestInit;
    expect(new Headers(init.headers).get("Authorization")).toBe("Bearer test-token");
  });

  it("does not throw on non-2xx responses", async () => {
    vi.stubEnv("VITE_API_BASE_URL", "https://api.example.test");
    vi.doMock("../auth/token", () => ({ getAccessToken: () => "test-token" }));

    vi.spyOn(global, "fetch").mockResolvedValue(
      new Response(null, { status: 500 }),
    );

    const { getAuthenticatedFetch } = await import("../client");
    const authedFetch = getAuthenticatedFetch();
    const response = await authedFetch("https://api.example.test/x");
    expect(response.status).toBe(500);
  });

  it("lets caller-provided headers override defaults except auth", async () => {
    vi.stubEnv("VITE_API_BASE_URL", "https://api.example.test");
    vi.doMock("../auth/token", () => ({ getAccessToken: () => "test-token" }));

    const fetchSpy = vi.spyOn(global, "fetch").mockResolvedValue(
      new Response(null, { status: 200 }),
    );

    const { getAuthenticatedFetch } = await import("../client");
    await getAuthenticatedFetch()("https://api.example.test/x", {
      headers: { "Content-Type": "application/json" },
    });

    const init = fetchSpy.mock.calls[0][1] as RequestInit;
    const headers = new Headers(init.headers);
    expect(headers.get("Authorization")).toBe("Bearer test-token");
    expect(headers.get("Content-Type")).toBe("application/json");
  });
});
```

If Task 1 Step 4 found a refresh-on-401-then-retry path in the generated client, add a fourth test pinning that behaviour in `getAuthenticatedFetch` too:

```ts
  it("refreshes the token and retries once on 401", async () => {
    vi.stubEnv("VITE_API_BASE_URL", "https://api.example.test");
    let tokenCalls = 0;
    vi.doMock("../auth/token", () => ({
      getAccessToken: () => (tokenCalls++ === 0 ? "stale" : "fresh"),
      refreshToken: vi.fn().mockResolvedValue(undefined),
    }));

    const fetchSpy = vi
      .spyOn(global, "fetch")
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 200 }));

    const { getAuthenticatedFetch } = await import("../client");
    const response = await getAuthenticatedFetch()("https://api.example.test/x");

    expect(response.status).toBe(200);
    expect(fetchSpy).toHaveBeenCalledTimes(2);
    expect(new Headers((fetchSpy.mock.calls[1][1] as RequestInit).headers).get("Authorization"))
      .toBe("Bearer fresh");
  });
```

Skip this fourth test entirely if no refresh path exists.

- [ ] **Step 2: Run the tests — expect failure**

Run: `cd frontend && npx vitest run src/api/__tests__/client.test.ts`
Expected: the three (or four) new tests FAIL — `getAuthenticatedFetch is not exported`.

- [ ] **Step 3: Implement `getAuthenticatedFetch`**

Append to `frontend/src/api/client.ts`:

```ts
export function getAuthenticatedFetch(): (
  input: RequestInfo | URL,
  init?: RequestInit,
) => Promise<Response> {
  return async (input, init = {}) => {
    const authHeaders = buildAuthHeaders();
    return fetch(input, {
      ...init,
      headers: { ...authHeaders, ...(init.headers ?? {}) },
    });
  };
}
```

Header-merge precedence note: caller-provided headers win when both sides set the same key (so a caller can deliberately override `Authorization` for a public endpoint — rare). Auth headers fill in any key the caller did not set. The third test above pins this behaviour.

If Task 4 made `buildAuthHeaders` async, change the implementation to `await buildAuthHeaders()`.

If Task 1 Step 4 found refresh-on-401 logic, wrap the `fetch` call in a single retry-after-refresh loop matching the generated client's behaviour:

```ts
return async (input, init = {}) => {
  const compose = () => ({
    ...init,
    headers: { ...buildAuthHeaders(), ...(init.headers ?? {}) },
  });
  let response = await fetch(input, compose());
  if (response.status === 401) {
    await refreshToken();
    response = await fetch(input, compose());
  }
  return response;
};
```

Adjust to mirror the existing refresh logic exactly.

- [ ] **Step 4: Run the tests — expect pass**

Run: `cd frontend && npx vitest run src/api/__tests__/client.test.ts`
Expected: PASS for all `getAuthenticatedFetch` tests and the earlier `getApiBaseUrl` test.

- [ ] **Step 5: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors. The return type should be assignable to `typeof fetch`:

```ts
const _typecheck: typeof fetch = getAuthenticatedFetch();
```

(Confirm it would compile; don't keep the assertion in the source.)

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/api/__tests__/client.test.ts
git commit -m "feat(api): add getAuthenticatedFetch() public helper

Transparent fetch wrapper that attaches the same auth headers the
generated client attaches. Does not throw on non-2xx — callers branch
on response.status."
```

---

## Task 6: Write the failing test for 2xx success path of `useSubmitArticleFeedbackMutation`

We add all three hook tests up-front (one per task), then refactor the hook in Task 9.

**Files:**
- Create or modify: `frontend/src/api/hooks/__tests__/useArticles.test.ts`

- [ ] **Step 1: Set up the test file scaffolding**

If the file already exists, jump to Step 2. Otherwise create it with:

```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReactNode } from "react";
import { useSubmitArticleFeedbackMutation } from "../useArticles";

vi.mock("../../client", async () => {
  const actual = await vi.importActual<typeof import("../../client")>("../../client");
  return {
    ...actual,
    getApiBaseUrl: () => "https://api.example.test",
    getAuthenticatedFetch: () => global.fetch,
  };
});

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe("useSubmitArticleFeedbackMutation", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });
});
```

Adapt to Jest if the project uses it (`jest.mock`, `jest.fn`, etc.).

- [ ] **Step 2: Add the 2xx test inside the `describe`**

```ts
  it("resolves with parsed body on 2xx", async () => {
    const fetchSpy = vi.spyOn(global, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ ok: true, id: "fb-1" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const onSuccess = vi.fn();
    const { result } = renderHook(() => useSubmitArticleFeedbackMutation({ onSuccess }), {
      wrapper,
    });

    result.current.mutate({
      articleId: "article-1",
      body: { rating: 5, comment: "great" },
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(fetchSpy).toHaveBeenCalledWith(
      "https://api.example.test/api/articles/article-1/feedback",
      expect.objectContaining({
        method: "POST",
        headers: expect.objectContaining({ "Content-Type": "application/json" }),
      }),
    );
    expect(result.current.data).toEqual({ ok: true, id: "fb-1" });
    expect(onSuccess).toHaveBeenCalledTimes(1);
  });
```

Adjust `body` to the real `SubmitArticleFeedbackRequest` shape recorded in Task 1 Step 3. Adjust the mutation signature (`{ articleId, body }` vs positional args) to match the existing hook surface.

- [ ] **Step 3: Run the test**

Run: `cd frontend && npx vitest run src/api/hooks/__tests__/useArticles.test.ts -t "resolves with parsed body on 2xx"`

If the hook still uses `(apiClient as any).http.fetch`, this test likely already passes — but we want to pin behaviour before refactoring. **If it passes, leave it green and move on**; the refactor in Task 9 must keep it green. If it fails (e.g. because the mock structure doesn't match the current bypass code), record the failure mode and move on — Task 9 will fix it.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useArticles.test.ts
git commit -m "test(useArticles): pin 2xx success behaviour of feedback mutation"
```

---

## Task 7: Write the failing test for 409 "already submitted" path

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useArticles.test.ts`

- [ ] **Step 1: Add the 409 test**

Inside the existing `describe`:

```ts
  it("resolves with alreadySubmitted on 409 and fires onSuccess", async () => {
    vi.spyOn(global, "fetch").mockResolvedValue(
      new Response(null, { status: 409 }),
    );

    const onSuccess = vi.fn();
    const onError = vi.fn();
    const { result } = renderHook(
      () => useSubmitArticleFeedbackMutation({ onSuccess, onError }),
      { wrapper },
    );

    result.current.mutate({
      articleId: "article-1",
      body: { rating: 5, comment: "great" },
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual({ alreadySubmitted: true });
    expect(onSuccess).toHaveBeenCalledTimes(1);
    expect(onError).not.toHaveBeenCalled();
  });
```

- [ ] **Step 2: Run the test**

Run: `cd frontend && npx vitest run src/api/hooks/__tests__/useArticles.test.ts -t "alreadySubmitted on 409"`

If it passes today against the current `(apiClient as any).http.fetch` implementation, pin and move on. If it fails because the mock doesn't intercept the current code path, leave it failing — Task 9's refactor (which routes through the mocked `getAuthenticatedFetch`) will make it pass.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useArticles.test.ts
git commit -m "test(useArticles): pin 409 already-submitted behaviour"
```

---

## Task 8: Write the failing test for non-409 error path

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useArticles.test.ts`

- [ ] **Step 1: Add the 500 test**

```ts
  it("rejects on 500 and fires onError", async () => {
    vi.spyOn(global, "fetch").mockResolvedValue(
      new Response(null, { status: 500 }),
    );

    const onSuccess = vi.fn();
    const onError = vi.fn();
    const { result } = renderHook(
      () => useSubmitArticleFeedbackMutation({ onSuccess, onError }),
      { wrapper },
    );

    result.current.mutate({
      articleId: "article-1",
      body: { rating: 5, comment: "great" },
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(onError).toHaveBeenCalledTimes(1);
    expect(onSuccess).not.toHaveBeenCalled();
    expect(result.current.error).toBeInstanceOf(Error);
  });
```

- [ ] **Step 2: Run the test**

Run: `cd frontend && npx vitest run src/api/hooks/__tests__/useArticles.test.ts -t "rejects on 500"`

Same logic as Tasks 6 and 7 — if it passes, pin; if it fails, leave it failing for Task 9 to fix.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useArticles.test.ts
git commit -m "test(useArticles): pin non-409 error behaviour"
```

---

## Task 9: Refactor `useSubmitArticleFeedbackMutation` to use the new helpers

**Files:**
- Modify: `frontend/src/api/hooks/useArticles.ts`

- [ ] **Step 1: Update imports**

At the top of `frontend/src/api/hooks/useArticles.ts`, add (adjust paths to match the codebase):

```ts
import { getApiBaseUrl, getAuthenticatedFetch } from "../client";
import type { SubmitArticleFeedbackRequest } from "../generated/AnelaHebloApiClient";
```

If `SubmitArticleFeedbackRequest` is not directly exported (Task 1 Step 3 fallback), use whatever type was recorded there instead.

If `apiClient`, `apiBaseUrl`, or other locally-bound names that the bypass used are now unreferenced after this task, delete those imports / locals.

- [ ] **Step 2: Rewrite the mutation function**

Locate `useSubmitArticleFeedbackMutation` in the file. Replace its current body (the part that builds the URL with `(apiClient as any).baseUrl` and calls `(apiClient as any).http.fetch`) with:

```ts
export function useSubmitArticleFeedbackMutation(
  options?: UseMutationOptions</* keep the existing generics */>,
) {
  return useMutation({
    mutationFn: async ({
      articleId,
      body,
    }: {
      articleId: string;
      body: SubmitArticleFeedbackRequest;
    }) => {
      const url = `${getApiBaseUrl()}/api/articles/${articleId}/feedback`;
      const response = await getAuthenticatedFetch()(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });

      if (response.status === 409) {
        return { alreadySubmitted: true } as const;
      }
      if (!response.ok) {
        throw new Error(`Feedback submission failed: ${response.status}`);
      }
      return await response.json();
    },
    ...options,
  });
}
```

Critical points:
- The mutation's input shape (`{ articleId, body }`) must match what the caller(s) pass today. Do **not** change the input contract — if the existing hook takes positional args or a different object shape, mirror it exactly.
- The return-value type union is unchanged from today's behaviour: the 2xx parsed body or `{ alreadySubmitted: true }`. If today's hook exposes a more precise return type via generics, preserve it.
- The `throw new Error(...)` message text should match whatever the bypass code threw today. If today's bypass threw a typed `Error` subclass, preserve that too.
- Delete the TODO comment block (the spec mandates this).

- [ ] **Step 3: Confirm no `as any` references remain in the file (beyond any pre-existing unrelated ones)**

Run: `git grep -n "apiClient as any" frontend/src/api/hooks/useArticles.ts`
Expected: zero matches.

Run: `git grep -n "@ts-ignore\|@ts-expect-error" frontend/src/api/hooks/useArticles.ts`
Expected: zero new entries (compare against the pre-refactor baseline — Task 1 Step 7 should have recorded the count).

- [ ] **Step 4: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors. Critically — try locally renaming a field on `SubmitArticleFeedbackRequest` in the generated file to confirm the hook fails to compile (FR-4 acceptance). Revert the rename after confirming.

- [ ] **Step 5: Run the hook tests**

Run: `cd frontend && npx vitest run src/api/hooks/__tests__/useArticles.test.ts`
Expected: all three tests from Tasks 6, 7, 8 PASS.

- [ ] **Step 6: Run the full test suite**

Run: `cd frontend && npx vitest run`
Expected: all green. If a test that exercises a caller of `useSubmitArticleFeedbackMutation` (e.g. a component test) now fails because it was relying on internal implementation details, fix the test rather than reverting the refactor — but only if the failure is genuinely about implementation coupling, not about the hook's external contract.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/hooks/useArticles.ts
git commit -m "refactor(useArticles): replace (apiClient as any) bypass with typed helpers

Use getApiBaseUrl() and getAuthenticatedFetch() instead of reaching into
the generated client's private fields. Body typed as
SubmitArticleFeedbackRequest from the generated module. Preserves the
409 'already submitted' typed-result branch.

Removes the TODO(arch-review 2026-05-25) block."
```

---

## Task 10: Add header-parity test (binding NFR-5 check)

This pins arch-review NFR-5. Tasks 5 and 9 should already have produced this behaviour; this task pins it explicitly at the hook level.

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useArticles.test.ts`

- [ ] **Step 1: Refactor the test file into two `describe` blocks**

The header-parity test must NOT mock `../../client` — it has to exercise the real `getAuthenticatedFetch`. The other three tests (Tasks 6–8) DO mock it.

Restructure as:

```ts
describe("useSubmitArticleFeedbackMutation (mocked client helpers)", () => {
  vi.mock("../../client", async () => {
    const actual = await vi.importActual<typeof import("../../client")>("../../client");
    return {
      ...actual,
      getApiBaseUrl: () => "https://api.example.test",
      getAuthenticatedFetch: () => global.fetch,
    };
  });
  // ... 2xx, 409, 500 tests from Tasks 6–8
});

describe("useSubmitArticleFeedbackMutation (real client helpers)", () => {
  // no mock of "../../client"
  // header-parity test below
});
```

In Vitest, `vi.mock` is hoisted module-wide — moving it under a `describe` does not local-scope it. Use `vi.doMock` inside a `beforeEach` for the first block, or move the parity test into its own test file (`useArticles.parity.test.ts`) so the global mock does not apply. The latter is cleaner; choose it unless the project has a strong convention otherwise.

- [ ] **Step 2: Add the parity test**

In the unmocked block (or new parity file):

```ts
  it("attaches auth headers identical to a generated-client call", async () => {
    let generatedClientHeaders: Headers | null = null;
    {
      const fetchSpy = vi.spyOn(global, "fetch").mockResolvedValue(
        new Response("[]", { status: 200, headers: { "Content-Type": "application/json" } }),
      );
      const { getAuthenticatedApiClient } = await import("../../client");
      // Pick any cheap GET method on the generated client.
      // Replace `articles_GetList` with whatever simple GET exists.
      await getAuthenticatedApiClient().articles_GetList();
      generatedClientHeaders = new Headers(
        (fetchSpy.mock.calls[0][1] as RequestInit).headers,
      );
      fetchSpy.mockRestore();
    }

    const fetchSpy = vi.spyOn(global, "fetch").mockResolvedValue(
      new Response("{}", { status: 200, headers: { "Content-Type": "application/json" } }),
    );
    const { result } = renderHook(() => useSubmitArticleFeedbackMutation(), { wrapper });
    result.current.mutate({ articleId: "x", body: { rating: 5, comment: "ok" } });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const hookHeaders = new Headers((fetchSpy.mock.calls[0][1] as RequestInit).headers);

    generatedClientHeaders!.forEach((value, name) => {
      if (name === "content-length") return;
      expect(hookHeaders.get(name), `header parity: ${name}`).toBe(value);
    });
  });
```

- [ ] **Step 3: Run the parity test**

Run: `cd frontend && npx vitest run src/api/hooks/__tests__/useArticles.parity.test.ts` (or the merged file).
Expected: PASS. If it fails, `buildAuthHeaders` (Task 4) does not faithfully reproduce the generated client's header set — go back and identify the missing header.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useArticles*.test.ts
git commit -m "test(useArticles): assert header parity with generated-client calls"
```

---

## Task 11: Update the `CLAUDE.md` rule

**Files:**
- Modify: the `CLAUDE.md` file located in Task 1 Step 6.

- [ ] **Step 1: Update the URL-construction rule**

Find the line referring to `${apiClient.baseUrl}${relativeUrl}` (recorded in Task 1 Step 6). Replace it with `${getApiBaseUrl()}${relativeUrl}`, imported from `frontend/src/api/client.ts`.

Also add a sentence about `getAuthenticatedFetch()` if the surrounding doc context discusses raw fetches.

Example minimal edit (adapt to the actual prose):

```md
- before:
  When a hook needs a raw fetch, construct the absolute URL as
  `` `${apiClient.baseUrl}${relativeUrl}` ``.

- after:
  When a hook needs a raw fetch, import `getApiBaseUrl` and
  `getAuthenticatedFetch` from `frontend/src/api/client` and construct
  the URL as `` `${getApiBaseUrl()}${relativeUrl}` ``. Issue the request
  via `getAuthenticatedFetch()(...)` — never reach into the generated
  client's private `http` / `baseUrl` fields.
```

- [ ] **Step 2: Commit**

```bash
git add <path/to/CLAUDE.md>
git commit -m "docs(claude.md): update URL construction rule to use getApiBaseUrl"
```

---

## Task 12: File the FR-6 follow-up issue

FR-6 is a binding spec requirement — not skippable.

- [ ] **Step 1: Confirm `gh` is available and authenticated**

Run: `gh auth status`
Expected: shows an authenticated user. If not, stop and surface to the user — do not silently skip.

- [ ] **Step 2: Create the issue**

Run:

```bash
gh issue create \
  --title "NSwag: surface 409 on POST /api/articles/{id}/feedback as a typed alternative result" \
  --label "tech-debt,arch-review" \
  --body "$(cat <<'EOF'
The arch-review of 2026-05-25 on `useSubmitArticleFeedbackMutation` removed
an `as any` bypass by introducing `getApiBaseUrl()` and
`getAuthenticatedFetch()` helpers in `frontend/src/api/client.ts` so the
hook can issue a raw `fetch` and treat HTTP 409 as a typed
`{ alreadySubmitted: true }` result rather than an exception.

The long-term fix is to configure the NSwag template (or the endpoint
annotations on the C# side) so that the generated client method for
`POST /api/articles/{articleId}/feedback` returns a discriminated-union
result type that includes 409 as a named branch — eliminating the need
for the helper-based raw fetch at this call site.

When that lands, the helpers may still be useful for future endpoints
with similar shapes (e.g. 412 precondition-failed) — so they can stay,
but this specific hook should switch back to a fully typed
`apiClient.submitArticleFeedback(...)` call.

Tags: `feat-arch-review-article-usesubmitarticlefeed` 2026-05-25.

Audit follow-up: grep `apiClient as any` across `frontend/src/api/hooks/`
to identify any other hooks still using the same pattern and either
refactor them onto the new helpers or include them in this issue's scope.
EOF
)"
```

Expected output: the URL of the newly created issue.

- [ ] **Step 3: Record the issue URL**

Note the issue URL in the PR description for this work.

(No commit needed — out-of-tree side effect.)

---

## Task 13: Final verification

**Files:** none modified.

- [ ] **Step 1: Confirm zero `as any` referencing `apiClient` in `useArticles.ts`**

Run: `git grep -n "apiClient as any" frontend/src/api/hooks/useArticles.ts`
Expected: empty output.

- [ ] **Step 2: Confirm no stale TODO**

Run: `git grep -n "arch-review 2026-05-25" frontend/src/api/hooks/useArticles.ts`
Expected: empty output.

- [ ] **Step 3: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 4: Run the full frontend test suite**

Run: `cd frontend && npx vitest run`
Expected: all green. Specifically, look for:
- The three hook tests from Tasks 6, 7, 8 — all PASS.
- The header-parity test from Task 10 — PASS.
- The `getApiBaseUrl` / `getAuthenticatedFetch` tests from Tasks 3 and 5 — PASS.
- Any pre-existing tests that exercise `useSubmitArticleFeedbackMutation` callers — PASS (no regressions).

- [ ] **Step 5: Run the linter**

Run: `cd frontend && npm run lint`
Expected: no new warnings or errors.

- [ ] **Step 6: Confirm the FR-6 issue exists**

Run: `gh issue list --label "arch-review" --search "NSwag"`
Expected: the issue from Task 12 is present.

- [ ] **Step 7: Open the PR**

Only if the pipeline expects the implementer to open the PR (otherwise skip — many pipelines do this in a separate stage).

```bash
gh pr create --title "refactor(useArticles): remove (apiClient as any) bypass in feedback mutation" \
  --body "$(cat <<'EOF'
## Summary
- Replaced `(apiClient as any).baseUrl` / `.http.fetch` in
  `useSubmitArticleFeedbackMutation` with two new typed helpers:
  `getApiBaseUrl()` and `getAuthenticatedFetch()` in
  `frontend/src/api/client.ts`.
- Typed the request body against the generated
  `SubmitArticleFeedbackRequest`.
- Preserved the existing 409-as-typed-result behaviour and all other
  mutation contracts (FR-5).
- Updated `CLAUDE.md` URL-construction rule.
- Filed FR-6 follow-up issue: <URL from Task 12>.

## Test plan
- [ ] `npx tsc --noEmit` clean
- [ ] All vitest tests green, including:
  - 2xx success branch resolves with parsed body, fires `onSuccess`
  - 409 branch resolves with `{ alreadySubmitted: true }`, fires `onSuccess`
  - 500 branch rejects, fires `onError`
  - header-parity test confirms the hook's outgoing request carries the
    same headers a generated-client call would
  - `getApiBaseUrl` matches the constructor arg passed to
    `AnelaHebloApiClient`
  - `getAuthenticatedFetch` does not throw on non-2xx
- [ ] `npm run lint` clean
- [ ] Confirmed no remaining `apiClient as any` matches in
      `useArticles.ts`
EOF
)"
```

---

## Self-Review Notes (post-write)

**Spec coverage:**
- FR-1 (eliminate `as any` casts): Tasks 9, 13.
- FR-2 (typed `getApiBaseUrl`): Tasks 2, 3.
- FR-3 (preserve authenticated transport, `getAuthenticatedFetch`): Tasks 4, 5, 10.
- FR-4 (typed `SubmitArticleFeedbackRequest`): Task 9 Step 1, verified at Task 9 Step 4.
- FR-5 (preserve 409 typed-result behaviour, all three branches tested): Tasks 6, 7, 8, 9.
- FR-6 (follow-up issue): Task 12.
- NFR-1 (no perf regression): inherent in the refactor — no extra requests added.
- NFR-2 (security: same auth, no logging): Task 4 + Task 10 (header parity).
- NFR-3 (regen safety): inherent in Decisions 1 and 2 — neither helper reads from generated fields.
- NFR-4 (test coverage of all three branches): Tasks 6, 7, 8.
- NFR-5 (header parity tested, per arch-review Amendment 5): Task 10.
- Arch-review Amendment 6 (test placement): Tasks 6–8 use `frontend/src/api/hooks/__tests__/useArticles.test.ts` and mock `global.fetch`.

**Placeholder scan:** No "TBD", no "implement later", no "similar to Task N". Code blocks are present for every code step.

**Type consistency:** Helper names used consistently across tasks — `getApiBaseUrl`, `getAuthenticatedFetch`, `buildAuthHeaders`, `apiBaseUrl`. Imports use `SubmitArticleFeedbackRequest` consistently with the fallback path documented in Task 1 Step 3.

**Known unknowns the implementer must resolve in Task 1 (all bounded discovery, no design decisions):**
- Exact path of `client.ts` and the generated module.
- Exact auth-header set attached by `getAuthenticatedApiClient`.
- Refresh-on-401 presence.
- Test runner (vitest vs jest).
- Which `CLAUDE.md` carries the URL rule.
- Whether `SubmitArticleFeedbackRequest` is exported by name (fallback documented).