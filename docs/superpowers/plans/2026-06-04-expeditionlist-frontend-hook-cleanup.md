# Eliminate `as any` Casts in `useExpeditionListArchive.ts` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all five `(apiClient as any)` casts in `frontend/src/api/hooks/useExpeditionListArchive.ts` (plus a sixth one in `frontend/src/pages/ExpeditionListArchivePage.tsx`) with typed generated-client calls and the public `getApiBaseUrl()` / `getAuthenticatedFetch()` helpers, then extend the existing `MIGRATED_HOOKS` Jest guardrail so the rule cannot regress.

**Architecture:** Pure call-site migration — no backend, no OpenAPI regeneration, no new files. Four React Query hooks switch from hand-rolled `fetch` to `getAuthenticatedApiClient().expeditionListArchive_*` / `expeditionList_RunFix()`. One URL builder switches to `getApiBaseUrl()`. One page-level blob-fetch switches to `getAuthenticatedFetch()`. Hook return types stay byte-compatible with today's consumer (`ExpeditionListArchivePage`) by mapping the generated DTOs' `Date | undefined` / `number | undefined` to the page-friendly `string | null` / `number | null` inside each `queryFn`. The `MIGRATED_HOOKS` Set in `frontend/src/api/__tests__/authenticated-api-usage.test.ts` is extended with the migrated file, locking the rule.

**Tech Stack:** TypeScript, React 18, `@tanstack/react-query` v5, NSwag-generated `ApiClient`, Jest + React Testing Library (`renderHook` / `waitFor`), `jest.mock` for module-level mocking.

---

## File Structure

**Modify:**
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — rewrite all four hooks and the URL helper. Drop `ReprintExpeditionListRequest` local interface. Keep the other four local interfaces (they are the consumer-facing contract).
- `frontend/src/pages/ExpeditionListArchivePage.tsx:62-78` — replace `(apiClient as any).http.fetch(url, ...)` in `handleOpen` with `getAuthenticatedFetch()(url, ...)`. Drop the now-unused `getAuthenticatedApiClient` import; add `getAuthenticatedFetch` import.
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts:197` — add `"useExpeditionListArchive.ts"` to the `MIGRATED_HOOKS` Set.

**Create:**
- `frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts` — happy-path tests for the four hooks. Uses the `useArticles.test.ts` mocking pattern (`jest.mock("../../client", () => ({...}))`).

**Untouched but verified:**
- `frontend/src/api/client.ts` — `getAuthenticatedApiClient`, `getApiBaseUrl`, `getAuthenticatedFetch`, `QUERY_KEYS` already exist. No changes.
- `frontend/src/api/generated/api-client.ts` — `expeditionListArchive_GetDates` (line 2712), `expeditionListArchive_GetByDate` (2754), `expeditionListArchive_Reprint` (2832), `expeditionList_RunFix` (2870), and the `ReprintExpeditionListRequest` class (17604) already exist. No regeneration needed.
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — mocks the hook module wholesale (line 8) so signature-preserving refactors do not break it. Verify it still passes.

---

## Important Reference Patterns

The canonical reference for "typed-client mutation with class-instance request" is `useArticles.ts:216-248`:

```typescript
mutationFn: async (payload: SubmitArticleFeedbackPayload): Promise<SubmitArticleFeedbackResult> => {
  const client = getAuthenticatedApiClient();
  const request = new SubmitArticleFeedbackRequest({
    articleId,
    precisionScore: payload.precisionScore,
    // ...
  });

  try {
    const response = await client.articles_SubmitFeedback(articleId, request);
    return { /* map fields with ?? defaults */ };
  } catch (e: unknown) {
    const err = e as { status?: number };
    if (err.status === 409) { /* typed branch */ }
    throw e;
  }
},
```

The canonical reference for "Jest test of a hook that mocks `../../client` wholesale and asserts on the typed-method mock" is `useArticles.test.ts:1-80` (already-loaded scaffolding pattern).

The canonical reference for `MIGRATED_HOOKS` enforcement (the regression gate) is `authenticated-api-usage.test.ts:194-247` — already loaded in the codebase as `MIGRATED_HOOKS = new Set(["useArticles.ts"])`.

---

## Validation Commands (run after each task that touches code)

From `frontend/`:

```bash
npm run lint
npm run build           # includes tsc --noEmit
npm test -- --watchAll=false useExpeditionListArchive
npm test -- --watchAll=false authenticated-api-usage
npm test -- --watchAll=false ExpeditionListArchivePage
```

The final validation also runs the full test suite (`npm test -- --watchAll=false`).

---

## Task 1: Create the test scaffold + first failing test for `useExpeditionDates`

**Files:**
- Create: `frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts`

- [ ] **Step 1: Create the test file with shared scaffolding and a failing test for `useExpeditionDates`**

Create `frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts`:

```typescript
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
  useRunExpeditionListPrintFix,
} from '../useExpeditionListArchive';
import { ReprintExpeditionListRequest } from '../../generated/api-client';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  getApiBaseUrl: jest.fn(() => 'https://api.example.test'),
  getAuthenticatedFetch: jest.fn(),
  QUERY_KEYS: {
    expeditionListArchive: ['expedition-list-archive'],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return React.createElement(
    QueryClientProvider,
    { client: queryClient },
    children,
  );
};

