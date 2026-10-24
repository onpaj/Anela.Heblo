# Design: #4438

No UI change. New module `frontend/src/components/pages/ExpeditionListArchive/formatDateTime.ts`:

```ts
export const formatDateTime = (iso: string | null): string => {
  if (!iso) return "–";
  return new Date(iso).toLocaleString("cs-CZ", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
};
```
(Exact body copied from existing code; keep U+2013.)

Consumers: `ExpeditionListArchivePage.tsx` imports from `../components/pages/ExpeditionListArchive/formatDateTime`; `ExpeditionJobControlsBar.tsx` imports from `./formatDateTime`. Test at `.../ExpeditionListArchive/__tests__/formatDateTime.test.ts`.
