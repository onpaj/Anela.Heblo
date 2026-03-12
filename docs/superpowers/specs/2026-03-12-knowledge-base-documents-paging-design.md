# Knowledge Base Documents — Paging, Sorting & Filtering

**Date:** 2026-03-12
**Status:** Approved
**Branch:** feature/export (to be continued on a new branch)

## Overview

Add server-side paging, sorting, and filtering to the Knowledge Base Documents tab. The visual feel and interaction pattern must match the existing CatalogList component.

## Goals

- Replace the flat document list with a paginated, sortable, filterable table
- All filtering/sorting/paging happens server-side (DB-level via EF Core IQueryable)
- URL state synchronization via `useSearchParams` (bookmarkable, browser back/forward works)
- Visual consistency with CatalogList (filter bar, sortable headers, pagination footer)

## Out of Scope

- Changes to the Search tab or Ask tab
- Changes to the Upload tab
- Date range filters (created/indexed date)
- Content type filter values are dynamic (derived from actual document types in DB, not hardcoded)

---

## Backend Design

### Request / Response

**`GetDocumentsRequest`** (extends existing):
```csharp
public class GetDocumentsRequest : IRequest<GetDocumentsResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public string? FilenameFilter { get; set; }
    public string? StatusFilter { get; set; }
    public string? ContentTypeFilter { get; set; }
}
```

**`GetDocumentsResponse`** (extends existing):
```csharp
public class GetDocumentsResponse : BaseResponse
{
    public List<DocumentSummary> Documents { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
```

`DocumentSummary` is unchanged.

### Repository Interface

Remove `GetAllDocumentsAsync`. Add:

```csharp
Task<(List<KnowledgeBaseDocument> Documents, int TotalCount)> GetDocumentsPagedAsync(
    string? filenameFilter,
    string? statusFilter,
    string? contentTypeFilter,
    string sortBy,
    bool sortDescending,
    int pageNumber,
    int pageSize,
    CancellationToken cancellationToken);
```

### Repository Implementation

Uses EF Core `IQueryable`:
1. Start with `_context.KnowledgeBaseDocuments.AsQueryable()`
2. Apply `Where()` for each non-null filter (filename: case-insensitive contains; status: exact match; contentType: exact match)
3. Apply `OrderBy()`/`OrderByDescending()` based on `sortBy` (valid values: `Filename`, `Status`, `CreatedAt`, `IndexedAt`)
4. Run `CountAsync()` for total count
5. Apply `Skip((pageNumber - 1) * pageSize).Take(pageSize)`
6. Return `(documents, totalCount)`

### Handler

Thin — maps request params to repository call, maps result to response.

### Controller

Existing `GET /api/knowledgebase/documents` gains query string params (all optional):
- `pageNumber` (default: 1)
- `pageSize` (default: 20)
- `sortBy` (default: `CreatedAt`)
- `sortDescending` (default: `true`)
- `filenameFilter`
- `statusFilter`
- `contentTypeFilter`

---

## Frontend Design

### API Hook

`useKnowledgeBaseDocumentsQuery` accepts params object:
```typescript
{
  pageNumber: number;
  pageSize: number;
  sortBy: string;
  sortDescending: boolean;
  filenameFilter: string;
  statusFilter: string;
  contentTypeFilter: string;
}
```

Uses absolute URL: `${(apiClient as any).baseUrl}/api/knowledgebase/documents`
Returns React Query result with `{ documents, totalCount, pageNumber, pageSize }`.

### Component: KnowledgeBaseDocumentsTab

**State (URL-synced via `useSearchParams`):**
- `pageNumber`, `pageSize` — pagination
- `sortBy`, `sortDescending` — sorting
- `filenameFilter`, `statusFilter`, `contentTypeFilter` — applied filters

**Additional local state (not URL-synced):**
- `filenameInput` — typing buffer, applied on Enter or Apply button click

**Layout (matches CatalogList structure):**
```
┌─────────────────────────────────────────────────────┐
│ Filter bar: [filename input] [Status ▾] [Type ▾] [Apply] [Clear] │
├─────────────────────────────────────────────────────┤
│ Table with sticky header                            │
│  Soubor ↕  |  Stav ↕  |  Typ  |  Vytvořeno ↕  |  Indexováno ↕  | (delete) │
│  ...rows...                                         │
├─────────────────────────────────────────────────────┤
│ Pagination: [10 ▾] Page [1][2][3]... of N          │
└─────────────────────────────────────────────────────┘
```

**Sortable columns:** Filename, Status, Created, Indexed (using same SortableHeader pattern as CatalogList — chevron icons, toggle direction on same column, reset to ascending on new column)

**Filter behavior:**
- Filename: type → Enter or Apply button triggers filter; resets to page 1
- Status/ContentType: onChange triggers filter immediately; resets to page 1
- Clear button: resets all filters + sort to defaults

**URL sync pattern:** Same dual-direction `useEffect` pattern as CatalogList — component state → URL (with `replace: true`) and URL → component state on mount.

**Unchanged components:** `StatusBadge`, `ConfirmDeleteDialog`

---

## Data Flow

```
URL params
    ↓ (on mount / back-forward)
Component state
    ↓ (useEffect)
useKnowledgeBaseDocumentsQuery(params)
    ↓ (React Query fetch)
GET /api/knowledgebase/documents?pageNumber=1&pageSize=20&sortBy=CreatedAt&...
    ↓
GetDocumentsHandler → KnowledgeBaseRepository.GetDocumentsPagedAsync()
    ↓ (EF Core IQueryable: Where → OrderBy → Count → Skip/Take)
{ documents[], totalCount }
    ↓
Table render + pagination footer
```

---

## Content Type Filter Values

Content type values in the dropdown are loaded dynamically from a dedicated endpoint `GET /api/knowledgebase/documents/content-types` that returns distinct content types from the DB. This avoids hardcoding MIME types and stays current as new document types are added.

Alternatively (simpler): derive distinct content types from the current page result and populate the dropdown client-side. **Recommended simpler approach**: fetch all distinct content types once on tab mount via a separate lightweight query.

---

## Testing

- **Backend**: Unit tests for `GetDocumentsHandler` covering filter combinations, sort directions, pagination boundaries. Repository integration test with real EF Core in-memory DB.
- **Frontend**: Jest tests for `KnowledgeBaseDocumentsTab` — filter state changes, URL sync, sort header clicks, page navigation.
- **E2E**: Not required (knowledge base tests are not in the nightly suite yet).