describe('useExpeditionDates', () => {
  let mockGetDates: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetDates = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      expeditionListArchive_GetDates: mockGetDates,
    } as any);
  });

  it('calls expeditionListArchive_GetDates with the given page and pageSize and returns the mapped response', async () => {
    mockGetDates.mockResolvedValue({
      dates: ['2024-12-10', '2024-12-09'],
      totalCount: 2,
      page: 3,
      pageSize: 50,
    });

    const { result } = renderHook(() => useExpeditionDates(3, 50), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetDates).toHaveBeenCalledTimes(1);
    expect(mockGetDates).toHaveBeenCalledWith(3, 50);
    expect(result.current.data).toEqual({
      dates: ['2024-12-10', '2024-12-09'],
      totalCount: 2,
      page: 3,
      pageSize: 50,
    });
  });
});
```

- [ ] **Step 2: Run the test to confirm it FAILS**

Run from `frontend/`:

```bash
npm test -- --watchAll=false useExpeditionListArchive
```

Expected: FAIL. The hook today calls `(apiClient as any).http.fetch(...)`, not `expeditionListArchive_GetDates`. The mock `getAuthenticatedApiClient` returns an object without `http.fetch`, so the test fails with an undefined property access. (If the test instead passes for the wrong reason — e.g. because `mockGetDates` is never invoked but `result.current.isSuccess` becomes false — that is still a failure of the `expect(mockGetDates).toHaveBeenCalled` assertion.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts
git commit -m "test: add failing test for useExpeditionDates typed client migration"
```

---

## Task 2: Migrate `useExpeditionDates` to the typed client

**Files:**
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts:46-74`

- [ ] **Step 1: Rewrite the `useExpeditionDates` hook to call the typed method**

In `frontend/src/api/hooks/useExpeditionListArchive.ts`, replace lines 46-74 with:

```typescript
export const useExpeditionDates = (page: number = 1, pageSize: number = 20) => {
  return useQuery<GetExpeditionDatesResponse>({
    queryKey: expeditionArchiveKeys.dates(page, pageSize),
    queryFn: async (): Promise<GetExpeditionDatesResponse> => {
      const client = getAuthenticatedApiClient();
      const response = await client.expeditionListArchive_GetDates(page, pageSize);
      return {
        dates: response.dates ?? [],
        totalCount: response.totalCount ?? 0,
        page: response.page ?? page,
        pageSize: response.pageSize ?? pageSize,
      };
    },
    staleTime: 1000 * 60 * 5,
  });
};
```

The local `GetExpeditionDatesResponse` interface at lines 14-19 stays — it is the consumer contract (`number`, not `number | undefined`). The `?? page` / `?? pageSize` defaults preserve the page/pageSize echoed back to consumers if the backend ever omits them.

- [ ] **Step 2: Run the test to confirm it PASSES**

Run from `frontend/`:

```bash
npm test -- --watchAll=false useExpeditionListArchive
```

Expected: PASS. `useExpeditionDates` test green.

- [ ] **Step 3: Verify the typed import is still satisfied (no unused imports yet — manual fetch helpers are still used by the other hooks)**

This step is "no-op verify" — do not modify imports yet. The hooks that still use `(apiClient as any)` need their imports until they too are migrated. Confirm the file compiles:

```bash
cd frontend && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useExpeditionListArchive.ts
git commit -m "refactor(expedition-archive): migrate useExpeditionDates to typed client"
```

---

## Task 3: Migrate `useExpeditionListsByDate` to the typed client (with Date → ISO-string mapping)

**Files:**
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts:76-98`

