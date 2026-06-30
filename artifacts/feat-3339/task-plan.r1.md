# Admin Permission Guard for ResponsiblePersonCombobox — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gate the `GET /api/UserManagement/group-members` query behind the `admin.administration.read` permission so non-admin users never trigger a 403.

**Architecture:** Add an `options?: { enabled?: boolean }` second argument to `useResponsiblePersonsQuery` so callers can disable the query. `ResponsiblePersonCombobox` reads `usePermissionsContext().hasPermission('admin.administration.read')` internally and passes the result as `enabled`. Non-admins fall back to the existing manual-entry path with no error banner.

**Tech Stack:** React, TanStack Query (`@tanstack/react-query`), TypeScript, Jest + React Testing Library.

---

## Files changed

| File | Action |
|------|--------|
| `frontend/src/api/hooks/useUserManagement.ts` | Modify — add `options` second parameter |
| `frontend/src/components/common/ResponsiblePersonCombobox.tsx` | Modify — import `usePermissionsContext`, pass `enabled` to hook |
| `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx` | Modify — add 2 test cases for `enabled` option |
| `frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx` | Modify — add `usePermissionsContext` mock + 2 test cases |

---

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

### task: guard-component

**Files:**
- Modify: `frontend/src/components/common/ResponsiblePersonCombobox.tsx`
- Modify: `frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx`

- [ ] **Step 1: Write the two failing component tests**

Open `frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx`.

Add the `usePermissionsContext` mock immediately after the existing `useUserManagement` mock (around line 10). Insert right below the `jest.mock('../../../api/hooks/useUserManagement', ...)` block:

```ts
// Mock PermissionsContext
const mockHasPermission = jest.fn();
jest.mock('../../../auth/PermissionsContext', () => ({
    usePermissionsContext: () => ({
        hasPermission: mockHasPermission,
        permissions: [],
        isSuperUser: false,
        groups: [],
        isLoading: false,
    }),
}));
```

Also add `mockHasPermission.mockReturnValue(true);` inside `beforeEach` so existing tests default to admin. The updated `beforeEach` block should look like:

```ts
    beforeEach(() => {
        jest.clearAllMocks();
        mockHasPermission.mockReturnValue(true);  // default: admin
    });
```

Then append these two `it` blocks inside the existing `describe('ResponsiblePersonCombobox', ...)` block, after the last existing test:

```ts
    it('non-admin: query is disabled — no error banner shown', () => {
        mockHasPermission.mockReturnValue(false);
        // Hook returns idle state when query is disabled
        mockUseResponsiblePersonsQuery.mockReturnValue({
            data: undefined,
            isLoading: false,
            isError: false,
        });

        render(<ResponsiblePersonCombobox {...defaultProps} />, {
            wrapper: createWrapper(),
        });

        // Error banner must NOT be visible for non-admins
        expect(
            screen.queryByText('Could not load team members. You can still enter names manually.')
        ).not.toBeInTheDocument();

        // Input must still be rendered and accept text
        expect(screen.getByRole('combobox')).toBeInTheDocument();
    });

    it('admin: query is enabled — members shown in dropdown', async () => {
        mockHasPermission.mockReturnValue(true);
        mockUseResponsiblePersonsQuery.mockReturnValue({
            data: { success: true, members: mockUsers },
            isLoading: false,
            isError: false,
        });

        render(<ResponsiblePersonCombobox {...defaultProps} />, {
            wrapper: createWrapper(),
        });

        // Component renders, combobox is accessible
        expect(screen.getByRole('combobox')).toBeInTheDocument();
        // No error banner for admins with data
        expect(
            screen.queryByText('Could not load team members. You can still enter names manually.')
        ).not.toBeInTheDocument();
    });
```

- [ ] **Step 2: Run the component tests and confirm the new ones fail**

```bash
cd /home/user/worktrees/feature-3339-Add-Admin-Permission-Guard-To-Responsiblepersoncom/frontend
npx jest --testPathPattern="ResponsiblePersonCombobox.test" --no-coverage 2>&1 | tail -30
```

Expected: Error — `usePermissionsContext must be used within PermissionsProvider` — because the component doesn't import it yet, but the mock isn't wired up until the component uses it. The two new tests may also fail for other reasons; either way the suite is not green.

- [ ] **Step 3: Update `ResponsiblePersonCombobox` to import and use the permission check**

In `frontend/src/components/common/ResponsiblePersonCombobox.tsx`, add the import on the line after the existing `useResponsiblePersonsQuery` import (around line 11):

```ts
import { usePermissionsContext } from "../../auth/PermissionsContext";
```

Then replace the single line in the component body that calls the hook:

Old line (line 43):
```ts
  const { data: response, isLoading, isError } = useResponsiblePersonsQuery(groupId);
```

New lines:
```ts
  const { hasPermission } = usePermissionsContext();
  const canReadGroupMembers = hasPermission('admin.administration.read');
  const { data: response, isLoading, isError } = useResponsiblePersonsQuery(groupId, { enabled: canReadGroupMembers });
```

No other changes to the component. The `isError` branch that renders the error banner is already gated on the `isError` flag — when the query is disabled, `isError` stays `false`, so the banner is naturally suppressed.

- [ ] **Step 4: Run the full component test suite and confirm all pass**

```bash
cd /home/user/worktrees/feature-3339-Add-Admin-Permission-Guard-To-Responsiblepersoncom/frontend
npx jest --testPathPattern="ResponsiblePersonCombobox.test" --no-coverage 2>&1 | tail -20
```

Expected: `9 passed` (7 existing + 2 new). If any existing test fails with `usePermissionsContext must be used within PermissionsProvider`, verify the mock block was inserted before the `describe` block (not inside it) and that `beforeEach` sets `mockHasPermission.mockReturnValue(true)`.

- [ ] **Step 5: Run both affected test files together**

```bash
cd /home/user/worktrees/feature-3339-Add-Admin-Permission-Guard-To-Responsiblepersoncom/frontend
npx jest --testPathPattern="(useUserManagement|ResponsiblePersonCombobox).test" --no-coverage 2>&1 | tail -20
```

Expected: `15 passed`, 0 failed.

- [ ] **Step 6: Build and lint**

```bash
cd /home/user/worktrees/feature-3339-Add-Admin-Permission-Guard-To-Responsiblepersoncom/frontend
npm run build 2>&1 | tail -20
npm run lint 2>&1 | tail -20
```

Expected: both exit 0 with no errors.

- [ ] **Step 7: Commit**

```bash
cd /home/user/worktrees/feature-3339-Add-Admin-Permission-Guard-To-Responsiblepersoncom
git add frontend/src/components/common/ResponsiblePersonCombobox.tsx \
        frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx
git commit -m "feat: gate group-members query behind admin.administration.read permission"
```
