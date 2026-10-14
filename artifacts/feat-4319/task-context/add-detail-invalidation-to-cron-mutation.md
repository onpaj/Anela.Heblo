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

