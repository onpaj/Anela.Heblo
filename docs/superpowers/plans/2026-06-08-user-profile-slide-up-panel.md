# User Profile Slide-Up Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the centered full-screen modal overlay in `UserProfile.tsx` with a panel that slides up from the sidebar's bottom user row, overlaying nav content, closing on click-outside or toggle.

**Architecture:** The Sidebar's bottom section wrappers gain `relative` so the panel can position absolutely against the full row width. `UserProfile.tsx` drops the `fixed inset-0` overlay in favour of a Headless UI `Transition`-animated absolute panel. Click-outside is handled via `useRef` + `useEffect`. The `compact` prop controls panel width (full-row for expanded, fixed 240px for the 64px collapsed sidebar).

**Tech Stack:** React 18, TypeScript, `@headlessui/react ^2.2.7` (`Transition`), Tailwind CSS, `react-scripts test` (Jest + @testing-library/react + @testing-library/user-event).

---

## File Structure

- **Modify:** `frontend/src/components/layout/Sidebar.tsx` — add `relative` to both bottom section wrappers (expanded row + compact column) so the panel's `absolute` positioning is scoped to the full row width, not just UserProfile's container.
- **Modify:** `frontend/src/components/auth/UserProfile.tsx` — replace modal with slide-up panel.
- **Modify:** `frontend/src/components/auth/UserProfile.test.tsx` — rename `openModal` → `openPanel`, add toggle test.

**Positioning note:** `UserProfile`'s own wrapper div must NOT carry `relative` — it holds `ref={panelRef}` for click-outside detection but must not be the positioning context. That role belongs to Sidebar's `relative` bottom section, so `inset-x-0` on the panel spans the full row (including the collapse-toggle button area).

---

## Task 1: Add `relative` to Sidebar bottom section wrappers

**Files:**
- Modify: `frontend/src/components/layout/Sidebar.tsx` (lines 633 and 647)

No test needed — this is a pure CSS positioning change with no behaviour change.

- [ ] **Step 1: Add `relative` to the expanded bottom row**

In `frontend/src/components/layout/Sidebar.tsx`, find line 633 and add `relative` to the class list:

```tsx
// BEFORE (line 633):
<div className="flex items-center justify-between h-16 py-2 border-t border-gray-100">

// AFTER:
<div className="relative flex items-center justify-between h-16 py-2 border-t border-gray-100">
```

- [ ] **Step 2: Add `relative` to the compact bottom column**

On line 647, add `relative`:

```tsx
// BEFORE (line 647):
<div className="flex flex-col items-center py-2 space-y-2 border-t border-gray-100">

// AFTER:
<div className="relative flex flex-col items-center py-2 space-y-2 border-t border-gray-100">
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/layout/Sidebar.tsx
git commit -m "refactor(ui): add relative positioning to sidebar user profile row containers"
```

---

## Task 2: Slide-up panel in UserProfile

**Files:**
- Modify: `frontend/src/components/auth/UserProfile.test.tsx`
- Modify: `frontend/src/components/auth/UserProfile.tsx`

- [ ] **Step 1: Write the failing toggle test**

Append this test inside the existing `describe` block in `frontend/src/components/auth/UserProfile.test.tsx`:

```tsx
  it("closes the panel when the trigger is clicked a second time", async () => {
    mockCtx = {
      permissions: ["catalog.read"],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    render(<UserProfile />);
    const button = screen.getByRole("button");

    await userEvent.click(button);
    expect(screen.getByText("Oprávnění")).toBeInTheDocument();

    await userEvent.click(button);
    expect(screen.queryByText("Oprávnění")).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the test and confirm it FAILS**

```bash
cd frontend && npx react-scripts test src/components/auth/UserProfile.test.tsx --watchAll=false --forceExit 2>&1 | tail -20
```

Expected: 6 pass, 1 fail — the new test fails because `onClick={() => setShowModal(true)}` never closes the panel once open.

- [ ] **Step 3: Rewrite UserProfile.tsx**

Replace the entire contents of `frontend/src/components/auth/UserProfile.tsx` with:

```tsx
import React, { useState, useRef, useEffect } from "react";
import { Transition } from "@headlessui/react";
import { User, LogIn, LogOut, ShieldCheck, KeyRound, Users } from "lucide-react";
import { useAuth } from "../../auth/useAuth";
import { useMockAuth, shouldUseMockAuth } from "../../auth/mockAuth";
import { usePermissionsContext } from "../../auth/PermissionsContext";

interface UserProfileProps {
  compact?: boolean;
  menuPosition?: "above" | "below";
}

