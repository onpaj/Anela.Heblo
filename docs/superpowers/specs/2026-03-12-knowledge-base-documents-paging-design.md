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

**Validation rules:**
- `PageSize` must be one of `[10, 20, 50]`. If an invalid value is passed, fall back to 20.
- `SortBy` must be one of `["Filename", "Status", "CreatedAt", "IndexedAt"]`. If an unrecognised value is passed, fall back to `"CreatedAt"`. Do not return a 400 — stale URL params should degrade gracefully.
- `PageNumber` must be ≥ 1. If < 1, clamp to 1.

**`GetDocumentsResponse`** (extends existing):
```csharp
public class GetDocumentsResponse : BaseResponse
{
    public List<DocumentSummary> Documents { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize); // computed, not stored
}
```

`DocumentSummary` is unchanged.

### Status Filter — Case Handling

The frontend sends status values as lowercase strings (`"indexed"`, `"processing"`, `"failed"`) because the existing handler lowercases enum output via `ToString().ToLowerInvariant()`. The handler must parse `StatusFilter` to the `DocumentStatus` enum using case-insensitive parsing before passing it to the repository:

```csharp
DocumentStatus? status = null;
if (!string.IsNullOrEmpty(request.StatusFilter) &&
    Enum.TryParse<DocumentStatus>(request.StatusFilter, ignoreCase: true, out var parsed))
{
    status = parsed;
}
```

Invalid/unknown status strings are silently ignored (treated as no status filter).

### Repository Interface

Remove `GetAllDocumentsAsync`. Add:

```csharp
Task<(List<KnowledgeBaseDocument> Documents, int TotalCount)> GetDocumentsPagedAsync(
    string? filenameFilter,
    DocumentStatus? statusFilter,
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
2. Apply `Where()` for each non-null filter:
   - Filename: `EF.Functions.Like(d.Filename, $"%{filenameFilter}%")` (case-insensitive via DB collation)
   - Status: `d.Status == statusFilter.Value` (enum comparison, no string case issue)
   - ContentType: `d.ContentType == contentTypeFilter` (exact match)
3. Apply `OrderBy()`/`OrderByDescending()` based on `sortBy`. Unrecognised values default to `CreatedAt` (validation already clamped this in the handler, but repository is defensive).
4. Run `CountAsync()` for total count
5. Apply `Skip((pageNumber - 1) * pageSize).Take(pageSize)`
6. Return `(documents, totalCount)`

### Handler

Thin — validates/clamps input params, parses `StatusFilter` to enum, calls repository, maps result to response (including computing `TotalPages`).

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

### Content Type Dropdown Values

Content type values are loaded dynamically via a dedicated hook `useKnowledgeBaseContentTypesQuery` that calls `GET /api/knowledgebase/documents/content-types`. This endpoint returns the distinct `ContentType` values present in the DB. The route uses a specific path segment so it does not conflict with the `DELETE /api/knowledgebase/documents/{id:guid}` route (the GUID constraint prevents the string `"content-types"` from matching).

This requires:
- A new `GetDocumentContentTypes` use case (request/handler)
- A new controller action: `GET /api/knowledgebase/documents/content-types` → `string[]`
- A new repository method: `GetDistinctContentTypesAsync(CancellationToken)`
- A new frontend hook: `useKnowledgeBaseContentTypesQuery()`

The dropdown shows "Vše" (All) as the first option, followed by sorted distinct content types.

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
Returns React Query result with `{ documents, totalCount, pageNumber, pageSize, totalPages }`.

React Query config:
- `queryKey: ['knowledge-base-documents', pageNumber, pageSize, sortBy, sortDescending, filenameFilter, statusFilter, contentTypeFilter]` — all params in key so any change triggers a new fetch
- `staleTime: 5 * 60 * 1000` (5 min), `gcTime: 10 * 60 * 1000` (10 min) — matches CatalogList
- `enabled: true` always (no conditional fetching needed)

### Tab Mount / Remount Behaviour

`KnowledgeBasePage` uses `useState<Tab>` for the active tab. When the user switches away from Documents and returns, `KnowledgeBaseDocumentsTab` unmounts and remounts. On remount:
- URL params are read and component state is initialised from them (filter/sort/page restored correctly)
- React Query serves cached data immediately (within `staleTime`), then refetches in the background

The active tab itself is **not** URL-synced — this is intentional and out of scope. Only the Documents tab's filter/sort/page state is URL-synced.

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

**Filter behaviour:**
- Filename: type → Enter or Apply button triggers filter; resets to page 1
- Status/ContentType: onChange triggers filter immediately; resets to page 1
- Clear button: resets all filters + sort to defaults

**URL sync pattern:** Same dual-direction `useEffect` pattern as CatalogList — component state → URL (with `replace: true`) and URL → component state on mount. URL params are removed for default values (page 1, pageSize 20, default sort).

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
GetDocumentsHandler (validate/clamp params, parse StatusFilter → enum)
    ↓
KnowledgeBaseRepository.GetDocumentsPagedAsync()
    ↓ (EF Core IQueryable: Where → OrderBy → CountAsync → Skip/Take)
{ documents[], totalCount }
    ↓
GetDocumentsResponse { documents, totalCount, pageNumber, pageSize, totalPages }
    ↓
Table render + pagination footer
```

---

## Testing

- **Backend**: Unit tests for `GetDocumentsHandler` covering: filter combinations, sort directions, pagination boundaries, invalid `sortBy` fallback, invalid `pageSize` clamp, unknown `statusFilter` ignored, `TotalPages` computed correctly.
- **Frontend**: Jest tests for `KnowledgeBaseDocumentsTab` — filter state changes, URL sync, sort header clicks, page navigation.
- **E2E**: Not required (knowledge base tests are not in the nightly suite yet).
