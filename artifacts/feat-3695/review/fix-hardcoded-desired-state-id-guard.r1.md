# Code Review: fix-hardcoded-desired-state-id-guard

## Summary
The implementation precisely executes the task specification: hardcoded state id `26` is removed from the static `NonPrintableStates` dictionary, replaced by a runtime equality check against `_options.Value.DesiredStateId` that runs before the dictionary lookup. The new test correctly verifies both halves of the fix (reconfigured `DesiredStateId = 99` now blocks correctly, and stale `26` no longer blocks). All existing tests remain unmodified and backward-compatible.

## Review Result: PASS

### task: fix-hardcoded-desired-state-id-guard
**Status:** PASS

## Overall Notes

**Correctness:** The equality check at lines 57–66 is correctly positioned after the Shoptet 404 handler and before the `NonPrintableStates` lookup. The response structure (error code `ExpeditionOrderInvalidState`, params `orderCode` and `currentStatusName = "Balí se"`) matches the task spec exactly. The dictionary now contains only the three genuinely stable lifecycle states (`-3`, `52`, `70`) per design.

**Test coverage:** 
- The existing theory test `Handle_OrderInNonPrintableState_ReturnsInvalidStateError` with `[InlineData(26, "Balí se")]` will continue to pass unmodified, now hitting the new equality branch instead of the dictionary — this preserves backward compatibility under default configuration (`DesiredStateId = 26`).
- The new test `Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26` correctly exercises the bug fix: it constructs a handler with `DesiredStateId = 99`, asserts that status `99` is rejected with `Times.Never`, then asserts that status `26` is no longer blocked (proceeds to print with `Times.Once`). This definitively proves the stale hardcoded value no longer drifts from the configured option.

**Architecture:** The comment at lines 14–15 clearly documents why the check is runtime and not static. No changes to contracts, error codes, or dependency injection. The guard ordering (404 check → equality check → dictionary check → print) remains sound.

**No regressions:** All five pre-existing tests are untouched; only one new test added. The `CreateHandler()` helper still uses default options, so all shared tests remain isolated from the non-default scenario.
