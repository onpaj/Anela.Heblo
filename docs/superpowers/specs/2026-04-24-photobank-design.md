# Photobank Feature Design

**Date:** 2026-04-24
**Status:** Approved
**Supersedes:** `2026-04-14-photo-bank-design.md` (that spec jumped to AI tagging in MVP; this one starts simpler and adds AI later)
**Branch target:** `feat/photobank`

---

## Overview

A photo catalog that allows internal users to find photos stored on SharePoint by browsing and filtering via tags. Photos remain on SharePoint; the application maintains a metadata database of photos and their tags. An automated daily index job keeps the catalog in sync with SharePoint.

**Scale:** 1,000–20,000 photos.
**Access:** All authenticated users can browse. Admin role required to manage tags, rules, and index configuration.

**Phasing:**
- **MVP (this spec):** Folder-based tag rules + manual tags + daily sync + gallery UI
- **Phase 2:** AI-generated tags (Azure AI Vision) — schema already prepared

---

## Architecture

New vertical slice `Photobank` inside the existing Clean Architecture monorepo. No new infrastructure — reuses Postgres, Hangfire, and the existing Microsoft Graph integration pattern from `KnowledgeBase`.

### Components

| Component | Layer | Role |
|---|---|---|
| `PhotobankIndexJob` | Application | Hangfire daily job — Graph delta sync, tag rule application |
| `PhotobankIndexRoot` | Domain / Persistence | Configured SharePoint root folders to index |
| `TagRule` | Domain / Persistence | Path pattern → tag mappings, managed by admins |
| `Photo` | Domain / Persistence | Photo metadata + SharePoint reference |
| `Tag` | Domain / Persistence | Canonical tag list (lowercase-normalized) |
| `PhotoTag` | Domain / Persistence | Many-to-many photo↔tag with source tracking |
| API controllers | API | REST endpoints under `/api/photobank` |
| React page | Frontend | Gallery + filter sidebar, listed under Marketing group |

---

## Data Model

```
PhotobankIndexRoot
  Id                  uuid PK
  FolderPath          text          -- SharePoint folder path (display/logging)
  DriveId             text          -- Graph API drive ID
  SiteId              text          -- Graph API site ID
  RootItemId          text          -- Graph API item ID of the root folder
  DeltaLink           text?         -- null = not yet indexed; stored after each run
  LastIndexedAt       timestamp?
  IsActive            bool

Photo
  Id                  uuid PK
  SharePointFileId    text UNIQUE   -- Graph API item ID; stable across renames/moves
  Name                text          -- filename
  FolderPath          text          -- full folder path string
  SharePointWebUrl    text          -- direct link to open in SharePoint
  FileSizeBytes       bigint
  LastModifiedAt      timestamp     -- from Graph API
  IndexedAt           timestamp     -- when our job last processed this photo
  IndexRootId         uuid FK → PhotobankIndexRoot

Tag
  Id                  uuid PK
  Name                text UNIQUE   -- lowercase-normalized (e.g. "products", not "Products")

PhotoTag
  PhotoId             uuid FK → Photo
  TagId               uuid FK → Tag
  Source              enum(Rule, Manual, AI)   -- AI reserved for Phase 2
  PRIMARY KEY (PhotoId, TagId)

TagRule
  Id                  uuid PK
  PathPattern         text          -- e.g. "/Fotky/Produkty/*"
  TagName             text          -- tag to apply (normalized at save time)
  IsActive            bool
  SortOrder           int
```

**Key decisions:**
- `SharePointFileId` (Graph item ID) is the stable identity — used to upsert on rename/move
- No `ThumbnailUrl` stored — thumbnails fetched by the frontend directly from Graph using the user's MSAL token, constructed from `DriveId` + `SharePointFileId`
- Tags are lowercase-normalized at write time to prevent duplicates
- `PhotoTag.Source` distinguishes rule-applied, manually added, and future AI tags — enables targeted re-apply without touching manual tags
- A photo can have zero or any number of tags

---

## Indexing Job

`PhotobankIndexJob` implements the existing `IRecurringJob` interface and runs daily via Hangfire.

### Per configured root folder:

1. Load `DeltaLink` from `PhotobankIndexRoot` (`null` = first run, full crawl)
2. Call Graph: `GET /drives/{driveId}/items/{rootItemId}/delta?$token={deltaLink}`
3. For each item in the delta response:
   - **Image file** (jpg, jpeg, png, webp, gif, tiff) → upsert `Photo` by `SharePointFileId`
   - **Deleted item** → delete `Photo` row and its `PhotoTag` rows
   - **Non-image / folder** → skip
4. For each upserted photo:
   - Delete existing `PhotoTag` rows with `Source = Rule` for this photo
   - Evaluate all active `TagRule`s against `FolderPath`
   - Write new `PhotoTag` rows for all matching rules (`Source = Rule`)
5. Save the new `deltaLink` back to `PhotobankIndexRoot.DeltaLink`
6. Update `LastIndexedAt`

### Tag rule pattern matching