const UserProfile: React.FC<UserProfileProps> = ({
  compact = false,
  menuPosition = "above",
}) => {
  const realAuth = useAuth();
  const mockAuth = useMockAuth();

  const auth = shouldUseMockAuth() ? mockAuth : realAuth;
  const { isAuthenticated, login, logout, getUserInfo, getStoredUserInfo, inProgress } = auth;

  const [showPanel, setShowPanel] = useState(false);
  const panelRef = useRef<HTMLDivElement>(null);
  const userInfo = getUserInfo();
  const storedUserInfo = getStoredUserInfo();
  const { permissions, groups, isSuperUser, isLoading: permissionsLoading } =
    usePermissionsContext();

  useEffect(() => {
    const handleMouseDown = (e: MouseEvent) => {
      if (panelRef.current && !panelRef.current.contains(e.target as Node)) {
        setShowPanel(false);
      }
    };
    if (showPanel) {
      document.addEventListener("mousedown", handleMouseDown);
    }
    return () => {
      document.removeEventListener("mousedown", handleMouseDown);
    };
  }, [showPanel]);

  const handleLogin = async () => {
    try {
      await login();
    } catch (error) {
      console.error("Login error:", error);
    }
  };

  const handleLogout = async () => {
    try {
      await logout();
      setShowPanel(false);
    } catch (error) {
      console.error("Logout error:", error);
    }
  };

  if (inProgress === "login" || inProgress === "logout") {
    return (
      <div className={`flex items-center ${compact ? "justify-center" : "justify-center p-2"}`}>
        <div className="w-8 h-8 bg-gray-300 rounded-full animate-pulse"></div>
      </div>
    );
  }

  if (!isAuthenticated) {
    if (compact) {
      return (
        <button
          onClick={handleLogin}
          className="w-8 h-8 bg-gray-400 rounded-full flex items-center justify-center hover:bg-gray-500 transition-colors"
          title="Sign in"
        >
          <User className="h-4 w-4 text-white" />
        </button>
      );
    }

    return (
      <button
        onClick={handleLogin}
        className="flex items-center space-x-3 p-2 w-full text-left hover:bg-secondary-blue-pale/50 rounded-md transition-colors group"
        title="Sign in"
      >
        <div className="w-8 h-8 bg-gray-400 rounded-full flex items-center justify-center">
          <User className="h-4 w-4 text-white" />
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-gray-700 group-hover:text-gray-900">Sign in</p>
          <p className="text-xs text-gray-500">Click to authenticate</p>
        </div>
        <LogIn className="h-4 w-4 text-gray-400 group-hover:text-gray-600" />
      </button>
    );
  }

  const panelPositionClass = compact
    ? "absolute bottom-full left-0 w-60"
    : "absolute bottom-full inset-x-0";

  return (
    <div ref={panelRef}>
      {/* Trigger */}
      {compact ? (
        <button
          onClick={() => setShowPanel((prev) => !prev)}
          className="w-8 h-8 bg-primary-blue rounded-full flex items-center justify-center hover:bg-accent-blue-bright transition-colors"
          title={`${userInfo?.name} (${userInfo?.email})`}
        >
          <span className="text-white text-sm font-medium">{userInfo?.initials || "U"}</span>
        </button>
      ) : (
        <button
          onClick={() => setShowPanel((prev) => !prev)}
          className="flex items-center space-x-3 p-2 w-full text-left hover:bg-secondary-blue-pale/50 rounded-md transition-colors group"
        >
          <div className="w-8 h-8 bg-primary-blue rounded-full flex items-center justify-center">
            <span className="text-white text-sm font-medium">{userInfo?.initials || "U"}</span>
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-sm font-medium text-neutral-slate group-hover:text-neutral-slate truncate">
              {userInfo?.name || "User"}
            </p>
            <p className="text-xs text-gray-500 truncate">{userInfo?.email || "user@example.com"}</p>
          </div>
        </button>
      )}

      {/* Slide-up panel */}
      <Transition
        show={showPanel}
        enter="transition ease-out duration-200"
        enterFrom="opacity-0 translate-y-2"
        enterTo="opacity-100 translate-y-0"
        leave="transition ease-in duration-150"
        leaveFrom="opacity-100 translate-y-0"
        leaveTo="opacity-0 translate-y-2"
      >
        <div
          className={`${panelPositionClass} z-10 rounded-t-xl shadow-lg bg-white border border-gray-100 overflow-hidden`}
        >
          <div className="max-h-[80vh] overflow-y-auto">
            {/* User identity */}
            <div className="px-5 py-5 flex items-center space-x-4 border-b border-gray-100">
              <div className="w-12 h-12 bg-primary-blue rounded-full flex items-center justify-center shrink-0">
                <span className="text-white text-lg font-semibold">
                  {userInfo?.initials || "U"}
                </span>
              </div>
              <div className="min-w-0">
                <p className="text-sm font-semibold text-neutral-slate truncate">
                  {userInfo?.name}
                </p>
                <p className="text-xs text-gray-500 truncate">{userInfo?.email}</p>
                {storedUserInfo?.lastLogin && (
                  <p className="text-xs text-gray-400 mt-0.5">
                    Poslední přihlášení:{" "}
                    {new Date(storedUserInfo.lastLogin).toLocaleString("cs-CZ")}
                  </p>
                )}
              </div>
            </div>

            {/* Roles */}
            {userInfo?.roles && userInfo.roles.length > 0 && (
              <div className="px-5 py-4 border-b border-gray-100">
                <div className="flex items-center space-x-2 mb-3">
                  <ShieldCheck className="h-4 w-4 text-primary-blue" />
                  <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    Role
                  </span>
                </div>
                <div className="flex flex-wrap gap-2">
                  {userInfo.roles.map((role) => (
                    <span
                      key={role}
                      className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-secondary-blue-pale text-primary-blue"
                    >
                      {role}
                    </span>
                  ))}
                </div>
              </div>
            )}

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

            {/* Footer */}
            <div className="px-5 py-4">
              <button
                onClick={handleLogout}
                className="flex items-center justify-center space-x-2 w-full px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
              >
                <LogOut className="h-4 w-4" />
                <span>Odhlásit se</span>
              </button>
            </div>
          </div>
        </div>
      </Transition>
    </div>
  );
};

