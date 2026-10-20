---
process: module-journal
kind: module
module: journal
summary: Company journal (Deník) — dated free-text notes tagged and linked to products or product families, shown on the /journal page and as highlighted months on the product charts in the catalog detail.
owns: []
verified_at: "5e993f9e2"
related: []
---

# Journal (Deník)

## Purpose
The journal records **why** something happened to a product: a price change, a campaign, a
recipe change, a stock-out, a supplier problem. Each entry has a date, a title, free text,
optional coloured tags (štítky) and optional links to products. A link can be a full product
code or a **prefix** that covers a whole product family (e.g. `AKL` covers every `AKL…` code).

The point is context when someone looks at numbers: in the catalog detail (Katalog → product)
the sales/consumption/purchase/manufacture chart and the margin chart enlarge and colour the
month points that have a journal entry for that product, and the tooltip lists the entries.
So "why did sales drop in March?" can be answered by hovering March.

The module is plain CRUD inside Heblo's own database. It has **no scheduled jobs, no
background tasks and no external systems**, so it has no process docs — only this overview.

## Users & screens
Permission: feature `Products_Journal` ("Žurnál"), roles `products.journal.read` (view, search,
list tags) and `products.journal.write` (create/edit/delete entries, create tags).

| Where | Route / place | What users do |
|---|---|---|
| Sidebar Produkty → **Deník** | `/journal` (`JournalList`) | Paged list (20 per page by default, page size selectable), newest entry date first; sort by date, title or author; free-text search over title + content; open an entry in a modal (`JournalEntryModal`) to edit or delete it. |
| New entry | `/journal/new` (`JournalEntryNew`) | Form: date (defaults to today), title (required), content, tags (pick existing or type a new one), products (catalog autocomplete or a typed prefix + Enter). |
| Edit entry | `/journal/:id/edit` (`JournalEntryEdit`) | Same form, prefilled. |
| Catalog detail → tab **Deník** | `JournalTab` in `CatalogDetail` | Entries linked to this product (up to 100, newest first); add a new entry (modal, prefilled with the product), edit one, "view all" link to `/journal`. |
| Catalog detail → charts | `ProductChart`, `MarginsTab/MarginsChart` | Months with entries get a larger orange point; the tooltip adds "Záznamy deníku:" with `• dd.MM: title` per entry. |

No MCP tool exposes the journal. No dashboard tile.

API (`JournalController`, `api/journal`):

| Method + path | Access | Purpose |
|---|---|---|
| `GET /api/journal` | read | Paged list, `PageNumber`=1, `PageSize`=20, `SortBy`=`EntryDate`, `SortDirection`=`DESC` |
| `GET /api/journal/search` | read | Same plus filters `SearchText`, `DateFrom`, `DateTo`, `ProductCodePrefix`, `TagIds`, `CreatedByUserId` |
| `GET /api/journal/{id}` | read | One entry |
| `POST /api/journal` | write | Create entry (201) |
| `PUT /api/journal/{id}` | write | Update entry; replaces the product and tag sets completely |
| `DELETE /api/journal/{id}` | write | Soft delete |
| `GET /api/journal/tags` | read | All tags ordered by name |
| `POST /api/journal/tags` | write | Create tag |

## Processes
None — no scheduled job, background task, external read or external write belongs to this
module. Everything below is user-driven CRUD:

- **Create / edit entry** — title is trimmed and required (`InvalidJournalTitle` 1602 if blank),
  content trimmed (max 10 000 chars), `EntryDate` cut to the date part (time dropped). Products
  are trimmed and upper-cased; duplicates are ignored. Edit replaces the whole product list and
  tag list with what the form sends. Author/modifier user id and display name are stored
  (`"Unknown User"` when the token has no name).
- **Delete entry** — soft delete: sets `IsDeleted`, `DeletedAt`, `DeletedBy*`. A global EF query
  filter hides deleted rows everywhere; there is no undelete in the UI.
- **Create tag** — name (max 50, unique) and colour (hex, max 7 chars). The form always sends
  indigo `#6366f1`; the server default is grey `#6B7280`. Tags cannot be renamed or deleted.

### How an entry reaches a product chart
1. `CatalogDetail` calls `useJournalEntriesByProduct(productCode)` →
   `GET /api/journal/search?productCodePrefix=<product code>&pageNumber=1&pageSize=100&sortBy=entryDate&sortDirection=desc`.
2. `JournalRepository.SearchEntriesAsync` keeps entries that have **any** stored link `P` where
   the requested product code starts with `P` (`productCode.StartsWith(P)`, SQL `LIKE 'P%'`
   semantics). A link `AKL` matches `AKL001`, `AKL001M`, …; a link `AKL001` does not match `AKL`.
3. The same list feeds the **Deník** tab and both charts (`journalData.entries`).
4. `ChartHelpers.getJournalEntriesForMonth` assigns each entry to a chart month by
   `EntryDate` year + month in the browser's local time. `ProductChart` has 13 points (the
   current month and the 12 before it); `MarginsChart` has 12 points (the 12 completed months,
   current month excluded). Entries outside the window are not drawn.
