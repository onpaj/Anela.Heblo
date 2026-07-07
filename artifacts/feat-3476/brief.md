# [arch-review] ExpeditionListArchive: RunExpeditionListPrintFixResult interface is dead code in useExpeditionListArchive.ts

## Module
ExpeditionListArchive

## Finding
`useExpeditionListArchive.ts` (lines 32–34) exports an interface that is never imported anywhere:

```typescript
// frontend/src/api/hooks/useExpeditionListArchive.ts  lines 32-34
export interface RunExpeditionListPrintFixResult {
  totalCount: number;
}
```

The hook that actually performs the print-fix operation — `useRunExpeditionListPrintFix` — lives in a different file (`frontend/src/api/hooks/useExpeditionList.ts`) and has no explicit return-type annotation. `ExpeditionListArchivePage.tsx` imports the hook from that file and accesses `result.totalCount` against the untyped `response.json()` result.

A project-wide grep confirms `RunExpeditionListPrintFixResult` has zero importers.

## Why it matters
- The orphaned type implies the archive hooks module is responsible for the print-fix feature, when it is not. Future developers may try to re-use or extend it, expecting it to be wired up.
- The actual `useRunExpeditionListPrintFix` return value is untyped (`any`), so the intended contract (`{ totalCount: number }`) is silently unenforced. A backend rename goes undetected by TypeScript.

## Suggested fix
Two small changes:

1. **Delete** `RunExpeditionListPrintFixResult` from `useExpeditionListArchive.ts`.
2. **Add** an explicit return-type annotation in `useExpeditionList.ts` so the contract is enforced where the hook actually lives:

```typescript
// frontend/src/api/hooks/useExpeditionList.ts
interface RunFixResult {
  totalCount: number;
}

export const useRunExpeditionListPrintFix = () => {
  return useMutation({
    mutationFn: async (): Promise => {
      // ...
      return await response.json() as RunFixResult;
    },
  });
};
```

---
_Filed by daily arch-review routine on 2026-07-03._