- [ ] **Step 1: Add a failing test for the mapping behavior**

Append to `frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts`:

```typescript
describe('useExpeditionListsByDate', () => {
  let mockGetByDate: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetByDate = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      expeditionListArchive_GetByDate: mockGetByDate,
    } as any);
  });

  it('passes the date through to the typed method and maps Date/undefined to string/null', async () => {
    mockGetByDate.mockResolvedValue({
      items: [
        {
          blobPath: '2024/12/10/file.pdf',
          fileName: 'expedice-2024-12-10.pdf',
          listId: 'L-1',
          createdOn: new Date('2024-12-10T10:00:00.000Z'),
          contentLength: 1024,
        },
        {
          blobPath: '2024/12/10/other.pdf',
          fileName: 'expedice-other.pdf',
          listId: 'L-2',
          createdOn: undefined,
          contentLength: undefined,
        },
      ],
    });

    const { result } = renderHook(() => useExpeditionListsByDate('2024-12-10'), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetByDate).toHaveBeenCalledWith('2024-12-10');
    expect(result.current.data).toEqual({
      items: [
        {
          blobPath: '2024/12/10/file.pdf',
          fileName: 'expedice-2024-12-10.pdf',
          listId: 'L-1',
          createdOn: '2024-12-10T10:00:00.000Z',
          contentLength: 1024,
        },
        {
          blobPath: '2024/12/10/other.pdf',
          fileName: 'expedice-other.pdf',
          listId: 'L-2',
          createdOn: null,
          contentLength: null,
        },
      ],
    });
  });

  it('does not call the API when date is empty (enabled: !!date)', async () => {
    renderHook(() => useExpeditionListsByDate(''), { wrapper: createWrapper });
    // Give React Query a microtask to evaluate `enabled`
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(mockGetByDate).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test to confirm it FAILS**

```bash
npm test -- --watchAll=false useExpeditionListsByDate
```

Expected: FAIL. The hook today does not call `expeditionListArchive_GetByDate`.

- [ ] **Step 3: Rewrite `useExpeditionListsByDate`**

Replace lines 76-98 of `frontend/src/api/hooks/useExpeditionListArchive.ts` with:

```typescript
export const useExpeditionListsByDate = (date: string) => {
  return useQuery<GetExpeditionListsByDateResponse>({
    queryKey: expeditionArchiveKeys.itemsByDate(date),
    queryFn: async (): Promise<GetExpeditionListsByDateResponse> => {
      const client = getAuthenticatedApiClient();
      const response = await client.expeditionListArchive_GetByDate(date);
      return {
        items: (response.items ?? []).map((item) => ({
          blobPath: item.blobPath ?? '',
          fileName: item.fileName ?? '',
          listId: item.listId ?? '',
          createdOn: item.createdOn ? item.createdOn.toISOString() : null,
          contentLength: item.contentLength ?? null,
        })),
      };
    },
    enabled: !!date,
    staleTime: 1000 * 60 * 5,
  });
};
```

The local interfaces `ExpeditionListItemDto` and `GetExpeditionListsByDateResponse` at lines 6-23 stay — see arch-review Decision 2.

- [ ] **Step 4: Run the test to confirm it PASSES**

```bash
npm test -- --watchAll=false useExpeditionListsByDate
```

Expected: PASS for both new assertions and the previous `useExpeditionDates` test.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useExpeditionListArchive.ts frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts
git commit -m "refactor(expedition-archive): migrate useExpeditionListsByDate to typed client with DTO mapping"
```

---

## Task 4: Migrate `useReprintExpeditionList` — drop the local request interface

