### task: domain-entities-and-repository-interface

**Goal:** Define the `LeafletDocument` and `LeafletChunk` domain entities plus the `ILeafletRepository` contract that the Application and Persistence layers will depend on.

**Context:**
The Leaflet feature stores ingested marketing leaflets as chunked, embedded text in a dedicated vector store separate from the existing KnowledgeBase store. This task creates the Domain layer artifacts. Per project rule, classes are used (not records) for entities. Vectors are 1536-dimensional (matches embedding model `text-embedding-3-small`). The repository interface is intentionally narrower than `IKnowledgeBaseRepository` — no question logs, no feedback, no paging today.

Required entity shapes:

```csharp
public class LeafletDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty; // SHA-256 hex (64 chars)
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

Repository contract:

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

**Files to create/modify:**
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletDocument.cs` — document entity
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletChunk.cs` — chunk entity
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs` — repository abstraction

**Implementation steps:**
1. Create folder `backend/src/Anela.Heblo.Domain/Features/Leaflet/` if it does not exist.
2. Create `LeafletDocument.cs` in namespace `Anela.Heblo.Domain.Features.Leaflet` with the class shape shown above. No methods, no constructors.
3. Create `LeafletChunk.cs` in the same namespace with the class shape shown above. No methods, no constructors.
4. Create `ILeafletRepository.cs` in the same namespace with the interface shown above. The `SearchSimilarAsync` return type is a tuple list — use `System.Collections.Generic.List<(LeafletChunk Chunk, double Score)>`.
5. Use Allman brace style, 4-space indentation; keep nullable annotations enabled.

**Tests to write:**
None — these are pure data contracts with no behaviour. Compilation of dependent layers will exercise them.

**Acceptance criteria:**
- `dotnet build` of the Domain project succeeds.
- The three files exist with the exact namespaces and shapes shown.
- No reference is added from Domain to Application, Persistence, or API.

---

### task: leaflet-options-configuration-class

**Goal:** Define `LeafletOptions`, the strongly-typed configuration class bound to the `Leaflet` section of `appsettings.json`, including default prompts, retrieval thresholds, chunking parameters, and OneDrive paths.

**Context:**
Operators tune the leaflet feature via `appsettings`; deployments do not require a code change. Required fields are validated at startup with `ValidateDataAnnotations().ValidateOnStart()` so missing values fail fast (FR-9 amendment). Embedding model must produce 1536-dim vectors to match the database column. Stage 1 and Stage 2 prompts contain placeholders `{topic}`, `{audience}`, `{length}`, `{kbContext}`, `{leafletContext}`, `{coldStart}` that the handler will substitute. Audience options are `EndConsumer`/`B2B`; length options are `Short`/`Medium`/`Long` with word targets 200/400/700.

```csharp
public class LeafletOptions
{
    public const string SectionName = "Leaflet";

    [Required] public string DriveId { get; set; } = string.Empty;
    [Required] public string InboxPath { get; set; } = "/Leaflets/Inbox";
    [Required] public string ArchivedPath { get; set; } = "/Leaflets/Archived";

    public int ChunkSizeWords { get; set; } = 800;
    public int ChunkOverlapWords { get; set; } = 80;

    public int KbTopK { get; set; } = 8;
    public int LeafletTopK { get; set; } = 5;
    public double MinSimilarityScore { get; set; } = 0.55;

    [Required] public string ChatModel { get; set; } = "claude-sonnet-4-6";
    public int ChatMaxTokens { get; set; } = 2048;

    [Required] public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    public string IngestionCronExpression { get; set; } = "*/15 * * * *";

    public string Stage1SystemPrompt { get; set; } = /* see step 3 */;
    public string Stage2SystemPrompt { get; set; } = /* see step 3 */;

    public int ShortWordTarget { get; set; } = 200;
    public int MediumWordTarget { get; set; } = 400;
    public int LongWordTarget { get; set; } = 700;
}
```

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletOptions.cs` — options class

**Implementation steps:**
1. Create `backend/src/Anela.Heblo.Application/Features/Leaflet/` folder.
2. Create `LeafletOptions.cs` in namespace `Anela.Heblo.Application.Features.Leaflet`. Class shape as shown above with `using System.ComponentModel.DataAnnotations;`.
3. Set default `Stage1SystemPrompt` to:
   ```
   "You extract factual ingredient and benefit information for cosmetics leaflets. " +
   "Topic: {topic}. Audience: {audience}. Target length: {length}. " +
   "Below is information from the company knowledge base. Build a structured outline " +
   "(headings, bullet points) covering ingredients, claimed benefits, target use cases, " +
   "and regulatory cautions. Do not invent facts. Use only the provided context. " +
   "If the context is empty, return a minimal outline based on common cosmetic-industry " +
   "knowledge for that topic.\n\nKnowledge Base context:\n{kbContext}"
   ```
4. Set default `Stage2SystemPrompt` to:
   ```
   "You are a Czech marketing copywriter for Anela cosmetics. Rewrite the provided " +
   "factual outline into a polished marketing leaflet. Audience: {audience}. " +
   "Target length: {length} words. Output Czech-language Markdown only. " +
   "Use the leaflet excerpts below as a tone/style reference — match their voice, " +
   "register, and rhythm.\nIs cold start (no leaflet examples available): {coldStart}. " +
   "If cold start is true, use a neutral professional marketing register.\n\n" +
   "Leaflet style references:\n{leafletContext}"
   ```
5. Use 4-space indentation, Allman braces.

**Tests to write:**
None — class is pure data. Validation is exercised via the registration test in the module wiring task.

**Acceptance criteria:**
- File compiles in the Application project.
- `LeafletOptions.SectionName == "Leaflet"`.
- `[Required]` annotations on `DriveId`, `InboxPath`, `ArchivedPath`, `ChatModel`, `EmbeddingModel`.

---

### task: audience-and-length-enums

**Goal:** Add the `AudienceType` and `LeafletLength` enums used by the request DTO and handler so that the OpenAPI client emits typed unions on the frontend.

**Context:**
Per architecture Decision 7, audience and length are strongly-typed enums end-to-end. They are part of the public contract — renames require deliberate version bumps. They live next to the use case they belong to.

```csharp
public enum AudienceType { EndConsumer = 0, B2B = 1 }
public enum LeafletLength { Short = 0, Medium = 1, Long = 2 }
```

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/AudienceType.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/LeafletLength.cs`

**Implementation steps:**
1. Create folder `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/`.
2. Create `AudienceType.cs` in namespace `Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet` with the enum shown above.
3. Create `LeafletLength.cs` in the same namespace with the enum shown above.
4. Each file holds exactly one enum.

**Tests to write:**
None — enums are pure data.

**Acceptance criteria:**
- Both files compile.
- Values match exactly (`EndConsumer = 0`, `B2B = 1`, `Short = 0`, `Medium = 1`, `Long = 2`).

---

### task: leaflet-store-database-migration

**Goal:** Add an EF Core migration that creates `LeafletDocuments` and `LeafletChunks` tables with a `vector(1536)` embedding column, an HNSW index, supporting indexes, and a cascade FK.

**Context:**
The `vector` extension is already enabled in all environments by the existing KnowledgeBase migration — do NOT re-add `CREATE EXTENSION`. The embedding column cannot be mapped by EF, so the column is added via raw SQL and the EF configuration ignores the property. HNSW index parameters: `m=16, ef_construction=64` (matches KB defaults). Required indexes per spec amendments:
- Non-unique index on `LeafletDocuments.ContentHash` (dedup is enforced at handler level, not DB level).
- Unique index on `LeafletDocuments.SourcePath` (mirrors KB; supports re-index on path collision).
- HNSW index on `LeafletChunks.Embedding` using `vector_cosine_ops`.
- Cascade FK from `LeafletChunks.DocumentId` to `LeafletDocuments.Id`.

Database migrations are manual — the deploy runbook will run `dotnet ef database update`.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddLeafletStore.cs` — new migration
- `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` — auto-updated by EF tooling

**Implementation steps:**
1. Run `dotnet ef migrations add AddLeafletStore --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API` after the EF configurations from the next task are in place. (Note: this migration depends on `ApplicationDbContext` having `DbSet<LeafletDocument>` and `DbSet<LeafletChunk>` — order this task immediately after the configuration task; if EF tooling complains, run that task's changes first.)
2. Open the generated migration file. Replace the auto-generated `Up` method with a hand-edited version that creates the tables with these columns:
   - `LeafletDocuments`: `Id uuid PK`, `Filename text NOT NULL`, `SourcePath text NOT NULL`, `ContentType text NOT NULL`, `ContentHash text NOT NULL`, `IngestedAt timestamp with time zone NOT NULL`, `WordCount int NOT NULL`.
   - `LeafletChunks`: `Id uuid PK`, `DocumentId uuid NOT NULL FK→LeafletDocuments(Id) ON DELETE CASCADE`, `ChunkIndex int NOT NULL`, `Content text NOT NULL`, `WordCount int NOT NULL`. **Do not** add `Embedding` via `migrationBuilder.AddColumn` — let EF skip it.
3. After the `migrationBuilder.CreateTable` calls, add raw SQL to add the vector column and HNSW index:
   ```csharp
   migrationBuilder.Sql(@"ALTER TABLE ""LeafletChunks"" ADD COLUMN ""Embedding"" vector(1536) NOT NULL;");
   migrationBuilder.Sql(@"CREATE INDEX IX_LeafletChunks_Embedding_HNSW ON ""LeafletChunks"" USING hnsw (""Embedding"" vector_cosine_ops) WITH (m = 16, ef_construction = 64);");
   ```
4. Add explicit indexes:
   ```csharp
   migrationBuilder.CreateIndex("IX_LeafletDocuments_ContentHash", "LeafletDocuments", "ContentHash");
   migrationBuilder.CreateIndex("IX_LeafletDocuments_SourcePath", "LeafletDocuments", "SourcePath", unique: true);
   migrationBuilder.CreateIndex("IX_LeafletChunks_DocumentId", "LeafletChunks", "DocumentId");
   ```
5. The `Down` method must drop the HNSW index, the indexes, then both tables in reverse order.
6. Do NOT include `CREATE EXTENSION vector;`. Verify the generated migration does not contain that statement.

**Tests to write:**
None — migrations are validated by `dotnet ef migrations script` and the integration test that boots the DbContext.

**Acceptance criteria:**
- `dotnet ef migrations script` runs without error.
- Generated SQL contains `vector(1536)` and `USING hnsw (...) vector_cosine_ops`.
- Generated SQL does NOT contain `CREATE EXTENSION`.
- `Down` method cleanly reverses `Up`.

---

### task: ef-configurations-and-dbcontext-updates

**Goal:** Add EF Core entity configurations for `LeafletDocument` and `LeafletChunk` and register them on `ApplicationDbContext`. The `Embedding` property is ignored — it is written via raw Npgsql by the repository.

**Context:**
EF Core does not support `Pgvector.Vector` natively in this project. The KB feature solves this by ignoring `Embedding` in EF config and using `NpgsqlCommand` with `Pgvector.Vector` to write/read the column. Mirror that pattern exactly. The configuration also enforces lengths and required-ness for normal columns. `ApplicationDbContext` lives at `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (verify path before editing).

