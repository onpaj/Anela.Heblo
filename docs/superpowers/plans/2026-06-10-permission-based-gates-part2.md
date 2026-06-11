# Permission-Based UI Gates — Part 2 (newly-found gates)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Migrate the four remaining legacy role-string gates the part-1 search missed (photobank ×3, marketing calendar ×1), and fix the dormant Dashboard `requiredPermissions`-vs-roles mismatch — completing "no role-string gates anywhere; only `heblo_user`/`super_user` remain as roles."

**Pattern:** Same as part 1 — `const { hasPermission } = usePermissionsContext(); ... hasPermission('<wire.string>')` (matches `Sidebar.tsx`). Remove the now-unused `useMsal`/`useAuth` import from each migrated file. Permissions resolve via `/api/auth/me` in both real and mock auth; `super_user` passes everything.

**Backend-confirmed permission strings:**
- Photobank Write (`AddPhotoTag`, `RemovePhotoTag`, `BulkAddPhotoTag`, `auto-tag/RetagPhotos`) → `marketing.photobank.write`
- Photobank Admin (`CreateTag`, `DeleteTag`, settings roots/rules) → `marketing.photobank.admin`
- MarketingCalendar `import-from-outlook` (Write) → `marketing.marketing_calendar.write`

**Test command:** `cd frontend && CI=true npx react-scripts test --watchAll=false <path>`

---

### Task 8: PhotoDrawer → `marketing.photobank.write`

`PhotoDrawer.tsx` gates tag add/remove/retag behind `isAdmin = roles.includes("marketing_writer")`. Those are write actions → `marketing.photobank.write`. The variable name `isAdmin` is now a misnomer; rename to `canEditTags`. No dedicated test file exists; verify via tsc.

**Files:** Modify `frontend/src/components/marketing/photobank/PhotoDrawer.tsx`

- [ ] **Step 1: Migrate source**

Remove `import { useMsal } from "@azure/msal-react";` (line 2). Add `import { usePermissionsContext } from "../../../auth/PermissionsContext";` (alongside the other imports).

Remove the constant `const ADMIN_ROLE = "marketing_writer";` (line 14).

Replace (lines 18, 22-23):
```tsx
  const { accounts } = useMsal();
  ...
  const isAdmin =
    (accounts[0]?.idTokenClaims as any)?.roles?.includes(ADMIN_ROLE) ?? false;
```
with:
```tsx
  const { hasPermission } = usePermissionsContext();
  ...
  const canEditTags = hasPermission('marketing.photobank.write');
```
(Keep the `const [newTagName...]`/`const [copySuccess...]` lines that sit between them.)

Rename the three remaining `isAdmin` usages (lines 142, 180, 186) to `canEditTags`. Confirm none remain:
```bash
grep -n "isAdmin\|ADMIN_ROLE\|useMsal\|accounts" frontend/src/components/marketing/photobank/PhotoDrawer.tsx
```
Expected: no output.

- [ ] **Step 2: Verify compile**

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json`
Expected: no error referencing `PhotoDrawer.tsx`.

- [ ] **Step 3: Commit**
```bash
git add frontend/src/components/marketing/photobank/PhotoDrawer.tsx
git commit -m "refactor(authz): gate photo tag editing on marketing.photobank.write permission"
```

---

### Task 9: PhotobankPage → `marketing.photobank.admin` (isAdmin) + `marketing.photobank.write` (canBulkTag)

`PhotobankPage.tsx` has `isAdmin = roles.includes("super_user")` (gates the settings link) and `canBulkTag = roles.includes("marketing_writer")` (gates bulk tag selection). Two test files mock `useMsal` with `roles: ["marketing_writer"]` and rely on `canBulkTag` being true.

**Files:**
- Modify: `frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx`
- Modify: `frontend/src/components/marketing/photobank/__tests__/PhotobankPage.test.tsx`
- Modify: `frontend/src/components/marketing/photobank/pages/__tests__/PhotobankPage.selection.test.tsx`

- [ ] **Step 1: Update both test files to mock `PermissionsContext` (grant write so canBulkTag stays true)**

In **`PhotobankPage.test.tsx`**, replace the mock (lines 5-9):
```tsx
jest.mock("@azure/msal-react", () => ({
  useMsal: () => ({
    accounts: [{ idTokenClaims: { roles: ["marketing_writer"] } }],
  }),
}));
```
with:
```tsx
jest.mock("../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => p === "marketing.photobank.write",
  }),
}));
```

In **`PhotobankPage.selection.test.tsx`**, replace the mock (lines 14-16):
```tsx
jest.mock("@azure/msal-react", () => ({
  useMsal: () => ({ accounts: [{ idTokenClaims: { roles: ["marketing_writer"] } }] }),
}));
```
with:
```tsx
jest.mock("../../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => p === "marketing.photobank.write",
  }),
}));
```
(Note the path depth: this test is one level deeper — `pages/__tests__/`.)

- [ ] **Step 2: Run both tests — expect FAIL**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing/photobank/__tests__/PhotobankPage.test.tsx src/components/marketing/photobank/pages/__tests__/PhotobankPage.selection.test.tsx`
Expected: FAIL — source still calls `useMsal`.

