# Code Review: guard-component

## Summary

The implementation correctly adds a permission guard to `ResponsiblePersonCombobox` by reading `hasPermission('admin.administration.read')` from `usePermissionsContext` and passing the result as `enabled` to `useResponsiblePersonsQuery`. The test file is updated with a `PermissionsContext` mock and two new test cases covering non-admin and admin paths. All 16 tests pass, build exits 0.

## Review Result: PASS

### task: guard-component
**Status:** PASS

## Overall Notes

- FR-2 (internal permission check, no new props) — satisfied
- FR-3 (no error banner for non-admins when `isError: false`) — covered by test and naturally enforced by the existing `{isError && ...}` render gate
- FR-4 (admin experience unchanged) — satisfied; `enabled: true` when admin
- FR-5 (PermissionsContext mock added, 2 new tests, existing tests preserved) — satisfied
- Architecture guidance re: PermissionsContext mock in existing `describe` block — correctly followed; `mockHasPermission.mockReturnValue(true)` in `beforeEach` ensures all pre-existing tests see an admin context

**Status:** PASS
