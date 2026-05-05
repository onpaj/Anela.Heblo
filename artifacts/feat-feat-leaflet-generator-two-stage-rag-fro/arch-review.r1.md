# Architecture Review: Leaflet Generator (Two-Stage RAG)

## Architectural Fit Assessment

The feature aligns cleanly with the existing Clean Architecture + Vertical Slice organization. It mirrors the `KnowledgeBase` feature in shape (Domain entities + Repository, Application services/jobs/handlers, EF configurations, Controller, MCP tool), so developers have a strong reference implementation a few directories away.

**Key findings from the existing codebase that reshape the spec:**

1. **`PlainTextExtractor` already exists** at `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentExtractors/PlainTextExtractor.cs` and already handles `text/*` and `application/markdown`. **Do not create a new one.** Reuse it (it's registered DI-wide as `IDocumentTextExtractor`). Spec FR-2 and the brief's file map are wrong on this point — formal spec amendment below.
2. **`react-markdown@^10.1.0` is already a dependency.** Open Question #1 resolved — reuse it. Do not add anything new.
3. **`DocumentChunker` reads from `KnowledgeBaseOptions.ChunkSize`** (currently 512). It cannot be shared as-is for the 800-word leaflet chunking requirement.
4. **The existing KB indexing pipeline summarizes each chunk before embedding** (HyDE-style). Leaflets are already polished prose — embed raw chunks directly. The leaflet ingestion path must NOT reuse `IIndexingStrategy`/`KnowledgeBaseDocIndexingStrategy`.
5. **`IRecurringJob` + `RecurringJobMetadata` + `IRecurringJobStatusChecker`** is the established Hangfire pattern. Follow it precisely (see `KnowledgeBaseIngestionJob`).
6. **The pgvector extension is already enabled** by the `AddKnowledgeBase` migration — the new migration only needs `CREATE TABLE` + `vector(1536)` column + HNSW index.

The main integration points are: shared `ApplicationDbContext`, shared `IOneDriveService`/`MockOneDriveService`, shared `IEmbeddingGenerator<string, Embedding<float>>` (Microsoft.Extensions.AI), shared `IChatClient`, shared `IRecurringJobStatusChecker`, shared MediatR pipeline, and the existing global `ValidationBehavior`.

## Proposed Architecture

### Component Overview

```
                    ┌────────────────────────────────────────────┐
                    │  UI: /leaflet-generator                    │
                    │  (LeafletGeneratorPage / Form / Result)    │
                    └──────────────────┬─────────────────────────┘
                                       │ generated TS client
                                       ▼
        ┌──────────────────────────────────────────────────────────┐
        │  REST: POST /api/leaflet/generate (LeafletController)    │
        │  MCP : GenerateLeaflet (LeafletTools)                    │
        └──────────────────┬───────────────────────────────────────┘
                           │ MediatR
                           ▼
            ┌──────────────────────────────────────┐
            │   GenerateLeafletHandler             │
            │   ┌────────────┐    ┌────────────┐   │
            │   │  Stage 1   │ →  │  Stage 2   │   │
            │   │  facts     │    │  style     │   │
            │   └─────┬──────┘    └─────┬──────┘   │
            └─────────┼─────────────────┼──────────┘
                      │                 │
            embed │   ▼   chunks       embed │   ▼   chunks      LLM
        ┌─────────┴─────────┐    ┌──────┴──────────┐    ┌─────────────┐
        │ IEmbeddingGenerator│   │ IEmbeddingGen.  │    │  IChatClient│
        └─────────┬─────────┘    └──────┬──────────┘    └──────┬──────┘
                  ▼                     ▼                      │
        ┌───────────────────┐ ┌──────────────────┐             │
        │ IKnowledgeBase    │ │ ILeafletRepo     │             │
        │ Repository (KB    │ │ (Leaflet store)  │             │
        │ chunks via cosine)│ │ via cosine       │             │
        └───────────────────┘ └──────────────────┘             │
                                                               │
                              ┌────────────────────────────────┘
                              ▼
                       polished markdown

        ┌─────────────────────────────────────────────────────┐
        │ Hangfire: LeafletIngestionJob (every 15 min)        │
        │   IOneDriveService → /Leaflets/Inbox                │
        │     → IndexLeafletHandler (MediatR)                 │
        │       → PlainTextExtractor | PdfTextExtractor       │
        │       → LeafletChunker (800/80)                     │
        │       → IEmbeddingGenerator                         │
        │       → ILeafletRepository (raw Npgsql for vec ops) │
        │     → MoveToArchivedAsync (/Leaflets/Archived)      │
        └─────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Separate vector store, separate repository — no shared abstraction over KB and Leaflet

**Options considered:**
- **A.** Extend the existing `DocumentType` enum with `Leaflet = 2` and reuse `KnowledgeBaseDocuments`/`KnowledgeBaseChunks`, filtering by `DocumentType` in retrieval.
- **B.** Introduce a generic `IVectorStore<TDocument, TChunk>` abstraction and have both KB and Leaflet implement it.
- **C.** Independent `LeafletDocuments`/`LeafletChunks` tables and a dedicated `ILeafletRepository` whose surface is intentionally narrower than `IKnowledgeBaseRepository`.

**Chosen approach:** **C.**

**Rationale:** The brief's HNSW post-filter argument is correct: HNSW recall degrades sharply when filtering out a sub-population after the ANN search, especially when the filtered population is the minority (leaflets will be far smaller than chat conversations). A generic abstraction (B) is YAGNI — the two repositories have ~30% method overlap and the KB repo has KB-specific concerns (question logs, feedback, summaries, content-type filtering). Keeping them separate produces ~150 lines of intentional duplication that pays for itself in clarity and isolation. Approach C also keeps the leaflet store independently re-indexable if we change the embedding model later without touching KB.

#### Decision 2: Do not reuse `IIndexingStrategy` — leaflets embed raw chunks, not summaries

**Options considered:**
- **A.** Add a `LeafletIndexingStrategy : IIndexingStrategy` and extend `DocumentType` with `Leaflet`.
- **B.** Bypass the strategy abstraction. Index leaflets via a small dedicated `LeafletIndexingService` (chunk → embed → persist).

**Chosen approach:** **B.**

**Rationale:** `IIndexingStrategy` is parameterized over `DocumentType` and writes to `IKnowledgeBaseRepository`. Coupling leaflets to it forces either a reverse-engineered abstraction over both repositories (Decision 1 says no) or a leaky polymorphism. Leaflets also do **not** want chunk summarization: the corpus is already in the target voice — embedding the summary would erase the very style signal we want to retrieve. Keep the leaflet pipeline a flat ~30-line service.

#### Decision 3: Dedicated `LeafletChunker` reading from `LeafletOptions`

**Options considered:**
- **A.** Refactor `DocumentChunker` to take `(int chunkSize, int overlap)` arguments and inject the right options into call sites.
- **B.** Create a separate `LeafletChunker` with its own `IOptions<LeafletOptions>` dependency.

**Chosen approach:** **B.**

**Rationale:** Refactoring the existing chunker (A) ripples into KB indexing strategies and tests for no benefit. The chunking logic is ~10 lines; duplicating it as `LeafletChunker` is cheaper than the cross-feature refactor and keeps each chunker bound to its feature's options.

#### Decision 4: Stage 1 embeds the topic directly — no query expansion

**Options considered:**
- **A.** Reuse `SearchDocumentsHandler` (KB query expansion + cosine search).
- **B.** Embed the raw topic directly via `IEmbeddingGenerator` and call `IKnowledgeBaseRepository.SearchSimilarAsync` from `GenerateLeafletHandler`.

**Chosen approach:** **B.**

**Rationale:** KB query expansion (HyDE) rewrites a customer's natural-language question into the structured "Problém / Kontext / …" template that matches stored chunks. A leaflet topic ("Bisabolol pro citlivou pleť") does not benefit from that rewrite — it's already a topical phrase. (A) adds an extra LLM call to the critical path with no recall benefit and breaks the P95 target. Keep it direct, log token usage and similarity scores so we can revisit if recall is poor.

#### Decision 5: Two-stage prompts owned in `LeafletOptions`, not hardcoded

**Options considered:**
- **A.** Hardcode prompts in `GenerateLeafletHandler`.
- **B.** Move prompts to `LeafletOptions` (mirrors `KnowledgeBaseOptions.AskQuestionSystemPrompt`), with `{topic}`, `{audience}`, `{length}`, `{kbContext}`, `{leafletContext}` placeholders.

**Chosen approach:** **B.**

**Rationale:** Prompt iteration is a marketing/ops concern. Operators tune via `appsettings`; deployments do not require a code change. The KB feature already proves this pattern works.

#### Decision 6: Cold-start fallback — ship the "neutral marketing voice" path

**Options considered:**
- **A.** Refuse Stage 2 with `503` until N leaflets are ingested.
- **B.** Stage 2 runs against an empty leaflet retrieval; the prompt instructs Claude to use a neutral marketing register; log `Warning`.

**Chosen approach:** **B.** (Resolves Open Question #5.)

**Rationale:** Before the OneDrive Inbox is seeded, the feature must still demo. Returning `503` in dev/stg surprises users and complicates the rollout. Logging at `Warning` plus an OpenAPI-level note that "tone improves as the leaflet corpus grows" is sufficient.

#### Decision 7: Audience and length as strongly-typed enums end-to-end

**Options considered:**
- **A.** String fields validated with `[RegularExpression]`.
- **B.** C# enums (`AudienceType`, `LeafletLength`) on the request DTO; OpenAPI generator emits TypeScript string-literal unions.

**Chosen approach:** **B.**

**Rationale:** Typed enums give compile-time guarantees on both sides and surface as proper enum schemas in OpenAPI. Project rule on classes-not-records still applies to the enclosing request/response DTOs.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Domain/Features/Leaflet/
  LeafletDocument.cs               // class, identity entity
  LeafletChunk.cs                  // class, identity entity
  ILeafletRepository.cs

backend/src/Anela.Heblo.Application/Features/Leaflet/
  LeafletModule.cs                 // AddLeafletModule extension
  LeafletOptions.cs                // bound to "Leaflet" section
  Services/
    LeafletChunker.cs              // 800/80 word chunking, reads LeafletOptions
    LeafletIndexingService.cs      // chunk → embed → persist (no summary)
    ILeafletIndexingService.cs
  Infrastructure/Jobs/
    LeafletIngestionJob.cs         // IRecurringJob, mirrors KnowledgeBaseIngestionJob
  UseCases/
    IndexLeaflet/
      IndexLeafletRequest.cs       // class, [Required] / [JsonPropertyName]
      IndexLeafletResponse.cs      // class
      IndexLeafletHandler.cs       // hash-based dedup, then ILeafletIndexingService
    GenerateLeaflet/
      GenerateLeafletRequest.cs    // class with AudienceType, LeafletLength enums
      GenerateLeafletResponse.cs   // class { Content: string }
      GenerateLeafletHandler.cs    // two-stage orchestration
      AudienceType.cs              // enum: EndConsumer, B2B
      LeafletLength.cs             // enum: Short, Medium, Long

backend/src/Anela.Heblo.Persistence/Features/Leaflet/
  LeafletRepository.cs             // EF + raw Npgsql for vector ops
  LeafletDocumentConfiguration.cs
  LeafletChunkConfiguration.cs     // ignores Embedding (raw Npgsql writes)

backend/src/Anela.Heblo.Persistence/
  ApplicationDbContext.cs          // add DbSet<LeafletDocument>, DbSet<LeafletChunk>
  PersistenceModule.cs             // register ILeafletRepository

backend/src/Anela.Heblo.Persistence/Migrations/
  <ts>_AddLeafletStore.cs          // tables, vector(1536) column, HNSW index, FK cascade

backend/src/Anela.Heblo.API/
  Controllers/LeafletController.cs
  MCP/Tools/LeafletTools.cs
  MCP/McpModule.cs                 // .WithTools<LeafletTools>()
  Program.cs                       // services.AddLeafletModule(configuration)

appsettings.json / appsettings.{Env}.json
  // "Leaflet": { ... }

frontend/src/features/leaflet-generator/
  LeafletGeneratorPage.tsx         // route container, uses generated client
  LeafletForm.tsx                  // topic input (200-char counter), audience, length
  LeafletResult.tsx                // ReactMarkdown, copy button, regenerate

frontend/src/App.tsx               // <Route path="/leaflet-generator" .../>
frontend/src/components/Layout/Sidebar.tsx
                                   // add 'leaflet-generator' under a 'Marketing' or
                                   //  'Knowledgebase' section

backend/test/Anela.Heblo.Tests/Features/Leaflet/
  Services/LeafletChunkerTests.cs
  UseCases/IndexLeafletHandlerTests.cs
  UseCases/GenerateLeafletHandlerTests.cs
  Infrastructure/LeafletIngestionJobTests.cs
  MCP/LeafletToolsTests.cs
```

### Interfaces and Contracts

**Domain entities** — plain classes with init-friendly properties (mirror `KnowledgeBaseChunk`/`KnowledgeBaseDocument`):

```csharp
namespace Anela.Heblo.Domain.Features.Leaflet;

public class LeafletDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty; // SHA-256 hex (64)
    public DateTime IngestedAt { get; set; }
    public int WordCount { get; set; }
    public ICollection<LeafletChunk> Chunks { get; set; } = new List<LeafletChunk>();
}

public class LeafletChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public LeafletDocument Document { get; set; } = null!;
}
```

**Repository contract** — narrower than KB. No question logs, no feedback, no paging today:

```csharp
public interface ILeafletRepository
{
    Task AddDocumentAsync(LeafletDocument document, CancellationToken ct = default);
    Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default);
    Task<LeafletDocument?> GetByHashAsync(string contentHash, CancellationToken ct = default);
    Task<LeafletDocument?> GetBySourcePathAsync(string sourcePath, CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid id, CancellationToken ct = default);
    Task<List<(LeafletChunk Chunk, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding, int topK, CancellationToken ct = default);
    Task UpdateSourcePathAsync(Guid documentId, string newPath, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

**REST DTOs (classes, not records — per project rule):**

```csharp
public class GenerateLeafletRequest : IRequest<GenerateLeafletResponse>
{
    [Required, MinLength(1), MaxLength(200)]
    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("audience")]
    public AudienceType Audience { get; set; }

    [Required]
    [JsonPropertyName("length")]
    public LeafletLength Length { get; set; }
}

