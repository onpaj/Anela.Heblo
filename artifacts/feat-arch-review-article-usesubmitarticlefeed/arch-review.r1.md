Note: per the spec, the worktree holds only artifacts — no source available to grep. The review is grounded in the spec, brief, and CLAUDE.md rules cited there.

```markdown
# Architecture Review: Remove `as any` bypass in `useSubmitArticleFeedbackMutation`

## Skip Design: true

This is a typed-API refactor of a single React Query mutation hook. No new UI, no visual components, no UX surface changes. The mutation's caller contract (`onSuccess` with the same typed shape on both 2xx and 409, `onError` otherwise) is preserved verbatim per FR-5.

## Architectural Fit Assessment

The feature is a localised correctness/typing fix sitting at the **frontend API-client seam** — the boundary between the NSwag-generated `AnelaHebloApiClient` and the React Query hooks that consume it. It does not introduce new architectural concepts; it formalises one that the codebase already implies:

> Hooks may construct URLs as `${apiClient.baseUrl}${relativeUrl}` and may issue raw `fetch` calls when a typed-result branch is needed that the generated client does not expose.

Today that pattern is encoded in `CLAUDE.md` but enforced only socially — there is no public, typed seam for "base URL" or "authenticated fetch", so the one place that needs to deviate from a pure generated-client call (`useSubmitArticleFeedbackMutation`) had to reach through `as any`. This review's central recommendation is to **promote that seam from convention to typed module API**, so future hooks with the same need (and there will be more — anywhere a non-2xx status carries a typed-result meaning, e.g. 409 duplicate, 412 precondition, 410 gone) can use it without re-introducing `as any`.

Main integration points:
- **`frontend/src/api/client.ts`** (the module exporting `getAuthenticatedApiClient`) gains the two new helpers and becomes the sole owner of "how the frontend talks HTTP to the backend".
- **`frontend/src/api/hooks/useArticles.ts`** consumes them; no other hook is touched in this change (audit of other hooks is explicitly out of scope).
- **Generated `AnelaHebloApiClient` module** is read-only and not modified — touching it would lose the edit on next NSwag run.

The fit is good: the change moves *toward* the codebase's stated rules rather than introducing a new pattern.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│                       frontend/src/api/client.ts                      │
│                                                                       │
│   ┌──────────────────────────┐    ┌──────────────────────────────┐   │
│   │ config / env source      │───▶│ apiBaseUrl: string  (module) │   │
│   │ (VITE_API_BASE_URL etc.) │    └──────────────┬───────────────┘   │
│   └──────────────────────────┘                   │                    │
│                                                  ▼                    │
│   ┌──────────────────────────┐    ┌──────────────────────────────┐   │
│   │ auth token / header      │───▶│ buildAuthHeaders()           │   │
│   │ provider (existing)      │    │   (internal, not exported)   │   │
│   └────────────┬─────────────┘    └──────────────┬───────────────┘   │
│                │                                  │                    │
│                ▼                                  ▼                    │
│   ┌────────────────────────────────────────────────────────────┐     │
│   │ Public exports:                                             │     │
│   │   getAuthenticatedApiClient(): AnelaHebloApiClient          │     │
│   │   getApiBaseUrl():            string             (NEW)      │     │
│   │   getAuthenticatedFetch():    AuthedFetch        (NEW)      │     │
│   └─────────┬────────────────────────────┬──────────────────────┘     │
└─────────────┼────────────────────────────┼────────────────────────────┘
              │                            │
              ▼                            ▼
   ┌──────────────────────┐    ┌────────────────────────────────────┐
   │ Generated NSwag      │    │  frontend/src/api/hooks/useArticles│
   │ client (read-only,   │    │  useSubmitArticleFeedbackMutation: │
   │ regenerated)         │    │    body: SubmitArticleFeedbackRequest
   │                      │    │    url = `${getApiBaseUrl()}       │
   │  - all other hooks   │    │           /api/articles/{id}       │
   │    use this directly │    │           /feedback`                │
   └──────────────────────┘    │    resp = getAuthenticatedFetch()  │
                               │           (url, { method:'POST',   │
                               │             body: JSON.stringify   │
                               │           })                       │
                               │    if (resp.status === 409) → typed│
                               │    else if (!ok) throw             │
                               │    else → typed success            │
                               └────────────────────────────────────┘
```

