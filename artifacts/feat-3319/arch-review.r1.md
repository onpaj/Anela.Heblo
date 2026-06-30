# Architecture Review: UserDetailPage Error and Not-Found State Handling

## Skip Design: true

## Architectural Fit Assessment

This fix is a pure rendering-logic correction to a single file. It aligns exactly with the established Users module pattern: `UsersGrid.tsx` already demonstrates the canonical guard sequence (loading → error → render) using the shared `<ErrorState>` component from `frontend/src/components/common/ErrorState.tsx`. `UserDetailPage.tsx` deviated from that pattern by omitting the error guard. No new patterns, components, or module boundaries are introduced.

Integration points are minimal:
- `ErrorState` — already imported and used in `UsersGrid.tsx`; `UserDetailPage.tsx` needs only to add the import and one guard branch.
- TanStack Query state (`usersQuery.isLoading`, `usersQuery.isError`) — already available on the existing `usersQuery` object at lines 117–128.
- React Router `useNavigate` or a plain `<a>` tag — needed for the not-found back-link; `requestNavigation` from `useUnsavedChangesDialog` is already in scope and is the correct recovery-path hook since it guards unsaved changes.

## Proposed Architecture

### Component Overview

```
UserDetailPage
│
├─ usersQuery (useUsers / TanStack Query)
│   ├─ .isLoading  ──► LoadingState branch (already present — no change)
│   ├─ .isError    ──► NEW: ErrorState branch
│   └─ .data.users ──► find(id) ──► user
│                         │
│                         ├─ undefined ──► NEW: not-found branch (back-link)
│                         └─ defined   ──► draft null-guard ──► full form
│
└─ ErrorState (common/ErrorState.tsx)   [already exists, new import here]
```

### Key Design Decisions

#### Decision 1: Container for error and not-found states

**Options considered:**
- (A) Full-page takeover — render `<ErrorState>` / not-found message without the page wrapper div.
- (B) Wrap inside the existing `<div className="p-8 max-w-5xl mx-auto">` container — consistent with the loading state already rendered inside that same container.

**Chosen approach:** Option B — wrap both new states inside `<div className="p-8 max-w-5xl mx-auto">`.

**Rationale:** The loading state (lines 120–126) already renders inside that wrapper. Keeping the same outer container for all early-exit states ensures consistent page width and padding across all non-success render paths. `ErrorState` accepts an optional `className` prop (default `h-64`) which provides sufficient vertical height without a full-screen takeover.

#### Decision 2: Not-found back-link mechanism

**Options considered:**
- (A) `useNavigate` from React Router (`navigate("/admin/access/users")`).
- (B) `requestNavigation` from the already-imported `useUnsavedChangesDialog` hook.
- (C) Plain `<a href="/admin/access/users">` anchor.

**Chosen approach:** Option B — `requestNavigation("/admin/access/users")`.

**Rationale:** `requestNavigation` is already in scope (line 96). More importantly, if a user somehow has a dirty draft state and then triggers a URL change that leads to the not-found branch, using `requestNavigation` is consistent with how every other back/cancel navigation in this component works. Using a plain anchor or raw `navigate` would bypass the unsaved-changes dialog in that edge case.

#### Decision 3: Retain the `!draft` guard

**Options considered:**
- (A) Remove `!draft` after separating the `!user` not-found guard.
- (B) Keep `!draft` as a standalone guard after the not-found check.

**Chosen approach:** Option B — keep `!draft` as a separate guard immediately after the not-found check.

**Rationale:** Code inspection confirms there is a one-render gap between the moment `user` becomes defined (query resolves) and the moment `draft` is set (the `useEffect` on line 42 runs after the render). During that render, `user` is defined but `draft` is still `null`. The full form accesses `draft.displayName`, `draft.email`, etc. directly — these would crash if `draft` is null. The `!draft` guard must remain as a null-safety fence for that transient render. Removing it would introduce a runtime exception on every successful page load.

#### Decision 4: Loading state component

**Options considered:**
- (A) Keep the existing inline `<div className="text-gray-500">Loading user…</div>` (lines 120–126).
- (B) Replace with `<LoadingState>` from `frontend/src/components/common/LoadingState.tsx` (the same component `UsersGrid` uses).

**Chosen approach:** Option A — leave the existing loading markup unchanged.

