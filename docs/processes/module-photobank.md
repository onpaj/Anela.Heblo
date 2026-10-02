---
process: module-photobank
kind: module
module: photobank
summary: Searchable, tagged index of Anela's marketing photos that live in SharePoint — Heblo stores only metadata and tags, the files stay in SharePoint.
owns: []
verified_at: "5e993f9e2"
related:
  - sync-photobank-index
  - job-photobank-auto-tag
  - flow-photobank-tag-rules
---

# Photobank (Fotobanka)

## Purpose
Marketing keeps thousands of product and campaign photos in SharePoint folders. The photobank
(Fotobanka) lets staff find them without browsing folders: filter by tags (štítky), search by
path or file name, preview a thumbnail and open or copy the SharePoint link. **Heblo never
stores or uploads the photo files** — it keeps a metadata index (file name, folder path,
SharePoint ids, size, modified date) plus tags. Every photo shown in Heblo exists in SharePoint;
editing or deleting a photo is done in SharePoint, and Heblo follows at the next nightly index.

Tags come from three sources (`PhotoTags.Source`):
- **Rule** — derived from the folder path by admin-defined tag rules (`flow-photobank-tag-rules`).
- **Manual** — added by a person, one photo or in bulk.
- **AI** — suggested by an LLM from the file path and name (`job-photobank-auto-tag`).

## Users & screens
Access is the `Marketing_Photobank` feature ("Fotobanka") with three levels: Read (browse),
Write (tag photos, trigger AI re-tag), Admin (settings, create/delete tags, rules, re-apply).

| Route | Who | What |
|---|---|---|
| `/marketing/photobank` (sidebar Marketing → "Fotobanka") | Read | Gallery: tag sidebar with counts (AND filter), path search (substring or regex), "without tags" filter, grid/list view, 48 per page newest-modified first; drawer with preview, tags, SharePoint link |
| same page, drawer / selection bar | Write | Add/remove a tag on one photo; bulk-tag the current filter result or selected photos; "AI re-tag" selected photos |
| `/marketing/photobank/settings` | Admin | Tabs: Index Roots (which SharePoint folders to index), Tag Rules (path regex → tag, re-apply), Tags (create/delete tags with assignment counts) |

API: `api/photobank/*` (`PhotobankController`). No MCP tool reads the photobank.

## Processes
- `sync-photobank-index` — nightly (Hangfire `photobank-index`, 03:00) Microsoft Graph delta walk of every active index root: adds/updates/deletes `Photos` and recomputes Rule tags of changed photos.
- `job-photobank-auto-tag` — nightly (Hangfire `photobank-auto-tag`, 04:00, **disabled by default**) plus on-demand "AI re-tag": an LLM assigns existing tags to untagged/changed photos.
- `flow-photobank-tag-rules` — admin maintains path→tag rules and re-applies them to the whole library (or for one rule).

Plain CRUD / read-only actions (no process doc): browse/search photos (`GET photos`), tag list
with counts (`GET tags`, in-memory cache), add/remove a manual tag on one photo, bulk add a
manual tag by filter (`POST photos/bulk-tag`) or by ids (`POST photos/tag-by-ids`), create/delete
a tag (deleting cascades its assignments), add/delete an index root. Thumbnails
(`GET photos/{id}/thumbnail/{medium|large}`) are proxied live from Graph per request and not
stored.

## Data owned
All tables are in `public` of the Heblo DB.
- `Photos` — one row per indexed image file; unique `SharePointFileId` (Graph item id), `DriveId`, `FolderPath` (relative to the drive root), `FileName`, `SharePointWebUrl`, `FileSizeBytes`, `ModifiedAt` (SharePoint last-modified), `IndexedAt` (first seen), `LastAutoTaggedAt` (null = waiting for AI tagging). `MimeType` and `TakenAt` exist but are never filled.
- `PhotobankTags` — tag vocabulary; `Name` is lowercase and unique.
- `PhotoTags` — photo↔tag assignment, PK (`PhotoId`, `TagId`), `Source` stored as text `Rule` / `Manual` / `AI`; cascades from both photo and tag.
- `PhotobankTagRules` — `PathPattern` (.NET regex), `TagName`, `IsActive`, `SortOrder`.
- `PhotobankIndexRoots` — SharePoint folders to index: `SharePointPath`, `DriveId`, `RootItemId` (resolved by the job), `DeltaLink` (Graph delta cursor), `LastIndexedAt`, `IsActive`.
- In-memory cache key `Photobank:Tags:WithCounts` — the tag list with counts for the sidebar, TTL `Photobank:TagsCache:TtlSeconds` (60 s), invalidated by every tag write. Per app instance.

