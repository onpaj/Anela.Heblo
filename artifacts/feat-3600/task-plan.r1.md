# Task Plan: Batch LeafletChunk inserts in AddChunksAsync

**Goal:** Replace the per-chunk `foreach`/`NpgsqlCommand` loop in `LeafletDocumentRepository.AddChunksAsync` with a single parameterised multi-row `INSERT` (batched under the Npgsql parameter ceiling), reducing N round trips to one per batch while preserving column order, idempotency, and pgvector binding.

**Architecture:** Localized rewrite of one method body in the Persistence layer. The method still obtains the underlying `NpgsqlConnection` from the shared `ApplicationDbContext`, opens it if closed, and never disposes it. The only structural addition is a class-scope `private const int MaxRowsPerBatch` and a `System.Text.StringBuilder`-built multi-row `VALUES` clause with per-row indexed parameter names. No interface, DI, schema, caller, or sibling-repository change.

**Tech Stack:** .NET 8, Npgsql (raw `NpgsqlCommand` + `AddWithValue`), Pgvector (`Pgvector.Vector` for the `vector(1536)` column), EF Core (`ApplicationDbContext` for connection acquisition only), xUnit + Testcontainers (`pgvector/pgvector:pg16`) for integration tests.

---

### task: batch-leaflet-addchunks-insert

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs:1-53` (add `using System.Text;`, add `MaxRowsPerBatch` const, rewrite `AddChunksAsync` body lines 23-53)
- Test: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs` (add two `[Fact]` methods)

**Context you must preserve (do not deviate):**
- Column list MUST stay exactly `("Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount", "Embedding")` in that order — it mirrors `LeafletChunkConfiguration`. Dropping/reordering a column caused a real silent-data-loss incident (`Summary = ""`); see `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md`. The inline comment referencing that file MUST survive the rewrite.
- `ON CONFLICT ("Id") DO NOTHING` MUST terminate every emitted command (idempotency, FR-3).
- `Embedding` MUST be bound as `new Vector(chunk.Embedding)` (FR-4) — never a string/array.
- `ct` MUST be passed to `OpenAsync` and every `ExecuteNonQueryAsync`.
- Do NOT close/dispose the connection (it is owned by the DbContext). Only the `NpgsqlCommand` is disposed (via `await using`).
- Empty input → return before building/executing any command (FR-5).

- [ ] **Step 1: Write the two failing integration tests first (TDD red).**
  Open `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs`. Add these two `[Fact]` methods anywhere inside the class (e.g. after `AddChunksAsync_IsIdempotent_WhenCalledTwiceWithSameId`, before line 174). The existing test schema declares `Embedding vector(3)`, so use 3-element embeddings — multi-row inserts behave identically at any dimension.

  ```csharp
      [Fact]
      public async Task AddChunksAsync_PersistsAllRows_WhenMultipleChunks()
      {
          // Arrange
          var doc = MakeDocument("multi-chunk-test.pdf", "leaflet-hash-010");
          await _repository.AddDocumentAsync(doc);

          var chunks = Enumerable.Range(0, 5)
              .Select(i => new LeafletChunk
              {
                  Id = Guid.NewGuid(),
                  DocumentId = doc.Id,
                  ChunkIndex = i,
                  Content = $"Content {i}",
                  Summary = $"Summary {i}",
                  WordCount = i + 1,
                  Embedding = [0.1f * i, 0.2f * i, 0.3f * i]
              })
              .ToList();

          // Act: single call inserts all five rows in one multi-row INSERT
          await _repository.AddChunksAsync(chunks);

          // Assert: every row persisted with its own distinct values
          var stored = await _context.LeafletChunks
              .AsNoTracking()
              .Where(c => c.DocumentId == doc.Id)
              .OrderBy(c => c.ChunkIndex)
              .ToListAsync();

          Assert.Equal(5, stored.Count);
          for (var i = 0; i < 5; i++)
          {
              Assert.Equal(chunks[i].Id, stored[i].Id);
              Assert.Equal(i, stored[i].ChunkIndex);
              Assert.Equal($"Content {i}", stored[i].Content);
              Assert.Equal($"Summary {i}", stored[i].Summary);
              Assert.Equal(i + 1, stored[i].WordCount);
          }
      }

      [Fact]
      public async Task AddChunksAsync_IsNoOp_WhenInputEmpty()
      {
          // Arrange
          var doc = MakeDocument("empty-input-test.pdf", "leaflet-hash-011");
          await _repository.AddDocumentAsync(doc);

          // Act: empty enumerable must not throw and must issue no INSERT
          var exception = await Record.ExceptionAsync(
              () => _repository.AddChunksAsync(Array.Empty<LeafletChunk>()));

          // Assert
          Assert.Null(exception);
          var rows = await _context.LeafletChunks
              .Where(c => c.DocumentId == doc.Id)
              .ToListAsync();
          Assert.Empty(rows);
      }
  ```
  Note: `System.Linq` is already available via the file's usings/implicit usings (the file already uses LINQ `.Where`/`.Select`). If `Enumerable` does not resolve, add `using System.Linq;` at the top.