public class GenerateLeafletResponse : BaseResponse
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public enum AudienceType { EndConsumer = 0, B2B = 1 }
public enum LeafletLength { Short = 0, Medium = 1, Long = 2 }
```

**`LeafletOptions` shape:**

```csharp
public class LeafletOptions
{
    public const string SectionName = "Leaflet";

    // OneDrive (mirror OneDriveFolderMapping fields — single mapping is sufficient today)
    public string DriveId { get; set; } = string.Empty;
    public string InboxPath { get; set; } = "/Leaflets/Inbox";
    public string ArchivedPath { get; set; } = "/Leaflets/Archived";

    // Chunking
    public int ChunkSizeWords { get; set; } = 800;
    public int ChunkOverlapWords { get; set; } = 80;

    // Retrieval
    public int KbTopK { get; set; } = 8;
    public int LeafletTopK { get; set; } = 5;
    public double MinSimilarityScore { get; set; } = 0.55;

    // LLM
    public string ChatModel { get; set; } = "claude-sonnet-4-6";
    public int ChatMaxTokens { get; set; } = 2048;

    // Embedding (must match column width 1536)
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    // Job
    public string IngestionCronExpression { get; set; } = "*/15 * * * *";

    // Prompts (placeholders: {topic}, {audience}, {length}, {kbContext}, {leafletContext}, {coldStart})
    public string Stage1SystemPrompt { get; set; } = /* see Decision 5 */;
    public string Stage2SystemPrompt { get; set; } = /* see Decision 5 */;

