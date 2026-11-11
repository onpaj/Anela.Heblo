# Specification: Relocate ExpeditionJobControlsBar out of ExpeditionListArchive

## Summary
`ExpeditionJobControlsBar` lives in `components/pages/ExpeditionListArchive/` but imports `useRunExpeditionListPrintFix` from the `ExpeditionList` hooks module and controls live print operations. Move it to a shared `components/pages/Expedition/` folder so the cross-module dependency is explicit. Pure refactor; no behaviour change.

## Background
Arch-review issue #4456. Sole consumer is `pages/ExpeditionListArchivePage.tsx`. The component bundles live-expedition operations (robot toggle, run fix, trigger job, print order modal) with an archive `refreshButton` slot.

## Functional Requirements

### FR-1: Move component to shared Expedition folder
Move `ExpeditionJobControlsBar.tsx` to `frontend/src/components/pages/Expedition/ExpeditionJobControlsBar.tsx` (git mv, keep name to minimise churn; issue allows rename but not required). Fix relative imports.
**Acceptance criteria:**
- No file under `components/pages/ExpeditionListArchive/` imports from `api/hooks/useExpeditionList`.
- Component renders and behaves identically.

### FR-2: Share formatDateTime
`formatDateTime` is used by both the bar and the archive page. Keep it in `ExpeditionListArchive/` is wrong for the bar's import direction, so move it (and its test) to `components/pages/Expedition/` and update both importers.
**Acceptance criteria:**
- Both consumers import from the new location; its unit test passes unchanged in logic.

### FR-3: Update consumers and tests
Move `__tests__/ExpeditionJobControlsBar.test.tsx` next to the component; update `ExpeditionListArchivePage.tsx` import and the `jest.mock` path in `pages/__tests__/ExpeditionListArchivePage.test.tsx`.
**Acceptance criteria:** `npm run build`, `npm run lint`, and the affected jest suites pass.

## Non-Functional Requirements
### NFR-1: No behaviour/UX change. NFR-2: No security or API change.

## Data Model
None.

## API / Interface Design
Component props unchanged (`refreshButton?: React.ReactNode`).

## Dependencies
None new.

## Out of Scope
Splitting the component into archive vs live parts; moving `PrintOrderModal`; adding the bar to the ExpeditionList page; backend changes.

## Open Questions
None. Assumption: keep the component name; the issue's rename is optional.
