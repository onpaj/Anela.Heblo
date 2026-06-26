# Code Review: fix-sidebar-section-label-and-ordering-test

## Summary
The implementation correctly replaces all references to the non-existent "Personální" section with "Anela" and fixes the sidebar ordering test to validate the real order (Anela → Sklad → Administrace). All five success criteria are satisfied and only the specified file was modified.

## Review Result: PASS

### task: fix-sidebar-section-label-and-ordering-test
**Status:** PASS

## Overall Notes
- Zero occurrences of `Personální` or `Automatizace` remain in the file.
- The `/Anela/i` locator appears exactly twice (lines 15 and 32), covering tests 1 and 2.
- The ordering test filter regex is `/^(Anela|Sklad|Administrace)$/` (line 57).
- The ordering assertions use `toBeGreaterThanOrEqual(0)` for `anelaIndex`, `toBeGreaterThan(anelaIndex)` for `skladIndex`, and `toBeGreaterThan(skladIndex)` for `administraceIndex` — matching the spec exactly.
- Only `frontend/test/e2e/core/sidebar-navigation.spec.ts` was modified; no other files were touched.

**Status:** PASS