**Files to create/modify:**
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletChunkConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — add `DbSet`s + apply configurations

**Implementation steps:**
1. Create `backend/src/Anela.Heblo.Persistence/Features/Leaflet/` folder.
2. `LeafletDocumentConfiguration.cs` (namespace `Anela.Heblo.Persistence.Features.Leaflet`):
   ```csharp
   public class LeafletDocumentConfiguration : IEntityTypeConfiguration<LeafletDocument>
   {
       public void Configure(EntityTypeBuilder<LeafletDocument> builder)
       {
           builder.ToTable("LeafletDocuments");
           builder.HasKey(x => x.Id);
           builder.Property(x => x.Filename).IsRequired();
           builder.Property(x => x.SourcePath).IsRequired();
           builder.Property(x => x.ContentType).IsRequired();
           builder.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
           builder.Property(x => x.IngestedAt).IsRequired();
           builder.Property(x => x.WordCount).IsRequired();
           builder.HasMany(x => x.Chunks).WithOne(x => x.Document)
               .HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
       }
   }
   ```
3. `LeafletChunkConfiguration.cs`:
   ```csharp
   public class LeafletChunkConfiguration : IEntityTypeConfiguration<LeafletChunk>
   {
       public void Configure(EntityTypeBuilder<LeafletChunk> builder)
       {
           builder.ToTable("LeafletChunks");
           builder.HasKey(x => x.Id);
           builder.Property(x => x.DocumentId).IsRequired();
           builder.Property(x => x.ChunkIndex).IsRequired();
           builder.Property(x => x.Content).IsRequired();
           builder.Property(x => x.WordCount).IsRequired();
           builder.Ignore(x => x.Embedding);
       }
   }
   ```
4. In `ApplicationDbContext.cs`:
   - Add `using Anela.Heblo.Domain.Features.Leaflet;`.
   - Add `public DbSet<LeafletDocument> LeafletDocuments => Set<LeafletDocument>();` and `public DbSet<LeafletChunk> LeafletChunks => Set<LeafletChunk>();`.
   - In `OnModelCreating`, add `modelBuilder.ApplyConfiguration(new LeafletDocumentConfiguration()); modelBuilder.ApplyConfiguration(new LeafletChunkConfiguration());`.

**Tests to write:**
None directly — covered indirectly by the migration generation step in the previous task and the repository integration tests later.

**Acceptance criteria:**
- `dotnet build` succeeds.
- `dotnet ef migrations add` (used by the migration task) successfully detects the two new tables.
- `Embedding` property is NOT generated as a normal column by EF.

---

### task: leaflet-repository-implementation

**Goal:** Implement `LeafletRepository` using EF Core for normal operations and raw Npgsql with `Pgvector.Vector` for embedding writes and HNSW similarity search.

**Context:**
The `Embedding` property is ignored by EF, so chunk inserts must use a raw `NpgsqlCommand` parameterized with `Pgvector.Vector`. Cosine similarity in pgvector uses the `<=>` distance operator; similarity is `1 - distance`. The repository is registered DI as `ILeafletRepository` against this implementation in a later module-wiring task. KB's `KnowledgeBaseRepository` is the reference implementation — read it first if uncertain.

