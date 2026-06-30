### task: gate-hook

**Files:**
- Modify: `frontend/src/api/hooks/useUserManagement.ts`
- Modify: `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx`

- [ ] **Step 1: Write the two failing tests**

Open `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx` and append these two `it` blocks inside the existing `describe('useResponsiblePersonsQuery', ...)` block, after the last existing test:

```ts
    it('should not fire the query when options.enabled is false', () => {
        const { result } = renderHook(
            () => useResponsiblePersonsQuery('test-group-id', { enabled: false }),
            { wrapper: createWrapper() },
        );

        // Query is disabled — should not be loading and API must not have been called
        expect(result.current.isLoading).toBe(false);
        expect(result.current.fetchStatus).toBe('idle');
        expect(mockGetGroupMembers).not.toHaveBeenCalled();
    });

    it('should fire the query when options.enabled is true', async () => {
        const { result } = renderHook(
            () => useResponsiblePersonsQuery('test-group-id', { enabled: true }),
            { wrapper: createWrapper() },
        );

        // Query is enabled — it should begin fetching
        expect(result.current.isLoading).toBe(true);
        expect(result.current.fetchStatus).toBe('fetching');
    });
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
cd /home/user/worktrees/feature-3339-Add-Admin-Permission-Guard-To-Responsiblepersoncom/frontend
npx jest --testPathPattern="useUserManagement.test" --no-coverage 2>&1 | tail -30
```

Expected: TypeScript compilation error — `useResponsiblePersonsQuery` does not accept a second argument.

- [ ] **Step 3: Update `useResponsiblePersonsQuery` to accept the `options` argument**

Replace the entire content of `frontend/src/api/hooks/useUserManagement.ts`:

```ts
import { useQuery } from '@tanstack/react-query';
import type { GetGroupMembersResponse } from '../generated/api-client';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export const useResponsiblePersonsQuery = (
  groupId: string,
  options?: { enabled?: boolean },
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.userManagement, 'group-members', groupId],
    enabled: Boolean(groupId) && (options?.enabled ?? true),
    queryFn: async (): Promise<GetGroupMembersResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.userManagement_GetGroupMembers(groupId);
    },
    staleTime: 15 * 60 * 1000,
    retry: 2,
    retryDelay: 1000,
  });
};
```

- [ ] **Step 4: Run the hook tests and confirm all pass**

```bash
cd /home/user/worktrees/feature-3339-Add-Admin-Permission-Guard-To-Responsiblepersoncom/frontend
npx jest --testPathPattern="useUserManagement.test" --no-coverage 2>&1 | tail -20
```

Expected: `6 passed` (4 existing + 2 new).

- [ ] **Step 5: Commit**

```bash
cd /home/user/worktrees/feature-3339-Add-Admin-Permission-Guard-To-Responsiblepersoncom
git add frontend/src/api/hooks/useUserManagement.ts \
        frontend/src/api/hooks/__tests__/useUserManagement.test.tsx
git commit -m "feat: add options.enabled to useResponsiblePersonsQuery"
```

---