**Files:**
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts:25-27` (delete the local `ReprintExpeditionListRequest` interface)
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts:100-128` (rewrite the mutation)
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts:1-2` (add import of `ReprintExpeditionListRequest` from `../generated/api-client`)

- [ ] **Step 1: Add a failing test for `useReprintExpeditionList`**

Append to `frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts`:

```typescript
describe('useReprintExpeditionList', () => {
  let mockReprint: jest.Mock;
  let mockInvalidateQueries: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockReprint = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      expeditionListArchive_Reprint: mockReprint,
    } as any);
  });

  it('instantiates ReprintExpeditionListRequest, calls the typed method, and returns the mapped response', async () => {
    mockReprint.mockResolvedValue({ success: true, errorMessage: null });

    const { result } = renderHook(() => useReprintExpeditionList(), {
      wrapper: createWrapper,
    });

    const response = await result.current.mutateAsync({ blobPath: '2024/12/10/file.pdf' });

    expect(mockReprint).toHaveBeenCalledTimes(1);
    const calledWith = mockReprint.mock.calls[0][0];
    expect(calledWith).toBeInstanceOf(ReprintExpeditionListRequest);
    expect(calledWith.blobPath).toBe('2024/12/10/file.pdf');
    expect(response).toEqual({ success: true, errorMessage: null });
  });

  it('rethrows SwaggerException-like errors so callers can surface a toast', async () => {
    mockReprint.mockRejectedValue({ status: 500, message: 'Internal Server Error' });

    const { result } = renderHook(() => useReprintExpeditionList(), {
      wrapper: createWrapper,
    });

    await expect(
      result.current.mutateAsync({ blobPath: '2024/12/10/file.pdf' }),
    ).rejects.toMatchObject({ status: 500 });
  });
});
```

- [ ] **Step 2: Run the test to confirm it FAILS**

```bash
npm test -- --watchAll=false useReprintExpeditionList
```

Expected: FAIL. The hook today builds a plain JSON body via `(apiClient as any).http.fetch(...)`, not a `ReprintExpeditionListRequest` instance.

- [ ] **Step 3: Update imports at the top of `useExpeditionListArchive.ts`**

Replace line 2 of `frontend/src/api/hooks/useExpeditionListArchive.ts`:

```typescript
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
```

with:

```typescript
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { ReprintExpeditionListRequest } from "../generated/api-client";
```

- [ ] **Step 4: Delete the local `ReprintExpeditionListRequest` interface**

Remove lines 25-27 of `frontend/src/api/hooks/useExpeditionListArchive.ts`:

```typescript
export interface ReprintExpeditionListRequest {
  blobPath: string;
}
```

The hook will accept a plain object literal `{ blobPath: string }` as input (typed inline in the mutation signature). Consumers in `ExpeditionListArchivePage.tsx` already pass `{ blobPath: reprintConfirm.blobPath }` (line 107) — no consumer change needed.

- [ ] **Step 5: Rewrite the mutation body**

Replace the (now-shifted) `useReprintExpeditionList` block with:

```typescript
export const useReprintExpeditionList = () => {
  const queryClient = useQueryClient();

  return useMutation<ReprintExpeditionListResponse, Error, { blobPath: string }>({
    mutationFn: async (input): Promise<ReprintExpeditionListResponse> => {
      const client = getAuthenticatedApiClient();
      const request = new ReprintExpeditionListRequest({ blobPath: input.blobPath });
      const response = await client.expeditionListArchive_Reprint(request);
      return {
        success: response.success ?? true,
        errorMessage: response.errorMessage ?? null,
      };
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive });
    },
  });
};
```

Note: the locally-declared `ReprintExpeditionListResponse` interface (lines 29-32 of the original file, now shifted up) stays — consumers expect `success: boolean` (non-optional) and `errorMessage: string | null` (not `undefined`). The mapping reconciles this.

- [ ] **Step 6: Run tests to confirm PASS**

```bash
npm test -- --watchAll=false useReprintExpeditionList
```

Expected: both new tests pass. Also confirm previously-green tests still pass:

```bash
npm test -- --watchAll=false useExpeditionListArchive
```

Expected: all tests so far pass.

- [ ] **Step 7: Verify the page test still passes (signature shape preserved)**

```bash
npm test -- --watchAll=false ExpeditionListArchivePage
```

Expected: PASS. The page test mocks the hook module wholesale (line 8 of `ExpeditionListArchivePage.test.tsx`), and the page itself still passes `{ blobPath }` to `mutateAsync` — fully structurally compatible with the new mutation input type `{ blobPath: string }`.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/api/hooks/useExpeditionListArchive.ts frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts
git commit -m "refactor(expedition-archive): migrate useReprintExpeditionList to typed client, drop local request interface"
```

---

