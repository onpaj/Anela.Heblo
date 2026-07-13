# Architecture Review: Collapse `IssuedInvoiceRepository.GetSyncStatsAsync` into a single aggregate query

## Skip Design: true

## Architectural Fit Assessment
This is a pure internal-implementation change confined to one method body in `Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`. It does not cross a module boundary, does not touch `Contracts/`, does not change `IIssuedInvoiceRepository`, and does not change the `IssuedInvoiceSyncStats` DTO. Per `docs/architecture/development_guidelines.md`, repository implementations live in `Anela.Heblo.Persistence` (ADR-001, Phase 1 single `ApplicationDbContext`) while the DI binding and interface stay in the Invoices module — neither of those is touched here, so there is no boundary risk. The change is a textbook fit for "surgical" per `CLAUDE.md`: one method, same signature, same call sites (`GetIssuedInvoiceSyncStatsHandler`, `InvoicesController`, frontend hook — all unaffected).

The only architecturally interesting question is **how to verify** the "one round trip" acceptance criterion, since the existing unit test suite for this repository (`IssuedInvoiceRepositoryTests`) runs against `Microsoft.EntityFrameworkCore.InMemory`, which silently accepts patterns that wouldn't translate to SQL and doesn't expose round-trip count. The codebase already has a solved pattern for exactly this problem — see Key Design Decision 1.

## Proposed Architecture

### Component Overview
No new components. Single method rewrite inside an existing class:

```
IssuedInvoiceRepository (Persistence/Invoices)
  └── GetSyncStatsAsync(fromDate, toDate, ct)   [body rewritten; signature unchanged]
        called by
  └── GetIssuedInvoiceSyncStatsHandler (Application/Features/Invoices/UseCases/GetIssuedInvoiceSyncStats)
        called by
  └── InvoicesController (API)
        polled by
  └── useIssuedInvoiceSyncStats (frontend hook, staleTime 5m) — unchanged, out of scope
```

### Key Design Decisions

#### Decision 1: How to verify "single round trip" without weakening the test suite
**Options considered:**
1. Trust the InMemory-provider unit test alone (build/test pass = signal it compiled and translated). This is what the spec's "Dependencies" section leaves as a fallback ("accept correctness-only coverage... confirm via manual EXPLAIN").
2. Add a manual, one-off verification step (local Postgres + SQL logging) before merge, with no lasting automated test.
3. Add a relational integration test using the project's existing Testcontainers-based Postgres fixture and command interceptor.

**Chosen approach:** Option 3. The repo already has this exact pattern, used for the same class of problem (verifying an aggregate query collapses to one SQL-side round trip): `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsSqlShapeTests.cs` plus the shared fixture `backend/test/Anela.Heblo.Tests/Common/PostgresSharedContainerFixture.cs`. That test class spins up a `postgres:16` Testcontainer (shared via `[Collection("PostgresIntegration")]`), attaches a `DbCommandInterceptor` that records every `CommandText` sent to the server, seeds a minimal hand-written schema for only the tables the query touches, and asserts `interceptor.Commands.Should().HaveCount(1)`. The same pattern also exists for Purchase (`PurchaseOrderRepositoryHistorySqlShapeTests`) and Article (`ArticleRepositoryFeedbackProjectionSqlTests`) — this is an established, repeated convention ("SqlShapeTests" naming), not a one-off.

