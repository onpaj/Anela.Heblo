---
process: sync-photobank-index
kind: sync
module: photobank
summary: Nightly Microsoft Graph delta walk of the configured SharePoint folders that adds, updates and removes photo metadata in Photos and recomputes the Rule tags of every changed photo.
owns:
  - backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs
  - backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankGraphService.cs
  - backend/src/Anela.Heblo.Application/Features/Photobank/Services/MockPhotobankGraphService.cs
  - backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/AddRoot/**
  - backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/DeleteRoot/**
  - backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetRoots/**
  - backend/src/Anela.Heblo.Domain/Features/Photobank/PhotobankIndexRoot.cs
  - backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRootRepository.cs
  - backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRootRepository.cs
  - backend/src/Anela.Heblo.Persistence/Photobank/PhotobankIndexRootConfiguration.cs
  - backend/src/Anela.Heblo.API/HealthChecks/Photobank/PhotobankSchemaHealthCheck.cs
verified_at: "5e993f9e2"
related:
  - flow-photobank-tag-rules
  - job-photobank-auto-tag
---

# Photobank index (SharePoint → Photos)

## Purpose
Keeps the photobank (Fotobanka) gallery in step with the marketing photo folders in SharePoint:
new photos appear, renamed/moved photos get their new path, deleted photos disappear. It also
gives each new or changed photo its path-based tags (Rule tags). Nobody reads the result except
the gallery `/marketing/photobank`. Photos added to SharePoint during the day appear in Heblo
the next morning (or after a manual run).

## Trigger
- Hangfire recurring job **`photobank-index`** ("Photobank Index", category Content), cron
  `0 3 * * *` Europe/Prague (03:00 daily), enabled by default. Cron and enable switch can be
  changed in the Recurring Jobs admin (`RecurringJobConfiguration`); a disabled job logs and exits.
- Manual run from the Recurring Jobs admin page.
- Adding an index root does **not** trigger a run; the new folder is indexed at the next run.

## Data flow
Source: Microsoft Graph (SharePoint/OneDrive drives), app-only token for scope
`https://graph.microsoft.com/.default`, named HttpClient `MicrosoftGraph`.

1. Load roots from `public."PhotobankIndexRoots"` where `IsActive` and `DriveId` is not null
   (admin enters `SharePointPath` + `DriveId` on the settings tab "Index Roots").
2. Per root, in turn (one failing root is logged and skipped; the others continue):
   1. If `RootItemId` is empty: `GET /v1.0/drives/{DriveId}/root:/{SharePointPath}:` → store the
      folder's item id in `RootItemId` (saved immediately).
   2. Delta: first run (no `DeltaLink`) `GET /v1.0/drives/{DriveId}/items/{RootItemId}/delta`;
      later runs call the stored `DeltaLink`. Follow `@odata.nextLink` until `@odata.deltaLink`.
      **All pages are read into memory before anything is written.**
   3. Map items (`PhotobankGraphService.MapItem`): items with a `deleted` facet → delete;
      folders and non-files skipped; files kept only with extension `.jpg .jpeg .png .webp .gif
      .tiff` (case-insensitive). `FolderPath` = `parentReference.path` after `/root:/`, i.e.
      relative to the drive root (not to the index root), no leading slash.
   4. Load active rules from `public."PhotobankTagRules"` once per root.
   5. Walk items in delta order, upserting in batches of 200 (`BatchSize`):
      - **Upsert** into `public."Photos"` keyed by `SharePointFileId` (= Graph item id): set
        `FileName`, `FolderPath`, `SharePointWebUrl`, `FileSizeBytes`, `ModifiedAt`
        (= Graph `lastModifiedDateTime`, else now), `DriveId` (= the root's drive id). New rows
        get `IndexedAt` = now. If folder or file name changed, `LastAutoTaggedAt` is reset to
        null so `job-photobank-auto-tag` looks at the photo again. Saved once per batch.
      - **Rule tags** for the batch: match each photo's `FolderPath/FileName` against the active
        rules (`TagRuleMatcher`), create missing tag names in `public."PhotobankTags"`, delete the
        photo's existing `Source = Rule` rows in `public."PhotoTags"` and insert the matched ones
        as `Rule` — unless that photo already has the same tag as `Manual` or `AI`. Saved once
        per batch.
      - **Delete**: a deleted item flushes the pending batch, then removes the `Photos` row by
        `SharePointFileId` (its `PhotoTags` cascade).
   6. Save `DeltaLink` = the new `@odata.deltaLink` and `LastIndexedAt` = now on the root.

Thumbnails shown in the gallery are not part of this sync: they are fetched live per request
(`GET /drives/{driveId}/items/{itemId}/thumbnails/0/{medium|large}/content`, cached by the
browser for a year under `?v=<ModifiedAt>`, so a replaced file gets a fresh thumbnail after the next index).

## Logic & formulas
- Only items the delta reports are touched; an unchanged photo keeps its row and tags as they
  are, even if the tag rules changed since (use re-apply in `flow-photobank-tag-rules`).
- Rule matching: virtual path `FolderPath + "/" + FileName`, each active rule's `PathPattern` as
  a .NET regex, case-insensitive, unanchored unless the pattern anchors it; every matching rule
  adds its tag (not just the first), tag names lowercased, duplicates removed.
- Manual and AI tags are never removed by the index; a Rule tag is not added where the same tag
  already exists as Manual/AI.
- A file moved between two different index roots, or appearing under two overlapping roots, is
  one `Photos` row (unique `SharePointFileId`); the last root processed wins `DriveId`/path.
- Dates are stored as `timestamp without time zone`, UTC.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `PhotobankIndexRoots` rows | none (DB, settings page) | Folders to index: `SharePointPath`, `DriveId` (required), optional `DisplayName` |
| `PhotobankTagRules` rows | none (DB, settings page) | Path → tag rules applied to changed photos |
| `RecurringJobConfiguration` `photobank-index` | cron `0 3 * * *`, enabled | Schedule and on/off switch |
| `UseMockAuth` / `BypassJwtValidation` | false | When true, `MockPhotobankGraphService` is used: delta returns nothing, the index is a no-op |
| AzureAd app credentials | secrets | App-only Graph token (needs read access to the SharePoint sites) |

## Runtime facts
- Marketing site document library drive id `b!jj_-5-FohEybxp_61HKbJLmG6KD2FqJOmZeQYQNbxqrCWYOs1rorQ5BtZfHEjWKj`, example root path `/Grafika_interní/PROFI_FOCENI` — `docs/features/photobank.md` (undated, written 2026-04/05 with the feature).
- Nightly index failed on `Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'` because timestamp-conversion migrations were missing in an environment — #3757, closed by #3915 on 2026-08-13; `PhotobankSchemaHealthCheck` (`photobank-schema`, `/health/ready`) now flags the drift.

## Known quirks
- **Failures are invisible in Hangfire.** Every exception inside a root is caught and logged
  (`Failed to index root {RootId}`); the job still ends Succeeded. Check `LastIndexedAt` on the
  settings tab or the logs.
- **An expired or invalid `DeltaLink` blocks the root every night.** Graph answers an expired
  delta token with an error (typically 410 Gone / resync required); the code calls
  `EnsureSuccessStatusCode` and has no resync handling, so the root fails on every run until
  `DeltaLink` is cleared in the DB (next run then does a full walk). (Read from code, not observed.)
- **Wrong `SharePointPath` or `DriveId` also fails silently** — the resolve call throws, the
  root is skipped and logged, nothing in the UI shows it except an empty `LastIndexedAt`.
- **Deleting an index root keeps its photos.** `DeleteRoot` removes only the root row; its
  `Photos` stay searchable forever and are never updated or deleted again.
- **No per-page commit.** The whole delta is buffered in memory first, and `DeltaLink` is saved
  only after all batches. A crash midway leaves the already-saved batches and repeats the whole
  delta next run (harmless — upserts are idempotent).
- **Files renamed to a non-image extension stay indexed** with their old name: the delta item
  is skipped by the extension filter, not treated as a delete.
- **`Photos.MimeType` and `Photos.TakenAt` are never filled** (no EXIF/mime read).
- **A path change resets `LastAutoTaggedAt` but keeps old AI tags**, so the auto-tagger may add
  more AI tags on top of ones that fit the old path.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — job metadata, per-root loop, batch upsert and Rule-tag recompute
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs` — Graph delta / resolve / thumbnail calls, extension filter, folder-path mapping
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` — real vs mock Graph service registration
- `backend/src/Anela.Heblo.Domain/Features/Photobank/TagRuleMatcher.cs` — rule matching
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRootRepository.cs` — active-roots query
- `backend/src/Anela.Heblo.API/HealthChecks/Photobank/PhotobankSchemaHealthCheck.cs` — timestamp column drift check
- `docs/development/setup.md` — "Photobank column-type drift" runbook