## Task 5: Migrate `useRunExpeditionListPrintFix` to the typed client

**Files:**
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts:130-152`

- [ ] **Step 1: Add a failing test**

Append to `frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts`:

```typescript
describe('useRunExpeditionListPrintFix', () => {
  let mockRunFix: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockRunFix = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      expeditionList_RunFix: mockRunFix,
    } as any);
  });

  it('calls expeditionList_RunFix and returns the mapped response', async () => {
    mockRunFix.mockResolvedValue({ totalCount: 7, errorMessage: null });

    const { result } = renderHook(() => useRunExpeditionListPrintFix(), {
      wrapper: createWrapper,
    });

    const response = await result.current.mutateAsync();

    expect(mockRunFix).toHaveBeenCalledTimes(1);
    expect(response).toEqual({ totalCount: 7, errorMessage: null });
  });

  it('defaults totalCount to 0 and errorMessage to null when the backend omits them', async () => {
    mockRunFix.mockResolvedValue({});

    const { result } = renderHook(() => useRunExpeditionListPrintFix(), {
      wrapper: createWrapper,
    });

    const response = await result.current.mutateAsync();
    expect(response).toEqual({ totalCount: 0, errorMessage: null });
  });
});
```

- [ ] **Step 2: Run the test to confirm it FAILS**

```bash
npm test -- --watchAll=false useRunExpeditionListPrintFix
```

Expected: FAIL — current implementation does not call `expeditionList_RunFix`.

- [ ] **Step 3: Add the consumer-side response type next to other local interfaces (top of the hook file)**

Add a new local interface just after `ReprintExpeditionListResponse`:

```typescript
export interface RunExpeditionListPrintFixResult {
  totalCount: number;
  errorMessage: string | null;
}
```

The page reads `result.totalCount` at `ExpeditionListArchivePage.tsx:98`. The generated `RunExpeditionListPrintFixResponse` has `totalCount?: number | undefined` — the mapping reconciles this to `number`.

- [ ] **Step 4: Rewrite the mutation**

Replace lines 130-152 of `frontend/src/api/hooks/useExpeditionListArchive.ts` with:

```typescript
export const useRunExpeditionListPrintFix = () => {
  return useMutation<RunExpeditionListPrintFixResult, Error, void>({
    mutationFn: async (): Promise<RunExpeditionListPrintFixResult> => {
      const client = getAuthenticatedApiClient();
      const response = await client.expeditionList_RunFix();
      return {
        totalCount: response.totalCount ?? 0,
        errorMessage: response.errorMessage ?? null,
      };
    },
  });
};
```

- [ ] **Step 5: Run tests to confirm PASS**

```bash
npm test -- --watchAll=false useRunExpeditionListPrintFix
```

Expected: PASS.

```bash
npm test -- --watchAll=false ExpeditionListArchivePage
```

Expected: PASS. The page test at `ExpeditionListArchivePage.test.tsx:78` mocks `mutateAsync` to resolve `{ totalCount: 5 }` and the page reads `result.totalCount`. The interface change (anonymous `useMutation` payload → named `RunExpeditionListPrintFixResult`) is structurally compatible.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useExpeditionListArchive.ts frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts
git commit -m "refactor(expedition-archive): migrate useRunExpeditionListPrintFix to typed client"
```

---

## Task 6: Fix `getExpeditionListDownloadUrl` to use the public `getApiBaseUrl()` helper

**Files:**
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts:154-159`
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts:2` (add `getApiBaseUrl` to existing import)

There is no clean unit-test seam for a pure URL builder (`getApiBaseUrl()` is module-mocked), and the existing page test does not exercise the URL. This task relies on `tsc` + lint + the `MIGRATED_HOOKS` guardrail (Task 8) to catch regressions, plus the manual sanity check in Task 9.

- [ ] **Step 1: Extend the `../client` import**

Replace line 2 of `frontend/src/api/hooks/useExpeditionListArchive.ts`:

```typescript
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
```

with:

```typescript
import { getAuthenticatedApiClient, getApiBaseUrl, QUERY_KEYS } from "../client";
```

(If the line was already extended in Task 4 with `ReprintExpeditionListRequest` on a separate line, leave that line untouched and only modify the `../client` import line.)

- [ ] **Step 2: Rewrite `getExpeditionListDownloadUrl`**

