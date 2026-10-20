---
process: job-photobank-auto-tag
kind: job
module: photobank
summary: Sends photos that have not been AI-tagged yet (file path and name only) to Claude in batches and stores the returned tags — restricted to the existing tag vocabulary — as AI tags; runs nightly when enabled and on demand for selected photos.
owns:
  - backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs
  - backend/src/Anela.Heblo.Application/Features/Photobank/AutoTagOptions.cs
  - backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/RetagPhotos/**
  - backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/RetagPhotosBody.cs
  - backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankAutoTagRepository.cs
  - backend/src/Anela.Heblo.Domain/Features/Photobank/PhotoAutoTagCandidate.cs
  - backend/src/Anela.Heblo.Persistence/Photobank/PhotobankAutoTagRepository.cs
verified_at: "5e993f9e2"
related:
  - sync-photobank-index
  - flow-photobank-tag-rules
---

# Photobank AI auto-tagging

## Purpose
Saves marketing from tagging thousands of photos by hand: an LLM (Claude Haiku) proposes tags
for each photo, choosing only from tags that already exist in the photobank (Fotobanka). The
result appears in the gallery `/marketing/photobank` as tags with source **AI**, next to
Manual and Rule tags.

**The model never sees the image.** It gets only the photo id and the text
`FolderPath/FileName` (e.g. `Grafika_interní/PROFI_FOCENI/Produkty/krem_levandule_01.jpg`),
so it can tag only what the path and file name say. It is a text classifier, not image
recognition.

## Trigger
- Hangfire recurring job **`photobank-auto-tag`** ("Photobank Auto-Tag", category Content),
  cron `0 4 * * *` Europe/Prague (04:00 daily, one hour after `sync-photobank-index`).
  **Disabled by default** (`DefaultIsEnabled = false`, and a missing config row also counts as
  disabled — it costs LLM tokens). Enable it in the Recurring Jobs admin.
- On demand — "AI re-tag" (Write access): in the gallery drawer for one photo, or in the
  selection bar for selected photos → `POST /api/photobank/photos/auto-tag`
  `{ photoIds, clearExistingAiTags }` (max 5,000 ids). This enqueues a one-off Hangfire job and
  **runs even when the recurring job is disabled**. The UI always sends
  `clearExistingAiTags: false`.

## Data flow
1. **Vocabulary**: all rows of `public."PhotobankTags"` (names, lowercase). Tag counts are
   loaded too but unused.
2. **Candidates**:
   - Nightly: `public."Photos"` where `LastAutoTaggedAt IS NULL`, ordered by `Id`, pages of
     `BatchSize` (50) until `MaxPhotosPerRun` (5,000) or no more rows. A photo is a candidate when
     it is new, when the index saw its folder or file name change, or after a manual re-tag.
   - On demand: the requested photo ids that exist. Before enqueueing, their `LastAutoTaggedAt`
     is set to null, and with `clearExistingAiTags` their `Source = AI` tags are deleted.
3. **LLM call per batch** (shared `IChatClient` → Anthropic Messages API, model
   `Photobank:AutoTag:Model`):
   - System prompt: "photo tagging assistant for a cosmetics company", use ONLY the listed
     vocabulary (exact, diacritic-sensitive), reply as
     `{"results":[{"id":<photo_id>,"tags":["tag1","tag2"]}]}` without markdown.
   - User prompt: one line per photo `id=<Id> path=<FolderPath>/<FileName>`.
4. **Validate** each result: drop ids not in the sent batch; keep only tag names that exactly
   match the vocabulary (ordinal, case-sensitive); de-duplicate; take the first
   `MaxTagsPerPhoto` (5); skip pairs the photo already has (any source).
5. **Write**: insert `public."PhotoTags"` rows with `Source = AI`, save, then stamp
   `LastAutoTaggedAt = now (UTC)` on **every photo of the batch** (also those the model returned
   nothing for), and invalidate the tag-count cache `Photobank:Tags:WithCounts`.

## Logic & formulas
- The model can only reuse existing tags; it never creates a tag. An empty vocabulary means every
  photo is stamped with no tags.
- A photo counts as done once stamped, whether it got 0 or 5 tags; it is not retried until its
  path changes or someone re-tags it.
- AI tags are never removed automatically (not by the index, not by rule re-apply). Rule
  re-apply does not add a Rule tag where the same tag already exists as AI.
- An LLM call that throws (timeout, HTTP error) skips the batch **without** stamping it, so those
  photos stay pending for the next run.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Photobank:AutoTag:BatchSize` | 50 | Photos per LLM call |
| `Photobank:AutoTag:MaxPhotosPerRun` | 5000 | Upper bound of photos fetched per nightly run |
| `Photobank:AutoTag:Model` | `claude-haiku-4-5-20251001` | Anthropic model id sent per request |
| `Photobank:AutoTag:MaxTagsPerPhoto` | 5 | Cap of AI tags kept per photo |
| `KnowledgeBase:ChatMaxTokens` | 1024 | Output-token limit of the shared Anthropic chat client (the job sets no own limit) |
| `Anthropic:ApiKey` | secret | Anthropic API key |
| `Anthropic:HttpTimeoutSeconds` | 180 | HTTP timeout of the Anthropic client |
| `RecurringJobConfiguration` `photobank-auto-tag` | cron `0 4 * * *`, **disabled** | Schedule and on/off switch |

## Runtime facts
- Production Hangfire runs a single worker (`WorkerCount = 1`), so an on-demand re-tag waits
  behind any running job (Plaud polling delayed other jobs 12–15 min) — agent memory
  `gotcha_hangfire_single_worker_starves_print` — 2026-09-29.
- Whether the nightly job is enabled in production is not recorded in the repo.

## Known quirks
- **The nightly run skips about half of the pending photos per run** (bug). It pages with
  `offset += batch.Count`, but each successful batch is stamped, which removes it from the
  `LastAutoTaggedAt IS NULL` set; the next query then skips the next `offset` untagged photos.
  With 200 pending photos and batch 50 it tags ids 1–50 and 101–150 and stops. The skipped ones
  are picked up in later runs, so a backlog drains over several nights. Unit tests mock the
  repository and do not catch it. (Read from code, not observed.)
- **A truncated LLM reply loses the whole batch silently.** The job sets no `MaxOutputTokens`,
  so the shared client's limit (`KnowledgeBase:ChatMaxTokens`, 1024) applies; 50 results with up
  to 5 tags each can approach it. Unparseable JSON falls back to "no results" and the batch is
  still stamped as done, so those photos never get AI tags unless re-tagged. (Read from code,
  not observed.)
- **A reply listing the same photo id twice fails the run.** The "already has this tag" check reads the DB and cannot see the batch's unsaved inserts, so a repeated id with the same tag inserts a duplicate (`PhotoId`, `TagId`) and `SaveChanges` throws; unlike an LLM error this is not caught. (Read from code, not observed.)
- **Tags are matched case-sensitively** against the lowercase vocabulary; a model answer like
  `"Krém"` for tag `krém` is dropped.
- **On-demand re-tag ignores the enable switch** and the `MaxPhotosPerRun` cap (only the
  5,000-id request limit applies), so it can spend tokens while the nightly job is off.
- **Re-tagging does not remove AI tags in the UI flow** (`clearExistingAiTags` is always false),
  so re-tagging only adds tags; stale AI tags must be removed by hand.
- The LLM sees folder names, which often already drive Rule tags; AI tags therefore mostly
  duplicate path information rather than describe image content.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs` — metadata, paging loop, prompts, validation, stamping
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankAutoTagRepository.cs` — pending query, stamp/reset
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/RetagPhotos/RetagPhotosHandler.cs` — on-demand reset + enqueue
- `backend/src/Anela.Heblo.Application/Features/Photobank/AutoTagOptions.cs` — option defaults
- `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` — shared chat client and its token limit
- `backend/src/Anela.Heblo.Application/Shared/Json/JsonResponseParser.cs` — parse-or-empty fallback
