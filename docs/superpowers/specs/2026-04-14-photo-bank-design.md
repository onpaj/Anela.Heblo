# Photo Bank — AI-Tagged Photo Library with Search

## Context

Anela Heblo has a growing collection of 5,000–50,000 photos stored in OneDrive, primarily used for marketing and social media (product shots, lifestyle photos, packaging). Currently there is no way to search these photos by content — finding "all photos containing our Rose Water Toner" requires manual browsing. Products have similar packaging and differ mainly by label text, making OCR especially valuable.

OneDrive's built-in AI tags are **not accessible via Microsoft Graph API**, ruling out relying on OneDrive's native intelligence. A custom pipeline is needed.

**Goal:** Build a Photo Bank feature in Heblo that automatically indexes OneDrive photos with AI-generated tags, OCR text, and vector embeddings, then provides tag-based search in the UI.

**Phasing:**
- **MVP (this spec):** OneDrive sync + Azure AI Vision auto-tags + OCR + tag-based search + stored embeddings
- **Phase 2:** Semantic natural language search + visual similarity ("find similar")
- **Phase 3:** Face recognition (pending Microsoft Limited Access approval)

## Approach

**Custom pipeline built into Heblo** using Azure AI Vision + existing infrastructure.

Why this over SaaS alternatives:
- Product identification via OCR on label text — no SaaS offers this out of the box
- Reuses existing Graph API integration (`GraphOneDriveService`), pgvector, Azure Blob Storage, Hangfire
- Lowest ongoing cost (~$50–150/month for 50K images)
- Full control over tagging taxonomy and search behavior

Alternatives considered and rejected:
- **SharePoint Premium:** Generic tags only ("person", "outdoor"), cannot recognize specific products, no OCR-based product identification
- **Cloudinary SaaS:** Photos leave Microsoft ecosystem, $89+/month base + per-image AI costs, no custom product recognition
- **Immich/PhotoPrism (self-hosted):** Consumer photo management, not enterprise DAM, no OneDrive integration

## Data Model

### Entities (EF Core, new `PhotoBank` feature)

```
PhotoAsset
├── Id (Guid, PK)
├── OneDriveItemId (string, unique) — Graph API item ID
├── OneDrivePath (string) — original file path in OneDrive
├── FileName (string)
├── MimeType (string)
├── FileSize (long) — bytes
├── Width (int?)
├── Height (int?)
├── TakenAt (DateTimeOffset?) — EXIF date taken
├── IndexedAt (DateTimeOffset) — when AI processing completed
├── ThumbnailBlobPath (string) — Azure Blob Storage path
├── Embedding (Vector) — 1024-dim pgvector column (Azure AI Vision multimodal)
├── OcrText (string?) — extracted text from labels/packaging
├── CreatedAt (DateTimeOffset)
├── ModifiedAt (DateTimeOffset)

PhotoTag
├── Id (Guid, PK)
├── PhotoAssetId (Guid, FK → PhotoAsset)
├── TagName (string) — normalized tag name (e.g., "bottle", "person", "outdoor")
├── Confidence (float) — AI confidence score (0.0–1.0)
├── Source (TagSource enum) — Auto (AI-generated), Manual (user-added)

TagSource (enum): Auto, Manual
```

**Indexes:**
- `PhotoAsset.OneDriveItemId` — unique, for delta sync deduplication
- `PhotoTag.TagName` — for tag-based filtering
- `PhotoTag.PhotoAssetId` — FK index
- `PhotoAsset.Embedding` — IVFFlat pgvector index (for future similarity search)
- `PhotoAsset.OcrText` — GIN trigram index for text search

### Database Migration

New table `photo_assets` and `photo_tags` in the existing PostgreSQL database. pgvector extension already enabled (used by KnowledgeBase).

## Data Pipeline

### 1. OneDrive Sync (Hangfire Recurring Job)

**Job:** `SyncOneDrivePhotosJob` — runs every 15 minutes (configurable via `RecurringJobs` cron config pattern).

**Flow:**
1. Call Microsoft Graph API delta query on the configured OneDrive folder(s)
2. For each new/modified file with image MIME type (JPEG, PNG, WebP, HEIC):
   - Check if `OneDriveItemId` already exists in `photo_assets`
   - If new: enqueue `IndexPhotoCommand` via Hangfire background job
   - If modified: re-enqueue for re-indexing
   - If deleted in OneDrive: soft-delete the `PhotoAsset` record
3. Store delta token for next sync cycle

**Configuration** (appsettings.json):
```json
{
  "PhotoBank": {
    "OneDriveFolderIds": ["folder-id-1", "folder-id-2"],
    "DriveId": "drive-id",
    "SyncCronExpression": "*/15 * * * *",
    "MinConfidenceThreshold": 0.7,
    "ThumbnailMaxWidth": 400,
    "ThumbnailMaxHeight": 400
  }
}
```

