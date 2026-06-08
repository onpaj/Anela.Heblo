# Architecture Review: Atomic Upsert for GridLayoutRepository

## Skip Design: true

Backend-only persistence change with no UI surface impact. `SaveGridLayoutHandler`, DTOs, and the OpenAPI surface remain untouched.

## Architectural Fit Assessment

The change is a textbook fit. The repository already lives in `Anela.Heblo.Persistence.GridLayouts` (Clean Architecture's outer ring), already isolates Npgsql concerns behind `IGridLayoutRepository`, and already funnels driver failures through `PostgresExceptionTranslator` so the Application layer only sees `GridLayoutPersistenceException`. The fix consolidates two SQL round-trips into one and stops treating a benign concurrency event as an error — it doesn't reshape any boundaries.

Two integration points need attention but are accommodated by existing infrastructure:
- **Translator contract** at `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionTranslator.cs` — already walks `InnerException` chains; works for both `DbUpdateException`-wrapped and direct `NpgsqlException` thrown by `ExecuteSqlInterpolatedAsync`.
- **`PostgresExceptionLoggingInterceptor`** at `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionLoggingInterceptor.cs` is a `SaveChangesInterceptor`. The new upsert path bypasses `SaveChangesAsync`, so this interceptor will **no longer enrich logs for GridLayout failures** with `Severity`, `MessageText`, `Detail`, `TableName`, `ConstraintName`. The handler's own log line (with `SqlState`) survives; the richer interceptor data does not. This is a real, accepted reduction in observability — see "Risks" below.

Testcontainers infrastructure already exists: `Testcontainers.PostgreSql 3.6.0` is referenced in `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, and `KnowledgeBaseRepositoryIntegrationTests` provides a working pattern (Podman-safe, manual DDL setup, `IAsyncLifetime`).

## Proposed Architecture

### Component Overview

```
SaveGridLayoutRequest (MediatR)
  └─> SaveGridLayoutHandler                 [Application]
        └─> IGridLayoutRepository.UpsertAsync   [Domain port]
              └─> GridLayoutRepository           [Persistence adapter]
                    ├─ TimeProvider.GetUtcNow()  [for LastModified]
                    └─ ApplicationDbContext.Database
                          .ExecuteSqlInterpolatedAsync(
                             INSERT … ON CONFLICT (UserId, GridKey) DO UPDATE …)
                          └─[on NpgsqlException]→
                             PostgresExceptionTranslator.TryTranslateGridLayout
                               → GridLayoutPersistenceException(SqlState, inner)
```

No new types, no new interfaces. Only `GridLayoutRepository.UpsertAsync` and its tests change.

### Key Design Decisions

#### Decision 1: `ExecuteSqlInterpolatedAsync` over `ExecuteSqlRawAsync`

**Options considered:**
1. `ExecuteSqlInterpolatedAsync(FormattableString)` with C# raw-string interpolation
2. `ExecuteSqlRawAsync(string, params object[])` with `{0}`-style placeholders
3. A hand-rolled `NpgsqlCommand` with named parameters (as in `KnowledgeBaseRepository.AddChunksAsync`)

**Chosen approach:** Option 1.

**Rationale:** `FormattableString` parameterization is checked by Roslyn analyzers (`EF1002`), is the most readable, and matches the spec. Option 2 is acceptable but easier to misuse (a future maintainer can slip into `string.Format` and lose parameterization). Option 3 is unnecessary ceremony for a four-parameter statement — reserve it for cases that need pgvector or other custom Npgsql features.

#### Decision 2: `DateTime.Kind` handling for the `LastModified` parameter

**Options considered:**
1. Pass `_timeProvider.GetUtcNow().DateTime` (Kind=`Utc`) directly — relies on Npgsql parameter-type inference.
2. Normalize kind: `DateTime.SpecifyKind(_timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified)` before interpolation.
3. Add an explicit cast in SQL: `{now}::timestamp`.

**Chosen approach:** Option 2.

**Rationale:** The `LastModified` column is `timestamp without time zone` (configured via `AsUtcTimestamp()` in `DateTimeConfigurationExtensions.cs`). Modern Npgsql maps `DateTime` with `Kind=Utc` to `timestamptz` and **throws** `InvalidCastException` ("Cannot write DateTime with Kind=UTC to PostgreSQL type 'timestamp without time zone'") unless the legacy timestamp behavior switch is set. No such `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` exists in this codebase (grep confirms). EF Core hides this today because it materializes through the entity property + value converter; raw SQL does not. Normalizing to `Unspecified` (option 2) is the minimal, type-safe fix and keeps the SQL portable. Option 3 also works but mixes responsibilities.

#### Decision 3: Conflict target — `(UserId, GridKey)` not the constraint name

**Options considered:**
1. `ON CONFLICT ("UserId", "GridKey")` (column inference)
2. `ON CONFLICT ON CONSTRAINT "IX_GridLayouts_UserId_GridKey"`

**Chosen approach:** Option 1.

**Rationale:** The unique index is auto-named by EF Core. Hard-coding the constraint name in SQL would couple application code to a generated identifier that future migrations could rename. Column inference is stable as long as the index exists on `(UserId, GridKey)` — which is guaranteed by `GridLayoutConfiguration` at compile time.

#### Decision 4: Integration test schema bootstrap

**Options considered:**
1. `Database.EnsureCreatedAsync()` — creates every table in `ApplicationDbContext`.
2. `Database.MigrateAsync()` — replays all migrations.
3. Manual DDL `CREATE TABLE` for `GridLayouts` only (matches `KnowledgeBaseRepositoryIntegrationTests`).

**Chosen approach:** Option 3.

**Rationale:** Fastest startup (1 statement vs. dozens), no cross-feature coupling, mirrors the established pattern. `EnsureCreated` would force the test to compile against and provision the entire schema (50+ tables), making the test brittle to unrelated changes. `MigrateAsync` is even heavier and re-imports historical schemas. Manual DDL keeps the test focused on the unit under test.

## Implementation Guidance

### Directory / Module Structure

No new files in production code. All changes live in:

- `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` — replace body of `UpsertAsync`. `GetAsync` and `DeleteAsync` untouched.

Test changes:

- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryTranslationTests.cs` — replace the three `UpsertAsync_*` tests so they exercise the new path. Recommended approach: keep the `ThrowingApplicationDbContext` pattern but switch the override from `SaveChangesAsync` to `Database.ExecuteSqlInterpolatedAsync`. Since `DatabaseFacade.ExecuteSqlInterpolatedAsync` is an extension method, override at the connection level instead — substitute the `RelationalConnection` via a fake `DbConnection` that throws on `OpenAsync` or `ExecuteReaderAsync`. **Simpler alternative**: move the three translation-contract cases into the new integration test class as parametrized cases, since translating thrown Npgsql exceptions requires a real provider anyway. Keep `DeleteAsync_WhenSaveChangesThrowsNpgsqlException_ThrowsGridLayoutPersistenceException` unchanged (still uses `SaveChangesAsync`).
- `backend/test/Anela.Heblo.Tests/Persistence/GridLayouts/GridLayoutRepositoryUpsertIntegrationTests.cs` — **new file**. Testcontainers-based integration test class implementing `IAsyncLifetime`, following the layout of `KnowledgeBaseRepositoryIntegrationTests`. Trait `[Trait("Category", "Integration")]`. Disable `ResourceReaper` in a static ctor for Podman compatibility.

### Interfaces and Contracts

Unchanged. Verbatim from the spec:

```csharp
// Domain/Features/GridLayouts/IGridLayoutRepository.cs — no edit
Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default);
```

Exception contract preserved: `GridLayoutPersistenceException` on Npgsql failure; rethrow otherwise; `OperationCanceledException` propagates uncaught-translated.

### Data Flow

**Happy path (insert):**
1. Handler calls `UpsertAsync(userId, gridKey, json, ct)`.
2. Repository computes `now = DateTime.SpecifyKind(_timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified)`.
3. `ExecuteSqlInterpolatedAsync` issues one `INSERT … ON CONFLICT … DO UPDATE` round-trip with four parameters.
4. PostgreSQL inserts; no conflict; returns affected rows (1).
5. Repository returns; handler returns `SaveGridLayoutResponse()`.

**Happy path (update, same payload concurrent):**
1–3 as above for both callers in parallel.
4. PostgreSQL serializes the two `INSERT`s via the unique index; first wins the insert path, second hits `ON CONFLICT` and takes the `DO UPDATE` branch.
5. Final row carries `LayoutJson`/`LastModified` of whichever transaction committed last. Both callers return success.

**Failure path:**
1–2 as above.
3. `ExecuteSqlInterpolatedAsync` throws (e.g. transient connection drop, `NpgsqlException` directly or wrapped in `DbUpdateException`).
4. Catch block calls `PostgresExceptionTranslator.TryTranslateGridLayout(ex, nameof(UpsertAsync))`.
5. If translation succeeds, throw `GridLayoutPersistenceException`; otherwise rethrow.
6. Handler catches `GridLayoutPersistenceException`, logs with `SqlState`, returns `ErrorCodes.DatabaseError`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `DateTime.Kind=Utc` written to `timestamp` column throws via Npgsql parameter inference, breaking happy path. | HIGH | Decision 2: normalize via `DateTime.SpecifyKind(..., Unspecified)` before interpolation. Cover in a unit/integration test that asserts `LastModified` is persisted correctly. |
| `PostgresExceptionLoggingInterceptor` no longer fires for upsert failures (it's a `SaveChangesInterceptor`), so Application Insights loses `Severity`/`Detail`/`ConstraintName` for this code path. | MEDIUM | Accept — the handler still logs `SqlState`, which is the field used to distinguish transient vs. application errors. If richer logging is required later, swap the interceptor for a `DbCommandInterceptor` (out of scope per Out of Scope §). Add a one-line comment in `UpsertAsync` flagging the trade-off. |
| Existing in-memory translation tests (`UseInMemoryDatabase`) cannot exercise `ExecuteSqlInterpolatedAsync` — it throws `InvalidOperationException` on the in-memory provider. | MEDIUM | FR-6 already requires rewriting these. Recommended: move translation-contract assertions for `UpsertAsync` into the new Testcontainers integration class. Keep `PostgresExceptionTranslatorTests` as the pure unit-level proof for the translator itself. |
| Test execution time grows because of Testcontainers warm-up (~3–8 s on first run per class). | LOW | Already accepted pattern in this codebase. Group all `UpsertAsync` cases (insert, update, concurrent, translated-exception) into one test class so the container starts once. Use `[Trait("Category", "Integration")]` so CI can filter. |
| Future change moves `GridLayouts` table or unique-index columns. | LOW | The SQL is colocated with the entity configuration in `Anela.Heblo.Persistence/GridLayouts/`. A reviewer sees both files in the same folder. No additional mitigation. |
| Roslyn might raise `EF1002` ("possible SQL injection") on raw-string interpolation. | LOW | `ExecuteSqlInterpolatedAsync(FormattableString)` is explicitly recognized by the analyzer as parameterized — no warning expected. If a future analyzer upgrade flags it, suppress at call site with a justification comment. |

## Specification Amendments

1. **FR-3 / API sketch — fix `DateTime.Kind`.** The spec's sample `var now = _timeProvider.GetUtcNow().DateTime;` will fail at runtime against the live PostgreSQL provider because the column is `timestamp without time zone` (see `GridLayoutConfiguration.cs:27-29` via `AsUtcTimestamp()`) and `GetUtcNow().DateTime` has `Kind=Utc`. Replace with:
   ```csharp
   var now = DateTime.SpecifyKind(_timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
   ```
   Add an explicit acceptance criterion under FR-3: "`UpsertAsync` does not throw `InvalidCastException` ('Cannot write DateTime with Kind=UTC …') when executed against PostgreSQL with the column configured via `AsUtcTimestamp()`."

2. **FR-6 — clarify the deletion of `UseInMemoryDatabase` tests.** State explicitly that the existing three `UpsertAsync_*` cases in `GridLayoutRepositoryTranslationTests` are to be **removed** and their assertions re-proven against the real provider in the new integration test class. Otherwise the spec leaves room to keep them and silently break (they would throw `InvalidOperationException` instead of the asserted types because the in-memory provider doesn't support `ExecuteSqlInterpolatedAsync`).

3. **NFR-4 — disclose interceptor regression.** Add: "The `PostgresExceptionLoggingInterceptor` (a `SaveChangesInterceptor`) will no longer enrich logs for `UpsertAsync` failures, since the new path bypasses `SaveChangesAsync`. The handler-side log of `SqlState` is sufficient for the currently consumed observability signal; richer log enrichment is out of scope."

4. **FR-5 — add `LastModified` assertion.** The current criterion checks `LayoutJson`; add an assertion that `LastModified` matches the `TimeProvider`-injected instant (or one of two instants for the concurrent case), to lock in FR-3 against the integration provider.

## Prerequisites

- **None for production.** No schema migration, no infrastructure change. The unique index already exists.
- **For tests:** `Testcontainers.PostgreSql 3.6.0` is already a `PackageReference`; Docker or Podman must be available on developer machines and CI runners (matches what `KnowledgeBaseRepositoryIntegrationTests` already requires). No additions to `Anela.Heblo.Tests.csproj` needed.
- **CI awareness:** if integration-tagged tests are filtered out of the default `dotnet test` invocation, ensure the new class is picked up by whatever pipeline runs integration suites; otherwise the concurrency assertion will not be exercised in CI.