- [ ] **Step 2: Verify the new tests compile and the multi-row test FAILS against the current per-row loop.**
  Run only the two new tests:
  ```bash
  cd /home/user/worktrees/feature-3600-Arch-Review-Leaflet-Addchunksasync-Inserts-Chunks
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~AddChunksAsync_PersistsAllRows_WhenMultipleChunks|FullyQualifiedName~AddChunksAsync_IsNoOp_WhenInputEmpty"
  ```
  Expectation: this is the TDD red step. The current implementation actually inserts each chunk in its own command, so `AddChunksAsync_PersistsAllRows_WhenMultipleChunks` and `AddChunksAsync_IsNoOp_WhenInputEmpty` may already pass functionally — they are correctness guards for the refactor, not a behavior change. If Testcontainers/Docker is unavailable in the environment and the tests error at container startup rather than assertion, record that and proceed to implement; the real gate is Step 5 (all tests green after the rewrite). Do NOT weaken the assertions to make them pass.

- [ ] **Step 3: Add the `using System.Text;` directive.**
  In `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`, the current usings (lines 1-4) are:
  ```csharp
  using Anela.Heblo.Domain.Features.Leaflet;
  using Microsoft.EntityFrameworkCore;
  using Npgsql;
  using Pgvector;
  ```
  Add `using System.Text;` so `StringBuilder` resolves. Resulting block:
  ```csharp
  using System.Text;
  using Anela.Heblo.Domain.Features.Leaflet;
  using Microsoft.EntityFrameworkCore;
  using Npgsql;
  using Pgvector;
  ```

