# Design: Remove duplicate SearchJournalEntryDto in favor of JournalEntryDto

## Component Design

### `JournalEntryDto` (survives, unchanged)
`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalEntryDto.cs` — no changes to this file's content. It becomes the single DTO for both the entry-detail/list read paths and the search read path. `JournalEntryTagDto` continues to be declared in this same file and is unaffected.

### `SearchJournalEntryDto` (removed)
`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs` is deleted outright. No replacement type — all former consumers repoint to `JournalEntryDto`.

### `SearchJournalEntriesResponse` (modified)
`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs` — the `Entries` property's declared type changes from `List<SearchJournalEntryDto>` to `List<JournalEntryDto>`. No other property changes (`TotalCount`, `PageNumber`, `PageSize`, `TotalPages`, `HasNextPage`, `HasPreviousPage` all unchanged).

### `JournalEntryMapper` (modified)
`backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs` — the `ToSearchDto(JournalEntry entry)` static method is deleted. `ToDto(JournalEntry entry)` is unchanged and becomes the sole mapping method, used by both the detail/list path and the search path.

### `SearchJournalEntriesHandler` (modified)
`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` — the LINQ projection over `result.Items`:
```csharp
var entryDtos = result.Items.Select(JournalEntryMapper.ToSearchDto).ToList();
```
becomes:
```csharp
var entryDtos = result.Items.Select(JournalEntryMapper.ToDto).ToList();
```
No other logic in the handler changes — same repository call, same pagination math, same response construction.

### Generated OpenAPI client (regenerated, not hand-edited)
`frontend/src/api/generated/api-client.ts` is regenerated per `docs/development/api-client-generation.md` after the backend change. The generator will stop emitting a `SearchJournalEntryDto` interface/class and will type `SearchJournalEntriesResponse.entries` as `JournalEntryDto[]`.

### Frontend consumers (modified — type references only)
Five files import or reference `SearchJournalEntryDto` from the generated client and must switch to `JournalEntryDto`. In every case this is a type-annotation substitution only — no logic, prop shape, or rendering change, since the two DTOs were structurally identical:

| File | Current reference | New reference |
|------|-------------------|----------------|
| `frontend/src/components/pages/Journal/JournalList.tsx` | `import { JournalEntryDto, SearchJournalEntryDto } from ...`; `(entries as SearchJournalEntryDto[])` | drop `SearchJournalEntryDto` from the import; `(entries as JournalEntryDto[])` |
| `frontend/src/components/pages/CatalogDetail.tsx` | `import { SearchJournalEntryDto } from ...`; `useState<SearchJournalEntryDto \| undefined>(...)` | `import { JournalEntryDto } from ...`; `useState<JournalEntryDto \| undefined>(...)` |
| `frontend/src/components/catalog/detail/CatalogDetailModals.tsx` | `import { SearchJournalEntryDto } from ...`; `selectedJournalEntry?: SearchJournalEntryDto` | `import { JournalEntryDto } from ...`; `selectedJournalEntry?: JournalEntryDto` |
| `frontend/src/components/catalog/detail/tabs/JournalTab.tsx` | `import type { SearchJournalEntryDto } from ...`; `onEditEntry: (entry: SearchJournalEntryDto) => void` | `import type { JournalEntryDto } from ...`; `onEditEntry: (entry: JournalEntryDto) => void` |
| `frontend/src/components/catalog/detail/CatalogDetailTabs.tsx` | `import { SearchJournalEntryDto } from ...`; `journalEntries: SearchJournalEntryDto[]`; `onEditJournalEntry: (entry: SearchJournalEntryDto) => void` | `import { JournalEntryDto } from ...`; `journalEntries: JournalEntryDto[]`; `onEditJournalEntry: (entry: JournalEntryDto) => void` |

Where a file already imports `JournalEntryDto` for another reason, the import list is merged rather than duplicated (none of the five currently do — each imports only `SearchJournalEntryDto`, except `JournalList.tsx` which imports both).

## Data Schemas

### Before (JSON wire shape — search response, per entry)
```json
{
  "id": 0,
  "title": "string",
  "content": "string",
  "entryDate": "2026-08-31T00:00:00Z",
  "createdAt": "2026-08-31T00:00:00Z",
  "modifiedAt": "2026-08-31T00:00:00Z",
  "createdByUserId": "string",
  "createdByUsername": "string",
  "modifiedByUserId": "string",
  "modifiedByUsername": "string",
  "associatedProducts": ["string"],
  "tags": [{ "id": 0, "name": "string", "color": "string" }]
}
```

### After
Identical — no field added, removed, or renamed. Only the OpenAPI schema name backing this shape changes (from a `SearchJournalEntryDto` component schema to the `JournalEntryDto` component schema); the JSON payload itself is byte-for-byte the same, since the two source types had identical properties in identical order-independent JSON serialization.

### `SearchJournalEntriesResponse` schema (full, for reference — only `entries` items schema changes)
```json
{
  "entries": [ /* JournalEntryDto[] — see above, was SearchJournalEntryDto[] */ ],
  "totalCount": 0,
  "pageNumber": 0,
  "pageSize": 0,
  "totalPages": 0,
  "hasNextPage": true,
  "hasPreviousPage": true
}
```
