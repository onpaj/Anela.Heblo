# Implementation: add-code-occupancy-sql-shape-test

## What was implemented

Added a real-PostgreSQL integration test class proving that `TransportBoxRepository.IsBoxCodeActiveAsync`
and `TransportBoxRepository.GetByCodeAsync` — both rewritten in earlier tasks to consume
`TransportBoxStateRules.OccupiesCodePredicate` — actually translate to server-side SQL against Npgsql,
rather than only "working" under `UseInMemoryDatabase`'s in-memory LINQ evaluation. This closes the one
gap called out by Amendment A2: a negated `Contains()` over a `HasConversion<string>()` enum inside an
`ORDER BY` (the occupancy-first sort in `GetByCodeAsync`) was not exercised anywhere else in `backend/src`.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs` — new
  integration test class. No production files were touched.

## Tests

Three `[Fact]`s in the new class, all `[Collection("PostgresIntegration")]` + `[Trait("Category", "Integration")]`:

1. `IsBoxCodeActiveAsync_TranslatesServerSide_ForQuarantineBox` — seeds a `Quarantine` box holding
   `B001`, asserts the call returns `true`, emits exactly one SQL statement, and that statement
   references `"State"` under a negation combined with set membership (`IN` or `= ANY(...)` — the exact
   Npgsql rendering is intentionally not pinned).
2. `GetByCodeAsync_EmitsOrderByOnState_AndDoesNotThrow` — asserts the call completes without
   `InvalidOperationException` (the signal an untranslatable `ORDER BY` would throw) and that the emitted
   SQL contains an `ORDER BY` referencing `"State"`.
3. `GetByCodeAsync_ResolvesToOccupyingBox_WhenMultipleBoxesShareCode` — seeds a `Quarantine` box first
   (lower `Id`), then a `Stocked` box second (higher `Id`), both holding `B001`, and asserts
   `GetByCodeAsync("B001")` returns the `Quarantine` box — proving occupancy outranks recency under the
   real Postgres `DESC` ordering.

Conventions followed (per task context): `CapturingCommandInterceptor` copied verbatim from
`PurchaseOrderRepositoryHistorySqlShapeTests`; DDL for `TransportBoxes` / `TransportBoxItems` /
`TransportBoxStateLogs` copied verbatim from `ChangeTransportBoxStateReceiveAtomicityIntegrationTests`
(`StockUpOperations` omitted, as instructed); seeding goes through the `TransportBox` aggregate +
`SaveChangesAsync` so the `HasConversion<string>()` value converter runs, per
`memory/gotchas/raw-sql-insert-must-match-ef-mapping.md`.

## How to verify

```bash
cd backend && dotnet build   # 0 errors, no new warnings
cd backend && dotnet format Anela.Heblo.sln --verify-no-changes --include test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxRepositoryCodeOccupancySqlShapeTests"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox"
```

## Notes

**Docker is genuinely unavailable in this execution environment** — `docker version` reaches the CLI but
there is no running daemon (`/var/run/docker.sock` does not exist), and starting `dockerd` fails
(`ulimit: error setting limit (Operation not permitted)`, no systemd, no privilege to work around it).
This is confirmed to be an environment-wide limitation, not specific to this new file: the pre-existing
`ChangeTransportBoxStateReceiveAtomicityIntegrationTests` (same `PostgresIntegration` collection) fails
with the identical `DotNet.Testcontainers` "Docker is either not running or misconfigured" error.

As a result **the three new integration tests are unverified against real Postgres in this environment**
and are reported as such per the task's explicit instruction — they were not deleted, skipped, or
weakened. What *was* verified in this environment:

- `dotnet build`: 0 errors, no new warnings attributable to this file.
- `dotnet format Anela.Heblo.sln --verify-no-changes --include <this file>`: clean (exit 0).
- The full non-Integration TransportBox surface (`--filter "FullyQualifiedName~TransportBox&Category!=Integration"`):
  222/222 passed, confirming this addition does not regress anything else in the module.
- Running the new test class alone reproduces the expected-in-this-sandbox `Docker is either not running
  or misconfigured` failure at `PostgresSharedContainerFixture` construction — i.e. the test fails for the
  same environment reason as the existing integration suite, not for a defect in the new test's logic.
  Both the query shape (via direct code reading of `TransportBoxRepository` / `TransportBoxStateRules`)
  and the seeding path (aggregate methods `Open`/`ToQuarantine`/`AddItem`/`ToTransit`/`Receive`/`ToPick`,
  all pre-existing) were manually traced to confirm the assertions match the actual implementation.
- CI does not close this gap either: PR CI runs `--filter "Category!=Playwright&Category!=Integration"`,
  so these tests will only execute where Docker is actually available (nightly E2E infra or a developer
  machine).

Per the task's escape hatch: this is stated explicitly rather than declaring the feature done on the
strength of the InMemory tests alone.

## PR Summary
Adds a PostgreSQL-backed integration test proving the two rewritten transport-box code-occupancy queries
(`IsBoxCodeActiveAsync`'s `WHERE` and `GetByCodeAsync`'s `ORDER BY`) translate to real server-side SQL
against Npgsql rather than only succeeding under EF Core's InMemory provider, closing the one gap where
this feature could reach staging broken. No production code changed.

### Changes
- `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs` — new `[Collection("PostgresIntegration")]` test class with three facts covering the `WHERE` translation, the `ORDER BY` translation, and end-to-end resolution order against real Postgres.

## Status
DONE_WITH_CONCERNS