- [ ] **Step 4: Add the batch-size constant and rewrite `AddChunksAsync`.**
  Add the constant immediately after the `_context` field (after line 10). Replace the entire current `AddChunksAsync` method (lines 23-53) with the batched implementation below. Use exact string replacement.

  Add the constant next to the existing field:
  ```csharp
      private readonly ApplicationDbContext _context;

      // 7 params/row × 1000 rows = 7,000 params — comfortably under Npgsql's 65,535 ceiling.
      private const int MaxRowsPerBatch = 1000;
  ```

  Replace the method body (old code = current lines 23-53) with:
  ```csharp
      public async Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default)
      {
          var chunkList = chunks.ToList();
          if (chunkList.Count == 0)
              return;

          var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
          if (connection.State != System.Data.ConnectionState.Open)
              await connection.OpenAsync(ct);

          for (var offset = 0; offset < chunkList.Count; offset += MaxRowsPerBatch)
          {
              var batch = chunkList.Skip(offset).Take(MaxRowsPerBatch).ToList();

              // Column list MUST mirror LeafletChunkConfiguration. See memory/gotchas/raw-sql-insert-must-match-ef-mapping.md
              var sql = new StringBuilder(
                  "INSERT INTO \"LeafletChunks\" (\"Id\", \"DocumentId\", \"ChunkIndex\", \"Content\", \"Summary\", \"WordCount\", \"Embedding\") VALUES ");

              await using var cmd = new NpgsqlCommand { Connection = connection };

              for (var i = 0; i < batch.Count; i++)
              {
                  var chunk = batch[i];
                  if (i > 0)
                      sql.Append(", ");
                  sql.Append($"(@id{i}, @documentId{i}, @chunkIndex{i}, @content{i}, @summary{i}, @wordCount{i}, @embedding{i})");

                  cmd.Parameters.AddWithValue($"id{i}", chunk.Id);
                  cmd.Parameters.AddWithValue($"documentId{i}", chunk.DocumentId);
                  cmd.Parameters.AddWithValue($"chunkIndex{i}", chunk.ChunkIndex);
                  cmd.Parameters.AddWithValue($"content{i}", chunk.Content);
                  cmd.Parameters.AddWithValue($"summary{i}", chunk.Summary);
                  cmd.Parameters.AddWithValue($"wordCount{i}", chunk.WordCount);
                  cmd.Parameters.AddWithValue($"embedding{i}", new Vector(chunk.Embedding));
              }

              sql.Append(" ON CONFLICT (\"Id\") DO NOTHING");
              cmd.CommandText = sql.ToString();

              await cmd.ExecuteNonQueryAsync(ct);
          }
      }
  ```
  Rationale for the details:
  - Empty-list early return satisfies FR-5 (no invalid `VALUES ()`).
  - Per-batch local parameter numbering (`i` restarts at 0 per slice) keeps any single command ≤ `MaxRowsPerBatch × 7` params (FR-6).
  - Placeholder text is appended in the same loop iteration that binds the matching parameter, so index/value can never drift (mitigates the placeholder-mismatch risk).
  - Only fixed, code-generated placeholder names (`@id{i}`) are interpolated into SQL; no chunk field value is ever concatenated (NFR-2).
  - Column list, `ON CONFLICT`, `Vector` binding, connection lifecycle, and `ct` propagation are all preserved.

- [ ] **Step 5: Build, format, and run the full Leaflet integration test class (TDD green).**
  ```bash
  cd /home/user/worktrees/feature-3600-Arch-Review-Leaflet-Addchunksasync-Inserts-Chunks
  dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
  dotnet format backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~LeafletRepositoryIntegrationTests"
  ```
  Expectation: build succeeds, formatting clean, and all `LeafletRepositoryIntegrationTests` pass — the two new tests plus the existing guards (`AddChunksAsync_PersistsSummary` proves the `Summary` column stays in the list, `AddChunksAsync_IsIdempotent_WhenCalledTwiceWithSameId` proves `ON CONFLICT` survives, `SearchSimilarAsync_*` prove the `Vector` embedding binding still works end-to-end). If Docker/Testcontainers is unavailable and the integration tests cannot start a container, note that these are environment-gated and confirm at minimum that `dotnet build` (both src and test projects) succeeds; do not mark the task complete on a build-only basis if the test harness is runnable.

- [ ] **Step 6: Confirm no unintended changes and commit.**
  Verify the diff touches only the two files above, `KnowledgeBaseRepository.cs` is untouched (explicitly out of scope), and the gotcha comment is present in the new code.
  ```bash
  cd /home/user/worktrees/feature-3600-Arch-Review-Leaflet-Addchunksasync-Inserts-Chunks
  git diff --stat
  git add backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs \
          backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs
  git commit -m "Batch LeafletChunk inserts into a single multi-row INSERT

Replace the per-chunk NpgsqlCommand loop in AddChunksAsync with one
parameterised multi-row INSERT per batch (MaxRowsPerBatch=1000), cutting
N round trips to one. Preserves the exact 7-column list, ON CONFLICT
idempotency, and Pgvector embedding binding. Adds multi-chunk and
empty-input integration tests.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01WRZWyCwJ1fyeA2SMEwi2Mi"
  ```