The generated client is **not modified** and **not subclassed**. The two helpers live alongside `getAuthenticatedApiClient` and read from the same configuration sources it does — so an NSwag regeneration that reshapes `AnelaHebloApiClient` cannot break them.

### Key Design Decisions

#### Decision 1: Source the base URL from config, not from the generated client

**Options considered:**
- **A.** Read `apiClient.baseUrl` after confirming NSwag exposes it as a typed `public` field (or modifying the template to do so).
- **B.** Read the same env/runtime-config value (`VITE_API_BASE_URL` or equivalent) that `getAuthenticatedApiClient` already passes into the generated client's constructor, and re-export it via `getApiBaseUrl()`.

**Chosen approach:** **B.** `getApiBaseUrl()` returns the configured base URL string from the same source `getAuthenticatedApiClient` uses to build the client.

**Rationale:**
- Option A couples `getApiBaseUrl()`'s correctness to the NSwag template. NFR-3 explicitly requires the hook to survive an NSwag regeneration that renames `baseUrl`; option A fails that test silently if the field is renamed and TypeScript still considers `string` a valid type for whatever replaces it (e.g. a getter on a parent class).
- Option B treats config as the source of truth — which it already is, since the generated client also receives it from there. Two consumers reading the same source cannot drift.
- Option B does not require any edit to generated code or the NSwag template.
- Trailing-slash handling (FR-2 acceptance criterion) is governed by a single normalisation step in `client.ts` that both `getAuthenticatedApiClient` and `getApiBaseUrl` must share. See Implementation Guidance.

#### Decision 2: Introduce `getAuthenticatedFetch()` rather than reusing `apiClient.http.fetch`

**Options considered:**
- **A.** Expose the generated client's internal `http` transport via a typed cast or a thin public accessor.
- **B.** Build a small standalone authenticated-fetch wrapper in `client.ts` that pulls auth headers from the same provider the generated client uses, and use it from the hook.
- **C.** Use plain `fetch` from the hook and have the hook itself attach auth headers from the auth provider.

**Chosen approach:** **B.** A `getAuthenticatedFetch(): (input: RequestInfo, init?: RequestInit) => Promise<Response>` helper exported from `client.ts`.

**Rationale:**
- A re-creates the original problem under a thinner disguise — any change to NSwag's `http` shape still breaks the seam.
- C duplicates auth-header plumbing into every hook that needs raw `fetch`. The very next hook that needs typed-result handling for some other status code will copy the duplication, and the two copies will drift.
- B keeps a single point of truth: the same auth-header builder that `client.ts` already feeds to the generated client is reused. Adding tenant headers, anti-forgery tokens, or refresh logic later happens in one place.
- B's return type is a plain `(input, init) => Promise<Response>` — assignable to `typeof fetch` — so call sites read like a normal `fetch` call.

#### Decision 3: Keep the 409 branching inside the hook, not in `getAuthenticatedFetch`

**Options considered:**
- **A.** Have `getAuthenticatedFetch` throw on all non-2xx statuses, matching the generated client's semantics, and let the hook catch and inspect.
- **B.** Have `getAuthenticatedFetch` be a transparent passthrough that returns whatever `fetch` returns; the caller decides what's an error.

**Chosen approach:** **B.** `getAuthenticatedFetch` is a thin passthrough — it does **not** throw on non-2xx.