- [ ] **Step 3: Migrate source**

In `PhotobankPage.tsx`, remove `import { useMsal } from "@azure/msal-react";` (line 4); add `import { usePermissionsContext } from "../../../../auth/PermissionsContext";`.

Remove the two constants (lines 17-18):
```tsx
const ADMIN_ROLE = "super_user";
const TAGGER_ROLE = "marketing_writer";
```

Replace (lines 50-53):
```tsx
  const { accounts } = useMsal();
  const roles = (accounts[0]?.idTokenClaims as any)?.roles as string[] | undefined;
  const isAdmin = roles?.includes(ADMIN_ROLE) ?? false;
  const canBulkTag = roles?.includes(TAGGER_ROLE) ?? false;
```
with:
```tsx
  const { hasPermission } = usePermissionsContext();
  const isAdmin = hasPermission('marketing.photobank.admin');
  const canBulkTag = hasPermission('marketing.photobank.write');
```
(The `isAdmin`/`canBulkTag` usages at lines ~229, 234, 268, 302 are unchanged.)

- [ ] **Step 4: Run both tests — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing/photobank/__tests__/PhotobankPage.test.tsx src/components/marketing/photobank/pages/__tests__/PhotobankPage.selection.test.tsx`
Expected: PASS (all tests in both files).

- [ ] **Step 5: Commit**
```bash
git add frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx \
  frontend/src/components/marketing/photobank/__tests__/PhotobankPage.test.tsx \
  frontend/src/components/marketing/photobank/pages/__tests__/PhotobankPage.selection.test.tsx
git commit -m "refactor(authz): gate photobank admin/bulk-tag on photobank admin/write permissions"
```

---

### Task 10: PhotobankSettingsPage → `marketing.photobank.admin`

`PhotobankSettingsPage.tsx` gates the whole page behind `isAdmin = roles.includes("super_user")`. Backend settings endpoints are Admin-level → `marketing.photobank.admin`. No dedicated test file; verify via tsc.

**Files:** Modify `frontend/src/components/marketing/photobank/pages/PhotobankSettingsPage.tsx`

- [ ] **Step 1: Migrate source**

Remove `import { useMsal } from '@azure/msal-react';` (line 3). Add `import { usePermissionsContext } from '../../../../auth/PermissionsContext';`.

Remove `const ADMIN_ROLE = 'super_user';` (line 9).

Replace (lines 12-14):
```tsx
  const { accounts } = useMsal();
  const isAdmin =
    (accounts[0]?.idTokenClaims as any)?.roles?.includes(ADMIN_ROLE) ?? false;
```
with:
```tsx
  const { hasPermission } = usePermissionsContext();
  const isAdmin = hasPermission('marketing.photobank.admin');
```
(The `if (!isAdmin)` 403 guard is unchanged.)

Confirm: `grep -n "useMsal\|ADMIN_ROLE\|accounts" frontend/src/components/marketing/photobank/pages/PhotobankSettingsPage.tsx` → no output.

- [ ] **Step 2: Verify compile**

Run: `cd frontend && npx tsc --noEmit -p tsconfig.json`
Expected: no error referencing `PhotobankSettingsPage.tsx`.

- [ ] **Step 3: Commit**
```bash
git add frontend/src/components/marketing/photobank/pages/PhotobankSettingsPage.tsx
git commit -m "refactor(authz): gate photobank settings page on marketing.photobank.admin permission"
```

---

### Task 11: MarketingCalendarPage → `marketing.marketing_calendar.write`

`MarketingCalendarPage.tsx` gates the "Import z Outlooku" button behind `isAdmin = getUserInfo()?.roles?.includes("super_user")`. Backend import endpoint is Write → `marketing.marketing_calendar.write`. Test currently mocks `useAuth` with `roles: []`.

**Files:**
- Modify: `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`
- Modify: `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`

- [ ] **Step 1: Update the test to mock `PermissionsContext`**

In `MarketingCalendarPage.test.tsx`, replace the `useAuth` mock (lines 120-124):
```tsx
jest.mock("../../../../auth/useAuth", () => ({
  useAuth: () => ({
    getUserInfo: () => ({ roles: [] }),
  }),
}));
```
with:
```tsx
jest.mock("../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: () => false,
  }),
}));
```

- [ ] **Step 2: Run the test — expect FAIL**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`
Expected: FAIL — source still calls `useAuth`.