**Rationale:** Reusing the fixture means zero new test infrastructure, a real Postgres provider (matching production, and satisfying the spec's own concern about `GroupBy` + conditional `Count`/`Max` translation), and a durable regression guard — if someone later reintroduces a second query (e.g. adds a new stat via another `CountAsync`), the interceptor test fails immediately instead of relying on manual `EXPLAIN` runs that won't happen again. Option 1 alone leaves NFR-1's core claim ("1 round trip") permanently unverified in CI. Option 2 verifies once and rots.

#### Decision 2: Where the new SQL-shape test lives, relative to the existing InMemory tests
**Options considered:** (a) put everything, including the SQL-shape/round-trip assertion, into the existing `IssuedInvoiceRepositoryTests.cs` (InMemory-backed); (b) add a second, separate test class `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs` (Postgres/Testcontainers-backed), mirroring `PhotobankRepositoryGetTagsSqlShapeTests`.

**Chosen approach:** (b) — a separate class, following the `*SqlShapeTests` naming and `[Collection("PostgresIntegration")]`/`[Trait("Category", "Integration")]` convention used by the Photobank/Purchase/Article examples. Keep `IssuedInvoiceRepositoryTests.cs` (InMemory) exactly as-is for FR-2's correctness assertions (`LastSyncTime` mix/empty cases) — those don't need a real database and should stay fast.

**Rationale:** Matches existing convention precisely (one InMemory correctness-test class per repository, one separate Postgres-backed `SqlShapeTests` class per query needing round-trip/shape verification). Mixing a Testcontainers dependency into the fast InMemory class would slow down the whole file and is inconsistent with how Photobank/Purchase/Article already split this.

## Implementation Guidance

### Directory / Module Structure
No new directories or modules. Two files change, one file is added:

- **Modify**: `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — rewrite `GetSyncStatsAsync` body only (lines 35–58), per the spec's FR-1 code sample. No using-statement changes needed beyond what's already imported (`Microsoft.EntityFrameworkCore` is already in scope).
- **Modify**: `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` — extend `GetSyncStatsAsync_WithVariousInvoices_ReturnsAccurateStats` (or add a sibling `[Fact]`) to assert `LastSyncTime`, per FR-2. Keep it InMemory-backed like the rest of the file.
- **Add**: `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs` — new Postgres/Testcontainers-backed class modeled directly on `PhotobankRepositoryGetTagsSqlShapeTests.cs`:
  - `[Collection("PostgresIntegration")]`, `[Trait("Category", "Integration")]`
  - Constructor takes `PostgresSharedContainerFixture`, calls `_fixture.CreateDatabaseAsync("issuedinvoices")`
  - Minimal hand-written schema: just the `IssuedInvoices` columns the query touches (`Id`, `InvoiceDate`, `IsSynced`, `ErrorType`, `LastSyncTime`) — no need to replicate the full EF-generated schema/FKs, matching how the Photobank example scopes its `CREATE TABLE` to only what's queried
  - `CapturingCommandInterceptor : DbCommandInterceptor` (copy the pattern verbatim; it's private/internal to each test class in the existing examples, not shared — follow that same non-DRY-on-purpose convention rather than introducing a shared test helper for one interceptor class)
  - One `[Fact]` asserting `interceptor.Commands.Should().HaveCount(1)` after calling `GetSyncStatsAsync`
  - One `[Fact]` asserting the returned stats are correct against seeded rows (belt-and-suspenders against the InMemory test, using real Postgres translation)

### Interfaces and Contracts
None change. `IIssuedInvoiceRepository.GetSyncStatsAsync` signature, `IssuedInvoiceSyncStats` DTO, and all caller signatures (`GetIssuedInvoiceSyncStatsHandler`, `InvoicesController`, frontend `useIssuedInvoiceSyncStats`) are untouched — confirmed by reading `IIssuedInvoiceRepository.cs`, which declares only the four repository methods with no other coupling to this method's internals.

### Data Flow
Unchanged end-to-end: `InvoicesController` → MediatR → `GetIssuedInvoiceSyncStatsHandler` → `IIssuedInvoiceRepository.GetSyncStatsAsync(fromDate, toDate, ct)` → EF Core query against `ApplicationDbContext.IssuedInvoices` (Npgsql provider in production) → `IssuedInvoiceSyncStats` DTO → JSON response → frontend hook. The only change is *inside* the repository method: five sequential `await`s against `IQueryable<IssuedInvoice>` collapse to one `await ... FirstOrDefaultAsync` against a `GroupBy(_ => 1).Select(...)` projection, which Npgsql's EF Core provider translates to a single `SELECT COUNT(*), COUNT(*) FILTER (...), ..., MAX(...) FROM ...` statement (Postgres uses `FILTER` or `CASE WHEN` for conditional aggregates — either translation satisfies "one round trip"; the SQL-shape test should assert on round-trip count and keyword presence like the Photobank example, not on the exact `FILTER`/`CASE` syntax, to avoid coupling the test to an EF Core version's specific translation choice).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `GroupBy(_ => 1)` on an empty filtered set returns zero groups, so `FirstOrDefaultAsync` returns `null` — a naive rewrite could NRE on `stats.Total` | Medium | Spec already specifies null-coalescing (`stats?.Total ?? 0` etc.); FR-1's acceptance criteria explicitly requires the zero-match case; covered by existing `oldInvoice`-style out-of-range fixture pattern already in the test file |
| EF Core / Npgsql provider silently falls back to client-side evaluation for part of the grouped projection instead of translating to SQL, defeating the whole point of the change | Medium | The new `SqlShapeTests` class makes this observable in CI (single-command assertion), not just at manual review time — this is the reason Decision 1 rejects "trust InMemory + manual check" |
| `LastSyncTime`'s nested `Where(...).Max(...)` inside the `GroupBy` projection is the one aggregate most likely to be mishandled (needs its own null-filtered `Max`, per spec FR-2) | Low-Medium | FR-2 already mandates a dedicated regression test for this; no additional architectural mitigation needed beyond following the spec as written |
| New Testcontainers-based test class adds ~seconds of container startup to the test run | Low | Shared container fixture (`PostgresSharedContainerFixture`) is already reused across Photobank/Purchase/Article/Bank/etc. — this is marginal incremental cost on infrastructure already paid for elsewhere in CI, not a new cost center |

## Specification Amendments
1. **FR-1's acceptance criterion "verified via query-count assertion or SQL-logging/interceptor in a test using a relational provider"** should be made concrete: implement it as a new `*SqlShapeTests` class using `PostgresSharedContainerFixture` + a local `DbCommandInterceptor`, mirroring `PhotobankRepositoryGetTagsSqlShapeTests.cs` exactly. The spec currently leaves this as an open choice ("or... or... recommended before merging") — it should be a hard requirement, not optional, since the pattern already exists and costs nothing extra to reuse. Recommend upgrading NFR-1's round-trip claim and the "Dependencies" section's hedging language ("accept correctness-only coverage... confirm via manual") to: *"A `*SqlShapeTests` integration test (Postgres via Testcontainers) asserting exactly one command via `DbCommandInterceptor` is required, following the existing `PhotobankRepositoryGetTagsSqlShapeTests` convention."*
2. No other amendments. The spec's FR-1/FR-2 code samples, acceptance criteria, and null-handling semantics are correct and consistent with the actual current implementation read from `IssuedInvoiceRepository.cs` (lines 35–58 match the spec's description exactly).

## Prerequisites
None. No migrations, no config, no new infrastructure — `Testcontainers.PostgreSql` and the `PostgresSharedContainerFixture` are already present and used by five other repository test suites in `backend/test/Anela.Heblo.Tests`. Docker (or Podman, per the fixture's macOS note) must be available in whatever environment runs this new integration test, same as it already must be for the five existing `*SqlShapeTests`/integration test classes — no new environment requirement introduced by this change.
