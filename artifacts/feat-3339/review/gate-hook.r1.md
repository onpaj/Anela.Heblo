# Code Review: gate-hook

## Summary

The implementation correctly adds an `options?: { enabled?: boolean }` second argument to `useResponsiblePersonsQuery` and updates the internal `enabled` expression to `Boolean(groupId) && (options?.enabled ?? true)`. Two new tests cover the disabled and enabled paths. All 6 tests pass.

## Review Result: PASS

### task: gate-hook
**Status:** PASS

## Overall Notes

Implementation fully satisfies all acceptance criteria. The hook remains backwards-compatible — omitting `options` preserves existing behavior. The `staleTime`, `retry`, and `retryDelay` values are unchanged.

**Status:** PASS
