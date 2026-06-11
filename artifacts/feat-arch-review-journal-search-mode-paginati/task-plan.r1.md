Plan saved to `docs/superpowers/plans/2026-06-04-journal-search-mode-pagination-sort-refetch.md`.

**Summary:** 8-task TDD implementation plan covering the two-file surgical fix:
- Tasks 1–3: hook-level TDD — pin the `enabled=false` default, add failing tests for the `enabled=true` paths, then parameterize `useSearchJournalEntries(params, enabled = false)`.
- Tasks 4–5: component-level RED/GREEN for the actual user-visible bug — failing pagination-in-search-mode test, then bind `isSearchMode` at the `JournalList.tsx` call site.
- Task 6: regression tests for sort + page-size in search mode (FR-4, FR-5).
- Task 7: comment refresh on the existing search test (per arch-review's spec-amendment note).
- Task 8: lint + build + full journal test sweep.

Each task includes exact file paths, complete code blocks, exact commands, expected pass/fail outcomes, and a single conventional-commit step. Self-review confirms full FR/NFR coverage with no placeholders and consistent types.