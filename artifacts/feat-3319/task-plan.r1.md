# UserDetailPage Error and Not-Found State Handling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the silent `null` fallback in `UserDetailPage.tsx` with explicit error and not-found UI states, matching the pattern used by `UsersGrid.tsx`.

**Architecture:** `UserDetailPage.tsx` currently collapses both "API error" and "user not found" into a single `if (!draft || !user) return null` guard, rendering a blank page. The fix splits that guard into three ordered checks — error, not-found, and draft-not-initialized — each returning a purposeful UI. The `<ErrorState>` component already exists and is already used elsewhere; this is purely a guard-block change plus one import.

**Tech Stack:** React 18, TypeScript, Tailwind CSS, TanStack Query (`isError`/`isLoading` flags), `<ErrorState>` from `frontend/src/components/common/ErrorState.tsx`.

---

### task: fix-user-detail-error-states

**Files:**
- Modify: `frontend/src/pages/UserDetailPage.tsx`

---

- [ ] **Step 1: Read the file to orient yourself**

  Open `frontend/src/pages/UserDetailPage.tsx` and confirm these two facts before touching anything:

  1. Line 1 starts with `import React, { useEffect, useRef, useState } from "react";` — the import block you will extend.
  2. Lines 117–128 contain the guard block you will replace:

  ```tsx
  const isLoading = usersQuery.isLoading;
  const isSaving = assignUserGroups.isPending || updateUser.isPending;

  if (isLoading) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <div className="text-gray-500">Loading user…</div>
      </div>
    );
  }

  if (!draft || !user) return null;
  ```

  No edits in this step — reading only.

---

- [ ] **Step 2: Add the `ErrorState` import**

  Insert one import line after the last existing import (line 17, after `useUnsavedChangesDialog`). The relative path from `frontend/src/pages/` to the component is `../components/common/ErrorState`.

  Find this exact text in the file:

  ```tsx
  import {
    draftsEqual,
    useUnsavedChangesDialog,
  } from "../hooks/useUnsavedChangesDialog";
  ```

  Replace it with:

  ```tsx
  import {
    draftsEqual,
    useUnsavedChangesDialog,
  } from "../hooks/useUnsavedChangesDialog";
  import ErrorState from "../components/common/ErrorState";
  ```

---

- [ ] **Step 3: Replace the guard block**

  Find this exact text in the file (lines 117–128):

  ```tsx
  const isLoading = usersQuery.isLoading;
  const isSaving = assignUserGroups.isPending || updateUser.isPending;

  if (isLoading) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <div className="text-gray-500">Loading user…</div>
      </div>
    );
  }

  if (!draft || !user) return null;
  ```

  Replace it with:

  ```tsx
  const isLoading = usersQuery.isLoading;
  const isSaving = assignUserGroups.isPending || updateUser.isPending;

  if (isLoading) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <div className="text-gray-500">Loading user…</div>
      </div>
    );
  }

  if (usersQuery.isError) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <ErrorState message="Failed to load users." className="flex-1" />
      </div>
    );
  }

  if (!user) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <p className="text-gray-500">User not found.</p>
        <button
          type="button"
          onClick={() => requestNavigation("/admin/access/users")}
          className="text-gray-500 hover:text-gray-700 text-sm mt-2"
        >
          ← Access management
        </button>
      </div>
    );
  }

  if (!draft) return null;
  ```

  **Guard ordering rationale (do not reorder):**
  1. `isLoading` — query in-flight, nothing to render.
  2. `isError` — query settled with failure, show error UI.
  3. `!user` — query succeeded but ID not found in list, show not-found UI with back-link.
  4. `!draft` — user found but `useEffect` hasn't run yet (one-render gap); return null to avoid rendering with stale draft. This guard must come after `!user` or it will shadow the not-found case.

  **Why `requestNavigation` not `navigate`:** `requestNavigation` is provided by `useUnsavedChangesDialog` and routes through the unsaved-changes dialog when `isDirty` is true. Using raw `navigate` would bypass that dialog. `requestNavigation` is already in scope (line 96–99 of the original file).

---

- [ ] **Step 4: Build and lint**

  Run from the `frontend/` directory:

  ```bash
  npm run build
  npm run lint
  ```

  Expected: zero TypeScript errors, zero lint errors. The only change visible in the build output should be `UserDetailPage` chunk size increasing by a few bytes (the new JSX nodes).

  If you see `Cannot find module '../components/common/ErrorState'`: verify the import path added in Step 2 exactly matches `../components/common/ErrorState` (no `.tsx` extension, note double-dot prefix).

  If you see `'requestNavigation' is not defined` or similar: confirm you did not accidentally remove or reorder anything above line 117. `requestNavigation` is destructured at line 96–99 and must remain there.

---

- [ ] **Step 5: Commit**

  Stage only the modified file and commit:

  ```bash
  git add frontend/src/pages/UserDetailPage.tsx
  git commit -m "feat: add error and not-found states to UserDetailPage

  Replaces the silent null fallback with explicit <ErrorState> for API
  failures and a not-found message with back-link for unresolvable IDs.
  Guard order: loading → error → not-found → draft-init → render."
  ```