**Rationale:** The spec explicitly limits the change surface to the error and not-found guards. The loading state is already functional and visible. Replacing it with `<LoadingState>` would be a correct improvement but is out of scope per NFR-3 (minimal change surface). Flag it as a follow-up cleanup if desired.

## Implementation Guidance

### Directory / Module Structure

Only one file changes:

```
frontend/src/pages/UserDetailPage.tsx
```

No new files. No directory changes. The `ErrorState` import is added to the existing import block at the top of the file.

### Interfaces and Contracts

`ErrorState` props (from `frontend/src/components/common/ErrorState.tsx`):

```ts
interface ErrorStateProps {
  message: string;       // required — human-readable error string
  className?: string;    // optional — defaults to "h-64"
}
```

Use `message="Failed to load users."` — this is the exact string used by `UsersGrid.tsx` for the same query failure, ensuring consistent wording across the module.

### Data Flow

**API error path:**
1. `useUsers()` settles with an error → `usersQuery.isError === true`, `usersQuery.isLoading === false`.
2. `isLoading` guard (line 120) is `false` — falls through.
3. NEW error guard fires → renders `<ErrorState message="Failed to load users." />` inside the page container.
4. Full form and `!draft` guard are never reached.

**Not-found path:**
1. `useUsers()` succeeds → `usersQuery.isLoading === false`, `usersQuery.isError === false`.
2. `user = usersQuery.data?.users?.find((u) => u.id === id)` returns `undefined` (ID not in list).
3. `isLoading` guard falls through. Error guard falls through.
4. NEW not-found guard fires (`!user`) → renders human-readable message with `requestNavigation` back-link.
5. `!draft` guard and full form are never reached.

**Happy path (unchanged):**
1. Query succeeds, `user` is found.
2. Loading → error → not-found guards all fall through.
3. `!draft` guard: fires on the first render (transient gap), returns `null`. On the subsequent render after `useEffect` sets `draft`, falls through.
4. Full form renders as before.

### Guard block — required final shape

```tsx
// --- guard block (lines 117–128 replacement) ---
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
      <ErrorState message="Failed to load users." />
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
        className="mt-2 text-sm text-indigo-600 hover:underline"
      >
        ← Back to users
      </button>
    </div>
  );
}

if (!draft) return null;
```

The `isSaving` variable currently on line 118 must stay on the same side of the guard block as before — it is referenced below in the JSX. Move it to just before the `if (isLoading)` check (as shown above) so it remains in scope for the full-form render path.

Note: The not-found message and back-link styling are implementation-level decisions. The architect's constraint is: (1) human-readable text, (2) `requestNavigation` for the recovery link, (3) same outer container wrapper as the other guard branches.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `!draft` removed accidentally | High | Spec note (confirmed by orchestrator) mandates keeping the `!draft` guard; code review must verify it is present after the `!user` guard |
| Not-found uses `navigate` instead of `requestNavigation` | Low | Unsaved-changes dialog bypass only possible if a user reaches the not-found state with a dirty draft (very unlikely but still inconsistent); use `requestNavigation` |
| Error message text diverges from `UsersGrid` | Low | Hardcode `"Failed to load users."` — same string as `UsersGrid.tsx` line 101 |
| `isSaving` moved out of scope | Medium | If the guard block is rearranged without moving `isSaving` above it, the variable would be unreachable in the full-form JSX; verify placement in review |

## Specification Amendments

**Amendment 1 — `isSaving` variable placement.**
The spec does not mention `isSaving` (currently line 118, between `isLoading` and the guard). It must be declared before the guard sequence (not after it) so it remains in scope for the button `disabled` props in the full form. Implementation must not move it below the new guards.

**Amendment 2 — Not-found back-link mechanism.**
The spec says "a back-link to `/admin/access/users`" but does not specify whether to use `navigate`, `requestNavigation`, or an anchor. This review mandates `requestNavigation` for consistency with the rest of the component's navigation pattern.

**Amendment 3 — Open question resolution.**
The spec's open question about `!draft` removal is resolved by the orchestrator note: retain `!draft` as a standalone guard after `!user`. The spec's suggested code block already reflects this; no ambiguity remains.

## Prerequisites

None. All dependencies are already satisfied:
- `ErrorState` component exists at `frontend/src/components/common/ErrorState.tsx`.
- `requestNavigation` is already imported and in scope via `useUnsavedChangesDialog`.
- No migrations, config changes, or infrastructure work required.
- No new npm packages required.
