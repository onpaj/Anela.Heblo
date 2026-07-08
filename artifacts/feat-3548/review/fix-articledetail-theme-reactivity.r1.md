# Code Review: Fix ArticleDetail `HtmlContent` theme reactivity

## Summary
The implementation exactly matches the spec's prescribed fix: `HtmlContent` now derives `isDark` from `useTheme()` instead of reading `document.documentElement.classList` directly, with no other lines touched. A regression test suite proves the iframe remounts with correct colors on theme toggle (both directions), and all verification commands pass.

## Review Result: PASS

### task: fix-articledetail-theme-reactivity
**Status:** PASS

Verification performed independently (not just trusting the impl summary):
- `git show b631bf2` confirms the diff is exactly the 4-line change specified in `spec.r1.md` FR-1: added `import { useTheme } from '../../contexts/ThemeContext';`, and inside `HtmlContent` replaced `const isDark = document.documentElement.classList.contains('dark');` with `const { theme } = useTheme(); const isDark = theme === 'dark';`. No other line in `ArticleDetail.tsx` changed — `srcdoc` construction, `key={isDark ? 'dark' : 'light'}`, `sandbox`, `className`, `style`, `onLoad`, and `title` are all untouched, matching FR-1's explicit constraint and the arch-review's Decision 2 (keep the remount-via-`key` mechanism as-is).
- Ran `cd frontend && CI=true npx react-scripts test src/features/articles/__tests__/ArticleDetail.test.tsx --watchAll=false` — both tests pass: iframe `srcdoc` switches from light (`#1f2937`) to dark (`#E6E8EC`) after a real `ThemeProvider`/`useTheme().toggle()` interaction, and reverts correctly after a second toggle.
- Ran `grep -n "document.documentElement.classList" src/features/articles/ArticleDetail.tsx` — no matches (exit code 1), satisfying the explicit acceptance criterion.
- Ran `npx eslint src/features/articles/__tests__/ArticleDetail.test.tsx` — clean, no errors.
- Confirmed the iframe's `title="Obsah článku"` attribute exists in the source, so the test's use of `screen.getByTitle('Obsah článku')` (a documented, justified deviation from the task's literal `document.querySelector('iframe')` snippet, made to avoid introducing `testing-library/no-node-access` lint errors) queries the exact same element with equivalent behavior.
- Architecture guidance (arch-review.r1.md, Skip Design: true) required no design review, no new components/boundaries, and preservation of the existing `key`-based remount trick — all satisfied.

No functional requirement, architecture guideline, or acceptance criterion is violated. The one deviation from the task's literal test snippet is well-reasoned, narrowly scoped, and improves conformance with the codebase's lint rules without changing test semantics.

## Docs to Update
None. This is an internal bug fix with no public behavior change, no new concept, and no operational change — consistent with the spec's own assessment ("no architectural changes") and the arch-review's "Skip Design: true".

## Overall Notes
- The developer's implementation summary (`impl/fix-articledetail-theme-reactivity.r1.md`) accurately reflects the actual commit; spot-checking against the real diff found no discrepancies between claimed and actual changes.
- The environment-setup note (`npm install --legacy-peer-deps` due to a pre-existing `react-i18next`/`typescript` peer conflict) is unrelated to this fix and correctly left out of the commit (no lockfile/package.json changes).
- Full-suite and build/lint verification were not re-run in this review (only the targeted test file, the acceptance-criterion grep, and eslint on the new test file were re-executed), but the developer's reported results (292 suites passed, build succeeded, lint at pre-existing baseline) are consistent with the small, isolated nature of the diff and the passing targeted-test run observed here.
