### task: extract-format-datetime
Goal: single co-located `formatDateTime` helper used by both files.

Files:
- Create `frontend/src/components/pages/ExpeditionListArchive/formatDateTime.ts` (body per design.r1.md, named export).
- Modify `frontend/src/pages/ExpeditionListArchivePage.tsx`: delete local `formatDateTime` (L25-34), add import `../components/pages/ExpeditionListArchive/formatDateTime`.
- Modify `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx`: delete local definition (L19-28), add `import { formatDateTime } from "./formatDateTime";`.

Steps:
1. Create the helper file; keep the en dash U+2013 placeholder.
2. Remove both local copies and add imports; remove any now-unused things only if caused by this change.
3. Add `__tests__/formatDateTime.test.ts`: null -> "–", empty string -> "–", a fixed ISO string yields a string matching /\d{2}\.\s?\d{2}\.\s?\d{4}/ with time (avoid timezone-brittle exact hour).
4. Verify: from `frontend/`, `npx jest`/`npm test -- ExpeditionList` (existing ExpeditionJobControlsBar and ExpeditionListArchivePage tests must pass), `npm run build`, `npm run lint`.

Acceptance: `grep -rn "const formatDateTime" frontend/src/pages/ExpeditionListArchivePage.tsx frontend/src/components/pages/ExpeditionListArchive` returns only the new helper file; all checks above pass.
