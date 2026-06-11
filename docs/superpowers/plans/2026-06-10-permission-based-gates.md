# Permission-Based UI Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all three legacy role-string UI gates (`meeting_manager`, `marketing_reader`, raw `super_user`) with permission checks via `usePermissionsContext().hasPermission(...)`, so the only roles left in the app are `heblo_user` and `super_user`.

**Architecture:** The frontend already has a permission system (`PermissionsContext` → `/api/auth/me` → `{ isSuperUser, permissions[] }`). `hasPermission(perm)` returns true for super-users or when the wire-string permission is present. The established pattern (`Sidebar.tsx`, `RequireMenuPath.tsx`, `UserProfile.tsx`) is to destructure `const { hasPermission } = usePermissionsContext()` and check a wire string. We delete the three legacy hooks and rewire each call site to the correct permission. Backend is already fully permission-based (`[FeatureAuthorize(...)]`) — no backend changes.

**Tech Stack:** React + TypeScript, Jest + React Testing Library, `react-scripts test`.

**Permission mapping (per backend `FeatureAuthorize`):**

| Call site | Old gate | New permission |
|-----------|----------|----------------|
| `MeetingTaskDetailPage` (`isMeetingManager`) | `meeting_manager` | `anela.meetings.write` |
| `ArticleGenerationForm` (`canGenerate`) | `marketing_reader` | `marketing.article.write` |
| `LeafletGeneratorPage` (`canUpload`) | `marketing_reader` | `marketing.leaflet.write` |
| `KnowledgeBasePage` (`canUpload`) | `super_user` | `customer.knowledge_base.write` |
| `MarketingFeedbackPage` (`hasKb` / `hasGenAi`) | `super_user` / `marketing_reader` | `customer.knowledge_base.write` / `marketing.article.write` |

**Test commands:** Run a single file with `cd frontend && CI=true npx react-scripts test --watchAll=false <pathOrPattern>`. (Per project memory: use `react-scripts test`, not `npx jest`.)

**Ordering note:** `useMarketingWriterPermission` is shared by Tasks 2, 3, 5; `useKnowledgeBaseUploadPermission` is shared by Tasks 4, 5. Both are deleted only in Task 6, after every consumer is migrated, so each intermediate commit stays green.

---

### Task 1: Migrate `MeetingTaskDetailPage` to `anela.meetings.write` and delete the meeting hook

**Files:**
- Modify: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`
- Modify: `frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.download.test.tsx`
- Delete: `frontend/src/api/hooks/useMeetingManagerPermission.ts`
- Delete: `frontend/src/api/hooks/__tests__/useMeetingManagerPermission.test.ts`

- [ ] **Step 1: Update the test to mock `PermissionsContext` and add access-button gating coverage**

In `MeetingTaskDetailPage.download.test.tsx`:

Remove this import (line 17):
```tsx
import { useMeetingManagerPermission } from '../../../../api/hooks/useMeetingManagerPermission';
```

Replace the mock on line 27:
```tsx
jest.mock('../../../../api/hooks/useMeetingManagerPermission');
```
with a mutable permission-context mock (place near the other `jest.mock` calls):
```tsx
let mockHasPermission: (perm: string) => boolean = () => false;
jest.mock('../../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => mockHasPermission(p),
  }),
}));
```

In `setupHooks`, remove line 85:
```tsx
  (useMeetingManagerPermission as jest.Mock).mockReturnValue(false);
