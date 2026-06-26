# Show DB Roles + Permissions + Groups in User Modal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add visible **Permissions** and **Groups** sections (with a super-user shortcut) to the user info modal opened from the sidebar avatar, alongside the existing **Role** chips.

**Architecture:** The user info modal in `UserProfile.tsx` currently renders only the user's id-token roles (from `useAuth().getUserInfo().roles`). With the DB-backed RBAC (commit `a6d281494`), the actual access truth lives in `/api/auth/me` and is exposed via `PermissionsContext`. This plan keeps the existing Role section untouched and adds two new sections fed by `usePermissionsContext()`. No backend changes. No new component file — the additions live inside `UserProfile.tsx`.

**Tech Stack:** React 18, TypeScript, `react-scripts test` (Jest + @testing-library/react + @testing-library/user-event), Tailwind, `lucide-react` icons.

---

## File Structure

- **Modify:** `frontend/src/components/auth/UserProfile.tsx` — add Permissions and Groups sections + super-user badge inside the existing modal.
- **Create:** `frontend/src/components/auth/UserProfile.test.tsx` — new test file (none exists today). Follows the mocking pattern from `RequireAccess.test.tsx`.

No other files are touched. The provider `PermissionsProvider` already wraps the sidebar's render tree (`App.tsx:407`), so `usePermissionsContext()` is safe to call from `UserProfile`.

---

## Task 1: Permissions section for a non-super-user

**Files:**
- Create: `frontend/src/components/auth/UserProfile.test.tsx`
- Modify: `frontend/src/components/auth/UserProfile.tsx` (add section between lines 171 and 173)

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/auth/UserProfile.test.tsx`:

```tsx
import React from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import UserProfile from "./UserProfile";

const baseAuth = {
  isAuthenticated: true,
  login: jest.fn(),
  logout: jest.fn(),
  inProgress: "none",
  getUserInfo: () => ({
    name: "Test User",
    email: "test@anela.cz",
    initials: "TU",
    roles: ["super_user"],
  }),
  getStoredUserInfo: () => null,
};

let mockCtx = {
  permissions: [] as string[],
  isSuperUser: false,
  groups: [] as string[],
  isLoading: false,
  hasPermission: (_: string) => false,
};

jest.mock("../../auth/useAuth", () => ({ useAuth: () => baseAuth }));
jest.mock("../../auth/mockAuth", () => ({
  useMockAuth: () => baseAuth,
  shouldUseMockAuth: () => false,
}));
jest.mock("../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => mockCtx,
}));

const openModal = async () => {
  render(<UserProfile />);
  await userEvent.click(screen.getByRole("button"));
};