- Case-insensitive
- `*` matches exactly one path segment (e.g. `/Fotky/*/Kampaně` matches `/Fotky/2024/Kampaně`)
- All matching rules apply — not first-match-wins
- Rules evaluated in `SortOrder` ascending order

### Re-apply rules (admin action)

Triggered via `POST /api/photobank/settings/rules/reapply`:

1. Delete all `PhotoTag` rows where `Source = Rule` (all photos)
2. Re-evaluate every active `TagRule` against every `Photo.FolderPath`
3. Write new rule-based `PhotoTag` rows
4. `Source = Manual` rows are never touched

---

## Tag System

**Rule tags:** Applied by the index job based on `TagRule` path patterns. Re-created on each index run for changed photos, or on-demand via "Re-apply rules".

**Manual tags:** Added/removed by admins per photo in the UI. Survive rule re-applies.

**AI tags (Phase 2):** `Source = AI` already in enum. Requires adding `AiTaggedAt timestamp?` to `Photo` and integrating Azure AI Vision in the index job. No structural schema migration beyond that column.

---

## API Endpoints

All under `/api/photobank`. Auth: existing Microsoft Entra ID. Admin endpoints require `AuthorizationConstants.Roles.Administrator` (`"administrator"`) role claim — same pattern as other admin-gated controllers.

### Gallery (all authenticated users)

```
GET /api/photobank/photos
    ?tags[]=products&tags[]=2024   -- AND filter: photos must have ALL specified tags
    &search=ruz                    -- filename substring search (case-insensitive)
    &page=1&pageSize=48
    → { items: PhotoDto[], total: int, page: int, pageSize: int }

GET /api/photobank/tags
    → { tags: [{ id, name, count }] }   -- all tags with photo counts, sorted by count desc
```

### Photo tag management (admin)

```
POST   /api/photobank/photos/{id}/tags          body: { tagName: string }
DELETE /api/photobank/photos/{id}/tags/{tagId}
```

### Settings (admin)

```
GET    /api/photobank/settings/roots
POST   /api/photobank/settings/roots            body: { siteId, driveId, rootItemId, folderPath }
    -- Note: siteId/driveId/rootItemId must be obtained via Graph Explorer or a future
    -- path-resolver endpoint. v1 does not include a UI folder picker.
DELETE /api/photobank/settings/roots/{id}

GET    /api/photobank/settings/rules
POST   /api/photobank/settings/rules            body: { pathPattern, tagName, sortOrder }
PUT    /api/photobank/settings/rules/{id}
DELETE /api/photobank/settings/rules/{id}

POST   /api/photobank/settings/rules/reapply    → { photosUpdated: int }
```

---

## Frontend

**Route:** `/marketing/photobank` — listed under the **Marketing** group in the sidebar (alongside marketing calendar).

### Gallery page

**Left sidebar (fixed, ~220px):**
- Filename search field
- Tag list with counts — multi-select, AND semantics
- "Clear filters" link when any filter is active

**Main area:**
- Responsive photo grid
- Thumbnails fetched directly from Microsoft Graph:
  `https://graph.microsoft.com/v1.0/drives/{driveId}/items/{fileId}/thumbnails/0/medium/content`
  with user's MSAL `Authorization` header
- Pagination (not infinite scroll) — consistent with other list pages in the app
- Selected photo highlighted

**Right drawer (opens on photo click):**
- Larger thumbnail
- Filename and folder path
- Tag chips
- "Open in SharePoint ↗" button
- "Copy link" button
- *(Admin only)* "+ Add tag" input and "✕" remove button per tag

### Admin settings page

**Route:** `/marketing/photobank/settings` — admin role required.

- **Index roots tab:** configured root folders, add/remove, last indexed timestamp, active toggle
- **Tag rules tab:** CRUD table (pattern, tag, sort order, active toggle)
- **"Re-apply rules" button:** triggers reapply endpoint, shows `photosUpdated` count

---

## Microsoft Graph Integration

Reuses the existing Graph client pattern from `GraphOneDriveService` (`KnowledgeBase` feature).

**Application permissions required:**
- `Sites.Read.All`
- `Files.Read.All`

**Delegated scope (frontend MSAL) required:**
- `Files.Read` or `Files.Read.All` — for thumbnail fetching from Graph in the browser

---

## Future AI Upgrade Path (Phase 2)

1. Add `AiTaggedAt timestamp?` column to `Photo`
2. Index job calls Azure AI Vision per photo, writes `PhotoTag` rows with `Source = AI`
3. Admin settings page gains "Re-run AI tagging" button using same re-apply pattern
4. UI optionally shows tag source badge (rule / manual / AI)

No structural schema changes beyond `AiTaggedAt`.

---

## Out of Scope (v1)

- Photo download proxying (users open in SharePoint directly)
- Per-folder access control (all authenticated users see all photos)
- Real-time indexing via SharePoint webhooks
- AI-generated tags (Phase 2)
- Vector embeddings / semantic search
- OCR text extraction
- Duplicate photo detection
- Bulk tag editing