Replace lines 154-159 of `frontend/src/api/hooks/useExpeditionListArchive.ts` with:

```typescript
export const getExpeditionListDownloadUrl = (blobPath: string): string => {
  const encodedPath = blobPath.split("/").map(encodeURIComponent).join("/");
  return `${getApiBaseUrl()}/api/expedition-list-archive/download/${encodedPath}`;
};
```

No `getAuthenticatedApiClient()` call — instantiating the client just to read its base URL is wasted work (arch-review Decision section, FR-5 amendment A5).

- [ ] **Step 3: Verify the file compiles**

```bash
cd frontend && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useExpeditionListArchive.ts
git commit -m "refactor(expedition-archive): use getApiBaseUrl() for download URL"
```

---

## Task 7: Fix `handleOpen` cast in `ExpeditionListArchivePage.tsx`

**Files:**
- Modify: `frontend/src/pages/ExpeditionListArchivePage.tsx:1-15` (swap imports)
- Modify: `frontend/src/pages/ExpeditionListArchivePage.tsx:62-78` (swap fetch call)

The page test at `ExpeditionListArchivePage.test.tsx` does not exercise `handleOpen`, so this task uses build + manual sanity check as its verification. (`getAuthenticatedFetch` already has the same auth-header / E2E-cookie behavior as `(apiClient as any).http.fetch` — see `client.ts:419-431`.)

- [ ] **Step 1: Update imports at the top of `ExpeditionListArchivePage.tsx`**

Read lines 1-15 of `frontend/src/pages/ExpeditionListArchivePage.tsx`. Replace the line:

```typescript
import { getAuthenticatedApiClient, QUERY_KEYS } from "../api/client";
```

with:

```typescript
import { getAuthenticatedFetch, QUERY_KEYS } from "../api/client";
```

`getAuthenticatedApiClient` is only used inside `handleOpen` today (the rest of the page consumes hooks). After this swap it is no longer needed.

- [ ] **Step 2: Rewrite `handleOpen` to use `getAuthenticatedFetch()`**

Replace lines 62-78 of `frontend/src/pages/ExpeditionListArchivePage.tsx`:

```typescript
  const handleOpen = async (item: ExpeditionListItemDto) => {
    const url = getExpeditionListDownloadUrl(item.blobPath);
    const apiClient = getAuthenticatedApiClient();
    try {
      const response = await (apiClient as any).http.fetch(url, { method: 'GET' });
      if (!response.ok) {
        showError('Chyba', `Nepodařilo se otevřít soubor (${response.status}).`);
        return;
      }
      const blob = await response.blob();
      const blobUrl = URL.createObjectURL(blob);
      window.open(blobUrl, '_blank', 'noopener,noreferrer');
      setTimeout(() => URL.revokeObjectURL(blobUrl), 10000);
    } catch {
      showError('Chyba', 'Nepodařilo se otevřít soubor.');
    }
  };
```

with:

```typescript
  const handleOpen = async (item: ExpeditionListItemDto) => {
    const url = getExpeditionListDownloadUrl(item.blobPath);
    try {
      const response = await getAuthenticatedFetch()(url, { method: 'GET' });
      if (!response.ok) {
        showError('Chyba', `Nepodařilo se otevřít soubor (${response.status}).`);
        return;
      }
      const blob = await response.blob();
      const blobUrl = URL.createObjectURL(blob);
      window.open(blobUrl, '_blank', 'noopener,noreferrer');
      setTimeout(() => URL.revokeObjectURL(blobUrl), 10000);
    } catch {
      showError('Chyba', 'Nepodařilo se otevřít soubor.');
    }
  };
```

- [ ] **Step 3: Run the page tests to confirm nothing broke**

```bash
cd frontend && npm test -- --watchAll=false ExpeditionListArchivePage
```

Expected: PASS. The test mocks `../../api/client` (line 20 of `ExpeditionListArchivePage.test.tsx`) — the import surface change (`getAuthenticatedApiClient` → `getAuthenticatedFetch`) means the test mock should now include `getAuthenticatedFetch`. **Check whether the existing mock needs `getAuthenticatedFetch: jest.fn()` added.** If the test does not reference it and only the page's imports changed (without test code calling it), Jest's automatic-property handling keeps it green; but if the test fails because of the missing mock property, add it.

If the test fails because of a missing `getAuthenticatedFetch` on the mock, edit `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx:20-25`:

```typescript
jest.mock("../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    expeditionListArchive: ["expedition-list-archive"],
  },
}));
```

→

```typescript
jest.mock("../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  getAuthenticatedFetch: jest.fn(() => jest.fn()),
  QUERY_KEYS: {
    expeditionListArchive: ["expedition-list-archive"],
  },
}));
```

Re-run the test and confirm PASS.

- [ ] **Step 4: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit
```

Expected: no errors. (`getAuthenticatedApiClient` is no longer used in the page — TypeScript will flag it as an unused import if it remains. The import-line replacement in Step 1 already removes it.)

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/ExpeditionListArchivePage.tsx frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx
git commit -m "refactor(expedition-archive): replace (apiClient as any).http.fetch in handleOpen with getAuthenticatedFetch"
```

(If the test file was not modified, omit it from the `git add`.)

---

## Task 8: Extend the `MIGRATED_HOOKS` guardrail

**Files:**
- Modify: `frontend/src/api/__tests__/authenticated-api-usage.test.ts:197`

This is the regression gate. After this task, any future `(apiClient as any)`, `as any).http`, or `as any).baseUrl` reintroduction in `useExpeditionListArchive.ts` fails CI.

- [ ] **Step 1: Add the migrated hook file to the `MIGRATED_HOOKS` Set**

In `frontend/src/api/__tests__/authenticated-api-usage.test.ts`, replace line 197:

```typescript
    const MIGRATED_HOOKS = new Set(["useArticles.ts"]);
```

with:

```typescript
    const MIGRATED_HOOKS = new Set([
      "useArticles.ts",
      "useExpeditionListArchive.ts",
    ]);
```

- [ ] **Step 2: Run the guardrail test to confirm it PASSES with the migrated file**

```bash
cd frontend && npm test -- --watchAll=false authenticated-api-usage
```

Expected: PASS — `useExpeditionListArchive.ts` no longer contains `(apiClient as any)` patterns, so adding it to `MIGRATED_HOOKS` is safe.

- [ ] **Step 3: Sandbox verification — the guardrail catches regressions**

Temporarily reintroduce a forbidden pattern in `frontend/src/api/hooks/useExpeditionListArchive.ts` at the top of the file (just under the imports):

```typescript
// SANDBOX — DO NOT COMMIT
const _sandbox = () => {
  const apiClient = getAuthenticatedApiClient();
  return (apiClient as any).baseUrl;
};
```

Run the guardrail test:

```bash
cd frontend && npm test -- --watchAll=false authenticated-api-usage
```

Expected: FAIL with a message naming `useExpeditionListArchive.ts` and the `(apiClient as any)` pattern at the sandbox line. This proves the guardrail works.

**Then revert the sandbox change:**

```bash
git checkout -- frontend/src/api/hooks/useExpeditionListArchive.ts
```

Re-run the guardrail test:

```bash
cd frontend && npm test -- --watchAll=false authenticated-api-usage
```