Required Npgsql packages already in the Persistence project: `Npgsql`, `Pgvector`. Connection access pattern: get the open `NpgsqlConnection` from `ApplicationDbContext.Database.GetDbConnection()` and cast.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs`

**Implementation steps:**
1. Create `LeafletRepository.cs` in namespace `Anela.Heblo.Persistence.Features.Leaflet` implementing `ILeafletRepository`. Constructor takes `ApplicationDbContext context`.
2. Implement methods:
   - `AddDocumentAsync(document, ct)` → `await _context.LeafletDocuments.AddAsync(document, ct);` then `await _context.SaveChangesAsync(ct);`. Document is persisted before chunks so the FK is satisfied.
   - `AddChunksAsync(chunks, ct)` → for each chunk, run a raw Npgsql `INSERT` because EF cannot persist the vector column:
     ```csharp
     await _context.Database.OpenConnectionAsync(ct);
     var conn = (NpgsqlConnection)_context.Database.GetDbConnection();
     foreach (var chunk in chunks)
     {
         await using var cmd = new NpgsqlCommand(
             @"INSERT INTO ""LeafletChunks"" (""Id"", ""DocumentId"", ""ChunkIndex"", ""Content"", ""WordCount"", ""Embedding"")
               VALUES (@id, @docId, @idx, @content, @words, @embedding)", conn);
         cmd.Parameters.AddWithValue("id", chunk.Id == Guid.Empty ? Guid.NewGuid() : chunk.Id);
         cmd.Parameters.AddWithValue("docId", chunk.DocumentId);
         cmd.Parameters.AddWithValue("idx", chunk.ChunkIndex);
         cmd.Parameters.AddWithValue("content", chunk.Content);
         cmd.Parameters.AddWithValue("words", chunk.WordCount);
         cmd.Parameters.AddWithValue("embedding", new Pgvector.Vector(chunk.Embedding));
         await cmd.ExecuteNonQueryAsync(ct);
     }
     ```
     Use the Npgsql connection that EF already manages — do not open a new one.
   - `GetByHashAsync(hash, ct)` → `_context.LeafletDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.ContentHash == hash, ct);`.
   - `GetBySourcePathAsync(path, ct)` → same pattern, filter by `SourcePath`.
   - `DeleteDocumentAsync(id, ct)` → load tracked entity, `Remove`, `SaveChangesAsync`. Cascade FK deletes chunks.
   - `UpdateSourcePathAsync(documentId, newPath, ct)` → load, set `SourcePath`, `SaveChangesAsync`.
   - `SaveChangesAsync(ct)` → `_context.SaveChangesAsync(ct);`.
   - `SearchSimilarAsync(queryEmbedding, topK, ct)`:
     ```csharp
     await _context.Database.OpenConnectionAsync(ct);
     var conn = (NpgsqlConnection)_context.Database.GetDbConnection();
     await using var cmd = new NpgsqlCommand(
         @"SELECT c.""Id"", c.""DocumentId"", c.""ChunkIndex"", c.""Content"", c.""WordCount"",
                  d.""Filename"", d.""SourcePath"",
                  1 - (c.""Embedding"" <=> @q) AS score
           FROM ""LeafletChunks"" c
           JOIN ""LeafletDocuments"" d ON d.""Id"" = c.""DocumentId""
           ORDER BY c.""Embedding"" <=> @q
           LIMIT @k", conn);
     cmd.Parameters.AddWithValue("q", new Pgvector.Vector(queryEmbedding));
     cmd.Parameters.AddWithValue("k", topK);
     var results = new List<(LeafletChunk, double)>();
     await using var reader = await cmd.ExecuteReaderAsync(ct);
     while (await reader.ReadAsync(ct))
     {
         var chunk = new LeafletChunk
         {
             Id = reader.GetGuid(0),
             DocumentId = reader.GetGuid(1),
             ChunkIndex = reader.GetInt32(2),
             Content = reader.GetString(3),
             WordCount = reader.GetInt32(4),
             Document = new LeafletDocument
             {
                 Id = reader.GetGuid(1),
                 Filename = reader.GetString(5),
                 SourcePath = reader.GetString(6),
             },
         };
         results.Add((chunk, reader.GetDouble(7)));
     }
     return results;
     ```
3. Register the repository in `PersistenceModule.cs`: add `services.AddScoped<ILeafletRepository, LeafletRepository>();`.

**Tests to write:**
- `LeafletRepositoryTests.AddDocumentAsync_persists_document` — using in-memory or test-container DB, add document, assert it can be retrieved.
- `LeafletRepositoryTests.GetByHashAsync_returns_null_when_missing` — query unknown hash, expect null.
- `LeafletRepositoryTests.GetByHashAsync_returns_document_when_present` — insert, query by hash, assert match.
- `LeafletRepositoryTests.DeleteDocumentAsync_cascades_to_chunks` — insert document with chunks, delete, assert chunk count zero.

(Vector-dependent methods `AddChunksAsync` and `SearchSimilarAsync` require a real PostgreSQL with pgvector — if a test container is not yet wired in this codebase, mark these tests as integration and leave a `TODO` comment with the test name; assertion of behaviour is then deferred to manual smoke testing per existing KB practice.)

**Acceptance criteria:**
- `dotnet build` succeeds.
- All non-vector unit tests pass.
- `ILeafletRepository` is resolvable from DI as scoped.
- No EF mapping is attempted on `Embedding`.

---

### task: leaflet-chunker-service

**Goal:** Implement `LeafletChunker`, an 800-word chunker with 80-word overlap that reads its parameters from `LeafletOptions` and emits `LeafletChunk` instances ready for embedding.

**Context:**
Per architecture Decision 3, the existing `DocumentChunker` cannot be reused — it is bound to `KnowledgeBaseOptions.ChunkSize` (512). The leaflet chunker has its own options to keep concerns isolated. Whitespace-tokenize, count words, slide a window of `ChunkSizeWords` with `ChunkOverlapWords` overlap. Final chunk shorter than threshold is still kept (no padding, no merging).

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletChunker.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/ILeafletChunker.cs`

**Implementation steps:**
1. Create folder `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/`.
2. `ILeafletChunker.cs` (namespace `Anela.Heblo.Application.Features.Leaflet.Services`):
   ```csharp
   public interface ILeafletChunker
   {
       IReadOnlyList<LeafletChunk> Chunk(string text, Guid documentId);
   }
   ```
3. `LeafletChunker.cs` implementing the interface:
   ```csharp
   public class LeafletChunker : ILeafletChunker
   {
       private readonly LeafletOptions _options;
       public LeafletChunker(IOptions<LeafletOptions> options) => _options = options.Value;

       public IReadOnlyList<LeafletChunk> Chunk(string text, Guid documentId)
       {
           if (string.IsNullOrWhiteSpace(text)) return Array.Empty<LeafletChunk>();
           var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
           var size = _options.ChunkSizeWords;
           var overlap = _options.ChunkOverlapWords;
           var step = Math.Max(1, size - overlap);
           var chunks = new List<LeafletChunk>();
           var idx = 0;
           for (var start = 0; start < words.Length; start += step)
           {
               var take = Math.Min(size, words.Length - start);
               var slice = string.Join(' ', words, start, take);
               chunks.Add(new LeafletChunk
               {
                   Id = Guid.NewGuid(),
                   DocumentId = documentId,
                   ChunkIndex = idx++,
                   Content = slice,
                   WordCount = take,
                   Embedding = Array.Empty<float>(),
               });
               if (start + size >= words.Length) break;
           }
           return chunks;
       }
   }
   ```

**Tests to write:**
- `LeafletChunkerTests.Chunk_empty_input_returns_empty` — input `""`, assert empty list.
- `LeafletChunkerTests.Chunk_short_input_below_size_returns_one_chunk` — 100 words, default options (800/80), assert single chunk with 100 words and `ChunkIndex == 0`.
- `LeafletChunkerTests.Chunk_long_input_overlaps_correctly` — 2000 words; assert chunks: count == 3, first chunk words 0..799, second chunk words 720..1519, third chunk words 1440..1999. Verify `ChunkIndex` is sequential.
- `LeafletChunkerTests.Chunk_assigns_DocumentId_to_each_chunk` — assert all chunks share the supplied `documentId`.

**Acceptance criteria:**
- All four tests pass.
- The chunker depends only on `LeafletOptions`, not `KnowledgeBaseOptions`.
- No usage of `DocumentChunker` is introduced.

---

### task: leaflet-indexing-service

**Goal:** Implement `LeafletIndexingService` — the flat chunk → embed → persist pipeline that does NOT summarize chunks (leaflets are already in target voice).

**Context:**
Per architecture Decision 2, leaflets bypass `IIndexingStrategy` and embed raw chunks. The service:
1. Calls `ILeafletChunker.Chunk(text, document.Id)`.
2. Calls `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync(...)` for the chunk contents (batch — single call with all chunk texts).
3. Assigns `Embedding` to each chunk.
4. Calls `repo.AddChunksAsync(chunks)`.
5. Sets `document.WordCount = chunks.Sum(c => c.WordCount)` (or word count of original text — the document-level count is the canonical total, computed once from input text — adopt input total to avoid double counting overlap).

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/ILeafletIndexingService.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs`

**Implementation steps:**
1. `ILeafletIndexingService.cs`:
   ```csharp
   public interface ILeafletIndexingService
   {
       Task IndexAsync(string text, LeafletDocument document, CancellationToken ct = default);
   }
   ```
2. `LeafletIndexingService.cs`:
   ```csharp
   public class LeafletIndexingService : ILeafletIndexingService
   {
       private readonly ILeafletChunker _chunker;
       private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
       private readonly ILeafletRepository _repo;
       private readonly ILogger<LeafletIndexingService> _logger;

       public LeafletIndexingService(
           ILeafletChunker chunker,
           IEmbeddingGenerator<string, Embedding<float>> embeddings,
           ILeafletRepository repo,
           ILogger<LeafletIndexingService> logger)
       {
           _chunker = chunker; _embeddings = embeddings; _repo = repo; _logger = logger;
       }

       public async Task IndexAsync(string text, LeafletDocument document, CancellationToken ct = default)
       {
           var chunks = _chunker.Chunk(text, document.Id);
           if (chunks.Count == 0)
           {
               _logger.LogWarning("Leaflet {DocumentId} produced zero chunks; skipping indexing", document.Id);
               return;
           }
           var inputs = chunks.Select(c => c.Content).ToList();
           var generated = await _embeddings.GenerateAsync(inputs, cancellationToken: ct);
           var vectors = generated.ToList();
           if (vectors.Count != chunks.Count)
               throw new InvalidOperationException(
                   $"Embedding count {vectors.Count} does not match chunk count {chunks.Count}");
           for (var i = 0; i < chunks.Count; i++)
               chunks[i].Embedding = vectors[i].Vector.ToArray();
           document.WordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
           await _repo.AddChunksAsync(chunks, ct);
       }
   }
   ```
3. Verify `IEmbeddingGenerator<string, Embedding<float>>` is the correct DI surface in this project (look at how `KnowledgeBaseDocIndexingStrategy` injects embeddings) — match that exact type.

**Tests to write:**
- `LeafletIndexingServiceTests.IndexAsync_no_chunks_skips_persistence` — text = `""`; assert `repo.AddChunksAsync` is never called and a warning is logged.
- `LeafletIndexingServiceTests.IndexAsync_assigns_embeddings_to_each_chunk` — mock chunker returns 3 chunks; mock embeddings returns 3 vectors; assert each persisted chunk has the expected vector array.
- `LeafletIndexingServiceTests.IndexAsync_throws_on_count_mismatch` — chunks=3, embeddings=2; assert `InvalidOperationException`.
- `LeafletIndexingServiceTests.IndexAsync_sets_WordCount_from_input_text` — text with 1500 whitespace-separated words; assert `document.WordCount == 1500`.

**Acceptance criteria:**
- All four tests pass.
- The service does NOT call `IIndexingStrategy` or `IKnowledgeBaseRepository`.
- Embedding generation uses a single batch call.

---

### task: index-leaflet-mediatr-handler

**Goal:** Implement `IndexLeafletRequest`, `IndexLeafletResponse`, and `IndexLeafletHandler` — the MediatR pipeline entry point used by the ingestion job to index a single file with hash-based dedup and source-path collision handling.

**Context:**
The handler is invoked once per file by `LeafletIngestionJob`. It handles:
1. SHA-256 content hash → `GetByHashAsync`. If hit, return `WasDuplicate=true` (file is still archived by the job).
2. `GetBySourcePathAsync` — if the same path returns a different document with a different hash, delete the old document (cascade deletes chunks) and re-index.
3. Resolve a `IDocumentTextExtractor` from `IEnumerable<IDocumentTextExtractor>` by `CanHandle(contentType)`. Per arch review amendment, the existing `PlainTextExtractor` and `PdfTextExtractor` are already DI-registered — do NOT create new ones.
4. Throw `NotSupportedException` if no extractor matches; the job logs `Warning` and leaves the file in Inbox.
5. Persist `LeafletDocument`, then call `LeafletIndexingService.IndexAsync(text, document, ct)`.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs`

**Implementation steps:**
1. Create folder `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/`.
2. `IndexLeafletRequest.cs`:
   ```csharp
   public class IndexLeafletRequest : IRequest<IndexLeafletResponse>
   {
       public byte[] Content { get; set; } = Array.Empty<byte>();
       public string Filename { get; set; } = string.Empty;
       public string SourcePath { get; set; } = string.Empty;
       public string ContentType { get; set; } = string.Empty;
   }
   ```
3. `IndexLeafletResponse.cs`:
   ```csharp
   public class IndexLeafletResponse
   {
       public Guid DocumentId { get; set; }
       public bool WasDuplicate { get; set; }
       public int ChunkCount { get; set; }
   }
   ```
4. `IndexLeafletHandler.cs`:
   ```csharp
   public class IndexLeafletHandler : IRequestHandler<IndexLeafletRequest, IndexLeafletResponse>
   {
       private readonly ILeafletRepository _repo;
       private readonly IEnumerable<IDocumentTextExtractor> _extractors;
       private readonly ILeafletIndexingService _indexing;
       private readonly ILogger<IndexLeafletHandler> _logger;

       public IndexLeafletHandler(
           ILeafletRepository repo,
           IEnumerable<IDocumentTextExtractor> extractors,
           ILeafletIndexingService indexing,
           ILogger<IndexLeafletHandler> logger)
       {
           _repo = repo; _extractors = extractors; _indexing = indexing; _logger = logger;
       }

       public async Task<IndexLeafletResponse> Handle(IndexLeafletRequest request, CancellationToken ct)
       {
           var hash = ComputeHash(request.Content);
           var existing = await _repo.GetByHashAsync(hash, ct);
           if (existing is not null)
           {
               _logger.LogInformation("Duplicate leaflet content detected, hash={Hash}, document={Id}",
                   hash, existing.Id);
               return new IndexLeafletResponse { DocumentId = existing.Id, WasDuplicate = true };
           }

           var byPath = await _repo.GetBySourcePathAsync(request.SourcePath, ct);
           if (byPath is not null)
           {
               _logger.LogInformation("Source path collision, replacing old document {Id}", byPath.Id);
               await _repo.DeleteDocumentAsync(byPath.Id, ct);
           }

           var extractor = _extractors.FirstOrDefault(e => e.CanHandle(request.ContentType))
               ?? throw new NotSupportedException($"No extractor for content type '{request.ContentType}'");
           var text = await extractor.ExtractAsync(request.Content, ct);

           var doc = new LeafletDocument
           {
               Id = Guid.NewGuid(),
               Filename = request.Filename,
               SourcePath = request.SourcePath,
               ContentType = request.ContentType,
               ContentHash = hash,
               IngestedAt = DateTime.UtcNow,
               WordCount = 0,
           };
           await _repo.AddDocumentAsync(doc, ct);
           await _indexing.IndexAsync(text, doc, ct);
           await _repo.SaveChangesAsync(ct);

           var chunkCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length; // approx
           return new IndexLeafletResponse { DocumentId = doc.Id, WasDuplicate = false, ChunkCount = chunkCount };
       }

       private static string ComputeHash(byte[] content)
       {
           using var sha = System.Security.Cryptography.SHA256.Create();
           var bytes = sha.ComputeHash(content);
           return Convert.ToHexString(bytes).ToLowerInvariant();
       }
   }
   ```
   Note: `ChunkCount` in the response is a best-effort hint for logging; if `LeafletIndexingService` is enhanced later to return the exact count, swap to that.
5. Verify the `IDocumentTextExtractor` interface lives at `Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors` (check actual namespace before adding `using`).

**Tests to write:**
- `IndexLeafletHandlerTests.Handle_duplicate_hash_returns_WasDuplicate_true_and_skips_indexing` — mock `GetByHashAsync` returns existing document; assert response `WasDuplicate==true`, indexing service is NEVER called.
- `IndexLeafletHandlerTests.Handle_path_collision_deletes_old_document_and_indexes_new` — mock `GetByHashAsync` returns null, `GetBySourcePathAsync` returns existing; assert `DeleteDocumentAsync` is called with the old id and indexing proceeds.
- `IndexLeafletHandlerTests.Handle_no_extractor_throws_NotSupportedException` — provide empty `IEnumerable<IDocumentTextExtractor>`; assert `NotSupportedException` is thrown.
- `IndexLeafletHandlerTests.Handle_happy_path_persists_and_calls_indexing` — assert document added with correct `Filename`, `SourcePath`, `ContentHash`, `ContentType`, and `LeafletIndexingService.IndexAsync` is called once.

**Acceptance criteria:**
- All four tests pass.
- Handler does not call any KB services.
- SHA-256 hash is lowercase hex, 64 chars.

---

### task: leaflet-ingestion-recurring-job

**Goal:** Implement `LeafletIngestionJob` — a Hangfire `IRecurringJob` that polls `/Leaflets/Inbox`, dispatches `IndexLeafletRequest` per file, and moves files to `/Leaflets/Archived` on success (including duplicates).

**Context:**
Mirror `KnowledgeBaseIngestionJob` exactly. Use the existing `IRecurringJob`, `RecurringJobMetadata`, and `IRecurringJobStatusChecker` abstractions. The shared `IOneDriveService` provides `ListInboxFilesAsync(driveId, inboxPath)`, `DownloadFileAsync(driveId, fileId)`, and `MoveToArchivedAsync(driveId, fileId, archivedPath)` (verify exact signatures by reading `KnowledgeBaseIngestionJob` first). Job name: `"leaflet-ingestion"` — does not collide with `"knowledge-base-ingestion"`. Cron expression read from `LeafletOptions.IngestionCronExpression`.

Per spec amendment, **duplicates are still archived** so they don't keep being polled.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs`

**Implementation steps:**
1. Create folder `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/`.
2. `LeafletIngestionJob.cs`:
   ```csharp
   public class LeafletIngestionJob : IRecurringJob
   {
       private readonly IOneDriveService _oneDrive;
       private readonly IMediator _mediator;
       private readonly LeafletOptions _options;
       private readonly ILogger<LeafletIngestionJob> _logger;

       public LeafletIngestionJob(
           IOneDriveService oneDrive,
           IMediator mediator,
           IOptions<LeafletOptions> options,
           ILogger<LeafletIngestionJob> logger)
       {
           _oneDrive = oneDrive; _mediator = mediator;
           _options = options.Value; _logger = logger;
       }

       public RecurringJobMetadata Metadata => new()
       {
           JobName = "leaflet-ingestion",
           CronExpression = _options.IngestionCronExpression,
           DisplayName = "Leaflet Ingestion",
       };

       public async Task ExecuteAsync(CancellationToken ct = default)
       {
           var files = await _oneDrive.ListInboxFilesAsync(_options.DriveId, _options.InboxPath, ct);
           foreach (var file in files)
           {
               try
               {
                   var content = await _oneDrive.DownloadFileAsync(_options.DriveId, file.Id, ct);
                   var response = await _mediator.Send(new IndexLeafletRequest
                   {
                       Content = content,
                       Filename = file.Name,
                       SourcePath = file.Path,
                       ContentType = file.ContentType,
                   }, ct);
                   await _oneDrive.MoveToArchivedAsync(_options.DriveId, file.Id, _options.ArchivedPath, ct);
                   _logger.LogInformation("Ingested leaflet {File}, document={Id}, duplicate={Dup}",
                       file.Name, response.DocumentId, response.WasDuplicate);
               }
               catch (NotSupportedException nex)
               {
                   _logger.LogWarning(nex, "Skipping unsupported file {File}; left in Inbox", file.Name);
               }
               catch (Exception ex)
               {
                   _logger.LogError(ex, "Failed to ingest {File}; left in Inbox for retry", file.Name);
               }
           }
       }
   }
   ```
3. Verify the exact shape of `RecurringJobMetadata` and the field/property names by reading `KnowledgeBaseIngestionJob` — adjust names if the project uses different conventions (e.g., `Schedule` vs `CronExpression`).
4. Verify `IOneDriveService` method signatures (`file.Id`, `file.Name`, `file.Path`, `file.ContentType`) by reading the existing KB job — adopt exactly the same.

**Tests to write:**
- `LeafletIngestionJobTests.Execute_calls_mediator_per_file_and_archives_on_success` — mock OneDrive returns 2 files; assert mediator is called twice with matching `Filename`, `MoveToArchivedAsync` is called twice.
- `LeafletIngestionJobTests.Execute_archives_duplicates` — mock mediator returns `WasDuplicate=true`; assert `MoveToArchivedAsync` is still called.
- `LeafletIngestionJobTests.Execute_skips_unsupported_file_without_archiving` — mock mediator throws `NotSupportedException`; assert `MoveToArchivedAsync` is NOT called and a warning is logged.
- `LeafletIngestionJobTests.Execute_continues_after_single_file_failure` — first file throws transient exception; assert second file is still processed.
- `LeafletIngestionJobTests.Metadata_has_unique_job_name` — assert `Metadata.JobName == "leaflet-ingestion"`.

**Acceptance criteria:**
- All five tests pass.
- Cron is read from options, not hardcoded.
- Job name does not collide with `"knowledge-base-ingestion"`.

---

### task: generate-leaflet-mediatr-handler

**Goal:** Implement `GenerateLeafletRequest`, `GenerateLeafletResponse`, and `GenerateLeafletHandler` — the two-stage RAG orchestration that returns polished Czech-language Markdown.

**Context:**
Two-stage flow per architecture (Decisions 4–6):
1. Embed the topic once. Reuse the same vector for KB and Leaflet retrieval (saves one embedding call per request — Stage 2 amendment).
2. Stage 1: KB retrieval → Claude builds factual outline.
3. Stage 2: Leaflet retrieval → Claude rewrites outline in marketing voice.
4. If Stage 1 retrieval is empty AND Stage 2 retrieval is empty, return `422` with friendly message `"Knowledge Base does not yet cover this topic; try a broader phrasing"` — but if only one side is sparse, continue with cold-start fallback.
5. Cold-start: if leaflet retrieval is empty, set `coldStart="true"` placeholder and log `Warning` once per request; Stage 2 prompt instructs the model to use a neutral marketing register.
6. Validation: Topic 1–200 chars, audience and length required — covered by global `ValidationBehavior` reading DataAnnotations.

Length word target: read from `LeafletOptions.ShortWordTarget` / `MediumWordTarget` / `LongWordTarget` and substitute into the prompt's `{length}` placeholder as the integer.

Reuse `IEmbeddingGenerator<string, Embedding<float>>` and `IChatClient` from DI — do NOT call any KB query-expansion handler. Call `IKnowledgeBaseRepository.SearchSimilarAsync` and `ILeafletRepository.SearchSimilarAsync` directly.

Failure handling:
- Validation → 400 `ProblemDetails` (handled by `ValidationBehavior` + `ProblemDetails` middleware — verify both exist).
- Transient errors from embedding/LLM/DB → handler retries once internally with a 1-second delay, then propagates as `InvalidOperationException` (the controller maps to 502 `ProblemDetails`).
- Dual-empty retrieval → throw a custom `EmptyRetrievalException` that the controller maps to 422.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/EmptyRetrievalException.cs`

**Implementation steps:**
1. `GenerateLeafletRequest.cs`:
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
   ```
2. `GenerateLeafletResponse.cs` — class extending the existing project `BaseResponse` (verify the type lives at `Anela.Heblo.Application.Shared` or similar — match the convention used by `AskKnowledgeBaseResponse`):
   ```csharp
   public class GenerateLeafletResponse : BaseResponse
   {
       [JsonPropertyName("content")]
       public string Content { get; set; } = string.Empty;
   }
   ```
3. `EmptyRetrievalException.cs`:
   ```csharp
   public class EmptyRetrievalException : Exception
   {
       public EmptyRetrievalException(string message) : base(message) { }
   }
   ```
4. `GenerateLeafletHandler.cs`:
   ```csharp
   public class GenerateLeafletHandler : IRequestHandler<GenerateLeafletRequest, GenerateLeafletResponse>
   {
       private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
       private readonly IKnowledgeBaseRepository _kb;
       private readonly ILeafletRepository _leaflets;
       private readonly IChatClient _chat;
       private readonly LeafletOptions _options;
       private readonly ILogger<GenerateLeafletHandler> _logger;

       public GenerateLeafletHandler(
           IEmbeddingGenerator<string, Embedding<float>> embeddings,
           IKnowledgeBaseRepository kb,
           ILeafletRepository leaflets,
           IChatClient chat,
           IOptions<LeafletOptions> options,
           ILogger<GenerateLeafletHandler> logger)
       {
           _embeddings = embeddings; _kb = kb; _leaflets = leaflets;
           _chat = chat; _options = options.Value; _logger = logger;
       }

       public async Task<GenerateLeafletResponse> Handle(GenerateLeafletRequest request, CancellationToken ct)
       {
           var topicVector = (await _embeddings.GenerateAsync(new[] { request.Topic }, cancellationToken: ct))
               .First().Vector.ToArray();

           var kbHits = (await _kb.SearchSimilarAsync(topicVector, _options.KbTopK, ct))
               .Where(x => x.Score >= _options.MinSimilarityScore).ToList();
           var leafletHits = (await _leaflets.SearchSimilarAsync(topicVector, _options.LeafletTopK, ct))
               .Where(x => x.Score >= _options.MinSimilarityScore).ToList();

           if (kbHits.Count == 0 && leafletHits.Count == 0)
               throw new EmptyRetrievalException(
                   "Knowledge Base does not yet cover this topic; try a broader phrasing");

           var lengthWords = request.Length switch
           {
               LeafletLength.Short => _options.ShortWordTarget,
               LeafletLength.Medium => _options.MediumWordTarget,
               LeafletLength.Long => _options.LongWordTarget,
               _ => _options.MediumWordTarget,
           };
           var audienceLabel = request.Audience == AudienceType.B2B ? "B2B" : "Koncový zákazník";
           var kbContext = string.Join("\n\n---\n\n", kbHits.Select(h => h.Chunk.Content));
           var leafletContext = string.Join("\n\n---\n\n", leafletHits.Select(h => h.Chunk.Content));
           var coldStart = leafletHits.Count == 0 ? "true" : "false";

           if (leafletHits.Count == 0)
               _logger.LogWarning("Leaflet cold-start: zero leaflet style references for topic '{Topic}'",
                   request.Topic);

           var stage1System = _options.Stage1SystemPrompt
               .Replace("{topic}", request.Topic)
               .Replace("{audience}", audienceLabel)
               .Replace("{length}", lengthWords.ToString())
               .Replace("{kbContext}", string.IsNullOrWhiteSpace(kbContext) ? "(empty)" : kbContext);

           var outlineResponse = await _chat.GetResponseAsync(
               new[] { new ChatMessage(ChatRole.System, stage1System), new ChatMessage(ChatRole.User, request.Topic) },
               new ChatOptions { ModelId = _options.ChatModel, MaxOutputTokens = _options.ChatMaxTokens },
               ct);
           var outline = outlineResponse.Text ?? string.Empty;

           var stage2System = _options.Stage2SystemPrompt
               .Replace("{topic}", request.Topic)
               .Replace("{audience}", audienceLabel)
               .Replace("{length}", lengthWords.ToString())
               .Replace("{coldStart}", coldStart)
               .Replace("{leafletContext}", string.IsNullOrWhiteSpace(leafletContext) ? "(none)" : leafletContext);

           var leafletResponse = await _chat.GetResponseAsync(
               new[] { new ChatMessage(ChatRole.System, stage2System), new ChatMessage(ChatRole.User, outline) },
               new ChatOptions { ModelId = _options.ChatModel, MaxOutputTokens = _options.ChatMaxTokens },
               ct);

           return new GenerateLeafletResponse { Content = leafletResponse.Text ?? string.Empty };
       }
   }
   ```
5. Verify the exact `IChatClient` API surface (`GetResponseAsync`, `ChatOptions`, `ChatMessage`, `ChatRole`) by reading the KB `AskKnowledgeBaseHandler` — adopt identical types. If the project uses `IChatClient.CompleteAsync` instead, swap accordingly.

**Tests to write:**
- `GenerateLeafletHandlerTests.Handle_dual_empty_retrieval_throws_EmptyRetrievalException` — mock both repos return empty; assert `EmptyRetrievalException`.
- `GenerateLeafletHandlerTests.Handle_only_leaflet_empty_logs_cold_start_and_continues` — KB returns 3 chunks above threshold, leaflet returns empty; assert chat client is called twice, `Warning` log emitted, response `Content` non-empty.
- `GenerateLeafletHandlerTests.Handle_only_kb_empty_continues_with_neutral_kb_context` — KB empty, leaflet has 2 chunks; assert chat is called twice, no exception.
- `GenerateLeafletHandlerTests.Handle_filters_below_threshold_chunks` — repo returns 5 chunks, 3 below threshold; assert prompt context uses only 2 chunks.
- `GenerateLeafletHandlerTests.Handle_uses_topic_embedding_only_once` — assert `IEmbeddingGenerator.GenerateAsync` is called exactly once.
- `GenerateLeafletHandlerTests.Handle_substitutes_length_word_target_per_LeafletLength` — for Short/Medium/Long, assert prompt contains "200"/"400"/"700" respectively (use captured prompt assertions).
- `GenerateLeafletHandlerTests.Handle_substitutes_audience_label_to_czech` — for B2B → "B2B"; for EndConsumer → "Koncový zákazník".

**Acceptance criteria:**
- All seven tests pass.
- Embedding called exactly once per request.
- Below-threshold chunks are excluded from the prompt context.
- Cold-start does NOT throw — only logs.

---

### task: leaflet-controller-rest-endpoint

**Goal:** Add `LeafletController` exposing `POST /api/leaflet/generate`, mapping handler exceptions to the right HTTP status codes (200/400/422/502) using `ProblemDetails`.

**Context:**
Project uses MVC controllers, not FastEndpoints. Controllers send via MediatR. Validation is enforced globally by `ValidationBehavior` — DTO `[Required]` annotations produce 400 automatically. Custom mappings for this endpoint:
- `EmptyRetrievalException` → 422.
- `OperationCanceledException` → propagate (default ASP.NET handling).
- Any other unhandled exception → 502 `ProblemDetails` with generic message; log the full exception.

Endpoint URL: `POST /api/leaflet/generate` per spec FR-6.

**Files to create/modify:**
- `backend/src/Anela.Heblo.API/Controllers/LeafletController.cs`

**Implementation steps:**
1. Create `LeafletController.cs` in namespace `Anela.Heblo.API.Controllers`:
   ```csharp
   [ApiController]
   [Route("api/leaflet")]
   [Authorize]
   public class LeafletController : ControllerBase
   {
       private readonly IMediator _mediator;
       private readonly ILogger<LeafletController> _logger;

       public LeafletController(IMediator mediator, ILogger<LeafletController> logger)
       {
           _mediator = mediator; _logger = logger;
       }

       [HttpPost("generate")]
       [ProducesResponseType(typeof(GenerateLeafletResponse), 200)]
       [ProducesResponseType(typeof(ProblemDetails), 400)]
       [ProducesResponseType(typeof(ProblemDetails), 422)]
       [ProducesResponseType(typeof(ProblemDetails), 502)]
       public async Task<IActionResult> Generate(
           [FromBody] GenerateLeafletRequest request, CancellationToken ct)
       {
           try
           {
               var response = await _mediator.Send(request, ct);
               return Ok(response);
           }
           catch (EmptyRetrievalException ex)
           {
               return UnprocessableEntity(new ProblemDetails
               {
                   Status = 422, Title = "Insufficient knowledge", Detail = ex.Message,
               });
           }
           catch (OperationCanceledException) { throw; }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Leaflet generation failed");
               return StatusCode(502, new ProblemDetails
               {
                   Status = 502, Title = "Generation failed",
                   Detail = "Leaflet generation failed. Please try again.",
               });
           }
       }
   }
   ```
2. Use `[Authorize]` to match the project's standard authentication pattern (verify by inspecting `KnowledgeBaseController`).

**Tests to write:**
- `LeafletControllerTests.Generate_returns_200_with_response_on_success` — mock mediator returns valid response; assert 200 + body.
- `LeafletControllerTests.Generate_returns_422_on_EmptyRetrievalException` — mock mediator throws `EmptyRetrievalException`; assert 422 + ProblemDetails.
- `LeafletControllerTests.Generate_returns_502_on_unexpected_exception` — mock mediator throws `InvalidOperationException`; assert 502 + ProblemDetails with generic message.
- `LeafletControllerTests.Generate_propagates_OperationCanceledException` — mock throws `OperationCanceledException`; assert exception propagates (no 200/422/502 result).

**Acceptance criteria:**
- All four tests pass.
- Endpoint route is exactly `POST /api/leaflet/generate`.
- 502 response body does NOT contain stack trace or inner exception text.

---

### task: leaflet-mcp-tool

**Goal:** Add `LeafletTools` exposing the `GenerateLeaflet` MCP tool that mirrors the REST endpoint, wrapping handler exceptions as `McpException` with generic messages.

**Context:**
Per spec amendment FR-7, `LeafletTools` mirrors `KnowledgeBaseTools.AskKnowledgeBase`: try/catch around `IMediator.Send`, rethrow as `McpException` from `ModelContextProtocol` namespace. Do NOT leak internal exception details. The tool registers via `[McpServerToolType]` on the class and `[McpServerTool]` on the method; parameters use `[Description]`. Method returns `Task<string>` with JSON-serialized response.

Inputs: `topic`, `audience`, `length`. Audience and length should be passed as strings ("EndConsumer"/"B2B", "Short"/"Medium"/"Long") and parsed into the enums internally — this matches MCP convention for typed enums.

**Files to create/modify:**
- `backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs`

**Implementation steps:**
1. Create `LeafletTools.cs` in namespace `Anela.Heblo.API.MCP.Tools`:
   ```csharp
   [McpServerToolType]
   public class LeafletTools
   {
       private readonly IMediator _mediator;
       private readonly ILogger<LeafletTools> _logger;

       public LeafletTools(IMediator mediator, ILogger<LeafletTools> logger)
       {
           _mediator = mediator; _logger = logger;
       }

       [McpServerTool, Description("Generates a marketing leaflet in Czech Markdown using the company knowledge base and historical leaflets as style references.")]
       public async Task<string> GenerateLeaflet(
           [Description("Leaflet topic (1-200 characters), e.g. 'Bisabolol pro citlivou pleť'")] string topic,
           [Description("Audience: 'EndConsumer' or 'B2B'")] string audience,
           [Description("Length: 'Short', 'Medium', or 'Long'")] string length,
           CancellationToken ct = default)
       {
           try
           {
               if (!Enum.TryParse<AudienceType>(audience, ignoreCase: true, out var audienceEnum))
                   throw new McpException($"Invalid audience '{audience}'");
               if (!Enum.TryParse<LeafletLength>(length, ignoreCase: true, out var lengthEnum))
                   throw new McpException($"Invalid length '{length}'");

               var response = await _mediator.Send(new GenerateLeafletRequest
               {
                   Topic = topic, Audience = audienceEnum, Length = lengthEnum,
               }, ct);
               return JsonSerializer.Serialize(response);
           }
           catch (McpException) { throw; }
           catch (EmptyRetrievalException ex) { throw new McpException(ex.Message); }
           catch (Exception ex)
           {
               _logger.LogError(ex, "MCP GenerateLeaflet failed");
               throw new McpException("Leaflet generation failed. Please try again.");
           }
       }
   }
   ```
2. Verify the exact namespace for `McpException` and the `[McpServerToolType]`/`[McpServerTool]` attributes by reading `KnowledgeBaseTools` — match exactly.

**Tests to write:**
- `LeafletToolsTests.GenerateLeaflet_returns_serialized_response_on_success` — mock mediator returns valid response; assert returned string is valid JSON containing `content`.
- `LeafletToolsTests.GenerateLeaflet_throws_McpException_on_invalid_audience` — pass `audience="Marketers"`; assert `McpException`.
- `LeafletToolsTests.GenerateLeaflet_throws_McpException_on_invalid_length` — pass `length="VeryLong"`; assert `McpException`.
- `LeafletToolsTests.GenerateLeaflet_wraps_EmptyRetrievalException_as_McpException` — mock mediator throws; assert `McpException` with the original message.
- `LeafletToolsTests.GenerateLeaflet_wraps_unexpected_exception_with_generic_message` — mock throws `InvalidOperationException("internal db crash")`; assert `McpException` whose message does NOT contain "internal db crash".

**Acceptance criteria:**
- All five tests pass.
- No internal exception details leak to MCP clients.
- Tool method returns `Task<string>` with JSON.

---

### task: leaflet-module-and-di-wiring

**Goal:** Add `LeafletModule` extension method that registers all Leaflet feature services, options, the recurring job, and the MCP tool. Wire it from `Program.cs`.

**Context:**
The project uses per-feature module extensions (e.g., `AddKnowledgeBase`). The Leaflet module should:
- Bind `LeafletOptions` from configuration with `ValidateDataAnnotations().ValidateOnStart()` (per amendment FR-9).
- Register `ILeafletChunker`, `ILeafletIndexingService` as scoped.
- Register `LeafletIngestionJob` as scoped (Hangfire `IRecurringJobStatusChecker` discovers it via DI).
- Register `LeafletTools` for the MCP tool host.
- The `MediatR` registration is centrally done by `services.AddMediatR(...)` scanning the Application assembly — the new handlers are auto-discovered if the Application assembly is already in the scan list (verify in `Program.cs`).
- The `ILeafletRepository` is registered in `PersistenceModule.cs` (already done in the repository task).

`McpModule` must add `.WithTools<LeafletTools>()` to its existing chain.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs`
- `backend/src/Anela.Heblo.API/Program.cs` (or the file that calls `services.AddXxx(...)` modules)
- `backend/src/Anela.Heblo.API/MCP/McpModule.cs`

**Implementation steps:**
1. Create `LeafletModule.cs`:
   ```csharp
   public static class LeafletModule
   {
       public static IServiceCollection AddLeafletModule(
           this IServiceCollection services, IConfiguration configuration)
       {
           services.AddOptions<LeafletOptions>()
               .Bind(configuration.GetSection(LeafletOptions.SectionName))
               .ValidateDataAnnotations()
               .ValidateOnStart();

           services.AddScoped<ILeafletChunker, LeafletChunker>();
           services.AddScoped<ILeafletIndexingService, LeafletIndexingService>();
           services.AddScoped<IRecurringJob, LeafletIngestionJob>();

           return services;
       }
   }
   ```
   Note: confirm the actual DI registration shape for `IRecurringJob` (it may use a keyed service or a list registration — read `KnowledgeBaseModule`).
2. In `Program.cs`, add `services.AddLeafletModule(builder.Configuration);` next to the existing `AddKnowledgeBase` call.
3. In `McpModule.cs`, locate the existing `.AddMcpServer(...).WithHttpTransport().WithTools<KnowledgeBaseTools>()` chain and append `.WithTools<LeafletTools>()`.
4. Verify `Program.cs` already calls `services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))` for the Application assembly — leaflet handlers register automatically.

**Tests to write:**
- `LeafletModuleTests.AddLeafletModule_resolves_chunker_indexing_and_options` — build a service provider with `ConfigurationBuilder` populating required `Leaflet:DriveId`, `Leaflet:InboxPath`, `Leaflet:ArchivedPath`, `Leaflet:ChatModel`, `Leaflet:EmbeddingModel`; assert `ILeafletChunker`, `ILeafletIndexingService`, and `IOptions<LeafletOptions>` are resolvable.
- `LeafletModuleTests.AddLeafletModule_throws_on_missing_DriveId` — populate config without `DriveId`; assert calling `IOptions<LeafletOptions>.Value` triggers `OptionsValidationException`.

**Acceptance criteria:**
- Both tests pass.
- `Program.cs` startup completes when configuration has required values.
- `Program.cs` startup fails with a clear options-validation error when required values are missing.
- MCP tool list includes `LeafletTools`.

---

### task: appsettings-configuration

**Goal:** Add the `Leaflet` configuration section with required values to `appsettings.json`, `appsettings.Development.json`, `appsettings.Staging.json`, and `appsettings.Production.json`.

**Context:**
`LeafletOptions` requires `DriveId`, `InboxPath`, `ArchivedPath`, `ChatModel`, `EmbeddingModel`. Other fields have safe defaults but should be present in `appsettings.json` for discoverability. Sensitive values (none here — `DriveId` is not secret; chat/embedding API keys come from existing AI config sections, not Leaflet) can be committed.

The OneDrive `DriveId` value comes from the same SharePoint drive used by KB — capture it from the KB configuration before committing. `InboxPath`/`ArchivedPath` are the new folders the operator must create as a prerequisite.

**Files to create/modify:**
- `backend/src/Anela.Heblo.API/appsettings.json`
- `backend/src/Anela.Heblo.API/appsettings.Development.json`
- `backend/src/Anela.Heblo.API/appsettings.Staging.json`
- `backend/src/Anela.Heblo.API/appsettings.Production.json`

**Implementation steps:**
1. Open `appsettings.json` and add a top-level `Leaflet` section:
   ```json
   "Leaflet": {
     "DriveId": "",
     "InboxPath": "/Leaflets/Inbox",
     "ArchivedPath": "/Leaflets/Archived",
     "ChunkSizeWords": 800,
     "ChunkOverlapWords": 80,
     "KbTopK": 8,
     "LeafletTopK": 5,
     "MinSimilarityScore": 0.55,
     "ChatModel": "claude-sonnet-4-6",
     "ChatMaxTokens": 2048,
     "EmbeddingModel": "text-embedding-3-small",
     "IngestionCronExpression": "*/15 * * * *",
     "ShortWordTarget": 200,
     "MediumWordTarget": 400,
     "LongWordTarget": 700
   }
   ```
2. In `appsettings.Development.json`, override `DriveId` with the dev SharePoint drive id (copy from the KB section). Same for staging and production files.
3. Do NOT add the prompt strings to JSON — defaults from the C# class are fine. If marketing wants to tune them per environment later, they can override individual keys.

**Tests to write:**
None — configuration files are tested implicitly by the module-wiring test above.

**Acceptance criteria:**
- The four appsettings files contain a `Leaflet` section.
- `dotnet build` and app startup succeed with the development config.

---

### task: regenerate-typescript-api-client

**Goal:** Trigger OpenAPI client regeneration so `frontend/src/api/generated/` includes `LeafletApi`, `GenerateLeafletRequest`, `GenerateLeafletResponse`, `AudienceType`, and `LeafletLength` types.

**Context:**
The project auto-generates the TS client on backend build. The generated client is consumed by `getAuthenticatedApiClient()`. Audience and length will appear as TS string-literal unions (or numeric enums depending on generator config — verify by inspecting an existing enum in `frontend/src/api/generated/`). Backend must be built first so the OpenAPI document includes the new endpoint.

**Files to create/modify:**
- `frontend/src/api/generated/*` — regenerated automatically

**Implementation steps:**
1. Build the backend: `dotnet build backend/src/Anela.Heblo.API`. Verify the build pipeline emits the OpenAPI document and triggers the TS client regeneration step (usually via an MSBuild target or post-build script — confirm via `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`).
2. Inspect `frontend/src/api/generated/`. Confirm the new types appear:
   - A `LeafletApi` class (or equivalent) with a `generate` method.
   - `GenerateLeafletRequest`, `GenerateLeafletResponse` types.
   - `AudienceType` and `LeafletLength` enums/unions.
3. If automatic regeneration does not run, follow `docs/development/api-client-generation.md` for the manual command.
4. Commit the regenerated files.

**Tests to write:**
None — generated code is exercised by the frontend component tests in subsequent tasks.

**Acceptance criteria:**
- `frontend/src/api/generated/` contains references to `Leaflet` and `Generate`.
- `npm run build` (frontend) succeeds.
- `tsc --noEmit` (frontend type-check) succeeds.

---

### task: frontend-leaflet-form-component

**Goal:** Implement `LeafletForm` — the presentational form component with topic input (200-char counter), audience radio group, length radio group, and submit button.

**Context:**
Per design, the form is a controlled-component pure view; state lives in the parent (`LeafletGeneratorPage`). Czech labels:
- Topic input label: `"Téma"`, placeholder `"např. Bisabolol pro citlivou pleť"`, max 200 chars, character counter shown as `"{n}/200"`.
- Audience: `"Cílová skupina"`, options `"Koncový zákazník"` (EndConsumer) and `"B2B"` (B2B).
- Length: `"Délka"`, options `"Krátký (~200 slov)"` (Short), `"Střední (~400 slov)"` (Medium), `"Dlouhý (~700 slov)"` (Long).
- Submit button label: `"Vygenerovat leták"`. Disabled when `isLoading` true or topic empty.

The component receives `value` and `onChange` props for each field plus `onSubmit` and `isLoading`. No API calls inside this component.

Style conventions follow `docs/design/ui_design_document.md` and `docs/design/layout_definition.md` — mirror an existing form (e.g., `KnowledgeBaseQuestionForm` if it exists, otherwise an analogous form in the codebase).

**Files to create/modify:**
- `frontend/src/features/leaflet-generator/LeafletForm.tsx`

**Implementation steps:**
1. Create folder `frontend/src/features/leaflet-generator/`.
2. `LeafletForm.tsx`:
   ```tsx
   import { AudienceType, LeafletLength } from '../../api/generated';

   interface LeafletFormProps {
     topic: string;
     audience: AudienceType;
     length: LeafletLength;
     isLoading: boolean;
     onTopicChange: (topic: string) => void;
     onAudienceChange: (audience: AudienceType) => void;
     onLengthChange: (length: LeafletLength) => void;
     onSubmit: () => void;
   }

   export function LeafletForm(props: LeafletFormProps) {
     const {
       topic, audience, length, isLoading,
       onTopicChange, onAudienceChange, onLengthChange, onSubmit,
     } = props;
     const remaining = 200 - topic.length;
     const canSubmit = topic.trim().length > 0 && !isLoading;
     return (
       <form onSubmit={(e) => { e.preventDefault(); if (canSubmit) onSubmit(); }} className="space-y-4">
         <div>
           <label className="block text-sm font-medium">Téma</label>
           <input
             type="text"
             maxLength={200}
             value={topic}
             onChange={(e) => onTopicChange(e.target.value)}
             placeholder="např. Bisabolol pro citlivou pleť"
             className="w-full border rounded px-3 py-2"
             aria-label="Téma"
           />
           <div className="text-xs text-gray-500">{topic.length}/200</div>
         </div>
         <fieldset>
           <legend className="text-sm font-medium">Cílová skupina</legend>
           {/* radio Koncový zákazník + B2B, bound to AudienceType enum */}
         </fieldset>
         <fieldset>
           <legend className="text-sm font-medium">Délka</legend>
           {/* radio Krátký / Střední / Dlouhý, bound to LeafletLength enum */}
         </fieldset>
         <button type="submit" disabled={!canSubmit} className="btn btn-primary">
           Vygenerovat leták
         </button>
       </form>
     );
   }
   ```
3. Implement the radio groups with proper `name` attribute, checked state, and `onChange` handlers using the imported enum values. Verify the generated enum import path — if the OpenAPI generator emitted string-literal unions, use those instead of named enum members.

**Tests to write:**
- `LeafletForm.test.tsx`
  - `renders all Czech labels` — assert `"Téma"`, `"Cílová skupina"`, `"Délka"`, `"Vygenerovat leták"` are in the document.
  - `disables submit when topic is empty` — render with `topic=""`, query the button, assert `disabled`.
  - `disables submit when isLoading is true` — render with `topic="x", isLoading=true`, assert disabled.
  - `calls onTopicChange when input changes` — type into the field, assert callback fired with new value.
  - `enforces 200-character maxLength` — assert `maxLength={200}` attribute present and counter renders `"5/200"` for 5-char input.
  - `calls onSubmit when form is submitted with valid topic` — fill topic, click submit, assert callback.
  - `prevents default form submission` — submit with empty topic, assert `onSubmit` is NOT called.

**Acceptance criteria:**
- All seven tests pass.
- Component is purely presentational (no API/state hooks).
- Czech labels match exactly.

---

### task: frontend-leaflet-result-component

**Goal:** Implement `LeafletResult` — displays the generated Markdown via `react-markdown`, plus a copy-to-clipboard button with a 2-second label swap and a regenerate button.

**Context:**
Per design and arch review, `react-markdown@^10.1.0` is already in `frontend/package.json` — reuse it, do not add a new dependency. Copy interaction: button label `"Kopírovat"` swaps to `"Zkopírováno"` for 2 seconds after click. Regenerate button label: `"Generovat znovu"`. If `content` is empty, render nothing (parent decides whether to show a loading skeleton or the result).

**Files to create/modify:**
- `frontend/src/features/leaflet-generator/LeafletResult.tsx`

**Implementation steps:**
1. Create `LeafletResult.tsx`:
   ```tsx
   import { useState } from 'react';
   import ReactMarkdown from 'react-markdown';

   interface LeafletResultProps {
     content: string;
     onRegenerate: () => void;
   }

   export function LeafletResult({ content, onRegenerate }: LeafletResultProps) {
     const [copied, setCopied] = useState(false);
     if (!content) return null;
     const handleCopy = async () => {
       await navigator.clipboard.writeText(content);
       setCopied(true);
       setTimeout(() => setCopied(false), 2000);
     };
     return (
       <div className="space-y-4">
         <div className="prose max-w-none">
           <ReactMarkdown>{content}</ReactMarkdown>
         </div>
         <div className="flex gap-2">
           <button type="button" onClick={handleCopy} className="btn">
             {copied ? 'Zkopírováno' : 'Kopírovat'}
           </button>
           <button type="button" onClick={onRegenerate} className="btn">
             Generovat znovu
           </button>
         </div>
       </div>
     );
   }
   ```
2. Verify `react-markdown`'s default export name in this codebase by reading any existing usage (search `frontend/src` for `react-markdown`). Match it.

**Tests to write:**
- `LeafletResult.test.tsx`
  - `renders nothing when content is empty` — render with `content=""`, assert container is empty (e.g., no buttons).
  - `renders Markdown content as HTML` — render with `content="# Heading"`, assert `<h1>` with text `"Heading"`.
  - `copy button toggles label for 2 seconds` — mock `navigator.clipboard.writeText`; click button; assert label is `"Zkopírováno"`; advance fake timers by 2000ms; assert label reverts to `"Kopírovat"`.
  - `clicking regenerate fires onRegenerate callback` — click button, assert callback invoked once.

**Acceptance criteria:**
- All four tests pass.
- No new npm dependencies are added.
- Copy uses native `navigator.clipboard.writeText`.

---

### task: frontend-leaflet-generator-page

**Goal:** Implement `LeafletGeneratorPage` — the route container that owns form state, calls the generated API client on submit, manages loading/error states, and renders `LeafletForm` and `LeafletResult` side by side.

**Context:**
Layout follows `docs/design/layout_definition.md`: page wrapper with `Marketing → Generátor letáků` header, two-panel layout (form left, result right). State the page owns:
- `topic: string` (default `""`)
- `audience: AudienceType` (default `EndConsumer`)
- `length: LeafletLength` (default `Medium`)
- `result: string` (default `""`)
- `isLoading: boolean` (default `false`)
- `errorBanner: { kind: 'insufficient'|'transient'; message: string } | null`

API call pattern (per CLAUDE.md rule 4): use the generated client from `getAuthenticatedApiClient()`. **Use absolute URLs via the generated client's own methods** — do NOT construct URLs manually. The generated `LeafletApi.generate(request)` method already uses the configured `baseUrl`.

Error mapping:
- 422 → set `errorBanner` to `{ kind: 'insufficient', message: <body.detail or fallback Czech> }`. Inline banner above the form, not a toast.
- 502 → set `errorBanner` to `{ kind: 'transient', message: 'Generování selhalo. Zkuste to prosím znovu.' }`.
- Network/unknown → same as 502.
- 200 → set `result`, clear errorBanner.

Loading state: `isLoading=true` while awaiting; render a simple skeleton (animated grey blocks) in the result panel.

**Files to create/modify:**
- `frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx`

**Implementation steps:**
1. `LeafletGeneratorPage.tsx`:
   ```tsx
   import { useState } from 'react';
   import { LeafletForm } from './LeafletForm';
   import { LeafletResult } from './LeafletResult';
   import { getAuthenticatedApiClient } from '../../api/client';
   import { AudienceType, LeafletLength } from '../../api/generated';

   export function LeafletGeneratorPage() {
     const [topic, setTopic] = useState('');
     const [audience, setAudience] = useState<AudienceType>(AudienceType.EndConsumer);
     const [length, setLength] = useState<LeafletLength>(LeafletLength.Medium);
     const [result, setResult] = useState('');
     const [isLoading, setIsLoading] = useState(false);
     const [errorBanner, setErrorBanner] = useState<{ kind: 'insufficient' | 'transient'; message: string } | null>(null);

     const generate = async () => {
       setIsLoading(true);
       setErrorBanner(null);
       try {
         const client = await getAuthenticatedApiClient();
         const response = await client.leaflet.generate({ topic, audience, length });
         setResult(response.content);
       } catch (err: any) {
         const status = err?.response?.status ?? err?.status;
         if (status === 422) {
           setErrorBanner({
             kind: 'insufficient',
             message: err?.response?.data?.detail
               ?? 'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
           });
         } else {
           setErrorBanner({
             kind: 'transient',
             message: 'Generování selhalo. Zkuste to prosím znovu.',
           });
         }
       } finally {
         setIsLoading(false);
       }
     };

     return (
       <div className="p-6 grid grid-cols-1 lg:grid-cols-2 gap-6">
         <div>
           <h1 className="text-2xl font-bold mb-4">Generátor letáků</h1>
           {errorBanner && (
             <div role="alert" className={`mb-4 rounded p-3 ${errorBanner.kind === 'insufficient' ? 'bg-amber-100' : 'bg-red-100'}`}>
               {errorBanner.message}
             </div>
           )}
           <LeafletForm
             topic={topic} audience={audience} length={length} isLoading={isLoading}
             onTopicChange={setTopic} onAudienceChange={setAudience} onLengthChange={setLength}
             onSubmit={generate}
           />
         </div>
         <div>
           {isLoading ? (
             <div className="animate-pulse space-y-2">
               <div className="h-4 bg-gray-200 rounded w-3/4" />
               <div className="h-4 bg-gray-200 rounded" />
               <div className="h-4 bg-gray-200 rounded w-5/6" />
             </div>
           ) : (
             <LeafletResult content={result} onRegenerate={generate} />
           )}
         </div>
       </div>
     );
   }
   ```
2. Verify the generated client property name (e.g., `client.leaflet.generate` vs `client.leafletApi.generate`) by inspecting `frontend/src/api/generated/`. Adjust accordingly.
3. Verify the import path of `getAuthenticatedApiClient` by reading any existing page that uses it (e.g., `frontend/src/features/knowledge-base/`).

**Tests to write:**
- `LeafletGeneratorPage.test.tsx`
  - `renders form and empty result panel on mount` — assert form is present and no result text.
  - `shows loading skeleton while request is pending` — mock client to return a never-resolving promise; click submit; assert `animate-pulse` element is present.
  - `displays generated content on success` — mock client to resolve with `{ content: "# Hello" }`; submit; assert `<h1>Hello</h1>` is in the document.
  - `shows insufficient-knowledge banner on 422` — mock client throws `{ response: { status: 422, data: { detail: "Specific msg" } } }`; assert banner with `"Specific msg"` is rendered with role=alert.
  - `shows transient error banner on 502` — mock client throws `{ response: { status: 502 } }`; assert banner with `"Generování selhalo..."`.
  - `clears banner on next successful submit` — first submit fails 502, second succeeds; assert banner disappears.
  - `regenerate button calls API again with same fields` — succeed once, click `Generovat znovu` from result panel; assert API called twice with same payload.

**Acceptance criteria:**
- All seven tests pass.
- Page uses the generated client (no manual `fetch` calls).
- Loading skeleton replaces the result, not the form.

---

### task: frontend-routing-and-sidebar-entry

**Goal:** Add a route `/leaflet-generator` that renders `LeafletGeneratorPage`, and add a `"Marketing → Generátor letáků"` entry to the sidebar.

**Context:**
Per spec amendment 9, the sidebar gets a new `"Marketing"` section with one item `"Generátor letáků"` linking to `/leaflet-generator`. Authentication is open to all authenticated users (no role gate), per prerequisite 8.

Routing pattern: `frontend/src/App.tsx` defines routes via React Router. Read it first to confirm the pattern (e.g., `<Route path="/knowledge-base" element={<KnowledgeBasePage />} />`).

Sidebar pattern: `frontend/src/components/Layout/Sidebar.tsx` — confirm by inspection. The "Marketing" section should be added in a sensible position, probably near `"Knowledgebase"` or whichever group is closest in concern.

**Files to create/modify:**
- `frontend/src/App.tsx` — add route
- `frontend/src/components/Layout/Sidebar.tsx` — add Marketing section + Generátor letáků link

**Implementation steps:**
1. Open `frontend/src/App.tsx`. Add `import { LeafletGeneratorPage } from './features/leaflet-generator/LeafletGeneratorPage';` and `<Route path="/leaflet-generator" element={<LeafletGeneratorPage />} />` next to the other routes (inside the same `<Routes>` block, behind any auth wrapper used by other authenticated routes).
2. Open `frontend/src/components/Layout/Sidebar.tsx`. Identify the data structure used for sidebar groups — typically an array of `{ section, items: [{ label, path, icon }] }`. Add a new section:
   ```ts
   {
     section: 'Marketing',
     items: [
       { label: 'Generátor letáků', path: '/leaflet-generator', icon: /* match neighbor icons */ },
     ],
   }
   ```
3. Pick an icon from the existing icon set (lucide-react, heroicons, etc. — match the import pattern used by neighboring entries). A reasonable choice is `Megaphone` or `FileText`.
4. If the sidebar uses static JSX rather than data, add a corresponding `<NavSection>` and `<NavLink>` block.

**Tests to write:**
- `App.test.tsx` (or routing-specific test, depending on existing structure)
  - `renders LeafletGeneratorPage when navigating to /leaflet-generator` — render the app at the route, assert `"Generátor letáků"` heading is present.
- `Sidebar.test.tsx`
  - `shows Marketing section with Generátor letáků link` — assert section header `"Marketing"` and a link with text `"Generátor letáků"` whose `href` is `/leaflet-generator`.

**Acceptance criteria:**
- Both tests pass.
- `npm run build` succeeds.
- Manually navigating to `/leaflet-generator` in `npm start` (port 3000) renders the page.

---

### task: e2e-test-leaflet-generator-flow

**Goal:** Add a Playwright E2E test under the new `marketing/` module that exercises the form-submit → result-display happy path against staging.

**Context:**
Per CLAUDE.md rule 7, E2E tests live in `/frontend/test/e2e/` organized into modules. This feature warrants a new `marketing/` module folder. Authentication MUST use `navigateToApp(page)` (rule 5). The test must FAIL clearly when expected data is missing, not skip (rule 6).

The staging environment must have at least ~10 historical leaflets in `/Leaflets/Inbox` (prerequisite 5). The test uses a topic that should match indexed KB and leaflet content. Use a robust topic like `"Bisabolol"` (or any topic confirmed via `TestCatalogItems.bisabolol` fixture).

**Files to create/modify:**
- `frontend/test/e2e/marketing/leaflet-generator.spec.ts`

**Implementation steps:**
1. Create folder `frontend/test/e2e/marketing/`.
2. `leaflet-generator.spec.ts`:
   ```ts
   import { test, expect } from '@playwright/test';
   import { navigateToApp } from '../helpers/e2e-auth-helper';

   test.describe('Leaflet Generator', () => {
     test.beforeEach(async ({ page }) => {
       await navigateToApp(page);
       await page.goto('/leaflet-generator');
     });

     test('generates a leaflet for a known topic', async ({ page }) => {
       await expect(page.getByRole('heading', { name: 'Generátor letáků' })).toBeVisible();
       await page.getByLabel('Téma').fill('Bisabolol pro citlivou pleť');
       await page.getByLabel('Koncový zákazník').check();
       await page.getByLabel('Střední (~400 slov)').check();
       await page.getByRole('button', { name: 'Vygenerovat leták' }).click();

       const result = page.locator('.prose');
       await expect(result).toBeVisible({ timeout: 30_000 });
       const text = (await result.textContent()) ?? '';
       if (text.trim().length === 0) {
         throw new Error('Test data missing or generation produced empty content; expected non-empty leaflet for "Bisabolol pro citlivou pleť"');
       }
       expect(text.length).toBeGreaterThan(100);

       await page.getByRole('button', { name: 'Kopírovat' }).click();
       await expect(page.getByRole('button', { name: 'Zkopírováno' })).toBeVisible();
     });
   });
   ```
3. Verify the imports and the helper module path against existing E2E specs (e.g., `frontend/test/e2e/knowledge-base/`).
4. If the test runner script `./scripts/run-playwright-tests.sh` accepts a module name, ensure the new `marketing/` module is auto-picked up (read the script to confirm — usually it greps directories under `frontend/test/e2e/`).

**Tests to write:**
The test file IS the test. No additional tests.

**Acceptance criteria:**
- `./scripts/run-playwright-tests.sh marketing` runs the test successfully against staging once at least one matching leaflet is in the corpus.
- The test FAILS with a clear message (not skip) if no content is generated.

---

### task: ingestion-job-integration-test-and-deploy-runbook

**Goal:** Add a backend integration test that boots the full DI graph and asserts the leaflet ingestion job is registered + scheduled, plus a deploy runbook entry that documents the manual EF migration step for each environment.

**Context:**
Per project rule, "database migrations are manual." The PR description must include the runbook entry. The integration test guards against accidental DI breakage (e.g., a missing `[Required]` causing `ValidateOnStart` to fail).

The runbook entry goes in the PR description, not a committed doc — but the migration command itself should be referenced in `docs/architecture/application_infrastructure.md` if that file maintains a list of migrations. Read it before editing.

**Files to create/modify:**
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletModuleIntegrationTests.cs`
- `docs/architecture/application_infrastructure.md` — append migration entry if the doc maintains a migration list

