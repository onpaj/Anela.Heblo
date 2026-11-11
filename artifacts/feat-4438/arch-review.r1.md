# Architecture review: #4438

- Frontend-only refactor, no API/contract/backend impact, no docs/process impact.
- A shared `formatDateTime` already exists in `frontend/src/utils/formatters.ts` (same cs-CZ options), but differs: placeholder is em dash "—" (U+2014) vs local en dash "–" (U+2013), accepts `Date`, and has try/catch. `ExpeditionJobControlsBar.test.tsx` L138 asserts `Další běh: –` (en dash).
- Options: (A) co-located helper, behavior-preserving (issue's primary suggestion); (B) reuse `utils/formatters.ts`, which changes the visible placeholder and the test.
- Recommendation: A. It is surgical (CLAUDE.md), zero behavior change. Unifying with the global util is a separate cross-module decision (about 25 files use ad hoc variants) and should be left to a follow-up.
- Risk: low. Import path from the page is `../components/pages/ExpeditionListArchive/formatDateTime`.