- [ ] **Step 3: Migrate source**

In `MarketingCalendarPage.tsx`, remove `import { useAuth } from '../../../auth/useAuth';` (line 23); add `import { usePermissionsContext } from '../../../auth/PermissionsContext';`.

Remove `const MARKETING_IMPORT_ROLE = 'super_user';` (line 28).

Replace (lines 83-84):
```tsx
  const { getUserInfo } = useAuth();
  const isAdmin = getUserInfo()?.roles?.includes(MARKETING_IMPORT_ROLE) ?? false;
```
with:
```tsx
  const { hasPermission } = usePermissionsContext();
  const isAdmin = hasPermission('marketing.marketing_calendar.write');
```
(The `{isAdmin && ...}` import button at line ~296 is unchanged.)

- [ ] **Step 4: Run the test — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add frontend/src/components/marketing/pages/MarketingCalendarPage.tsx \
  frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
git commit -m "refactor(authz): gate Outlook import on marketing.marketing_calendar.write permission"
```

---

### Task 12: Dashboard — fix `requiredPermissions` to use `hasPermission` (dormant-bug fix)

`Dashboard.tsx` filters tiles with `tile.requiredPermissions.every(role => userRoles.includes(role))`, comparing permission wire-strings against the user's token **roles** — a namespace mismatch. It is currently dormant (all backend tiles return empty `RequiredPermissions`), so the fix changes no current behavior but makes the gate correct and role-free. Test currently does NOT mock auth and uses `requiredPermissions: []` everywhere.

**Files:**
- Modify: `frontend/src/components/pages/Dashboard.tsx`
- Modify: `frontend/src/components/pages/__tests__/Dashboard.test.tsx`

- [ ] **Step 1: Update the test — add a `PermissionsContext` mock and a coverage test for the gate**

In `Dashboard.test.tsx`, add this mock near the other `jest.mock` calls (after the `useDashboard` mock, ~line 27):
```tsx
let mockHasPermission: (perm: string) => boolean = () => true;
jest.mock("../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => mockHasPermission(p),
  }),
}));
```

In the top-level `beforeEach` (after `jest.clearAllMocks();`, ~line 131), add:
```tsx
    mockHasPermission = () => true;
```

Append a new test inside the `describe("Dashboard", ...)` block that verifies a tile is hidden when its required permission is absent:
```tsx
  it("hides tiles whose requiredPermissions the user lacks", () => {
    mockHasPermission = (p) => p !== "finance.financial_overview.read";
    mockUseTileData.mockReturnValue({
      data: [
        { ...mockTileData[0], requiredPermissions: [] },
        { ...mockTileData[1], requiredPermissions: ["finance.financial_overview.read"] },
      ],
      isLoading: false,
      error: null,
    } as any);

    renderWithQueryClient(<Dashboard />);

    // tile1 (no requirement, visible) shows; tile2 (autoShow but requires the missing perm) is filtered out.
    expect(screen.getByTestId("tile-count")).toHaveTextContent("1");
  });
```

- [ ] **Step 2: Run the test — expect FAIL**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/pages/__tests__/Dashboard.test.tsx`
Expected: FAIL — source still references `useAuth`/`userRoles` and ignores `hasPermission`, so the new test's filtering assertion fails (and/or the real `usePermissionsContext` throws without a provider once source is migrated — handled in Step 3).

- [ ] **Step 3: Migrate source**

In `Dashboard.tsx`, remove `import { useAuth } from "../../auth/useAuth";` (line 12); add `import { usePermissionsContext } from "../../auth/PermissionsContext";`.

Replace `const { getUserInfo } = useAuth();` (line 28) with:
```tsx
  const { hasPermission } = usePermissionsContext();
```

In the `visibleTileData` memo, remove the line (line 38):
```tsx
    const userRoles = getUserInfo()?.roles ?? [];
```
and replace the `hasAccess` computation (lines 47-48):
```tsx
        const hasAccess = tile.requiredPermissions.length === 0 ||
          tile.requiredPermissions.every(role => userRoles.includes(role));
```
with:
```tsx
        const hasAccess = tile.requiredPermissions.length === 0 ||
          tile.requiredPermissions.every(perm => hasPermission(perm));
```

