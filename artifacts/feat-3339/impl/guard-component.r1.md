# Implementation: guard-component

## What was implemented

Added a permission guard to `ResponsiblePersonCombobox` so the `GET /api/UserManagement/group-members` API call is never fired for non-admin users. The guard checks the `admin.administration.read` permission via `usePermissionsContext` and passes `{ enabled: canReadGroupMembers }` to the `useResponsiblePersonsQuery` hook, which already supported the `enabled` option.

## Files created/modified

- `frontend/src/components/common/ResponsiblePersonCombobox.tsx` — Added `usePermissionsContext` import, `hasPermission`/`canReadGroupMembers` variables before the query hook, and passed `{ enabled: canReadGroupMembers }` to `useResponsiblePersonsQuery`.
- `frontend/src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx` — Added `jest.mock` for `PermissionsContext` with `mockHasPermission`, updated `beforeEach` to default to admin (`mockHasPermission.mockReturnValue(true)`), and added two new test cases for non-admin and admin paths.

## Tests

- `src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx` — 10 tests pass (7 existing + 2 new permission guard tests + 1 that already existed)
- `src/api/hooks/__tests__/useUserManagement.test.tsx` — 6 tests pass (unchanged)
- Total: 16 tests pass, 0 failed

## How to verify

```bash
cd frontend
npx react-scripts test --testPathPattern="(useUserManagement|ResponsiblePersonCombobox).test" --no-coverage --watchAll=false
npm run build
npx eslint src/components/common/ResponsiblePersonCombobox.tsx src/components/common/__tests__/ResponsiblePersonCombobox.test.tsx
```

## Notes

- The `useResponsiblePersonsQuery` hook already had the `options?: { enabled?: boolean }` parameter wired up, so no changes were needed to the hook itself.
- `npx jest` runs with a non-CRA Jest config and parses the file incorrectly (no TS/JSX transform). Must use `npx react-scripts test` for this project.
- The existing 161 lint errors in other files are pre-existing and unrelated to this change.

## PR Summary

Gates the `GET /api/UserManagement/group-members` call in `ResponsiblePersonCombobox` behind the `admin.administration.read` permission. Non-admin users will no longer trigger the API request — the query is disabled at the React Query level (`enabled: false`), so no network call is made. The combobox UI still renders normally for all users; it simply won't show members from the group. Two new tests cover the non-admin (query disabled, no error banner) and admin (query enabled, combobox renders) paths.

## Status
DONE