```

Update `beforeEach` (line 91) to reset the permission default:
```tsx
beforeEach(() => {
  jest.clearAllMocks();
  mockHasPermission = () => false;
});
```

Append a new describe block at the end of the file (after the `download transcript button` block):
```tsx
describe('manage access button', () => {
  it('is hidden when user lacks anela.meetings.write', () => {
    mockHasPermission = () => false;
    setupHooks();
    renderPage();
    expect(screen.queryByRole('button', { name: /spravovat přístup/i })).not.toBeInTheDocument();
  });

  it('is visible when user has anela.meetings.write', () => {
    mockHasPermission = (p) => p === 'anela.meetings.write';
    setupHooks();
    renderPage();
    expect(screen.getByRole('button', { name: /spravovat přístup/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/pages/automation/__tests__/MeetingTaskDetailPage.download.test.tsx`
Expected: FAIL — the source still imports `useMeetingManagerPermission`, so the real hook runs `useMsal()` outside an MsalProvider (or the access-button test fails because the button never renders).

- [ ] **Step 3: Migrate the source component**

In `MeetingTaskDetailPage.tsx`, remove the import (line 24):
```tsx
import { useMeetingManagerPermission } from '../../../api/hooks/useMeetingManagerPermission';
```
and add (next to the other auth imports):
```tsx
import { usePermissionsContext } from '../../../auth/PermissionsContext';
```

Replace line 107:
```tsx
  const isMeetingManager = useMeetingManagerPermission();
```
with:
```tsx
  const { hasPermission } = usePermissionsContext();
  const isMeetingManager = hasPermission('anela.meetings.write');
```

(The two `{isMeetingManager && ...}` blocks at lines ~285 and ~610 are unchanged.)

- [ ] **Step 4: Delete the now-unused hook and its test**

```bash
rm frontend/src/api/hooks/useMeetingManagerPermission.ts
rm frontend/src/api/hooks/__tests__/useMeetingManagerPermission.test.ts
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/pages/automation/__tests__/MeetingTaskDetailPage.download.test.tsx`
Expected: PASS (all download + manage-access tests green).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx \
  frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.download.test.tsx
git add -A frontend/src/api/hooks/useMeetingManagerPermission.ts \
  frontend/src/api/hooks/__tests__/useMeetingManagerPermission.test.ts
git commit -m "refactor(authz): gate meeting access button on anela.meetings.write permission"
```

---

### Task 2: Migrate `ArticleGenerationForm` to `marketing.article.write`

**Files:**
- Modify: `frontend/src/features/articles/ArticleGenerationForm.tsx`
- Modify: `frontend/src/features/articles/__tests__/ArticleGenerationForm.test.tsx`

- [ ] **Step 1: Update the test to mock `PermissionsContext`**

In `ArticleGenerationForm.test.tsx`, replace the mock block (lines 16-18):
```tsx
jest.mock('../../../api/hooks/useMarketingWriterPermission', () => ({
  useMarketingWriterPermission: () => true,
}));
```
with:
```tsx
let mockHasPermission: (perm: string) => boolean = () => true;
jest.mock('../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => mockHasPermission(p),
  }),
}));
```

In the existing `beforeEach` (line 21-23), reset the default:
```tsx
  beforeEach(() => {
    mockMutate.mockReset();
    mockHasPermission = () => true;
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/features/articles/__tests__/ArticleGenerationForm.test.tsx`
Expected: FAIL — source still calls `useMarketingWriterPermission`, whose real implementation runs `useMsal()` with no provider.

- [ ] **Step 3: Migrate the source component**

In `ArticleGenerationForm.tsx`, remove the import (line 5):
```tsx
import { useMarketingWriterPermission } from '../../api/hooks/useMarketingWriterPermission';
```
and add:
```tsx
import { usePermissionsContext } from '../../auth/PermissionsContext';
```

Replace line 25:
```tsx
  const canGenerate = useMarketingWriterPermission();
```
with:
```tsx
  const { hasPermission } = usePermissionsContext();
  const canGenerate = hasPermission('marketing.article.write');
```

(The `!canGenerate` usages at lines ~191 and ~197 are unchanged.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/features/articles/__tests__/ArticleGenerationForm.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/articles/ArticleGenerationForm.tsx \
  frontend/src/features/articles/__tests__/ArticleGenerationForm.test.tsx
git commit -m "refactor(authz): gate article generation on marketing.article.write permission"
```

---

### Task 3: Migrate `LeafletGeneratorPage` to `marketing.leaflet.write`

**Files:**
- Modify: `frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx`
- Modify: `frontend/src/features/leaflet-generator/__tests__/LeafletGeneratorPage.test.tsx`

- [ ] **Step 1: Update the test to mock `PermissionsContext`**

In `LeafletGeneratorPage.test.tsx`, remove the import (line 5):
```tsx
import * as marketingWriterHooks from '../../../api/hooks/useMarketingWriterPermission';
```

Replace the mock block (lines 24-26):
```tsx
jest.mock('../../../api/hooks/useMarketingWriterPermission', () => ({
  useMarketingWriterPermission: jest.fn(),
}));

const mockUseMarketingWriterPermission = marketingWriterHooks.useMarketingWriterPermission as jest.Mock;
```
with:
```tsx
let mockHasPermission: (perm: string) => boolean = () => false;
jest.mock('../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => mockHasPermission(p),
  }),
}));

function setCanUpload(value: boolean) {
  mockHasPermission = (p) => value && p === 'marketing.leaflet.write';
}
```

Replace every `mockUseMarketingWriterPermission.mockReturnValue(true);` in the file with `setCanUpload(true);` and every `mockUseMarketingWriterPermission.mockReturnValue(false);` with `setCanUpload(false);`. (Use find-and-replace across the whole file — there are several occurrences.)

In `beforeEach` (lines 38-40), reset the default:
```tsx
beforeEach(() => {
  jest.clearAllMocks();
  setCanUpload(false);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/features/leaflet-generator/__tests__/LeafletGeneratorPage.test.tsx`
Expected: FAIL — source still calls `useMarketingWriterPermission` (real hook runs `useMsal()`).

- [ ] **Step 3: Migrate the source component**

In `LeafletGeneratorPage.tsx`, remove the import (line 6):
```tsx
import { useMarketingWriterPermission } from '../../api/hooks/useMarketingWriterPermission';
```
and add:
```tsx
import { usePermissionsContext } from '../../auth/PermissionsContext';
```

Replace line 12:
```tsx
  const canUpload = useMarketingWriterPermission();
```
with:
```tsx
  const { hasPermission } = usePermissionsContext();
  const canUpload = hasPermission('marketing.leaflet.write');
```

(The `canUpload` usages at lines ~20, ~50, ~51 are unchanged.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/features/leaflet-generator/__tests__/LeafletGeneratorPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx \
  frontend/src/features/leaflet-generator/__tests__/LeafletGeneratorPage.test.tsx
git commit -m "refactor(authz): gate leaflet upload on marketing.leaflet.write permission"
```

---

### Task 4: Migrate `KnowledgeBasePage` to `customer.knowledge_base.write`

**Files:**
- Modify: `frontend/src/pages/KnowledgeBasePage.tsx`

(No dedicated test file for `KnowledgeBasePage` exists; the hook's own tests are cleaned up in Task 6. This task is a straight source migration — verified by build + the full suite in Task 7.)

- [ ] **Step 1: Migrate the source component**

In `KnowledgeBasePage.tsx`, remove the import (line 6):
```tsx
import { useKnowledgeBaseUploadPermission } from '../api/hooks/useKnowledgeBase';
```
and add:
```tsx
import { usePermissionsContext } from '../auth/PermissionsContext';
```

Replace line 12:
```tsx
  const canUpload = useKnowledgeBaseUploadPermission();
```
with:
```tsx
  const { hasPermission } = usePermissionsContext();
  const canUpload = hasPermission('customer.knowledge_base.write');
```

(The `canUpload` usages at lines ~24, ~54, ~55 are unchanged.)

- [ ] **Step 2: Verify the file compiles**

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json`
Expected: no errors referencing `KnowledgeBasePage.tsx`. (Other files still importing `useKnowledgeBaseUploadPermission`, e.g. `MarketingFeedbackPage`, remain valid until Task 5/6.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/KnowledgeBasePage.tsx
git commit -m "refactor(authz): gate knowledge base upload on customer.knowledge_base.write permission"
```

---

### Task 5: Migrate `MarketingFeedbackPage` to `customer.knowledge_base.write` + `marketing.article.write`

**Files:**
- Modify: `frontend/src/pages/MarketingFeedbackPage.tsx`
- Modify: `frontend/src/pages/__tests__/MarketingFeedbackPage.test.tsx`

- [ ] **Step 1: Update the test to mock `PermissionsContext`**

In `MarketingFeedbackPage.test.tsx`, remove these imports (lines 4-5):
```tsx
import * as kbHooks from '../../api/hooks/useKnowledgeBase';
import * as marketingWriterHooks from '../../api/hooks/useMarketingWriterPermission';
```

Remove these mock lines (11-12):
```tsx
jest.mock('../../api/hooks/useKnowledgeBase');
jest.mock('../../api/hooks/useMarketingWriterPermission');
```
and add a permission-context mock in their place:
```tsx
let mockHasPermission: (perm: string) => boolean = () => false;
jest.mock('../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => mockHasPermission(p),
  }),
}));
```

In `setupMocks` (lines 41-57), replace the two spy lines (45-46):
```tsx
  jest.spyOn(kbHooks, 'useKnowledgeBaseUploadPermission').mockReturnValue(hasKb);
  jest.spyOn(marketingWriterHooks, 'useMarketingWriterPermission').mockReturnValue(hasGenAi);
```
with:
```tsx
  mockHasPermission = (p) =>
    (hasKb && p === 'customer.knowledge_base.write') ||
    (hasGenAi && p === 'marketing.article.write');
```

In `beforeEach` (line 59), reset the default:
```tsx
beforeEach(() => {
  jest.clearAllMocks();
  mockHasPermission = () => false;
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/pages/__tests__/MarketingFeedbackPage.test.tsx`
Expected: FAIL — source still calls `useKnowledgeBaseUploadPermission` / `useMarketingWriterPermission` (real hooks run `useMsal()`).

- [ ] **Step 3: Migrate the source component**

In `MarketingFeedbackPage.tsx`, remove the imports (lines 3-4):
```tsx
import { useKnowledgeBaseUploadPermission } from '../api/hooks/useKnowledgeBase';
import { useMarketingWriterPermission } from '../api/hooks/useMarketingWriterPermission';
```
and add:
```tsx
import { usePermissionsContext } from '../auth/PermissionsContext';
```

Replace lines 47-48:
```tsx
  const hasKb = useKnowledgeBaseUploadPermission();
  const hasGenAi = useMarketingWriterPermission();
```
with:
```tsx
  const { hasPermission } = usePermissionsContext();
  const hasKb = hasPermission('customer.knowledge_base.write');
  const hasGenAi = hasPermission('marketing.article.write');
```

(The `if (!hasKb && !hasGenAi)` guard at line ~62 is unchanged.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/pages/__tests__/MarketingFeedbackPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/MarketingFeedbackPage.tsx \
  frontend/src/pages/__tests__/MarketingFeedbackPage.test.tsx
git commit -m "refactor(authz): gate marketing feedback page on knowledge-base/article write permissions"
```

---

### Task 6: Delete the now-unused legacy hooks

By this point no source file imports `useMarketingWriterPermission` or `useKnowledgeBaseUploadPermission`.

**Files:**
- Delete: `frontend/src/api/hooks/useMarketingWriterPermission.ts`
- Modify: `frontend/src/api/hooks/useKnowledgeBase.ts` (remove the `useKnowledgeBaseUploadPermission` export)
- Modify: `frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts` (remove its tests)

- [ ] **Step 1: Confirm there are no remaining consumers**

Run:
```bash
grep -rn "useMarketingWriterPermission\|useKnowledgeBaseUploadPermission" frontend/src --include="*.ts" --include="*.tsx"
```
Expected: matches ONLY inside `frontend/src/api/hooks/useKnowledgeBase.ts`, `frontend/src/api/hooks/useMarketingWriterPermission.ts`, and `frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts`. If any other file matches, stop — that consumer was missed in an earlier task.

- [ ] **Step 2: Delete the marketing-writer hook**

```bash
rm frontend/src/api/hooks/useMarketingWriterPermission.ts
```

- [ ] **Step 3: Remove the `useKnowledgeBaseUploadPermission` export and its docblock from `useKnowledgeBase.ts`**

Delete this block (lines ~159-179):
```tsx
// ---- Permission hooks ----

/**
 * Returns true when the current MSAL account has the super_user role.
 * Controls visibility of the Upload tab and delete buttons.
 */
export const useKnowledgeBaseUploadPermission = (): boolean => {
  const { accounts } = useMsal();

  // In mock auth mode, MSAL has no accounts — read roles from mockAuthService instead
  if (shouldUseMockAuth()) {
    const user = mockAuthService.getUser();
    return !!(Array.isArray(user?.roles) && user?.roles.includes('super_user'));
  }

  const account = accounts[0];
  if (!account) return false;
  const claims = account.idTokenClaims as Record<string, unknown> | undefined;
  const roles = claims?.['roles'];
  return Array.isArray(roles) && roles.includes('super_user');
};
```

If `useMsal`, `shouldUseMockAuth`, or `mockAuthService` are now unused in `useKnowledgeBase.ts`, remove their imports too. Verify with:
```bash
grep -n "useMsal\|shouldUseMockAuth\|mockAuthService" frontend/src/api/hooks/useKnowledgeBase.ts
```
Remove any import whose only references were inside the deleted block.

- [ ] **Step 4: Remove the dead tests from `useKnowledgeBase.test.ts`**

Remove the `useKnowledgeBaseUploadPermission` import line (line 10) from the multi-line import of `../useKnowledgeBase`.

Delete both describe blocks (lines ~259-309):
```tsx
  describe('useKnowledgeBaseUploadPermission', () => {
    ...
  });

  describe('useKnowledgeBaseUploadPermission (mock auth mode)', () => {
    ...
  });
```
Leave the shared `mockShouldUseMockAuth` / `mockGetUser` scaffolding and `beforeEach` in place — they are still referenced by other tests in the file.

- [ ] **Step 5: Verify the knowledge-base hook tests still pass**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/api/hooks/__tests__/useKnowledgeBase.test.ts`
Expected: PASS (remaining KB query/mutation tests green; no references to the deleted hook).

- [ ] **Step 6: Commit**

```bash
git add -A frontend/src/api/hooks/useMarketingWriterPermission.ts \
  frontend/src/api/hooks/useKnowledgeBase.ts \
  frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts
git commit -m "refactor(authz): remove legacy marketing/knowledge-base role hooks"
```

---

### Task 7: Final verification sweep

**Files:** none modified (verification only).

- [ ] **Step 1: Confirm no legacy role-string gates remain**

Run:
```bash
grep -rniE "roles\.includes\('(meeting_manager|marketing_reader|super_user)'\)|includes\(\"(meeting_manager|marketing_reader)\"" frontend/src --include="*.ts" --include="*.tsx"
```
Expected: no matches. (Any `super_user` check should now go through `usePermissionsContext`/`isSuperUser`, not a raw `roles.includes`.)

Also confirm the deleted hooks are fully gone:
```bash
grep -rn "useMeetingManagerPermission\|useMarketingWriterPermission\|useKnowledgeBaseUploadPermission" frontend/src
```
Expected: no matches.

- [ ] **Step 2: Run the full frontend build**

Run: `cd frontend && npm run build`
Expected: build succeeds with no type errors. (Per project memory, `npm run build` is stricter than `tsc --noEmit`.)

- [ ] **Step 3: Run lint**

Run: `cd frontend && npm run lint`
Expected: no errors (no unused imports left behind by the migrations).

- [ ] **Step 4: Run the full frontend test suite**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false`
Expected: all tests pass.

- [ ] **Step 5: Commit (only if any incidental fixes were needed)**

```bash
git add -A
git commit -m "chore(authz): verification fixes for permission-based gates"
```
If nothing changed in this task, skip the commit.
