# Implementation: fix-articledetail-theme-reactivity

## What was implemented
`ArticleDetail.tsx`'s `HtmlContent` subcomponent computed `isDark` by reading
`document.documentElement.classList.contains('dark')` directly at render time.
Since this is a plain DOM read (not a React state/context subscription), the
component never re-rendered when the user toggled the app theme, so the
article's iframe (`srcDoc`) kept the stale light/dark colors until something
else forced a re-render. Fixed by subscribing to the app's `ThemeContext` via
`useTheme()` and deriving `isDark` from `theme === 'dark'`, so toggling the
theme now properly re-renders `HtmlContent` and remounts the iframe (the
existing `key={isDark ? 'dark' : 'light'}` was already correct, it just never
got a fresh `isDark` value before).

## Files created/modified
- `frontend/src/features/articles/ArticleDetail.tsx` — added `import { useTheme } from '../../contexts/ThemeContext';`; in `HtmlContent`, replaced `const isDark = document.documentElement.classList.contains('dark');` with `const { theme } = useTheme(); const isDark = theme === 'dark';`. No other lines changed.
- `frontend/src/features/articles/__tests__/ArticleDetail.test.tsx` — new regression test file. Renders `ArticleDetail` inside a real `ThemeProvider` (with `ThemeContext`'s global jest mock un-mocked for this file) alongside a `ToggleButton` that calls the real `toggle()`, then asserts the rendered iframe's `srcdoc` reflects the light/dark colors correctly both after a single toggle and after toggling twice (back to light).

## Tests
- `frontend/src/features/articles/__tests__/ArticleDetail.test.tsx`:
  - `remounts the article iframe with dark colors after the theme is toggled to dark` — proves the iframe's `srcdoc` switches from light (`#1f2937`) to dark (`#E6E8EC`) colors after clicking the real theme toggle. This test reproduced the bug (RED) against the pre-fix code and now passes (GREEN) against the fix.
  - `remounts the article iframe back to light colors after toggling twice` — proves toggling dark then light again correctly reverts the iframe content.

## How to verify
```
cd frontend
CI=true npx react-scripts test src/features/articles/__tests__/ArticleDetail.test.tsx --watchAll=false   # 2 passed
CI=true npx react-scripts test --watchAll=false                                                          # 292 suites passed, no regressions
grep -n "document.documentElement.classList" src/features/articles/ArticleDetail.tsx                     # no matches
npm run build                                                                                              # Compiled successfully
npm run lint                                                                                                # 162 problems (148 errors/14 warnings) - identical to pre-existing baseline, 0 new
```

## Notes
- **Environment setup required first-time work**: this worktree had no `node_modules` installed. `npm ci` failed on a pre-existing peer-dependency conflict (`react-i18next@15` wants `typescript@^5`, project pins `typescript@^4.9.5`); installed with `npm install --legacy-peer-deps`, matching the flag already used in `.github/workflows/ci-feature-branch.yml` / `ci-main-branch.yml`. This is unrelated to the fix itself and no lockfile/package.json changes were made or committed.
- **One deviation from the task's literal test code**: the plan's Step 1 test body used `document.querySelector('iframe')` to grab the iframe. Running it as-written passed functionally but introduced 6 new `testing-library/no-node-access` ESLint errors (confirmed by diffing lint output with/without the new test file: baseline 148 errors → 154 errors with the literal `document.querySelector` version). Per the task's own Step 3 acceptance bar ("no new lint errors introduced by this change") and the instruction to use judgment while staying faithful to intent, I replaced both `document.querySelector('iframe')` calls with `screen.getByTitle('Obsah článku')` (the iframe already has `title="Obsah článku"` in `ArticleDetail.tsx`), which queries the same element via Testing Library's recommended API. Test assertions and behavior are otherwise identical to the plan; verified lint is back to the exact pre-existing baseline (162 problems / 148 errors / 14 warnings, 0 attributable to the new/changed files).
- All other verification steps (RED confirmation, GREEN fix, full suite, grep, build, lint) matched the plan's expected outcomes exactly.

## PR Summary
Fixes a bug where the article detail view's rendered HTML content (shown in an iframe) did not update its color theme when the user toggled light/dark mode. The root cause was `HtmlContent` reading `document.documentElement.classList.contains('dark')` directly instead of subscribing to React state, so it never re-rendered on theme change. The fix swaps that DOM read for the app's existing `useTheme()` hook from `ThemeContext`, so the iframe (keyed on `isDark`) now correctly remounts with the right colors whenever the theme toggles. Added a regression test suite that renders `ArticleDetail` under a real `ThemeProvider` with a live toggle button and asserts the iframe's `srcdoc` content switches between light/dark styling correctly, including toggling back and forth.

### Changes
- `frontend/src/features/articles/ArticleDetail.tsx` — `HtmlContent` now derives `isDark` from `useTheme()` instead of a direct DOM read.
- `frontend/src/features/articles/__tests__/ArticleDetail.test.tsx` — new regression tests for theme reactivity.

## Status
DONE