**Reuses:** `IOneDriveService` interface (extend with delta query and photo-specific methods). Same Graph API auth via `ITokenAcquisition.GetAccessTokenForAppAsync`.

### 2. Photo Indexing (Background Job per Photo)

**Handler:** `IndexPhotoHandler` — processes a single photo.

**Flow:**
1. Download image from OneDrive via Graph API
2. Extract EXIF metadata (date taken, dimensions)
3. Generate thumbnail (resize to max 400px), upload to Azure Blob Storage via `IBlobStorageService`
4. Call **Azure AI Vision Image Analysis 4.0** with features: `Tags`, `Read` (OCR), `DenseCaptions`
   - Tags: returns object/scene labels with confidence scores → store as `PhotoTag` records
   - Read/OCR: returns extracted text → store in `PhotoAsset.OcrText`
   - DenseCaptions: returns descriptive captions → store top caption as a tag
5. Call **Azure AI Vision multimodal embeddings** (`vectorizeImage`) → store 1024-dim vector in `PhotoAsset.Embedding`
6. Filter tags below `MinConfidenceThreshold` (default 0.7)
7. Save all data to PostgreSQL

**Error handling:** Failed indexing jobs retry 3 times with exponential backoff (Hangfire default). Photos that permanently fail are marked with an error state for manual review.

**Adapter:** New `IAzureAiVisionService` / `AzureAiVisionService` in `Adapters/AzureAiVision/` project, following the existing adapter pattern (e.g., ShoptetApi adapter).

### 3. Processing Estimates

For 50,000 photos (initial indexing):
- Azure AI Vision tags + OCR: ~$50 (at $1/1,000 transactions)
- Multimodal embeddings: ~$15 (at $0.30/1,000 images)
- Total one-time cost: ~$65
- Processing time: ~8-12 hours with Hangfire parallelism (rate-limited to 10 concurrent)

Ongoing (assuming ~100 new photos/week):
- ~$0.50/month for AI Vision
- ~$0.15/month for embeddings
- Negligible blob storage for thumbnails

## Search

### MVP: Tag-Based Search

**Endpoint:** `GET /api/photo-bank?tags=bottle,outdoor&ocrText=bisabolol&from=2026-01-01&to=2026-04-01&page=1&pageSize=50`

**Filters:**
- `tags` — comma-separated tag names, AND logic (photo must have all specified tags)
- `ocrText` — free text search on OCR-extracted text (trigram similarity)
- `from` / `to` — date range filter on `TakenAt`
- `searchTerm` — searches across tag names AND OCR text

**Response:** Paginated list of `PhotoAssetDto` with thumbnail URL, tags, OCR text excerpt, metadata.

**Handler:** `SearchPhotosHandler` — builds EF Core query with tag joins and text search. Same filter composition pattern as `GetCatalogListHandler`.

### Future: Semantic Search (Phase 2)

- Convert user query text to embedding via Azure AI Vision `vectorizeText`
- pgvector cosine similarity on `PhotoAsset.Embedding`
- Same pattern as `KnowledgeBaseRepository.SearchSimilarAsync`

### Future: Visual Similarity (Phase 2)

- User selects a photo → use its stored embedding
- pgvector nearest-neighbor query
- Returns visually similar photos ranked by cosine distance

## API Endpoints

| Method | Path | Handler | Description |
|--------|------|---------|-------------|
| GET | `/api/photo-bank` | `SearchPhotosHandler` | Search/filter photos with pagination |
| GET | `/api/photo-bank/{id}` | `GetPhotoDetailHandler` | Full photo detail with all tags and metadata |
| GET | `/api/photo-bank/{id}/thumbnail` | `GetPhotoThumbnailHandler` | Redirect to blob storage thumbnail URL |
| GET | `/api/photo-bank/{id}/original` | `GetPhotoOriginalHandler` | Proxy download from OneDrive via Graph API |
| POST | `/api/photo-bank/{id}/tags` | `AddManualTagHandler` | Add manual tag to a photo |
| DELETE | `/api/photo-bank/{id}/tags/{tagId}` | `RemoveTagHandler` | Remove a manual tag |
| GET | `/api/photo-bank/tags` | `GetAllTagsHandler` | List all unique tags with counts (for filter UI) |
| POST | `/api/photo-bank/sync` | `TriggerSyncHandler` | Manually trigger OneDrive sync (admin) |

## Frontend

### New Page: `/photo-bank`

**Components:**
- `PhotoBankPage` — main page container
- `PhotoSearchBar` — search input + tag filter chips (reuse filter pattern from Catalog)
- `PhotoGrid` — masonry/grid layout of thumbnails with lazy loading and infinite scroll
- `PhotoDetailPanel` — slide-out panel (same pattern as Catalog detail) showing:
  - Larger preview (thumbnail)
  - All tags (AI + manual) as chips, with ability to add/remove manual tags
  - OCR text (if any)
  - Metadata: file name, date taken, dimensions, file size
  - "Open in OneDrive" link