describe("UserProfile permissions display", () => {
  it("renders Permissions chips when user has DB permissions", async () => {
    mockCtx = {
      permissions: ["catalog.read", "journal.read"],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    await openModal();

    expect(screen.getByText("Oprávnění")).toBeInTheDocument();
    expect(screen.getByText("catalog.read")).toBeInTheDocument();
    expect(screen.getByText("journal.read")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `cd frontend && npx jest src/components/auth/UserProfile.test.tsx --watchAll=false`

Expected: FAIL — `Unable to find an element with the text: Oprávnění` (the Permissions section does not exist yet).

- [ ] **Step 3: Add the Permissions section to UserProfile.tsx**

In `frontend/src/components/auth/UserProfile.tsx`:

a) Add the icon import on line 2 — replace:

```tsx
import { User, LogIn, LogOut, X, ShieldCheck } from "lucide-react";
```

with:

```tsx
import { User, LogIn, LogOut, X, ShieldCheck, KeyRound, Users } from "lucide-react";
```

b) Add the context import on line 4 — after the `useMockAuth` import:

```tsx
import { usePermissionsContext } from "../../auth/PermissionsContext";
```

c) Inside the component body, just after line 23 (`const storedUserInfo = getStoredUserInfo();`):

```tsx
  const { permissions, groups, isSuperUser, isLoading: permissionsLoading } =
    usePermissionsContext();
```

d) Insert the new Permissions section between the existing Role block (closes on line 171) and the Footer block (opens on line 174). Place this immediately after `)}` on line 171:

```tsx
              {/* Permissions */}
              {!permissionsLoading && !isSuperUser && permissions.length > 0 && (
                <div className="px-5 py-4 border-b border-gray-100">
                  <div className="flex items-center space-x-2 mb-3">
                    <KeyRound className="h-4 w-4 text-emerald-600" />
                    <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
                      Oprávnění
                    </span>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {permissions.map((perm) => (
                      <span
                        key={perm}
                        className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-emerald-50 text-emerald-700"
                      >
                        {perm}
                      </span>
                    ))}
                  </div>
                </div>
              )}
```

- [ ] **Step 4: Run the test and watch it pass**

Run: `cd frontend && npx jest src/components/auth/UserProfile.test.tsx --watchAll=false`

Expected: PASS — 1 test passing.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/auth/UserProfile.tsx frontend/src/components/auth/UserProfile.test.tsx
git commit -m "feat(authz): show DB permissions chips in user info modal"
```

---

## Task 2: Super-user badge (skip permission list when wildcard)

**Files:**
- Modify: `frontend/src/components/auth/UserProfile.test.tsx`
- Modify: `frontend/src/components/auth/UserProfile.tsx`

- [ ] **Step 1: Write the failing test**

Append to `frontend/src/components/auth/UserProfile.test.tsx` inside the existing `describe` block:

```tsx
  it("renders a single super-user badge when isSuperUser is true", async () => {
    mockCtx = {
      permissions: ["catalog.read", "journal.read", "finance.read"],
      isSuperUser: true,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    await openModal();

    expect(screen.getByText("Super User · vše povoleno")).toBeInTheDocument();
    expect(screen.queryByText("catalog.read")).not.toBeInTheDocument();
    expect(screen.queryByText("journal.read")).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `cd frontend && npx jest src/components/auth/UserProfile.test.tsx --watchAll=false`

Expected: FAIL — `Unable to find an element with the text: Super User · vše povoleno`, AND the "should not be in document" assertions fail because the implementation still lists `catalog.read` etc. for super-users.

- [ ] **Step 3: Update the Permissions section to handle the super-user case**

In `frontend/src/components/auth/UserProfile.tsx`, replace the entire Permissions block added in Task 1 with:

```tsx
              {/* Permissions */}
              {!permissionsLoading && (isSuperUser || permissions.length > 0) && (
                <div className="px-5 py-4 border-b border-gray-100">
                  <div className="flex items-center space-x-2 mb-3">
                    <KeyRound className="h-4 w-4 text-emerald-600" />
                    <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
                      Oprávnění
                    </span>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {isSuperUser ? (
                      <span className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-amber-50 text-amber-700">
                        Super User · vše povoleno
                      </span>
                    ) : (
                      permissions.map((perm) => (
                        <span
                          key={perm}
                          className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-emerald-50 text-emerald-700"
                        >
                          {perm}
                        </span>
                      ))
                    )}
                  </div>
                </div>
              )}
```

- [ ] **Step 4: Run all UserProfile tests and watch them pass**

Run: `cd frontend && npx jest src/components/auth/UserProfile.test.tsx --watchAll=false`

Expected: PASS — 2 tests passing.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/auth/UserProfile.tsx frontend/src/components/auth/UserProfile.test.tsx
git commit -m "feat(authz): show single super-user badge instead of full permission list"
```

---

## Task 3: Groups section

**Files:**
- Modify: `frontend/src/components/auth/UserProfile.test.tsx`
- Modify: `frontend/src/components/auth/UserProfile.tsx`

- [ ] **Step 1: Write the failing test**

Append to `frontend/src/components/auth/UserProfile.test.tsx` inside the existing `describe` block:

```tsx
  it("renders Groups chips when user belongs to DB groups", async () => {
    mockCtx = {
      permissions: ["catalog.read"],
      isSuperUser: false,
      groups: ["Finance", "Marketing"],
      isLoading: false,
      hasPermission: () => true,
    };

    await openModal();

    expect(screen.getByText("Skupiny")).toBeInTheDocument();
    expect(screen.getByText("Finance")).toBeInTheDocument();
    expect(screen.getByText("Marketing")).toBeInTheDocument();
  });

  it("hides Groups section when user has no groups", async () => {
    mockCtx = {
      permissions: ["catalog.read"],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    await openModal();

    expect(screen.queryByText("Skupiny")).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `cd frontend && npx jest src/components/auth/UserProfile.test.tsx --watchAll=false`

Expected: FAIL — `Unable to find an element with the text: Skupiny` on the first new test. (The "hides" test happens to pass already, but it locks the behavior in once the section exists.)

- [ ] **Step 3: Add the Groups section to UserProfile.tsx**

In `frontend/src/components/auth/UserProfile.tsx`, insert immediately after the Permissions section closing `)}` and before the `{/* Footer */}` comment (around the original line 173):

```tsx
              {/* Groups */}
              {!permissionsLoading && groups.length > 0 && (
                <div className="px-5 py-4 border-b border-gray-100">
                  <div className="flex items-center space-x-2 mb-3">
                    <Users className="h-4 w-4 text-primary-blue" />
                    <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
                      Skupiny
                    </span>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {groups.map((group) => (
                      <span
                        key={group}
                        className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-secondary-blue-pale text-primary-blue"
                      >
                        {group}
                      </span>
                    ))}
                  </div>
                </div>
              )}
```

- [ ] **Step 4: Run all UserProfile tests and watch them pass**

Run: `cd frontend && npx jest src/components/auth/UserProfile.test.tsx --watchAll=false`

Expected: PASS — 4 tests passing.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/auth/UserProfile.tsx frontend/src/components/auth/UserProfile.test.tsx
git commit -m "feat(authz): show DB group memberships in user info modal"
```

---

## Task 4: Build, lint, and full test suite verification

**Files:** none (verification only)

- [ ] **Step 1: Run the frontend production build**

Run: `cd frontend && npm run build`

Expected: build succeeds with no TypeScript errors. (Remember: `npm run build` is stricter than `npx tsc --noEmit`.)

- [ ] **Step 2: Run lint**

Run: `cd frontend && npm run lint`

Expected: no new lint errors.

- [ ] **Step 3: Run the existing auth test suite to confirm nothing else broke**

Run: `cd frontend && npx jest src/components/auth src/auth --watchAll=false`

Expected: all tests pass (existing `RequireAccess.test.tsx`, `AuthGuard.test.tsx`, `useAuth.test.ts`, plus the new `UserProfile.test.tsx`).

- [ ] **Step 4: Manual smoke test in mock auth mode**

Run: `cd frontend && REACT_APP_USE_MOCK_AUTH=true npm start`

In the browser: log in with mock auth, click the avatar in the sidebar, and confirm the modal shows:
- Existing **Role** chips (from id-token roles)
- New **Oprávnění** section with either the "Super User · vše povoleno" badge or per-permission chips
- New **Skupiny** section if the mock identity has any group memberships (hidden otherwise)

- [ ] **Step 5: Commit any cleanup (if needed) and finish**

If the build/lint steps revealed any tweaks, commit them:

```bash
git add frontend/src/components/auth/UserProfile.tsx
git commit -m "chore(authz): cleanup after lint/build verification"
```

If no changes were needed, this step is a no-op.

---

## Self-review notes (already applied)

- **Spec coverage:** "Render a list in the sidebar (display)" is implemented in Tasks 1–3 (Permissions chips, super-user badge, Groups chips). The existing Role section is preserved as the user requested both visible.
- **Scope discipline:** No changes to `Sidebar.tsx`'s `canSee()` gating — that's a separate concern (the idToken vs DB-permissions inconsistency) and is intentionally out of scope.
- **Type consistency:** All three new sections read from the same `usePermissionsContext()` shape declared in `PermissionsContext.tsx:4-10` (`permissions: string[]`, `groups: string[]`, `isSuperUser: boolean`, `isLoading: boolean`).
- **Empty-state handling:** Each section is wrapped in a guard so empty arrays / `isLoading` simply hide it.
- **i18n:** Labels use Czech ("Oprávnění", "Skupiny", "Super User · vše povoleno") to match the existing "Role" / "Uživatel" / "Poslední přihlášení" / "Odhlásit se" labels in the same modal.
