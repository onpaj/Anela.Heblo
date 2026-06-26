## Module
ExpeditionListArchive

## Finding
`useRunExpeditionListPrintFix` is defined inside `frontend/src/api/hooks/useExpeditionListArchive.ts` (lines 130–152) but calls an endpoint that belongs to the `ExpeditionList` module:

```typescript
// useExpeditionListArchive.ts, line 133
const relativeUrl = `/api/expedition-list/run-fix`;
```

`/api/expedition-list/...` is served by `ExpeditionListController.cs`, not `ExpeditionListArchiveController.cs`. The hook is also imported by `ExpeditionListArchivePage.tsx` (line 11), which means the Archive page silently acquires a dependency on the ExpeditionList module through its own hook file.

## Why it matters
- **Module boundary leakage**: the Archive hook file owns and exposes a concern that belongs to a different module. If the ExpeditionList endpoint changes, the fix point is obscured because it lives in the wrong file.
- **Discoverability**: developers looking for all callers of the ExpeditionList run-fix endpoint will search `useExpeditionList*` files and miss this one.
- **Cohesion**: `useExpeditionListArchive.ts` now has two responsibilities — archive data access and triggering ExpeditionList operations.

## Suggested fix
Move `useRunExpeditionListPrintFix` to `frontend/src/api/hooks/useExpeditionList.ts` (or create that file if it doesn't exist). Update the import in `ExpeditionListArchivePage.tsx` accordingly. No functional change is needed — just a relocation to the correct module boundary.

---
_Filed by daily arch-review routine on 2026-06-04._