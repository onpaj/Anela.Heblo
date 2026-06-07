# User-Based Authorization Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a user-centric authorization editor so admins can open a user and assign groups to them via drag-and-drop, see user info, toggle active state, and preview effective permissions.

**Architecture:** Frontend-only; all backend endpoints already exist. `UserDetailPage` mirrors `GroupDetailPage` in structure. A new `GroupsPicker` component wraps `TransferList` (same pattern as `IncludedGroupsPicker`). The existing `useAssignUserGroups` mutation gains permission-cache invalidation and a new `useUserPermissions` query is added to `useAccessManagement.ts`.

**Tech Stack:** React, React Router v6, TanStack Query, `@testing-library/react`, Tailwind CSS, Lucide icons, generated TypeScript API client.

---

## File Map

| Action | File |
|--------|------|
| Create | `frontend/src/components/access-management/GroupsPicker.tsx` |
| Create | `frontend/src/pages/UserDetailPage.tsx` |
| Create | `frontend/src/components/access-management/__tests__/GroupsPicker.test.tsx` |
| Create | `frontend/src/pages/__tests__/UserDetailPage.test.tsx` |
| Modify | `frontend/src/api/hooks/useAccessManagement.ts` |
| Modify | `frontend/src/pages/AccessManagementPage.tsx` |
| Modify | `frontend/src/pages/__tests__/AccessManagementPage.test.tsx` |
| Modify | `frontend/src/App.tsx` |

---

### Task 1: Add `useUserPermissions` hook and update `useAssignUserGroups` invalidation

**Files:**
- Modify: `frontend/src/api/hooks/useAccessManagement.ts`

- [ ] **Step 1: Write the failing test**

There is no dedicated hook-unit test file — the hooks are tested through component tests. The `useUserPermissions` hook will be exercised in the `UserDetailPage` test (Task 5). Skip to Step 3.

- [ ] **Step 2: Add `userPermissions` to the `keys` map**

In `frontend/src/api/hooks/useAccessManagement.ts`, update the `keys` object (currently lines 24-30):

```typescript
const keys = {
  catalogue: ["authz", "catalogue"] as const,
  groups: ["authz", "groups"] as const,
  group: (id: string) => ["authz", "group", id] as const,
  users: ["authz", "users"] as const,
  entraUsers: ["authz", "entra-users"] as const,
  userPermissions: (id: string) => ["authz", "user-permissions", id] as const,
};
```

- [ ] **Step 3: Add the `useUserPermissions` query**

After the `useUsers` export (line 71), add:

```typescript
import type {
  // existing imports...
  GetUserEffectivePermissionsResponse,
} from "../generated/api-client";
```

Add the query export after `useUsers`:

```typescript
export const useUserPermissions = (id: string | null) => {
  return useQuery({
    queryKey: id ? keys.userPermissions(id) : keys.userPermissions(""),
    enabled: id !== null,
    queryFn: async (): Promise<GetUserEffectivePermissionsResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_GetUserPermissions(id!);
    },
  });
};
```

- [ ] **Step 4: Update `useAssignUserGroups` `onSuccess` to also invalidate user permissions**

The current `onSuccess` (lines 134-136) only invalidates `keys.users`. Update it to also invalidate the `user-permissions` prefix so `UserDetailPage`'s effective-permissions preview refreshes after Save:

```typescript
export const useAssignUserGroups = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: string;
      request: AssignUserGroupsRequest;
    }): Promise<AssignUserGroupsResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_AssignGroups(id, request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.users });
      queryClient.invalidateQueries({ queryKey: ["authz", "user-permissions"] });
    },
  });
};
```

- [ ] **Step 5: Build to verify no type errors**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: build succeeds (no new errors).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useAccessManagement.ts
git commit -m "feat(authz): add useUserPermissions hook and invalidate permissions cache on group assignment"
```

---

### Task 2: Create `GroupsPicker` component

**Files:**
- Create: `frontend/src/components/access-management/GroupsPicker.tsx`
- Create: `frontend/src/components/access-management/__tests__/GroupsPicker.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/access-management/__tests__/GroupsPicker.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import GroupsPicker from "../GroupsPicker";

