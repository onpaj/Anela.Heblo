# Invalidate Detail Query on Recurring Job CRON Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `useUpdateRecurringJobCronMutation`'s `onSuccess` handler invalidate the affected job's detail query (`recurringJobsKeys.detail(jobName)`) in addition to the list query, matching the already-correct `useUpdateRecurringJobStatusMutation` pattern, so shortcut controls (e.g. `ExpeditionJobControlsBar`) stop showing a stale CRON expression after a successful edit.

**Architecture:** One-line change to an existing `onSuccess` handler in `frontend/src/api/hooks/useRecurringJobs.ts`, no new files, no new abstraction (see `arch-review.r1.md` Decision 1). A new unit test asserts both invalidations fire. No backend, API, or UI changes.

**Tech Stack:** React, TypeScript, `@tanstack/react-query`, Jest, `@testing-library/react`.

---

### task: add-detail-invalidation-to-cron-mutation

**Files:**
- Modify: `frontend/src/api/hooks/useRecurringJobs.ts:100-102`
- Test: `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts`

- [ ] **Step 1: Write the failing test**

Add this test inside the existing `describe('useUpdateRecurringJobCronMutation', ...)` block in `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts` (after the existing `'throws on API error'` test, before its closing `});`):

```typescript
  it('invalidates both list and detail queries on success', async () => {
    mockApiClient.recurringJobs_UpdateJobCron.mockResolvedValue({
      success: true,
      jobName: 'test-job',
      cronExpression: '0 3 * * *',
      lastModifiedAt: new Date().toISOString(),
      lastModifiedBy: 'test-user',
    });

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
    const invalidateSpy = jest.spyOn(queryClient, 'invalidateQueries');
    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children);

    const { result } = renderHook(() => useUpdateRecurringJobCronMutation(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ jobName: 'test-job', cronExpression: '0 3 * * *' });
    });

    expect(invalidateSpy).toHaveBeenCalledWith(
      expect.objectContaining({ queryKey: expect.arrayContaining(['recurring-jobs', 'list']) })
    );
    expect(invalidateSpy).toHaveBeenCalledWith(
      expect.objectContaining({ queryKey: expect.arrayContaining(['recurring-jobs', 'detail', 'test-job']) })
    );
  });
```

This test file already imports `QueryClient`, `QueryClientProvider`, `React`, `renderHook`, `act`, and `useUpdateRecurringJobCronMutation` at the top — no new imports are needed.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useRecurringJobs.test.ts -t "invalidates both list and detail queries on success"`

Expected: FAIL — the second `expect(invalidateSpy).toHaveBeenCalledWith(...)` assertion for the `detail` query key fails, because the current `onSuccess` handler only calls `invalidateQueries` once, with the `list()` key.

- [ ] **Step 3: Write the minimal implementation**

In `frontend/src/api/hooks/useRecurringJobs.ts`, change `useUpdateRecurringJobCronMutation`'s `onSuccess` handler from:

```typescript
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
    },
```

to:

```typescript
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.detail(variables.jobName) });
    },
```

This mirrors `useUpdateRecurringJobStatusMutation`'s `onSuccess` handler in the same file verbatim, substituting no other logic.

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useRecurringJobs.test.ts`

Expected: PASS — all tests in the file pass, including the new one and the two pre-existing `useUpdateRecurringJobCronMutation` tests and the `useRecurringJobsQuery` test.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useRecurringJobs.ts frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts
git commit -m "fix(background-jobs): invalidate detail query on CRON mutation success"
```

---

### task: full-verification

**Files:**
- None (verification only, no new files)

- [ ] **Step 1: Run the frontend build**

Run: `cd frontend && npm run build`
Expected: build succeeds with no TypeScript errors.

- [ ] **Step 2: Run the frontend lint**

Run: `cd frontend && npm run lint`
Expected: no new lint errors introduced by the change in `useRecurringJobs.ts` or its test file.

- [ ] **Step 3: Run the full frontend test suite for the touched files**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useRecurringJobs.test.ts src/pages/__tests__/RecurringJobsPage.test.tsx src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx`
Expected: all tests pass — the two consumer test files (`RecurringJobsPage.test.tsx`, `ExpeditionJobControlsBar.test.tsx`) must still pass unmodified, confirming the invalidation change did not alter any consumer-visible behavior they assert on.

- [ ] **Step 4: Run the full frontend test suite**

Run: `cd frontend && npm test -- --watchAll=false`
Expected: PASS — no regressions anywhere else in the frontend suite.

- [ ] **Step 5: Commit (if verification uncovered any fix-up changes)**

```bash
git add -A
git commit -m "chore(background-jobs): verification pass for CRON detail-invalidation fix"
```

If verification required no code changes, skip this commit — Task 1's commit already covers the full fix.