    // Length targets (used in prompt only — soft enforcement)
    public int ShortWordTarget { get; set; } = 200;
    public int MediumWordTarget { get; set; } = 400;
    public int LongWordTarget { get; set; } = 700;
}
```

Bind via `services.AddOptions<LeafletOptions>().Bind(config.GetSection(LeafletOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();`.

### Data Flow

**Generate (happy path):**
1. Controller receives `GenerateLeafletRequest`. Global `ValidationBehavior` validates DTO before handler runs.
2. `GenerateLeafletHandler` correlation-ID-scopes the call (Activity / structured logging).
3. **Stage 1**:
   - `IEmbeddingGenerator.GenerateAsync([request.Topic])` → `topicEmbedding`.
   - `IKnowledgeBaseRepository.SearchSimilarAsync(topicEmbedding, options.KbTopK)` → KB chunks.
   - Filter by `MinSimilarityScore`; if zero remain, log `Warning`, continue with empty `kbContext`.
   - Build messages: system = `Stage1SystemPrompt` rendered with placeholders; user = topic.
   - `IChatClient.GetResponseAsync(...)` with `ChatOptions { ModelId = options.ChatModel, MaxOutputTokens = options.ChatMaxTokens }` → outline.
4. **Stage 2**:
   - Reuse `topicEmbedding` (no second embedding call — saves one round trip).
   - `ILeafletRepository.SearchSimilarAsync(topicEmbedding, options.LeafletTopK)` → leaflet excerpts.
   - If empty: set `coldStart = "true"` placeholder; log `Warning` once per request.
   - Build messages: system = `Stage2SystemPrompt` rendered with `{coldStart}`, `{leafletContext}`, length target, audience; user = Stage 1 outline.
   - `IChatClient.GetResponseAsync(...)` → final markdown.
5. Return `GenerateLeafletResponse { Content = response.Text }`.
6. **Failures:**
   - Validation → 400 `ProblemDetails`.
   - Transient embedding/LLM/DB → retry once, then 502 `ProblemDetails` with generic message; structured log at `Error` with full exception.
   - Stage 1 failure short-circuits Stage 2.

**Ingestion (per file):**
1. `LeafletIngestionJob.ExecuteAsync` polls `_oneDrive.ListInboxFilesAsync(driveId, inbox)`.
2. For each file: download bytes → MediatR `IndexLeafletRequest`.
3. `IndexLeafletHandler`:
   - Compute SHA-256 → `GetByHashAsync` (skip if duplicate; emit `WasDuplicate=true` and still archive the source so it doesn't keep getting re-processed — same as KB).
   - `GetBySourcePathAsync` (if hash mismatch on same path: delete old document, re-index).
   - Resolve `IDocumentTextExtractor` from `IEnumerable<IDocumentTextExtractor>` by `CanHandle(contentType)` (existing `PdfTextExtractor` and `PlainTextExtractor` are already DI-registered).
   - Throw `NotSupportedException` if no match → job logs `Warning`, file stays in Inbox.
   - Persist `LeafletDocument`; call `LeafletIndexingService.IndexAsync(text, document, ct)` which chunks (`LeafletChunker`), embeds, calls `repo.AddChunksAsync`, sets `document.WordCount`.
   - One transaction per document (`SaveChangesAsync` after success).
4. On success → `IOneDriveService.MoveToArchivedAsync(...)`.
5. On failure → leave file in Inbox, log `Error`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Stage 1 retrieves zero KB chunks above similarity threshold → Stage 2 polishes a thin outline. | High | Log retrieval count + below-threshold count per stage. Surface a 422 with a marketing-friendly message ("Knowledge Base does not yet cover this topic; try a broader phrasing") when both `chunks.Count == 0` AND `coldStart` is also true. Do **not** fail when only one side is sparse — that's expected during ramp-up. |
| Two LLM calls + two retrievals push P95 over 12 s under load. | Medium | (1) Reuse Stage 1's topic embedding for Stage 2 — saves ~150 ms. (2) Cap `ChatMaxTokens` at 2048 (long target ~700 words ≈ 1100 tokens with overhead). (3) Run Stage 1 KB embedding and any cache-warmup in parallel (`Task.WhenAll`) where feasible. (4) Add OTel/Activity spans around each external call so regressions are debuggable. |
| Prompt injection via `topic` (e.g. "Ignore prior instructions and …"). | Medium | Topic is bound only to the user message; system message contains all instructions. 200-char cap blunts payload size. Treat `{topic}` placeholder substitution as data — never use user-controlled string in system message. Validate at boundary; reject control characters that aren't whitespace. |
| `DocumentChunker` reuse temptation for leaflets (chunk size mismatch). | Medium | Code reviewer must reject any PR that injects `IOptions<KnowledgeBaseOptions>` into leaflet code. `LeafletChunker` is the only chunker for this feature. |
| Cold-start (empty leaflet store) produces output indistinguishable from raw Stage 1. | Low | Acceptable for ship (Decision 6). Track via `Warning` log; ops dashboard alert when "cold-start" log fires more than N times/day. |
| OneDrive `MockOneDriveService` hides real SharePoint failures in dev/stg. | Low | Mirror KB's existing detection: if `kbOptions.OneDriveFolderMappings` has any drive ID and auth is real, register `GraphOneDriveService`; else mock. Apply identical condition to Leaflet drive ID. Emit a startup log line declaring which implementation is bound. |
| `IRecurringJobStatusChecker` job name collision. | Low | Use `"leaflet-ingestion"` as `Metadata.JobName` — does not collide with `"knowledge-base-ingestion"`. |
| HNSW index recall on small corpus (<200 chunks) is poor due to graph sparsity. | Low | pgvector's HNSW degrades gracefully to near-exhaustive when M/efConstruction defaults match KB (m=16, ef_construction=64). Acceptable. |
| EF Core property mapping for the `Embedding` column. | Medium | Mirror KB exactly: `LeafletChunkConfiguration` calls `builder.Ignore(x => x.Embedding)`; the migration adds the column via raw SQL; `LeafletRepository.AddChunksAsync` uses `NpgsqlCommand` with `Pgvector.Vector`. Do not attempt EF mapping. |
| OpenAPI client regen drift if enums are renamed. | Low | Place `AudienceType` and `LeafletLength` in dedicated files in `UseCases/GenerateLeaflet/`. Treat them as part of the public contract — renames require a deliberate version bump. |

## Specification Amendments

1. **FR-2 is wrong** — `PlainTextExtractor` already exists at `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentExtractors/PlainTextExtractor.cs` and is DI-registered as `IDocumentTextExtractor`. **Remove** the "create new `PlainTextExtractor`" task from the brief's File Map and from FR-2. Add a one-line note to FR-3 that the ingestion job resolves both `PdfTextExtractor` and the existing `PlainTextExtractor` via the `IEnumerable<IDocumentTextExtractor>` injection point — no DI changes required.
2. **FR-5, Stage 2 embedding** (Open Question #3): explicitly decide **reuse Stage 1 topic embedding** in Stage 2. Saves one embedding call and is a safe default. No second embedding generated. Add this to acceptance criteria.
3. **FR-1, schema:** include `WordCount` on both `LeafletDocument` (sum) and `LeafletChunk` (per-chunk) — already in the spec's data model but not in FR-1's acceptance criteria. Add it to FR-1 explicitly.
4. **FR-1, dedup:** clarify "unique constraint on `ContentHash`" — to mirror KB exactly, do **not** add a DB unique constraint on `ContentHash` (KB doesn't either). Use a single non-unique index on `ContentHash` for `GetByHashAsync` performance. Dedup is enforced at handler level.
5. **FR-1, source-path index:** add a unique index on `LeafletDocuments.SourcePath` (mirrors KB) so `GetBySourcePathAsync` is O(log n) and re-indexing on path collision works.
6. **FR-3, archive-on-duplicate:** make explicit that **duplicates are still archived** (so they don't keep being polled). Spec currently says "duplicate files are skipped without re-embedding" — clarify the file is moved to `/Leaflets/Archived` regardless. Mirrors KB job behavior.
7. **FR-6, validation behavior:** the project already has a global `ValidationBehavior<TRequest, TResponse>` MediatR pipeline that validates DataAnnotations. **No need to register a feature-scoped validator.** Just put `[Required]`, `[MaxLength]` on the DTO.
8. **FR-7, ProblemDetails on MCP:** `LeafletTools` should wrap the MediatR call in try/catch and rethrow as `McpException` with a generic message — mirroring `KnowledgeBaseTools.AskKnowledgeBase`. Do **not** leak internal exception details to MCP clients.
9. **FR-8, sidebar placement** (Open Question #6): place the link as `/leaflet-generator` under a **new** "Marketing" sidebar section (next to a future "Marketing Calendar"). Czech labels: section `"Marketing"`, item `"Generátor letáků"`. Audience options: `"Koncový zákazník"` / `"B2B"`. Length labels: `"Krátký (~200 slov)"`, `"Střední (~400 slov)"`, `"Dlouhý (~700 slov)"`.
10. **FR-9, validate-on-start:** wire `ValidateDataAnnotations().ValidateOnStart()` and add `[Required]` on `DriveId`, `InboxPath`, `ArchivedPath`, `EmbeddingModel`, `ChatModel`. Missing values must fail at app startup, not at first request.
11. **Open Question #1 (markdown renderer):** **resolved** — `react-markdown@^10.1.0` is already in `frontend/package.json`. Reuse it. No new dependency.
12. **Open Question #7 (cron):** **`*/15 * * * *`** — same cadence as `KnowledgeBaseIngestionJob`, no ops impact. Configurable via `LeafletOptions.IngestionCronExpression`.

## Prerequisites

Before any implementation work starts:

1. **OneDrive folders exist.** Create `/Leaflets/Inbox` and `/Leaflets/Archived` on the same SharePoint drive used by KB. Capture the `DriveId` (same Graph navigation as KB: `/v1.0/sites/{siteId}/drives`).
2. **Configuration in all environments.** Add the `Leaflet` section to `appsettings.Development.json`, `appsettings.Staging.json`, `appsettings.Production.json` (or the equivalent Azure App Service settings). Required: `DriveId`, `InboxPath`, `ArchivedPath`, `ChatModel`, `EmbeddingModel`. The remaining options have safe defaults.
3. **pgvector verification.** The extension is already enabled in all environments (KB depends on it). The new migration must NOT re-add `CREATE EXTENSION` — only `CREATE TABLE` + vector column + HNSW index.
4. **Manual EF migration plan.** Per project notes, "database migrations are manual." The implementation PR must include `<timestamp>_AddLeafletStore.cs` plus a runbook entry: `dotnet ef database update --project backend/src/Anela.Heblo.Persistence` against each environment's connection string at deploy time, documented in the PR description.
5. **Seed corpus for staging.** Marketing must drop at least ~10 historical leaflets into `/Leaflets/Inbox` on staging before Stage 2 demo testing — otherwise reviewers will see only the cold-start fallback and assume the feature is broken.
6. **Embedding model alignment.** Confirm `text-embedding-3-small` (1536 dim) is the deployed embedding model. KB currently configures `EmbeddingModel = "text-embedding-3-large"` but with `EmbeddingDimensions = 1536` — verify the OpenAI adapter is actually issuing `text-embedding-3-small` or that `-large` is being requested with `dimensions=1536`. **Whichever the KB store uses, the Leaflet store must use the same** so a single embedding call serves both Stage 1 and Stage 2 retrieval (Decision 4 / Amendment 2 depend on this).
7. **MCP tool registration.** Add `.WithTools<LeafletTools>()` to `McpModule.AddMcpServices` — must land in the same PR as `LeafletTools` so the OpenAPI/MCP manifest is consistent.
8. **Sidebar role-gating decision.** Confirm whether `/leaflet-generator` should be open to all authenticated users (assumed) or gated behind a new role (`marketing` or `leaflet_generator`). Default to "all authenticated" unless marketing/ops object — no role gate adds simplicity and matches KB's `/knowledge-base` ungated default.