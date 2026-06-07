## Module
Journal

## Finding
There is a mismatch between the sort column values sent by the frontend and those handled by the backend, causing a silent wrong sort.

**Frontend** (`frontend/src/components/pages/Journal/JournalList.tsx`, line 411):
```tsx
<SortableHeader
  column="createdByUsername"   // ← sent as sortBy when user clicks "Autor"
  ...
>
```

**Backend** (`backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`, lines 44–55 and 135–146) only handles:
```csharp
sortBy?.ToLower() switch
{
    "title"     => ...,
    "createdat" => ...,   // ← no UI control sends this value
    _           => EntryDate sort (default)
};
```

When the user clicks the "Autor" column header, `sortBy` becomes `"createdByUsername"`. After `.ToLower()` it becomes `"createdbyusername"`, which matches no case and silently falls through to the EntryDate default. The results appear to re-sort but actually sort by EntryDate — no error, no visual indication.

Additionally, the `"createdat"` branch is effectively unreachable from the current frontend (no column sends that value), making it dead code.

## Why it matters
Clicking a sort header that does nothing is a UX defect and a contract violation between frontend and backend. The backend silently ignores an unrecognised sort key rather than returning an error, making it impossible to detect the bug from logs or tests.

## Suggested fix
Two equally valid options:

1. **Add the missing backend case** — add `"createdbyusername"` to the repository switch to sort by `x.CreatedByUsername`, and remove the unused `"createdat"` case (no frontend column sends it).
2. **Remove the unsupported frontend column** — if sorting by author is not yet implemented, remove the `SortableHeader` wrapper from the "Autor" column in `JournalList.tsx` (line 411) and replace with a plain `<th>`.

Whichever option is chosen, the `"createdat"` dead-code case should be cleaned up at the same time.

---
_Filed by daily arch-review routine on 2026-06-04._