## External systems
- **Microsoft 365 / SharePoint via Microsoft Graph** (read only, app-only token, named HttpClient `MicrosoftGraph`):
  `GET /drives/{driveId}/root:/{path}:` (resolve root folder id),
  `GET /drives/{driveId}/items/{rootItemId}/delta` (+ `@odata.nextLink` / `@odata.deltaLink`),
  `GET /drives/{driveId}/items/{itemId}/thumbnails/0/{medium|large}/content` (gallery thumbnails).
  Heblo never writes to SharePoint.
- **Anthropic Claude** (via the shared `IChatClient`) — only for AI tagging; model `Photobank:AutoTag:Model` (`claude-haiku-4-5-20251001`).

## Dependencies
- Reads from: Background jobs module (`RecurringJobConfiguration` enable switch and cron, Hangfire), Microsoft365 adapter (`PhotobankGraphService`), Anthropic adapter (`IChatClient`).
- Read by: nobody — no other module or report uses photobank data.

## Known quirks
- **`docs/features/photobank.md` is partly outdated**: it says AI tags are "reserved for phase 2 / Azure AI Vision" (they exist, via Claude, from path text only), that thumbnails load with the user's MSAL token (they are proxied by the backend with the app token), and that settings need the `administrator` role (it is `Marketing_Photobank` Admin).
- **The settings route has no frontend guard** (`frontend/src/App.tsx` wraps only the gallery route in `guard`); a non-admin can open the page by URL, but every settings API call is rejected (Admin required).
- **Bulk tag by filter ("Otagovat") ignores the regex switch and the "without tags" filter.** `BulkTagDialog` sends only the selected tags and the search text, and `PhotobankPhotoRepository.CountFilteredPhotosAsync` / `GetFilteredPhotoIdsMissingTagAsync` always build the filter with `useRegex: false`. With regex mode on, the regex is matched as a literal substring (usually 0 photos); with "without tags" on, already-tagged photos are tagged too. The count in the dialog and the photos tagged can differ from the gallery. Tagging selected photos (`tag-by-ids`) is not affected. (Read from code, not observed.)
- **Index roots cannot be edited or deactivated** — there is only add and delete. `IsActive` is always true; changing a path means delete + re-add (the new root starts a full delta from scratch).
- **Deleting an index root does not remove its photos.** They stay searchable but are never updated or removed again (see `sync-photobank-index`).
- **Removing a Rule tag by hand does not stick** — the next re-apply, or the next time the photo changes in SharePoint, puts it back. Change or deactivate the rule instead.
- **Schema drift on timestamp columns** broke the nightly index in 2026 (#3757, `Cannot write DateTime with Kind=Unspecified to … 'timestamp with time zone'`); `PhotobankSchemaHealthCheck` (`photobank-schema` under `/health/ready`) now reports it. See `docs/development/setup.md` "Photobank column-type drift" — fixed 2026-08-13 (#3915).

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` — all endpoints and their access levels
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` — DI, options, validators
- `backend/src/Anela.Heblo.Domain/Features/Photobank/` — entities, `TagRuleMatcher`, repository interfaces
- `backend/src/Anela.Heblo.Persistence/Photobank/` — EF configurations and repositories (gallery filter in `PhotobankPhotoRepository.BuildFilterQuery`)
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs` — Graph calls (delta, resolve, thumbnails)
- `backend/src/Anela.Heblo.API/HealthChecks/Photobank/PhotobankSchemaHealthCheck.cs` — column-type drift check
- `frontend/src/components/marketing/photobank/` — gallery, drawer, bulk bar, settings tabs
- `docs/features/photobank.md` — original (Czech) feature description, partly outdated
