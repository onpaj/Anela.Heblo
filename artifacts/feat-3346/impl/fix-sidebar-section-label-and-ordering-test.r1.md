# Implementation: fix-sidebar-section-label-and-ordering-test

## What was implemented

Updated `frontend/test/e2e/core/sidebar-navigation.spec.ts` to target the real "Anela" sidebar section instead of the non-existent "Personální" section, and corrected the ordering assertion to reflect the actual sidebar order (Anela → Sklad → Administrace).

## Files created/modified
- `frontend/test/e2e/core/sidebar-navigation.spec.ts` — replaced all "Personální" references with "Anela", fixed ordering test

## Tests
No new tests written — this IS the test fix. The three existing tests now correctly target the live sidebar:
1. `should display Anela section with Struktura link` — locator updated
2. `should open Struktura in new window` — locator updated
3. `should display Anela section before Sklad and Administrace` — filter regex, variable names, and positional assertions all corrected

## How to verify

Run against staging:
```
npx playwright test frontend/test/e2e/core/sidebar-navigation.spec.ts
```

All three tests should pass. Verify no occurrences of "Personální" or "Automatizace" remain in the file.

## Notes

- TypeScript deprecation warnings in tsconfig (ES5 target, moduleResolution=node10) are pre-existing and unrelated to this change.
- The filter regex in test 3 was corrected from `/^(Sklad|Personální|Automatizace)$/` to `/^(Anela|Sklad|Administrace)$/` because the `automatizace` section renders as "Administrace" in the component.
- Only one file was modified. No production code or helper files were touched.

## PR Summary

Three nightly E2E failures in `sidebar-navigation.spec.ts` were caused by tests targeting a sidebar button named "Personální" that has never existed — the real section is "Anela". Additionally, the ordering test referenced "Automatizace" when the component renders that section as "Administrace", and asserted a wrong order (Personální between Sklad and Automatizace, instead of Anela before Sklad and Administrace).

### Changes
- `frontend/test/e2e/core/sidebar-navigation.spec.ts` — replaced all "Personální" locators with `/Anela/i`, fixed filter regex from `Sklad|Personální|Automatizace` to `Anela|Sklad|Administrace`, renamed variables, and corrected positional ordering assertions

## Status
DONE
