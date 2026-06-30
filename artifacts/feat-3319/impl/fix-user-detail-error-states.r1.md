# Implementation: fix-user-detail-error-states

## What was implemented
Added explicit error and not-found guard branches to `UserDetailPage.tsx`, replacing the silent `if (!draft || !user) return null;` with three distinct states: API failure (shows `<ErrorState>`), user not found (shows message + back-link), and draft-not-yet-initialized (brief null return).

## Files created/modified
- `frontend/src/pages/UserDetailPage.tsx` — added ErrorState import, replaced guard block

## Tests
No new tests (per spec: out of scope for this fix).

## How to verify
1. Navigate to a user detail page while the API is healthy — normal render.
2. Simulate an API error (e.g., network offline or mock query error) — page shows "Failed to load users." via ErrorState component.
3. Navigate to `/admin/access/users/<non-existent-id>` — page shows "User not found." with "← Access management" back-link.
4. All existing functionality (save, toggle active, groups picker) continues to work normally.

## Notes
- Pre-existing lint errors exist in the codebase (161 errors across test files). The modified file `UserDetailPage.tsx` lints cleanly with zero errors.
- Build succeeded: `Compiled successfully` with no TypeScript errors.
- Guard ordering follows the spec exactly: loading → isError → !user → !draft → render.

## PR Summary
Added explicit error and not-found states to UserDetailPage, replacing the silent null return. API errors now show the shared ErrorState component; unknown IDs show a "User not found" message with a back-link to the users list.

### Changes
- `frontend/src/pages/UserDetailPage.tsx` — added ErrorState import and three new guard branches (isError, !user, !draft) replacing the single `if (!draft || !user) return null;`

## Status
DONE