- `TagFilterBar` — horizontal scrollable bar of most-used tags for quick filtering

**Layout:** Follows `docs/design/layout_definition.md` — standard page layout with sidebar navigation, top filter bar, content area.

**State management:** React Query for data fetching (same as other Heblo features). URL-based filter state for shareable links.

### Navigation

Add "Photo Bank" to the sidebar navigation, under a new "Media" section or alongside existing items.

## MCP Tools

Two new tools for AI assistant access:

**`SearchPhotoBank`** — Search photos by tags, OCR text, or date range. Returns list of photos with metadata.

**`GetPhotoDetail`** — Get full details of a single photo including all tags, OCR text, and OneDrive path.

Pattern: Thin wrappers around MediatR handlers, same as existing MCP tools in `API/MCP/Tools/`.

## Infrastructure

### New Azure Resources

- **Azure AI Vision** (Computer Vision) resource — Standard S1 tier
  - Image Analysis 4.0 API for tags, OCR, dense captions
  - Multimodal embeddings API for vector generation
- **Azure Blob Storage** — new `photo-thumbnails` container in existing storage account

### Configuration

New appsettings section `PhotoBank` (see Data Pipeline section above).

Secrets (in Azure Key Vault / user-secrets):
- `PhotoBank:AzureAiVisionEndpoint`
- `PhotoBank:AzureAiVisionKey`

### New Adapter Project

`Anela.Heblo.Adapters.AzureAiVision` — follows existing adapter pattern:
- `IAzureAiVisionService` interface in Application layer
- `AzureAiVisionService` implementation in adapter project
- `MockAzureAiVisionService` for development/testing

## Feature Module Structure

```
backend/src/Anela.Heblo.Domain/Features/PhotoBank/
├── PhotoAsset.cs
├── PhotoTag.cs
├── TagSource.cs
└── IPhotoAssetRepository.cs

backend/src/Anela.Heblo.Application/Features/PhotoBank/
├── PhotoBankModule.cs
├── Services/
│   └── IAzureAiVisionService.cs
├── UseCases/
│   ├── SearchPhotos/
│   │   ├── SearchPhotosRequest.cs
│   │   ├── SearchPhotosResponse.cs
│   │   └── SearchPhotosHandler.cs
│   ├── GetPhotoDetail/
│   │   ├── GetPhotoDetailRequest.cs
│   │   ├── GetPhotoDetailResponse.cs
│   │   └── GetPhotoDetailHandler.cs
│   ├── AddManualTag/
│   │   ├── AddManualTagRequest.cs
│   │   └── AddManualTagHandler.cs
│   ├── RemoveTag/
│   │   └── RemoveTagHandler.cs
│   ├── GetAllTags/
│   │   └── GetAllTagsHandler.cs
│   └── TriggerSync/
│       └── TriggerSyncHandler.cs
├── Jobs/
│   ├── SyncOneDrivePhotosJob.cs
│   └── IndexPhotoJob.cs

backend/src/Adapters/Anela.Heblo.Adapters.AzureAiVision/
├── AzureAiVisionService.cs
├── MockAzureAiVisionService.cs
└── AzureAiVisionModule.cs

backend/src/Anela.Heblo.Persistence/PhotoBank/
├── PhotoAssetConfiguration.cs
├── PhotoTagConfiguration.cs
└── PhotoAssetRepository.cs

backend/src/Anela.Heblo.API/Controllers/PhotoBankController.cs
backend/src/Anela.Heblo.API/MCP/Tools/PhotoBankTools.cs

frontend/src/pages/PhotoBank/
├── PhotoBankPage.tsx
├── components/
│   ├── PhotoSearchBar.tsx
│   ├── PhotoGrid.tsx
│   ├── PhotoDetailPanel.tsx
│   └── TagFilterBar.tsx
├── hooks/
│   ├── usePhotoSearch.ts
│   ├── usePhotoDetail.ts
│   └── usePhotoTags.ts
```

## Verification

### Backend Tests
- Unit tests for `SearchPhotosHandler` (filter composition, pagination)
- Unit tests for `IndexPhotoJob` (tag extraction, OCR text handling, error cases)
- Integration test for `PhotoAssetRepository` (pgvector storage, tag queries)
- MCP tool tests (parameter mapping, JSON serialization)

### Frontend Tests
- Component tests for `PhotoGrid`, `PhotoSearchBar`, `PhotoDetailPanel`
- Hook tests for `usePhotoSearch` with mock API responses

### E2E Verification
- Manually upload test photos to configured OneDrive folder
- Trigger sync via admin endpoint
- Verify photos appear in Photo Bank page with AI-generated tags
- Verify OCR text extraction on photos with visible product labels
- Verify tag-based search returns correct results
- Verify thumbnail loading and "Open in OneDrive" link

### Build Validation
- `dotnet build` — backend compiles
- `dotnet format` — formatting passes
- `npm run build` — frontend compiles
- `npm run lint` — linting passes
- `dotnet test` — all tests pass
