### task: add-code-occupancy-sql-shape-test

**Files:**
- Create (test): `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs`
- No production file changes.

**Depends on:** `consume-rule-in-transport-box-repository`.

#### Goal

Amendment A2, **mandatory**: prove against real PostgreSQL that both rewritten queries translate server-side. Every other test in this feature runs on `UseInMemoryDatabase`, which evaluates LINQ in memory and will happily "translate" anything — a query Npgsql cannot translate passes there and fails in staging. `Contains` over a `HasConversion<string>()` enum inside a `WHERE` is proven in this codebase (it is what the *old* `IsBoxCodeActiveAsync` did), but the same construct inside an `ORDER BY` is **not exercised anywhere in `backend/src` today**. This is the one way this change can reach staging broken.

#### Context

- Conventions to follow, all already used in this repo:
  - `[Collection("PostgresIntegration")]` (definition: `backend/test/Anela.Heblo.Tests/Common/PostgresIntegrationCollection.cs`) + `[Trait("Category", "Integration")]`.
  - Constructor-injected `PostgresSharedContainerFixture`, `IAsyncLifetime`, and `await _fixture.CreateDatabaseAsync("<hint>")` for an isolated database in the shared `postgres:16` container.
  - A private `CapturingCommandInterceptor : DbCommandInterceptor` overriding `ReaderExecuting` / `ReaderExecutingAsync` and collecting `command.CommandText`, wired via `DbContextOptionsBuilder.AddInterceptors(...)`.
  - Reference shape: `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistorySqlShapeTests.cs` (copy its interceptor class and lifecycle verbatim).
- DDL: copy the `TransportBoxes` / `TransportBoxItems` / `TransportBoxStateLogs` `CREATE TABLE` block verbatim from `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs` (`InitializeAsync`). **All three tables are required** — `GetByCodeAsync` `Include`s both child collections. Note `TransportBoxes."State"` is `text` (`HasConversion<string>()`) while `TransportBoxStateLogs."State"` is `integer` (default enum mapping); the copied DDL already gets this right. `StockUpOperations` is not needed.
- Construct the repository as `new TransportBoxRepository(context, NullLogger<TransportBoxRepository>.Instance)`.
- Seed via the aggregate + `SaveChangesAsync` (so the value converter runs) rather than raw SQL — see `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md`.

#### Implementation steps

- [ ] **Step 1: Create the test class skeleton**

`[Collection("PostgresIntegration")]`, `[Trait("Category", "Integration")]`, namespace `Anela.Heblo.Tests.Repositories`, `IAsyncLifetime` creating the database + DDL + a `CapturingCommandInterceptor`-wired `ApplicationDbContext` in `InitializeAsync`, disposing the context in `DisposeAsync`.

- [ ] **Step 2: Assertion 1 — `IsBoxCodeActiveAsync` translates server-side**

Seed one `Quarantine` box holding `B001`. `_interceptor.Reset()`, call `await _repository.IsBoxCodeActiveAsync("B001")`, then assert:

- the result is `true` (the bug fix, now proven against real Postgres);
- `_interceptor.Commands` contains exactly **one** statement (single round trip, no client-side evaluation);
- that statement references the `"State"` column **and** contains a negation combined with set membership.

**SQL-assertion caveat — this is load-bearing.** Npgsql may render the negated membership either as inlined literals (`NOT ("State" IN ('Closed','Stocked'))` / `"State" NOT IN (...)`) or as a parameterised array (`NOT ("State" = ANY (@__CodeReleasingStates_0))`), because `CodeReleasingStates` is a captured static field rather than an inline constant. The spec's prose says `NOT IN ('Closed','Stocked')` — **do not pin that literal string**, or the assertion will fail on a correct implementation. Match on `"State"` plus a negation plus set membership (`IN` **or** `= ANY`), e.g.:

```csharp
var sql = _interceptor.Commands.Should().ContainSingle().Subject;
sql.Should().Contain("\"State\"");
sql.Should().MatchRegex("NOT\\s*\\(?[^)]*\"State\"|\"State\"\\s+NOT\\s+IN|NOT\\s*\\(\\s*[a-z0-9_.\"]*\"State\"\\s*=\\s*ANY");
```

Prefer a readable pair of `Should().Contain(...)` assertions over a brittle regex if that expresses it more clearly. What is being verified is that translation happens server-side **at all**, not its exact rendering.

- [ ] **Step 3: Assertion 2 — `GetByCodeAsync` emits the occupancy `ORDER BY` and does not throw**

`_interceptor.Reset()`, call `await _repository.GetByCodeAsync("B001")`, assert it completes without `InvalidOperationException` (an untranslatable `ORDER BY` throws here — that is the primary signal) and that the emitted SQL contains an `ORDER BY` referencing the `"State"` column.

- [ ] **Step 4: Assertion 3 — resolution order against real Postgres**

Seed a `Quarantine` box holding `B001` **first** (lower `Id`), then a `Stocked` box holding `B001` (higher `Id`). Assert `GetByCodeAsync("B001")` returns the `Quarantine` box — i.e. `false < true` under `DESC` puts the occupying box first, as designed.

- [ ] **Step 5: Run the integration test (Docker required)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxRepositoryCodeOccupancySqlShapeTests"
```

Expected: all PASS. Docker must be available (`postgres:16` is pulled by `PostgresSharedContainerFixture`) — this is already a prerequisite of the existing `PostgresIntegration` collection, including `ChangeTransportBoxStateReceiveAtomicityIntegrationTests` in this same module.

If Docker is genuinely unavailable in the execution environment, say so explicitly in the completion report and record the test as **unverified** — do **not** delete, skip, or weaken it, and do not declare the feature done on the strength of the InMemory tests alone. Note that PR CI runs `--filter "Category!=Playwright&Category!=Integration"`, so CI will not cover this gap for you.

- [ ] **Step 6: Build and format**

```bash
cd backend && dotnet build && dotnet format
```

#### Acceptance criteria

- The new class carries both `[Collection("PostgresIntegration")]` and `[Trait("Category", "Integration")]`.
- `IsBoxCodeActiveAsync("B001")` against a real `Quarantine` row returns `true` and emits exactly one statement whose text references the `"State"` column under a negated set membership.
- `GetByCodeAsync` completes without `InvalidOperationException` and emits an `ORDER BY` referencing `"State"`.
- With a `Quarantine` box at a lower `Id` and a `Stocked` box at a higher `Id` sharing `B001`, `GetByCodeAsync("B001")` returns the `Quarantine` box.
- The SQL assertions accept **both** the inlined-literal and the `= ANY(...)` parameterised renderings; no assertion pins the exact string `NOT IN ('Closed','Stocked')`.
- No production file is modified by this task.
- `dotnet build` and `dotnet format` succeed with no new warnings.

#### Tests to run

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxRepositoryCodeOccupancySqlShapeTests"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox"
```

The second command intentionally omits `Category!=Integration` so the whole transport-box surface, integration tests included, runs together.

---