5. `generatePointStyling` makes points with ≥1 entry orange `#F97316`, radius 6 (normal radius 3);
   `generateTooltipCallback` appends the entry list to the tooltip.

Entries with no product link never appear in the catalog — only on `/journal`.

## Data owned
All in the `public` schema of the main Heblo database:

- `JournalEntries` — one row per note: `Title` (≤200), `Content` (≤10 000), `EntryDate`,
  `CreatedAt`/`ModifiedAt`, `CreatedBy/ModifiedBy/DeletedBy UserId + Username`, soft-delete
  `IsDeleted` + `DeletedAt`. Indexes on `EntryDate`, `CreatedByUserId`, `(IsDeleted, EntryDate)`.
- `JournalEntryProducts` — one row per (entry, product code or prefix); key
  `(JournalEntryId, ProductCodePrefix)`, value ≤50 chars, stored upper-case. Cascade-deleted with
  the entry (only on a hard delete, which the app never does).
- `JournalEntryTags` — the tag catalogue: `Name` (unique, ≤50), `Color`, `CreatedByUserId`.
- `JournalEntryTagAssignments` — one row per (entry, tag).

Timestamps are `timestamp without time zone` holding UTC (`AsUtcTimestamp`). No cache, no blob
storage, no reporting view (the journal is not exposed to Metabase).

## External systems
None.

## Dependencies
- Reads nothing from other modules. Product links are free text: they are **not** validated
  against the catalog, so a typo or a discontinued code is stored as-is.
- **Catalog** (frontend only) reads the journal: the catalog detail Deník tab and the product
  and margin charts. The chart series themselves come from the catalog detail endpoint and the
  margin calculation (see `calc-margins`), not from this module.
- Uses `ICurrentUserService` (Users) for the author identity and `FeatureAuthorize` for access.

## Known quirks
- **Catalog shows at most 100 entries per product.** The by-product query requests one page of
  100, newest first; older entries are missing from the tab and the chart with no "more" hint.
- **"View all" from the catalog loses the product filter.** `CatalogDetail` navigates to
  `/journal?productCode=<code>`, but `JournalList` never reads that query parameter, so the full
  unfiltered list opens.
- **The list page only searches text.** The API supports date, product, tag and author filters,
  but `/journal` exposes only the free-text box; those filters are reachable only via the API.
- **Text search is a substring match on `lower()`** of title and content — case-insensitive, but
  diacritics must match exactly (searching "cistic" does not find "čistič").
- **Product prefix match is case-sensitive on the requested side.** Stored links are
  upper-cased, the requested code is not; this works because catalog codes are upper-case, but a
  lower-case `ProductCodePrefix` sent to the search API matches nothing.
- **Deník tab is not permission-gated in the catalog.** A user with catalog access but without
  `products.journal.read` still sees the tab; the search call is rejected and the tab shows an
  "Chyba při načítání deníku" error, and charts show no markers.
- **`/journal/new` and `/journal/:id/edit` have no route guard** (`/journal` does); the API still
  enforces `products.journal.write`, so saving fails for users without it.
- **Any writer can edit or delete anyone's entry** — the delete handler has an explicit "allow all
  authenticated users" comment; there is no author check.
- **Duplicate tag name gives a server error, not a friendly message.** The unique index
  `IX_JournalEntryTags_Name` is the only guard; `DuplicateJournalTag` (1609) exists but is never
  returned. Error codes 1603–1607, 1609 and 1610 are defined but unused.
- **Tags cannot be renamed, recoloured or deleted** — there is no endpoint; colours are always
  `#6366f1` when created from the form. The colour value is not validated server-side.
- **Link rows have no real creation time.** `JournalEntryProducts.CreatedAt` and
  `JournalEntryTagAssignments.CreatedAt` are never set by the domain methods and the DbContext
  does not stamp them, so they hold `0001-01-01`.
- **Month assignment uses browser local time.** `EntryDate` is stored as midnight UTC of the chosen
  day; in Czech time (UTC+1/+2) it stays on the same calendar day, so markers are correct for
  users in Europe.

## Code entry points
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs` — entity, trimming/normalising, product-prefix and tag replacement, soft delete
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/` — one handler per API call (create, update, delete, get, list, search, tags)
- `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs` — search filters (text, dates, prefix `StartsWith`, tags, author) and sorting
- `backend/src/Anela.Heblo.Persistence/Journal/JournalEntryConfiguration.cs` — tables, indexes, soft-delete query filter
- `backend/src/Anela.Heblo.API/Controllers/JournalController.cs` — endpoints and read/write permissions
- `frontend/src/api/hooks/useJournal.ts` — React Query hooks, incl. `useJournalEntriesByProduct` (catalog query, pageSize 100)
- `frontend/src/components/pages/Journal/JournalList.tsx` — `/journal` page
- `frontend/src/components/JournalEntryForm.tsx` — create/edit form, tag creation, product prefix input
- `frontend/src/components/catalog/detail/charts/ChartHelpers.tsx` — month matching, chart point styling and tooltip
- `frontend/src/components/catalog/detail/tabs/JournalTab.tsx` — the Deník tab in the catalog detail