**Implementation steps:**
1. `LeafletModuleIntegrationTests.cs`:
   ```csharp
   public class LeafletModuleIntegrationTests
   {
       [Fact]
       public void Application_starts_with_valid_leaflet_configuration()
       {
           var builder = WebApplication.CreateBuilder(new WebApplicationOptions
           {
               EnvironmentName = "Development",
           });
           // populate required config values
           builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
           {
               ["Leaflet:DriveId"] = "test-drive",
               ["Leaflet:InboxPath"] = "/Leaflets/Inbox",
               ["Leaflet:ArchivedPath"] = "/Leaflets/Archived",
               ["Leaflet:ChatModel"] = "claude-sonnet-4-6",
               ["Leaflet:EmbeddingModel"] = "text-embedding-3-small",
           });
           builder.Services.AddLeafletModule(builder.Configuration);
           // resolve key services
           using var sp = builder.Services.BuildServiceProvider(validateScopes: true);
           Assert.NotNull(sp.GetRequiredService<IOptions<LeafletOptions>>().Value);
           // job is registered
           var jobs = sp.GetServices<IRecurringJob>().ToList();
           Assert.Contains(jobs, j => j.Metadata.JobName == "leaflet-ingestion");
       }

       [Fact]
       public void Application_fails_to_start_without_DriveId()
       {
           var builder = WebApplication.CreateBuilder(new WebApplicationOptions
           {
               EnvironmentName = "Development",
           });
           builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
           {
               ["Leaflet:InboxPath"] = "/Leaflets/Inbox",
               ["Leaflet:ArchivedPath"] = "/Leaflets/Archived",
               ["Leaflet:ChatModel"] = "claude-sonnet-4-6",
               ["Leaflet:EmbeddingModel"] = "text-embedding-3-small",
           });
           builder.Services.AddLeafletModule(builder.Configuration);
           using var sp = builder.Services.BuildServiceProvider();
           var ex = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<LeafletOptions>>().Value);
           Assert.Contains("DriveId", ex.Message);
       }
   }
   ```
   Match imports to the project conventions; if the project does not boot via `WebApplicationOptions` in tests, use a plain `ServiceCollection` + the same helpers.
2. Open `docs/architecture/application_infrastructure.md`. If it has a "Migrations" or "Database changes" section, append:
   ```
   - 2026-04-30 AddLeafletStore — adds LeafletDocuments and LeafletChunks tables with vector(1536) embedding column and HNSW index. Apply via:
     dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
   ```
3. The PR description must list:
   - Manual migration command per environment.
   - Required SharePoint folders to create (`/Leaflets/Inbox`, `/Leaflets/Archived`).
   - Required appsettings overrides (`Leaflet:DriveId` per environment).
   - Verification step: hit `POST /api/leaflet/generate` with a sample topic and confirm 200 response.

**Tests to write:**
The two integration tests above. No additional tests.

**Acceptance criteria:**
- Both integration tests pass.
- The PR description (or `application_infrastructure.md` entry) lists the manual migration command and the prerequisite folder/config setup.
- `dotnet test backend/test/Anela.Heblo.Tests` passes the entire suite, including pre-existing tests.