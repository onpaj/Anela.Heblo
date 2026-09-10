# Implementation: update-consumeinventoryresult-test-call-sites

## What was implemented
Replaced the three remaining positional-constructor calls to `ConsumeInventoryResult` in
`AddItemToBoxHandlerTests.cs` with the corresponding static factory methods, matching the
API shape introduced by the prior task:

- `new ConsumeInventoryResult(ConsumeInventoryOutcome.Success)` → `ConsumeInventoryResult.Success()`
- `new ConsumeInventoryResult(ConsumeInventoryOutcome.InsufficientStock)` → `ConsumeInventoryResult.InsufficientStock()`
- `new ConsumeInventoryResult(ConsumeInventoryOutcome.InventoryNotFound)` → `ConsumeInventoryResult.InventoryNotFound()`

No other lines in the file (matcher arguments, assertions, other tests) were touched.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs` — 3 line substitutions inside existing `.Setup(...).ReturnsAsync(...)` chains (lines 175, 222, 260). **Committed.**
- `memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md` — appended a "Confirmed again 2026-09-10" section documenting the `vstest.console.dll`-direct and `dotnet build` + `dotnet test --no-build` fallbacks that worked this session (the gotcha itself, and its primary documented fix, already existed from a prior session). **Left uncommitted** — not part of this task's literal steps; a separate commit for the pipeline/user to make if desired.

## Tests
- `Anela.Heblo.Tests.Features.Logistics.Transport.AddItemToBoxHandlerTests` — 7 tests, all passing after the change (covers box-not-found, no-source-inventory, merge-on-duplicate-lot, successful consume, insufficient-stock, inventory-not-found, and invalid-state-transition scenarios).
- `Anela.Heblo.Tests....ManufactureInventoryReservationAdapterTests` — 7 tests, unaffected by this change, all passing.
- Full solution test suite (`Anela.Heblo.sln`) — 7283 passing / 190 failed / 10 skipped (7483 total) across all test projects; the 190 failures present are pre-existing and unrelated to this change (see Notes).

## How to verify
```bash
cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
grep -rn "new ConsumeInventoryResult(" backend/                      # expect no matches
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj  # expect 0 Error(s)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AddItemToBoxHandlerTests"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureInventoryReservationAdapterTests"
dotnet build Anela.Heblo.sln                                          # expect 0 Error(s)
dotnet test Anela.Heblo.sln                                           # expect only pre-existing Docker/Shoptet/Flexi failures
```

## Notes
- **Environment note (not a code concern):** In this sandbox, `dotnet test` (as opposed to
  `dotnet build`) intermittently stalled indefinitely mid-build — twice, at the exact same
  point (the custom "Generating access matrix artifacts" codegen step invoked while building
  `Anela.Heblo.API`/`Anela.Heblo.Domain`). All build/msbuild-node/VBCSCompiler processes went
  fully idle (futex/epoll wait, zero CPU growth) with no forward progress. Plain `dotnet build`
  of the same project graph completed reliably and quickly (13–32s incremental, ~21s for the
  full solution). Work-arounds used to make progress, in order: (1) ran the specific test
  classes directly via `vstest.console.dll` against the already-built test assembly (fast,
  reliable, matches expected results); (2) for the full-solution regression gate, ran
  `dotnet build Anela.Heblo.sln` first (succeeded, 0 errors) then `dotnet test Anela.Heblo.sln
  --no-build` (succeeded, ran to completion). This is worth a `memory/gotchas/` entry for
  future sessions in this repo/sandbox — the recommended pattern is: build the solution first,
  then `dotnet test --no-build`, and if `dotnet test` (which builds implicitly) hangs, retry
  with `--no-build` rather than waiting indefinitely.
- **Full-suite result interpretation:** `dotnet test Anela.Heblo.sln --no-build` completed with
  190 failures across the whole solution, none touching `AddItemToBoxHandlerTests` or
  `ManufactureInventoryReservationAdapterTests`. All 190 are pre-existing, environment-caused,
  and unrelated to this change:
  - 107 failures: `System.ArgumentException: Docker is either not running or misconfigured`
    (Testcontainers-based Postgres integration tests; no Docker daemon in this sandbox).
  - 70 failures: Flexi integration test fixture DI errors (`FlexiIntegrationTestFixture` /
    `implementationInstance` null) — live-API-dependent fixture, consistent with
    `docs/integrations/shoptet-api.md`-style "no sandbox" adapters.
  - 13 failures: Shoptet adapter tests requiring live API credentials/config
    (missing status ID, invalid/expired token, placeholder URL, "must not run against live
    environment" guard).
  This matches the project's documented reality (no Docker, no live external API access in
  this environment) rather than a regression from this change. This is a behavior-preserving
  refactor confined to test-code call sites; the targeted test classes (steps 4–5 of the task)
  both pass 7/7 with 0 failures, and the build is clean solution-wide.
- `dotnet format` scoped to the touched file produced no output (nothing to reformat); the
  file was re-read afterward and the three substitutions are intact, confirmed via `git diff`
  (exactly 3 insertions / 3 deletions, no other changes).

## Status
DONE_WITH_CONCERNS