**Rationale:**
- The whole point of this seam is to let a specific caller treat a specific non-2xx status (409) as a typed result. A throwing helper would force every such call site to wrap in `try`/`catch` and re-throw, which is exactly the awkwardness the generated client already has. Keeping the helper transparent is the simplest contract that satisfies all callers, present and future.
- Network errors (DNS, abort, offline) still surface as rejections from the underlying `fetch`, which the hook should let propagate (matching today's behaviour from the bypass code).

#### Decision 4: Type the request body via the generated `SubmitArticleFeedbackRequest`

**Options considered:**
- **A.** Import the type from the generated client module and assert `body: SubmitArticleFeedbackRequest = { … }`.
- **B.** Redeclare an inline interface in the hook.

**Chosen approach:** **A.**

**Rationale:** FR-4 mandates this. The whole point is that a field rename on the C# DTO produces a compile error here. A redeclared inline interface defeats that. The generated module already exports `SubmitArticleFeedbackRequest` (NSwag generates types for every request DTO it sees); the hook imports it from the same path other hooks import generated types from.

## Implementation Guidance

### Directory / Module Structure

No new files. Two surgical edits:

```
frontend/src/api/
├── client.ts                  ← edit: add getApiBaseUrl(), getAuthenticatedFetch();
│                                      extract shared baseUrl + auth-header helpers
│                                      so getAuthenticatedApiClient and the new
│                                      helpers read from the same sources
└── hooks/
    └── useArticles.ts         ← edit: useSubmitArticleFeedbackMutation only
                                       — remove both `as any`, remove TODO block,
                                         import SubmitArticleFeedbackRequest,
                                         call getApiBaseUrl + getAuthenticatedFetch
```

Tests:

```
frontend/src/api/hooks/__tests__/useArticles.test.ts   ← add 3 cases (2xx, 409, 500)
                                                          if the file already exists,
                                                          extend it; if not, create it
                                                          colocated next to the hook
```

### Interfaces and Contracts

```ts
// frontend/src/api/client.ts  (additions only — existing exports unchanged)

/**
 * Resolves the configured backend base URL. Same value the NSwag-generated
 * client is constructed with. Use this when constructing absolute URLs for
 * a raw `fetch` call that needs to bypass the generated client (e.g. when a
 * specific non-2xx status carries a typed-result meaning).
 */
export function getApiBaseUrl(): string;

/**
 * Returns a fetch function that attaches the same authentication headers
 * that the NSwag-generated client attaches to its requests. Does NOT throw
 * on non-2xx responses — callers inspect `response.status` and decide.
 */
export function getAuthenticatedFetch(): (
  input: RequestInfo | URL,
  init?: RequestInit,
) => Promise<Response>;
```

Implementation sketch (illustrative — exact auth wiring depends on what `getAuthenticatedApiClient` does today, which the implementer must read):

```ts
// internal helpers (not exported)
const apiBaseUrl: string = normaliseBaseUrl(readApiBaseUrlFromConfig());
//   ^ same value passed to `new AnelaHebloApiClient(apiBaseUrl, ...)`

function buildAuthHeaders(): HeadersInit {
  // exact body matches whatever getAuthenticatedApiClient injects today
  // (bearer token, tenant id, anti-forgery, etc.) — single source of truth
}

export function getApiBaseUrl(): string {
  return apiBaseUrl;
}

export function getAuthenticatedFetch() {
  return (input: RequestInfo | URL, init: RequestInit = {}) =>
    fetch(input, {
      ...init,
      headers: { ...buildAuthHeaders(), ...(init.headers ?? {}) },
    });
}
```

> **Implementer's note:** `buildAuthHeaders` and `apiBaseUrl` must be extracted from the existing `getAuthenticatedApiClient` body — not parallel-implemented. If extraction would significantly rewrite that function, prefer letting `getAuthenticatedApiClient` continue to call the same helpers (DRY) over duplicating logic.

Hook call site:

```ts
// frontend/src/api/hooks/useArticles.ts (excerpt)
import { getApiBaseUrl, getAuthenticatedFetch } from '../client';
import type { SubmitArticleFeedbackRequest } from '../generated/AnelaHebloApiClient';
//                                                ^ adjust import path to match
//                                                  wherever the generated module
//                                                  exports its request DTOs

const body: SubmitArticleFeedbackRequest = { /* fields */ };
const url = `${getApiBaseUrl()}/api/articles/${articleId}/feedback`;
const response = await getAuthenticatedFetch()(url, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
});

if (response.status === 409) {
  return { alreadySubmitted: true } as const;   // existing typed result shape
}
if (!response.ok) {
  throw new Error(`Feedback submission failed: ${response.status}`);
}
return await response.json();                   // existing 2xx parse
```

### Data Flow

For the success path:

1. React component invokes mutation with `{ articleId, body }`.
2. Hook reads `getApiBaseUrl()` (synchronous, returns memoised module-level string).
3. Hook obtains `getAuthenticatedFetch()` (synchronous, returns a closure over current auth headers).
4. Hook serialises the typed `SubmitArticleFeedbackRequest` body, calls the authed fetch.
5. Inside the fetch helper, headers are composed: caller-provided headers override defaults, but auth headers are always present (defaults applied via spread order in the implementation).
6. Response returns. Branch on status:
   - `409` → resolve `{ alreadySubmitted: true }`, React Query routes to `onSuccess`.
   - `2xx` → resolve parsed body, React Query routes to `onSuccess`.
   - anything else → throw, React Query routes to `onError`.

For NSwag regeneration:

1. Generated client file is rewritten. Its internal `http`/`baseUrl` fields may rename or vanish.
2. `client.ts` does not read those fields. Compilation is unaffected.
3. If the generated **`SubmitArticleFeedbackRequest`** type renames a field, the hook fails to compile at the body literal — exactly the desired behaviour (FR-4 acceptance).
4. If the generated client's **constructor signature** changes (e.g. new required arg), `getAuthenticatedApiClient` fails to compile — surfacing the breakage at one well-known location.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Drift between `apiBaseUrl` value passed to the NSwag client and the value returned by `getApiBaseUrl()` (e.g. different trailing-slash handling, one source reads `VITE_*` and the other reads a different env var). | High | Extract a single `apiBaseUrl` module-level constant in `client.ts` and use it both when constructing the generated client and as the return of `getApiBaseUrl()`. Add a unit test asserting `getApiBaseUrl() === <the value passed to the client constructor>` via a spy on `AnelaHebloApiClient`'s constructor. |
| `getAuthenticatedFetch` misses an auth header the generated client attaches (e.g. tenant id, anti-forgery, `Accept-Language`, custom interceptor logic). 409-bypass call hits the backend without all required headers and is rejected as 401/403, masquerading as the "real error" branch. | High | Identify the **full** set of headers the generated client attaches by reading `getAuthenticatedApiClient` and the NSwag-generated `transformOptions`/`http` hooks. Encode all of them in `buildAuthHeaders`. Add a unit test that mocks `fetch` and asserts the outgoing `Request` headers include every header the generated client adds to a comparable call. |
| Refresh-token / interceptor logic lives inside the NSwag `http` field and silently re-issues 401s. `getAuthenticatedFetch` does not, so the new path skips refresh and the user sees a one-shot 401 instead of a re-authed success. | Medium | Audit the existing `apiClient.http` setup for refresh-on-401 behaviour. If present, replicate it in `getAuthenticatedFetch` (a `response.status === 401` → refresh → retry-once wrapper). If absent, no action. Document either outcome in the PR. |
| Test coverage drifts: someone later changes the success-shape parsing in the hook and the existing 2xx test still passes because it mocks a body shape that's no longer realistic. | Low | Have the 2xx test assert against a payload whose shape comes from the generated `SubmitArticleFeedbackResponse` type (if one exists) rather than an inline object. If no response type is generated, accept the trade-off and note it in a code comment. |
| `SubmitArticleFeedbackRequest` is not actually exported by the generated client (NSwag may inline request DTOs as anonymous types depending on its config). | Medium | Verify the type is exported before relying on it. If not, FR-4 can't be satisfied as written — fall back to importing the closest exported type (e.g. a parameters interface for the client method `submitArticleFeedback`) and amend the spec (see Specification Amendments). |
| Other hooks in `useArticles.ts` (or elsewhere) currently use the same `as any` bypass pattern and silently rot, since the audit is out of scope per the spec. | Low | Grep `apiClient as any` across `frontend/src/api/hooks/` during PR review. If hits exist, file follow-up issues per occurrence — do **not** widen scope of this PR. |

## Specification Amendments

1. **FR-2 base-URL source — clarify config-sourced.** The spec leaves "if `baseUrl` is not exposed publicly on the generated client, the helper resolves the base URL from the same configuration source the client uses" as a conditional fallback. This review elevates that to the primary approach unconditionally (Decision 1). FR-2 should be amended to state: *"`getApiBaseUrl()` returns the same configured base-URL string passed to the `AnelaHebloApiClient` constructor in `getAuthenticatedApiClient`, sourced from the project's runtime config. It does not read any field on the generated client."*

2. **FR-3 explicit helper.** The spec says `getAuthenticatedFetch` may be introduced "if the existing auth wiring is exposed only through `apiClient.http`". This review prescribes it unconditionally (Decision 2). FR-3 should be amended to state: *"A public `getAuthenticatedFetch()` helper is exported from `client.ts` and is the mechanism the hook uses to issue its raw `fetch`."*

3. **FR-3 non-throwing contract.** Add: *"`getAuthenticatedFetch` does not throw on non-2xx HTTP statuses; it is a transparent fetch wrapper. Callers inspect `response.status`."* (Decision 3 — closes an under-specified detail that would otherwise cause implementer churn.)

4. **FR-4 fallback if `SubmitArticleFeedbackRequest` is not exported.** Add: *"If NSwag does not export a named request type for this endpoint, import the closest available generated type (e.g. the parameter type of `AnelaHebloApiClient.submitArticleFeedback`) and use it; the type-safety acceptance criterion is satisfied as long as a field rename in the C# DTO produces a compile error at the hook."*

5. **New NFR: header parity tested.** Add NFR-5: *"A unit test asserts that the headers on the outgoing request from `useSubmitArticleFeedbackMutation` are a superset of (or equal to) the headers attached by a comparable call through the generated client. Refresh-on-401 behaviour, if present in the generated client, is mirrored in `getAuthenticatedFetch`."* — turns the "Risk: missed header" mitigation into a binding acceptance criterion.

6. **Test placement.** The spec lists three required unit tests (FR-5) but does not pick a file. Amend FR-5 to: *"Tests live in `frontend/src/api/hooks/__tests__/useArticles.test.ts`, mocking `global.fetch` (not the generated client) so the assertion covers the real code path including the auth helper."*

## Prerequisites

Before implementation begins, the implementer must read and confirm:

1. **`frontend/src/api/client.ts`** as it exists today — specifically:
   - the constructor call to `AnelaHebloApiClient` (to identify the base-URL config source);
   - any `transformOptions`, `transformHttpRequestOptions`, or `http`-field initialisation (to identify what auth headers are attached and whether refresh-on-401 is wired);
   - whether `getAuthenticatedApiClient` returns a memoised singleton or a fresh instance per call (affects whether `getAuthenticatedFetch`'s closure-over-headers semantics match).
2. **The NSwag-generated module** — confirm `SubmitArticleFeedbackRequest` is exported as a named type. If not, identify the fallback type per Amendment 4.
3. **Existing test infrastructure** — confirm whether the project uses `vi.spyOn(global, 'fetch')`, MSW, or another approach. The three new tests follow the project's existing convention rather than introducing a new one.
4. **CLAUDE.md** — re-read the rule about `${apiClient.baseUrl}${relativeUrl}` and confirm whether it should be updated to say `${getApiBaseUrl()}${relativeUrl}` once this PR lands. If yes, include that doc edit in the PR.
5. **The auth-header provider** — locate the module that supplies the bearer token (and any other auth headers). `getAuthenticatedFetch` must call into it on every invocation (not capture a stale token at module-load time), so identify whether the existing provider is sync or async. If async, the helper's signature becomes `async (input, init?) => Promise<Response>` — still assignable to `typeof fetch`'s shape from callers, but verify.

No infrastructure, migration, or backend prerequisites — this is a frontend-only refactor.
```