export default UserProfile;
```

- [ ] **Step 4: Rename `openModal` → `openPanel` in the test helper**

In `frontend/src/components/auth/UserProfile.test.tsx`, rename the helper and its call sites:

```tsx
// BEFORE (around line 45):
const openModal = async () => {
  render(<UserProfile />);
  await userEvent.click(screen.getByRole("button"));
};

// AFTER:
const openPanel = async () => {
  render(<UserProfile />);
  await userEvent.click(screen.getByRole("button"));
};
```

Also update every call site: replace `await openModal()` with `await openPanel()` throughout the file (6 occurrences in the existing tests).

- [ ] **Step 5: Run all UserProfile tests and confirm all 7 pass**

```bash
cd frontend && npx react-scripts test src/components/auth/UserProfile.test.tsx --watchAll=false --forceExit 2>&1 | tail -20
```

Expected: 7 tests passing.

> **If the toggle test fails with the panel still in the DOM:** Headless UI's Transition may not fire `transitionend` in jsdom. Wrap the closing assertion with `waitFor`:
> ```tsx
> await userEvent.click(button);
> await waitFor(() => {
>   expect(screen.queryByText("Oprávnění")).not.toBeInTheDocument();
> });
> ```
> Add `import { waitFor } from "@testing-library/react";` at the top of the test file.

- [ ] **Step 6: Run the full frontend build to confirm no TypeScript errors**

```bash
cd frontend && CI=false npm run build 2>&1 | tail -20
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/components/auth/UserProfile.tsx frontend/src/components/auth/UserProfile.test.tsx
git commit -m "feat(ui): replace user profile modal with slide-up panel"
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Covered by |
|---|---|
| Remove `fixed inset-0` overlay | Task 2 Step 3 — new JSX has no fixed overlay |
| `relative` on Sidebar bottom wrappers | Task 1 |
| `Transition` slide-up animation | Task 2 Step 3 — enter/leave classes |
| Close on toggle | Task 2 Step 3 — `setShowPanel((prev) => !prev)` |
| Close on click-outside | Task 2 Step 3 — `useEffect` + `mousedown` |
| `rounded-t-xl shadow-lg` panel shell | Task 2 Step 3 — panel div classes |
| Compact mode `w-60` | Task 2 Step 3 — `panelPositionClass` |
| Expanded mode `inset-x-0` | Task 2 Step 3 — `panelPositionClass` |
| No X close button | Task 2 Step 3 — removed from JSX |
| All existing content preserved | Task 2 Step 3 — sections unchanged |
| Toggle test (TDD anchor) | Task 2 Steps 1–2 |
| Test helper rename | Task 2 Step 4 |

**Placeholder scan:** None found — all steps include full code.

**Type consistency:** `showPanel` (boolean state), `panelRef` (RefObject<HTMLDivElement>), `panelPositionClass` (string) — consistent across all references in the implementation.