Expected: PASS again.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/__tests__/authenticated-api-usage.test.ts
git commit -m "test: extend MIGRATED_HOOKS guardrail to cover useExpeditionListArchive"
```

---

## Task 9: Final validation

**Files:** None modified.

- [ ] **Step 1: Run the full lint pass**

```bash
cd frontend && npm run lint
```

Expected: 0 errors. If lint complains about unused imports in `useExpeditionListArchive.ts` or `ExpeditionListArchivePage.tsx` (the most likely surface after the migration), remove them. Re-run.

- [ ] **Step 2: Run the full build (includes `tsc --noEmit`)**

```bash
cd frontend && npm run build
```

Expected: build succeeds with no TypeScript errors.

- [ ] **Step 3: Run the full Jest test suite**

```bash
cd frontend && npm test -- --watchAll=false
```

Expected: all tests pass.

- [ ] **Step 4: Final visual confirmation that the hook file has zero `as any` casts**

```bash
cd frontend && grep -nE "as any|\.http\.|\.baseUrl" src/api/hooks/useExpeditionListArchive.ts
```

Expected: **no output** (zero matches). If anything matches, it is a regression — revisit the offending hook.

```bash
cd frontend && grep -nE "\(apiClient as any\)" src/pages/ExpeditionListArchivePage.tsx
```

Expected: **no output** (zero matches).

- [ ] **Step 5: Manual sanity check in the browser**

From the repo root:

```bash
./scripts/start-dev-servers.sh   # or whichever script CLAUDE.md / docs/development/setup.md prescribes
```

Open the **Archiv expedičních listů** page. Verify:

1. Date list loads (left sidebar populated).
2. Selecting a date loads the items table on the right.
3. Clicking **Otevřít** on a row opens the PDF in a new tab.
4. Clicking **Přetisk** → confirming → shows a success toast and the items list refreshes.
5. Clicking **Spustit tisk oprav** shows a success toast with a total count.

If any step fails, the network tab should show the same HTTP verb/path/body as before the refactor — debug from there.

- [ ] **Step 6: Final commit (only if any cleanup edits were made in Step 1)**

If Step 1 forced any additional cleanup edits (unused imports etc), commit them:

```bash
git add -A
git commit -m "chore: clean up unused imports after expedition-archive migration"
```

Otherwise skip.

---

## Self-Review

### Spec coverage check

| Requirement | Task |
|---|---|
| FR-1: `useExpeditionDates` no `as any`, typed call, signatures preserved | Tasks 1+2 |
| FR-2: `useExpeditionListsByDate` no `as any`, typed call, `enabled: !!date`, signatures preserved | Task 3 |
| FR-3: `useReprintExpeditionList` uses `new ReprintExpeditionListRequest(...)`, query invalidation preserved | Task 4 |
| FR-4: `useRunExpeditionListPrintFix` no `as any`, typed call | Task 5 |
| FR-5: `getExpeditionListDownloadUrl` uses `getApiBaseUrl()`, no client instantiation, per-segment encoding | Task 6 |
| FR-6 (amended A2): extend `MIGRATED_HOOKS` Jest guardrail | Task 8 |
| NFR-1: behavior parity — same HTTP method/path/body, same React Query keys | Implicit in all refactor tasks; guarded by tests in Tasks 2-5 + page test in Tasks 4, 5, 7 |
| NFR-2: zero `as any` casts in the hook file + zero in `ExpeditionListArchivePage.tsx` `handleOpen` (amendment A1) | Tasks 2-7; verified explicitly in Task 9 Step 4 |
| NFR-3: ≥1 happy-path test per refactored hook | Tasks 1, 3, 4, 5 |
| NFR-4: bundle size neutral or smaller | Implicit — refactor removes hand-rolled fetch code, reuses existing methods |
| NFR-5: auth interceptor coverage guaranteed-by-type | Implicit; arch-review confirms `authenticatedHttp.fetch` is preserved by construction |
| Data model: keep local interfaces, map inside `queryFn` (amendment A3); drop only `ReprintExpeditionListRequest` (amendment A4) | Tasks 3, 4 |

### Placeholder scan

No "TBD", "TODO", "Add appropriate error handling" anywhere. Every step shows the exact code or command.

### Type consistency

- `useReprintExpeditionList` mutation input is `{ blobPath: string }` everywhere (Task 4 declaration + Task 4 Step 5 mutation signature + page consumer at `ExpeditionListArchivePage.tsx:107` already passes `{ blobPath: reprintConfirm.blobPath }`).
- `useRunExpeditionListPrintFix` returns `RunExpeditionListPrintFixResult` everywhere (Task 5 declaration + Step 4 mutation signature + page consumer at `ExpeditionListArchivePage.tsx:98` reads `result.totalCount`).
- `ExpeditionListItemDto.createdOn` is `string | null` everywhere (declaration in untouched lines 6-12, mapping in Task 3 Step 3 produces `string | null`, page consumer at `ExpeditionListArchivePage.tsx:253` calls `formatDateTime(item.createdOn)` which accepts `string | null`).
- `ExpeditionListItemDto.contentLength` is `number | null` everywhere (same chain as above; consumer at `ExpeditionListArchivePage.tsx:256` calls `formatFileSize(item.contentLength)` which accepts `number | null`).
- `getApiBaseUrl()` is imported in Task 6 Step 1 and used in Task 6 Step 2.
- `getAuthenticatedFetch()` is imported in Task 7 Step 1 and used in Task 7 Step 2.
- `ReprintExpeditionListRequest` is the generated class everywhere it appears post-migration (imported in Task 4 Step 3, instantiated in Task 4 Step 5, asserted on in Task 4 Step 1 test).