jest.mock("../../../api/hooks/useAccessManagement", () => ({
  useGroups: () => ({
    data: {
      groups: [
        { id: "g1", name: "Admins", description: "Admin group" },
        { id: "g2", name: "Editors", description: "Editor group" },
      ],
    },
    isLoading: false,
  }),
}));

describe("GroupsPicker", () => {
  it("renders available and assigned column labels", () => {
    render(<GroupsPicker value={[]} onChange={jest.fn()} />);
    expect(screen.getByText("Available groups")).toBeInTheDocument();
    expect(screen.getByText("Member of")).toBeInTheDocument();
  });

  it("shows all groups in available column when none are assigned", () => {
    render(<GroupsPicker value={[]} onChange={jest.fn()} />);
    expect(screen.getByText("Admins")).toBeInTheDocument();
    expect(screen.getByText("Editors")).toBeInTheDocument();
  });

  it("shows assigned group in right column", () => {
    render(<GroupsPicker value={["g1"]} onChange={jest.fn()} />);
    // g1 is assigned so it appears in the right column
    const assigned = screen.getAllByText("Admins");
    expect(assigned.length).toBeGreaterThan(0);
  });

  it("calls onChange with new id set when + button is clicked", () => {
    const onChange = jest.fn();
    render(<GroupsPicker value={[]} onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: /assign admins/i }));
    expect(onChange).toHaveBeenCalledWith(["g1"]);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd frontend && npx jest --testPathPattern="GroupsPicker.test" --no-coverage 2>&1 | tail -20
```

Expected: FAIL — `GroupsPicker` module not found.

- [ ] **Step 3: Create the component**

Create `frontend/src/components/access-management/GroupsPicker.tsx`:

```typescript
import React, { useMemo } from "react";
import { useGroups } from "../../api/hooks/useAccessManagement";
import TransferList, { TransferItem } from "./TransferList";

interface GroupsPickerProps {
  value: string[];
  onChange: (groupIds: string[]) => void;
}

export default function GroupsPicker({ value, onChange }: GroupsPickerProps) {
  const groups = useGroups();

  const items: TransferItem[] = useMemo(
    () =>
      (groups.data?.groups ?? []).map((g) => ({
        id: g.id ?? "",
        label: g.name ?? g.id ?? "",
      })),
    [groups.data]
  );

  if (groups.isLoading) return <div className="text-gray-500 text-sm">Loading groups…</div>;

  return (
    <TransferList
      available={items}
      assignedIds={value}
      onChange={onChange}
      labels={{ available: "Available groups", assigned: "Member of" }}
    />
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd frontend && npx jest --testPathPattern="GroupsPicker.test" --no-coverage 2>&1 | tail -20
```

Expected: PASS — all 4 tests green.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/access-management/GroupsPicker.tsx \
        frontend/src/components/access-management/__tests__/GroupsPicker.test.tsx
git commit -m "feat(authz): add GroupsPicker component wrapping TransferList for user→group assignment"
```

---

### Task 3: Create `UserDetailPage`

**Files:**
- Create: `frontend/src/pages/UserDetailPage.tsx`

The test for this page is written in Task 4 before touching the implementation. However, because the page and the test are both new files we write both together — write the test first, run it (it should fail), then create the page.

- [ ] **Step 1: Create the test file first** (see Task 4 Step 1 — do that step now)

Proceed to Task 4 Step 1, then come back to Step 2 below.

- [ ] **Step 2: Create `UserDetailPage.tsx`**

Create `frontend/src/pages/UserDetailPage.tsx`:

```typescript
import React, { useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  useUsers,
  useAssignUserGroups,
  useSetUserActive,
  useUserPermissions,
} from "../api/hooks/useAccessManagement";
import { AssignUserGroupsRequest, SetUserActiveRequest } from "../api/generated/api-client";
import { useToast } from "../contexts/ToastContext";
import GroupsPicker from "../components/access-management/GroupsPicker";

interface UserDraft {
  groupIds: string[];
}

export default function UserDetailPage() {
  const { id = "" } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();

  const usersQuery = useUsers();
  const permissionsQuery = useUserPermissions(id || null);
  const assignUserGroups = useAssignUserGroups();
  const setActive = useSetUserActive();

  const [draft, setDraft] = useState<UserDraft | null>(null);
  const [original, setOriginal] = useState<UserDraft | null>(null);
  const initialized = useRef(false);

  const user = usersQuery.data?.users?.find((u) => u.id === id);

  useEffect(() => {
    if (initialized.current) return;
    if (!user) return;

    const d: UserDraft = { groupIds: user.groupIds ?? [] };
    setDraft(d);
    setOriginal(d);
    initialized.current = true;
  }, [user]);

  const updateDraft = (patch: Partial<UserDraft>) =>
    setDraft((prev) => (prev ? { ...prev, ...patch } : prev));

  const onSave = async () => {
    if (!draft) return;
    try {
      await assignUserGroups.mutateAsync({
        id,
        request: new AssignUserGroupsRequest({ userId: id, groupIds: draft.groupIds }),
      });
      toast.showSuccess("Saved", "User groups updated successfully");
      setOriginal(draft);
    } catch {
      toast.showError("Save failed", "An error occurred while saving changes");
    }
  };

  const onToggleActive = () => {
    if (!user?.id) return;
    setActive.mutate({
      id: user.id,
      request: new SetUserActiveRequest({ userId: user.id, isActive: !user.isActive }),
    });
  };

  const isLoading = usersQuery.isLoading;
  const isSaving = assignUserGroups.isPending;

  if (isLoading) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <div className="text-gray-500">Loading user…</div>
      </div>
    );
  }

  if (!draft || !user) return null;

  const lastLoginText = user.lastLoginAt
    ? new Date(user.lastLoginAt as unknown as string).toLocaleDateString("cs-CZ", {
        day: "numeric",
        month: "long",
        year: "numeric",
      })
    : "Never logged in";

  return (
    <div className="p-8 max-w-5xl mx-auto space-y-8">
      <div className="flex items-center gap-4">
        <button
          type="button"
          onClick={() => navigate("/admin/access")}
          className="text-gray-500 hover:text-gray-700 text-sm"
        >
          ← Access management
        </button>
        <h1 className="text-2xl font-semibold text-gray-900">{user.displayName}</h1>
      </div>

      <div className="bg-white border border-gray-200 rounded-lg p-4 flex items-center justify-between">
        <div className="space-y-1">
          <p className="text-sm text-gray-700">{user.email}</p>
          <p className="text-sm text-gray-500">Last login: {lastLoginText}</p>
        </div>
        <button
          type="button"
          onClick={onToggleActive}
          disabled={setActive.isPending}
          className={`text-sm ${user.isActive ? "text-red-600" : "text-green-600"} hover:underline disabled:opacity-50`}
          aria-label={user.isActive ? "Disable user" : "Enable user"}
        >
          {user.isActive ? "Disable" : "Enable"}
        </button>
      </div>

      <section>
        <h2 className="text-lg font-medium text-gray-900 mb-3">Group membership</h2>
        <GroupsPicker
          value={draft.groupIds}
          onChange={(groupIds) => updateDraft({ groupIds })}
        />
      </section>

      <section>
        <h2 className="text-lg font-medium text-gray-900 mb-3">Effective permissions</h2>
        <p className="text-sm text-gray-500 mb-3">
          Reflects the last saved group assignment. Updates after Save.
        </p>
        {permissionsQuery.isLoading ? (
          <div className="text-gray-500 text-sm">Loading permissions…</div>
        ) : (
          <div className="flex flex-wrap gap-2">
            {(permissionsQuery.data?.permissions ?? []).map((p) => (
              <span
                key={p}
                className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-indigo-100 text-indigo-800"
              >
                {p}
              </span>
            ))}
            {(permissionsQuery.data?.permissions ?? []).length === 0 && (
              <p className="text-sm text-gray-500">No permissions assigned.</p>
            )}
          </div>
        )}
      </section>

      <div className="flex gap-3 pt-4 border-t border-gray-200">
        <button
          type="button"
          onClick={onSave}
          disabled={isSaving}
          className="px-5 py-2 bg-indigo-600 text-white rounded-md text-sm font-medium hover:bg-indigo-700 disabled:opacity-50"
        >
          {isSaving ? "Saving…" : "Save"}
        </button>
        <button
          type="button"
          onClick={() => navigate("/admin/access")}
          disabled={isSaving}
          className="px-5 py-2 border border-gray-300 text-gray-700 rounded-md text-sm font-medium hover:bg-gray-50 disabled:opacity-50"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}
```

---

### Task 4: Write and run `UserDetailPage` tests

**Files:**
- Create: `frontend/src/pages/__tests__/UserDetailPage.test.tsx`

- [ ] **Step 1: Create the test file**

Create `frontend/src/pages/__tests__/UserDetailPage.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import UserDetailPage from "../UserDetailPage";

const mockAssignUserGroups = jest.fn().mockResolvedValue({});
const mockSetActive = jest.fn();
const mockNavigate = jest.fn();

jest.mock("../../api/hooks/useAccessManagement", () => ({
  useUsers: () => ({
    data: {
      users: [
        {
          id: "user-1",
          displayName: "Alice",
          email: "alice@test.com",
          groupIds: ["g1"],
          isActive: true,
          lastLoginAt: null,
        },
      ],
    },
    isLoading: false,
  }),
  useAssignUserGroups: () => ({ mutateAsync: mockAssignUserGroups, isPending: false }),
  useSetUserActive: () => ({ mutate: mockSetActive, isPending: false }),
  useUserPermissions: () => ({
    data: { permissions: ["catalog.read", "orders.write"] },
    isLoading: false,
  }),
  useGroups: () => ({
    data: {
      groups: [
        { id: "g1", name: "Admins" },
        { id: "g2", name: "Editors" },
      ],
    },
    isLoading: false,
  }),
}));

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

jest.mock("../../contexts/ToastContext", () => ({
  useToast: () => ({
    showSuccess: jest.fn(),
    showError: jest.fn(),
  }),
}));

const renderWithRoute = (id: string) =>
  render(
    <MemoryRouter initialEntries={[`/admin/access/users/${id}`]}>
      <Routes>
        <Route path="/admin/access/users/:id" element={<UserDetailPage />} />
      </Routes>
    </MemoryRouter>
  );

beforeEach(() => {
  mockAssignUserGroups.mockClear();
  mockSetActive.mockClear();
  mockNavigate.mockClear();
});

describe("UserDetailPage", () => {
  it("renders user displayName and email", async () => {
    renderWithRoute("user-1");
    expect(await screen.findByText("Alice")).toBeInTheDocument();
    expect(screen.getByText("alice@test.com")).toBeInTheDocument();
  });

  it("shows 'Never logged in' when lastLoginAt is null", async () => {
    renderWithRoute("user-1");
    expect(await screen.findByText(/never logged in/i)).toBeInTheDocument();
  });

  it("Save calls assignUserGroups with userId and current groupIds", async () => {
    renderWithRoute("user-1");
    await screen.findByText("Alice");
    fireEvent.click(screen.getByRole("button", { name: /save/i }));
    await waitFor(() =>
      expect(mockAssignUserGroups).toHaveBeenCalledWith(
        expect.objectContaining({
          id: "user-1",
          request: expect.objectContaining({
            userId: "user-1",
            groupIds: ["g1"],
          }),
        })
      )
    );
  });

  it("enable/disable button calls setActive with toggled isActive", async () => {
    renderWithRoute("user-1");
    await screen.findByText("Alice");
    fireEvent.click(screen.getByRole("button", { name: /disable user/i }));
    expect(mockSetActive).toHaveBeenCalledWith(
      expect.objectContaining({
        id: "user-1",
        request: expect.objectContaining({ userId: "user-1", isActive: false }),
      })
    );
  });

  it("renders effective permissions from useUserPermissions", async () => {
    renderWithRoute("user-1");
    expect(await screen.findByText("catalog.read")).toBeInTheDocument();
    expect(screen.getByText("orders.write")).toBeInTheDocument();
  });

  it("Cancel navigates back to access management", async () => {
    renderWithRoute("user-1");
    await screen.findByText("Alice");
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd frontend && npx jest --testPathPattern="UserDetailPage.test" --no-coverage 2>&1 | tail -20
```

Expected: FAIL — `UserDetailPage` module not found (you already created it in Task 3 Step 2; if that is done, expect PASS here and skip to Step 3).

- [ ] **Step 3: Run test to verify it passes**

```bash
cd frontend && npx jest --testPathPattern="UserDetailPage.test" --no-coverage 2>&1 | tail -20
```

Expected: PASS — all 6 tests green.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/UserDetailPage.tsx \
        frontend/src/pages/__tests__/UserDetailPage.test.tsx
git commit -m "feat(authz): add UserDetailPage for user-centric group assignment"
```

---

### Task 5: Wire up navigation in `AccessManagementPage` (Users tab)

**Files:**
- Modify: `frontend/src/pages/AccessManagementPage.tsx`
- Modify: `frontend/src/pages/__tests__/AccessManagementPage.test.tsx`

- [ ] **Step 1: Write the failing test**

Add two tests to the existing `describe("AccessManagementPage")` block in
`frontend/src/pages/__tests__/AccessManagementPage.test.tsx`.

First, update the `useUsers` mock to return a user with an id:

```typescript
jest.mock("../../api/hooks/useAccessManagement", () => ({
  useGroups: () => ({
    data: {
      groups: [
        {
          id: "1",
          name: "Spravce",
          permissionCount: 52,
          memberCount: 1,
        },
      ],
    },
    isLoading: false,
  }),
  useUsers: () => ({
    data: {
      users: [
        {
          id: "user-1",
          displayName: "Alice",
          email: "alice@test.com",
          groupIds: [],
          isActive: true,
        },
      ],
    },
    isLoading: false,
  }),
  useCatalogue: () => ({ data: { permissions: ["catalog.read"] } }),
  useDeleteGroup: () => ({ mutate: jest.fn(), isPending: false }),
  useSetUserActive: () => ({ mutate: jest.fn(), isPending: false }),
}));
```

Then add these tests inside the existing `describe` block:

```typescript
it("clicking a user name in Users tab navigates to user detail page", () => {
  renderPage();
  fireEvent.click(screen.getByRole("button", { name: /users/i }));
  fireEvent.click(screen.getByRole("button", { name: /alice/i }));
  expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/user-1");
});

it("clicking Edit icon in Users tab navigates to user detail page", () => {
  renderPage();
  fireEvent.click(screen.getByRole("button", { name: /users/i }));
  fireEvent.click(screen.getByRole("button", { name: /edit alice/i }));
  expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/user-1");
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd frontend && npx jest --testPathPattern="AccessManagementPage.test" --no-coverage 2>&1 | tail -20
```

Expected: FAIL — the user name is a plain div, not a button, so the test can't find it.

- [ ] **Step 3: Update `AccessManagementPage.tsx` Users tab**

In `frontend/src/pages/AccessManagementPage.tsx`, replace the Users tab user row (lines 97–123):

```tsx
{tab === "users" && (
  <div className="space-y-3">
    {users.isLoading && <div className="text-gray-500">Loading users…</div>}
    {users.data?.users?.map((u) => (
      <div
        key={u.id}
        className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-4"
      >
        <div className="min-w-0 flex-1">
          <button
            onClick={() => u.id && navigate(`/admin/access/users/${u.id}`)}
            className="font-medium text-gray-900 hover:text-indigo-600 text-left"
          >
            {u.displayName}
          </button>
          <p className="text-sm text-gray-500">
            {u.email} · {u.groupIds?.length ?? 0} groups
          </p>
        </div>
        <div className="flex items-center gap-2 ml-4">
          <button
            onClick={() => u.id && navigate(`/admin/access/users/${u.id}`)}
            className="p-2 text-gray-400 hover:text-indigo-600 rounded-lg hover:bg-gray-50"
            aria-label={`Edit ${u.displayName}`}
          >
            <Edit className="w-4 h-4" />
          </button>
          <button
            onClick={() =>
              u.id &&
              setActive.mutate({
                id: u.id,
                request: new SetUserActiveRequest({ userId: u.id, isActive: !u.isActive }),
              })
            }
            disabled={setActive.isPending}
            className={`text-sm ${u.isActive ? "text-red-600" : "text-green-600"} hover:underline`}
            aria-label={`Toggle active ${u.email}`}
          >
            {u.isActive ? "Disable" : "Enable"}
          </button>
        </div>
      </div>
    ))}
  </div>
)}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd frontend && npx jest --testPathPattern="AccessManagementPage.test" --no-coverage 2>&1 | tail -20
```

Expected: PASS — all tests green including the 2 new ones.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/AccessManagementPage.tsx \
        frontend/src/pages/__tests__/AccessManagementPage.test.tsx
git commit -m "feat(authz): make user name and edit icon navigate to UserDetailPage in Users tab"
```

---

### Task 6: Register the route in `App.tsx`

**Files:**
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Add the import for `UserDetailPage`**

In `frontend/src/App.tsx`, add after line 39 (after the `GroupDetailPage` import):

```typescript
import UserDetailPage from "./pages/UserDetailPage";
```

- [ ] **Step 2: Add the route**

In `frontend/src/App.tsx`, add after the `GroupDetailPage` route (after line 505):

```tsx
<Route
  path="/admin/access/users/:id"
  element={
    <RequireAccess requiredRole="administration.read">
      <UserDetailPage />
    </RequireAccess>
  }
/>
```

- [ ] **Step 3: Build to verify**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: build succeeds with no errors.

- [ ] **Step 4: Lint check**

```bash
cd frontend && npm run lint 2>&1 | tail -20
```

Expected: no errors.

- [ ] **Step 5: Run full test suite**

```bash
cd frontend && npm test -- --no-coverage 2>&1 | tail -30
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/App.tsx
git commit -m "feat(authz): register /admin/access/users/:id route for UserDetailPage"
```

---

## Self-Review

### Spec coverage

| Spec requirement | Task covering it |
|---|---|
| `GroupsPicker` wraps `TransferList`, mirrors `IncludedGroupsPicker`, no `currentGroupId` filter | Task 2 |
| `UserDetailPage` — `useParams`, load via `useUsers().find` | Task 3 |
| Header: back link, displayName, email, lastLoginAt / "Never logged in" | Task 3 |
| Enable/disable button via `useSetUserActive`, immediate | Task 3 |
| Draft state seeded from `user.groupIds`, `initialized` ref guard | Task 3 |
| `GroupsPicker` wired to draft | Task 3 |
| Effective permissions read-only preview with stale note | Task 3 |
| Save: single `assignUserGroups.mutateAsync` call | Task 3 |
| `useUserPermissions` hook | Task 1 |
| `useAssignUserGroups` invalidates user-permissions cache | Task 1 |
| Users tab: name button + Edit icon → `/admin/access/users/:id` | Task 5 |
| Route in `App.tsx` with `RequireAccess` | Task 6 |
| Tests: `GroupsPicker` labels, mapping, onChange | Task 2 |
| Tests: `UserDetailPage` — loads user, save calls mutation, enable/disable, permissions render | Task 4 |
| Tests: `AccessManagementPage` — clicking user name/edit navigates | Task 5 |
| Build + lint gate | Tasks 1, 6 |

All requirements covered. No gaps found.

### Placeholder scan

No placeholders found. All code blocks are complete and self-contained.

### Type consistency

- `GroupsPicker` props: `value: string[]`, `onChange: (groupIds: string[]) => void` — consistent across Task 2 component and Task 3 usage.
- `useUserPermissions(id: string | null)` — matches Task 1 definition and Task 4 mock.
- `AssignUserGroupsRequest({ userId: id, groupIds: draft.groupIds })` — consistent with existing `GroupDetailPage` usage pattern.
- `SetUserActiveRequest({ userId: user.id, isActive: !user.isActive })` — consistent with existing `AccessManagementPage` usage.
- `keys.userPermissions` added to `keys` map before use in `onSuccess`.