Update the memo dependency array (line 59) from `[userSettings, allTileData, getUserInfo]` to `[userSettings, allTileData, hasPermission]`.

- [ ] **Step 4: Run the test — expect PASS**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/pages/__tests__/Dashboard.test.tsx`
Expected: PASS (all existing tests + the new one).

- [ ] **Step 5: Commit**
```bash
git add frontend/src/components/pages/Dashboard.tsx \
  frontend/src/components/pages/__tests__/Dashboard.test.tsx
git commit -m "fix(authz): filter dashboard tiles by permission instead of token roles"
```

---

### Task 13: Cleanups from final review

**Files:**
- Modify: `frontend/src/api/hooks/useKnowledgeBase.ts` (stale JSDoc)
- Modify: `frontend/src/features/articles/__tests__/ArticleGenerationForm.test.tsx` (mock default consistency)

- [ ] **Step 1: Fix the stale JSDoc in `useKnowledgeBase.ts`**

Find the JSDoc on `useKnowledgeBaseFeedbackListQuery` (~line 430) that reads `Only accessible to super_user role.` and update it to reflect the permission-based gate, e.g. change that line to:
```tsx
 * Gated in the UI by the customer.knowledge_base.write permission.
```
(If the surrounding comment no longer matches reality, adjust minimally; do not change any code.)

- [ ] **Step 2: Make the ArticleGenerationForm test default consistent**

In `frontend/src/features/articles/__tests__/ArticleGenerationForm.test.tsx`, change the module-level default and the `beforeEach` reset for `mockHasPermission` from `() => true` to `() => false`, matching the least-privilege baseline used by the other migrated test files. Then update the happy-path tests that rely on `canGenerate` being true to set `mockHasPermission = () => true` (or `(p) => p === 'marketing.article.write'`) explicitly at their start.

- [ ] **Step 3: Run the affected test to confirm green**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/features/articles/__tests__/ArticleGenerationForm.test.tsx`
Expected: PASS (4 tests).

- [ ] **Step 4: Commit**
```bash
git add frontend/src/api/hooks/useKnowledgeBase.ts frontend/src/features/articles/__tests__/ArticleGenerationForm.test.tsx
git commit -m "chore(authz): fix stale super_user comment and test mock default"
```

---

### Task 14: Final verification sweep

- [ ] **Step 1: Confirm zero role-string gates remain**
```bash
grep -rnE "roles[\"']?\]?\)?\.includes\(|\.roles\??\.includes\(|getUserInfo\(\)\?\.roles|idTokenClaims.*roles" frontend/src --include="*.ts" --include="*.tsx" | grep -viE "PermissionsContext|mockAuth|UserProfile\.tsx|accessMatrix"
```
Expected: no output (UserProfile display-logic line was already assessed as out-of-scope display, not a gate — if it appears, confirm it's still the display-only `displayRoles` builder).

Also confirm the legacy role names are gone as gates:
```bash
grep -rn "marketing_writer\|marketing_reader\|meeting_manager" frontend/src --include="*.ts" --include="*.tsx"
```
Expected: no output.

- [ ] **Step 2: Build + lint changed files + affected tests**

```bash
cd frontend && npm run build
```
Expected: compiles.

```bash
cd frontend && npx eslint \
  src/components/marketing/photobank/PhotoDrawer.tsx \
  src/components/marketing/photobank/pages/PhotobankPage.tsx \
  src/components/marketing/photobank/pages/PhotobankSettingsPage.tsx \
  src/components/marketing/pages/MarketingCalendarPage.tsx \
  src/components/pages/Dashboard.tsx \
  src/components/marketing/photobank/__tests__/PhotobankPage.test.tsx \
  src/components/marketing/photobank/pages/__tests__/PhotobankPage.selection.test.tsx \
  src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx \
  src/components/pages/__tests__/Dashboard.test.tsx
```
Expected: exit 0 (changed files lint-clean; the repo-wide `npm run lint` has pre-existing unrelated testing-library errors — out of scope).

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false \
  src/components/marketing/photobank/__tests__/PhotobankPage.test.tsx \
  src/components/marketing/photobank/pages/__tests__/PhotobankPage.selection.test.tsx \
  src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx \
  src/components/pages/__tests__/Dashboard.test.tsx \
  src/features/articles/__tests__/ArticleGenerationForm.test.tsx
```
Expected: all pass.
