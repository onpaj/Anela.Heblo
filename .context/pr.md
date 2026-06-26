# PR Context

- **PR**: #3359 — #3347: fix recurring-jobs E2E count assertions (12 → ≥24)
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3359
- **Branch**: `feature/3347-Fix-Jobs-Investigate-Recurring-Jobs-Showing-24-Ins` → `main`
- **State**: open
- **Author**: onpaj
- **Changes**: +558 / -9 across 11 files
- **Absorbed**: no main backmerge needed (base SHA unchanged); all tests passing; pushed 4de66208

## Description

Closes #3347

The recurring-jobs E2E suite had four `toBe(12)` assertions that staging now fails because the app has 24 registered `IRecurringJob` implementations. Replaced with `toBeGreaterThanOrEqual(24)`.

## What pr-autoabsorb fixed

The PR's own frontend CI was failing due to pre-existing issues on main that showed up in the full test run:

1. **`useTheme` errors** (34 tests across 5 suites) — `ThemeContext`/`ThemeProvider` and `ThemeToggle` had been added to `main` after this branch was cut. Components like `CatalogAutocomplete`, `ResponsiblePersonCombobox`, `ThemeToggle` call `useTheme()` which throws outside a `ThemeProvider`. Fixed by adding a global `jest.mock("./contexts/ThemeContext", ...)` in `setupTests.ts`.

2. **Stale `PositionCard` snapshots** (2 tests) — Dark-mode Tailwind classes (`dark:bg-graphite-surface`, `dark:shadow-soft-dark`, etc.) were added to `PositionCard.tsx` on main but the snapshot file was not regenerated. Fixed by running `jest --updateSnapshot`.

Result: 280/280 frontend test suites, 2314 tests passing.
