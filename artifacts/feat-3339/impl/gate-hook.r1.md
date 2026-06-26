# Implementation: gate-hook

## What was implemented

Added an optional `options?: { enabled?: boolean }` second argument to `useResponsiblePersonsQuery`. The internal `enabled` condition is now `Boolean(groupId) && (options?.enabled ?? true)`, so callers can suppress the query by passing `{ enabled: false }` without removing the `groupId` guard.

## Files created/modified

- `frontend/src/api/hooks/useUserManagement.ts` — added `options` parameter and updated `enabled` expression
- `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx` — added two new test cases covering `options.enabled: false` (query suppressed) and `options.enabled: true` (query fires)

## Tests

`frontend/src/api/hooks/__tests__/useUserManagement.test.tsx` — 6 tests total, all passing:
- `should show loading state initially`
- `should have correct initial state`
- `should be a query hook with proper structure`
- `should handle hook cleanup properly`
- `should not fire the query when options.enabled is false` (new)
- `should fire the query when options.enabled is true` (new)

## How to verify

```bash
cd frontend
CI=true npm test -- --testPathPattern="useUserManagement.test" --no-coverage
```

## Notes

The worktree had no `node_modules` directory; tests were run using a symlink to the main repo's `node_modules`. No deviation from the specified implementation.

## PR Summary

Gates `useResponsiblePersonsQuery` with an optional `enabled` flag so consumers (e.g. admin-permission guards) can prevent the query from firing until preconditions are met, without losing the existing `groupId` presence check.

## Status
DONE
