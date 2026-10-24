# Architecture Review: Relocate ExpeditionJobControlsBar

## Skip Design: true
Pure file move/refactor with no visual change.

## Architectural Fit Assessment
Fits the frontend convention of placing page components in `components/pages/<Feature>/`. A neutral `Expedition/` folder covering both ExpeditionList hooks and the archive page makes the dependency explicit. Verified: only `ExpeditionListArchivePage.tsx` consumes the bar; `formatDateTime` is consumed by the bar and the page; `components/pages/Expedition/` does not yet exist.

## Proposed Architecture

### Component Overview
```
pages/ExpeditionListArchivePage.tsx
  -> components/pages/Expedition/ExpeditionJobControlsBar.tsx
       -> api/hooks/useExpeditionList, useRecurringJobs
       -> components/modals/PrintOrderModal
       -> components/pages/Expedition/formatDateTime.ts
  -> components/pages/Expedition/formatDateTime.ts
```

### Key Design Decisions
#### Decision 1: Move whole component vs split
**Options considered:** move to shared folder; split archive/live parts.
**Chosen approach:** move whole component (issue's first suggestion).
**Rationale:** smallest surgical change; no behaviour risk; the split has no second consumer to justify it.

#### Decision 2: formatDateTime location
Move with the bar to avoid the shared component importing from the archive folder.

## Implementation Guidance
Use `git mv` to preserve history. Fix relative import depth (one level unchanged: `../../../`). Update `jest.mock` path. Grep for stale references afterwards.

## Risks
Missed jest.mock path leads to failing page test; mitigated by running affected suites.
