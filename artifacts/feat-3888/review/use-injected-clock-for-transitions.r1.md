# Code Review: use-injected-clock-for-transitions

## Summary

The implementation matches the task spec exactly: the three test assertion
blocks were strengthened to check `LastStateChanged` and `StateLog` against
the frozen clock fixture first (confirmed red — `Failed: 3, Passed: 4`), then
the three `DateTime.UtcNow` call sites in `TransportBoxCompletionService.ProcessBoxAsync`
were replaced with `_timeProvider.GetUtcNow().UtcDateTime` (confirmed green —
`Passed: 7`). All constraints from the task context were honored.

## Review Result: PASS

### task: use-injected-clock-for-transitions
**Status:** PASS

Verified:
- All three call sites (`:94` no-ops error, `:114` ToPick, `:134` any-failed error) use `_timeProvider.GetUtcNow().UtcDateTime`, not `.DateTime` and not wrapped in `DateTime.SpecifyKind`.
- The clock read was not hoisted to a local at the top of `ProcessBoxAsync` — each branch reads it independently, and the two skip paths (`pendingOrSubmitted` and the unexpected-state fallthrough) read the clock zero times, matching the spec's explicit constraint.
- `"System"` string, both error message strings, branch conditions, `UpdateAsync`/`SaveChangesAsync` call ordering, returned `BoxProcessingResult` values, and all log statements are byte-identical to the pre-change version (confirmed via diff — only the `DateTime.UtcNow` → `_timeProvider.GetUtcNow().UtcDateTime` token changed on 3 lines).
- Test assertions added match the spec's exact expected blocks for all three scenarios (Stocked transition, error-from-failed-operations, error-from-no-operations including the `Description` check).
- Red-then-green TDD sequence was actually executed and both runs verified (not just asserted): 3 failures with the expected `LastStateChanged` mismatch message before the fix, 7 passes after.
- Guardrail greps for stray `DateTime.UtcNow`/`DateTime.Now` and the `.DateTime`/`SpecifyKind` traps both returned no matches.

## Docs to Update

None. This is an internal implementation detail (swapping a wall-clock read for an injected `TimeProvider` call) with no change to public API, CLI, configuration, or operational behavior.

## Overall Notes

No cross-cutting concerns. The unrelated pre-existing `MSB3073`/`AccessMatrixGen` warning noted in the impl artifact is a pre-existing build-step issue unrelated to this task's scope and did not affect the build or test outcome — correctly left untouched.

**Status:** PASS
