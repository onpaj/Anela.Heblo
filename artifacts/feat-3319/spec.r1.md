# Specification: UserDetailPage Error and Not-Found State Handling

## Summary

`UserDetailPage.tsx` silently renders a blank page when the `useUsers()` query fails or when the URL parameter ID does not match any user in the loaded list. This fix adds an explicit error state and a "user not found" state, bringing the page in line with the existing pattern used by `UsersGrid.tsx`.

## Background

The Users module has an established pattern for handling query failures: `UsersGrid.tsx` guards against `users.isError` (lines 100–102) and renders the shared `<ErrorState>` component with a human-readable message. `UserDetailPage.tsx` was not updated to follow this pattern. As a result, two failure cases produce an empty page with no feedback and no recovery path:

1. **API error** — any network failure or 5xx response causes `usersQuery.data` to be `undefined`. `isLoading` becomes `false` and the `!draft || !user` guard on line 128 silently returns `null`.
2. **Unknown ID** — navigating directly to `/admin/access/users/<nonexistent-id>` with a successful API response still leaves `user` as `undefined`, producing the same silent `null` return.

Both cases violate the frontend quality criterion that all query error and not-found states must be surfaced to the user.

## Functional Requirements

### FR-1: API error state

When `usersQuery.isError` is `true`, `UserDetailPage` must render an error indicator instead of returning `null`.

**Acceptance criteria:**
- When the `useUsers()` query settles with `isError === true`, the page renders the `<ErrorState>` component (imported from `../../components/common/ErrorState`) with the message `"Failed to load users."`.
- The error state is displayed in the same container as the rest of the page content (consistent layout — not a full-screen takeover unless `ErrorState` provides that naturally).
- No blank/empty page is shown under any API error condition.

### FR-2: User not found state

When the query succeeds but the `id` URL parameter does not match any user in `usersQuery.data.users`, the page must render a "not found" message instead of returning `null`.

**Acceptance criteria:**
- When `usersQuery.isLoading` is `false`, `usersQuery.isError` is `false`, and `user` is `undefined`, the page renders a message indicating the user was not found.
- The not-found view includes a back-link to `/admin/access/users` so the user has a recovery path.
- The message is human-readable (e.g., "User not found.").

### FR-3: Guard ordering

The three guards — loading, error, not-found — must be evaluated in the correct order to avoid incorrect branch selection.

**Acceptance criteria:**
- The loading guard (`isLoading`) fires first, before any error or not-found check.
- The error guard (`usersQuery.isError`) fires second, after loading resolves.
- The not-found guard (`!user`) fires third, only after both loading and error are ruled out.
- The existing `!draft` condition in the original guard on line 128 is either preserved as a further sub-condition or removed if it is now redundant (see Open Questions).

## Non-Functional Requirements

### NFR-1: Performance

No new data fetching is introduced. All guards operate on already-resolved query state. No performance impact.

### NFR-2: Consistency with existing patterns

The implementation must use `<ErrorState>` from `frontend/src/components/common/ErrorState.tsx` — the same component used by `UsersGrid.tsx`. No new error components should be introduced.

### NFR-3: Minimal change surface

Only lines 117–128 of `UserDetailPage.tsx` (the guard block) require modification. No other logic, state, or JSX in the file should be changed.

## Data Model

No data model changes. The fix operates entirely on existing TanStack Query state (`isLoading`, `isError`, `data`) already present in `usersQuery`.

## API / Interface Design

No API or interface changes. This is a pure frontend rendering fix.

**Affected file:** `frontend/src/pages/UserDetailPage.tsx`

**Change to guard block (lines 117–128):**

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
        className="text-gray-500 hover:text-gray-700 text-sm mt-2"
      >
        ← Access management
      </button>
    </div>
  );
}
```

`ErrorState` must be added to the import list at the top of the file:
```tsx
import ErrorState from "../components/common/ErrorState";
```

The `requestNavigation` call in the not-found view is safe because `useUnsavedChangesDialog` is already initialised above the guards; `draft` is `null` when the user is not found, so `isDirty` is `false` and navigation proceeds without a dialog.

## Dependencies

- `frontend/src/components/common/ErrorState.tsx` — already exists; no new dependencies.
- `useUnsavedChangesDialog` — already in scope; behaviour is unchanged.

## Out of Scope

- Handling `permissionsQuery.isError` (permissions section within the fully rendered page). That is a separate, lower-severity gap and should be filed as its own issue.
- Any changes to `UsersGrid.tsx` or other access-management pages.
- Unit or E2E test additions for this fix (desirable but tracked separately; the existing `UsersGrid.test.tsx` pattern can be referenced if tests are added later).

## Open Questions

1. **`!draft` guard removal.** The original line 128 checks `!draft || !user`. Once the not-found guard on `!user` is in place, `draft` will always be `null` when `user` is `undefined` (the `useEffect` that sets draft requires `user` to be truthy). The `!draft` condition becomes redundant. Assumption: remove it and rely solely on `!user`. If there is a race condition where `user` is defined but `draft` has not yet been set by the effect (first render after query resolves), the component would briefly render the full form with a `null` draft, which `updateDraft` and `onSave` already guard against. Confirm this assumption is acceptable before implementation.

## Status: HAS_QUESTIONS
