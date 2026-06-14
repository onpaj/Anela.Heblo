Plan saved to `docs/superpowers/plans/2026-06-11-journal-list-remove-manual-refetch.md`.

**Summary of the plan:**

The plan is structured as 7 tasks (0 through 6) with tight, TDD-style steps:

- **Task 0** — Verify starting state: confirm the three `.refetch()` call sites and three target tests exist, and the existing test suite is green.
- **Tasks 1–3** — Rewrite the three existing tests (`should handle search input and apply search`, `should handle Enter key press in search input`, `should clear search and return to normal mode`) to assert on hook re-invocation with the new params object — matching the existing pattern at `JournalList.test.tsx:485–528`. These tests pass against the **unchanged** source (they assert on something that already happens), so each gets its own commit.
- **Task 4** — Add a new failing regression test for `handleCloseModal`. This test FAILS against the unchanged source (refetch is called), proving it actually exercises the handler. Committed red.
- **Task 5** — Strip the three `.refetch()` blocks, drop `async` from the two handlers that no longer await. Verifications grep for residual `.refetch(` and `async`. The Task 4 red test turns green here.
- **Task 6** — `npm run build`, `npm run lint`, surgical-diff check (only the two expected files appear in `git diff --stat`), and a final full-suite test run.

The plan also includes an explicit spec→task mapping table at the end covering all FRs (FR-1 through FR-4), NFRs (NFR-1 through NFR-4), and all five spec amendments from the architecture review.