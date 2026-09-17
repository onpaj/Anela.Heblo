# Implementation: full-solution-verification

## What was implemented

This is a verification-only task (no files created/modified). Ran all six steps from the task context against the current branch to confirm the `IEshopOrderClient` shrink + `IShoptetOrderTestClient` split (tasks 1-7 of this feature) is complete, compiles cleanly, and introduces no regressions.

## Files created/modified

None. Verification only, per the task's own Step 6 instruction ("no commit needed for this task").

## Tests

Ran the full backend build, format check, the full Application unit test suite, a repo-wide `IEshopOrderClient` grep, and the full adapter integration test project. No test files were added or changed by this task.

## How to verify

1. **Full backend build** — `dotnet build` from repo root (the solution `Anela.Heblo.sln` lives at the repo root, not under `backend/`; the task context's `cd backend && dotnet build` command does not resolve a project/solution there, so this step was run from root instead, which is functionally equivalent per `docs/development/setup.md`).
   Result: **Build succeeded, 0 errors**, 256 pre-existing nullable-reference warnings (unrelated to this feature, present across many unrelated files) across every backend project.

2. **Format check** — `dotnet format --verify-no-changes` from repo root.
   Result: **exit code 0, no output** — no formatting violations.

3. **Full Application unit test suite (FR-5 regression check)** — `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`.
   Result: `Failed: 110, Passed: 7282, Skipped: 4, Total: 7396`.
   - All 110 failures are `System.ArgumentException: Docker is either not running or misconfigured` from Testcontainers-backed Postgres integration tests (e.g. `BankStatementImportRepositoryIntegrationTests`, `LeafletDocumentRepositoryPagedTests`, `GridLayoutRepositoryUpsertIntegrationTests`, etc.) — this sandbox has no Docker daemon. None of these failing classes touch `IEshopOrderClient`/`IShoptetOrderTestClient` or any Shoptet-orders code; they are a pre-existing environment limitation, not caused by this change.
   - The seven test classes this task calls out by name — `BlockOrderProcessingHandlerTests`, `ScanPackingOrderHandlerTests`, `ScanPackingOrderHandlerPackagePersistenceTests`, `CompletePackingOrderHandlerTests`, `CompleteDeliveredOrdersJobTests`, `PrintExpeditionOrderHandlerTests`, `ModuleBoundariesTests` — have **zero** failures in the run (confirmed via targeted grep against the failure list); none of them mock/stub the 4 relocated methods, matching the planning-time analysis.

4. **Grep for stray `IEshopOrderClient` references** — `cd backend && grep -rn "IEshopOrderClient" --include="*.cs" . | grep -v "IShoptetOrderTestClient"`.
   Result: every remaining hit is either the interface's own declaration (`IEshopOrderClient.cs`, now 7 methods: `GetOrderStatusIdAsync`, `UpdateStatusAsync`, `GetEshopRemarkAsync`, `UpdateEshopRemarkAsync`, `AppendEshopRemarkAsync`, `ListOrdersByStatusAsync`, `MarkAsPackedAsync`), a DI registration, or a consumer (`ScanPackingOrderHandler`, `CompletePackingOrderHandler`, `BlockOrderProcessingHandler`, `CompleteDeliveredOrdersJob`, `PrintExpeditionOrderHandler`, `ShoptetApiExpeditionListSource`, and their test doubles/mocks) that only needs the 7 surviving methods. No line injects `IEshopOrderClient` solely to call one of the 4 relocated methods (`CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`) — FR-4's acceptance criterion holds.

5. **Adapter integration test project (final compile/DI-resolution gate)** — `cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj`.
   Result: `Failed: 13, Passed: 85, Skipped: 1, Total: 99`. Compiles and resolves DI for both `IEshopOrderClient` and `IShoptetOrderTestClient` cleanly (a DI resolution failure from a missing/duplicate registration would have surfaced as every test in the collection failing at fixture construction, which did not happen — 85 tests in the same shared collection passed). All 13 failures are pre-existing environment/credential gates unrelated to the interface split:
     - `PickingListIntegrationTests.PrintPickingList_ProducesPdfs_ForRecentOrders` (touched by task 7) — `ShoptetTestGuard.Assert` throws "Integration test must not run against live environment. Set Shoptet:IsTestEnvironment=true" — this sandbox's `appsettings`/user-secrets aren't configured as a Shoptet test store.
     - `ShoptetTestEnvironmentHydrationTests.*` (6 tests, touched by task 6) — its constructor unconditionally reads `Shoptet:StatusId:EXP`/`Shoptet:StatusId:PACK` from configuration and throws `InvalidOperationException` if absent. Confirmed via `git show 3204a6c7` that this constructor's config-throw block is untouched by this feature's changes — only the `_testClient` field and its 3 call sites were added/rewired. This is pre-existing, config-gated behavior, not a regression.
     - `ShoptetApiInvoiceSourceIntegrationTests.*` (3 tests) and `ShoptetStockClientIntegrationTests.*` (3 tests) — unrelated files (invoices, stock), failing on a 401 "Shoptet API token is invalid or expired" and a placeholder stock URL respectively. Neither file was touched by this feature.
     - `BlockOrderProcessingIntegrationTests` (touched by task 5) had **zero** failures — its `SHOPTET_BLOCK_ORDER` env-var self-skip works as designed.
   No new failures are attributable to this change; the failure set is entirely explained by missing live-Shoptet credentials/config in this sandbox (per `docs/integrations/shoptet-api.md`: "No sandbox — every call hits a live store").

6. **No commit made** — this task is verification-only per its own Step 6, and no fix was required (nothing failed that traces back to the interface split).

## Notes

- The task context's Steps 1-2 assumed `cd backend && dotnet build`/`dotnet format` — the solution and format config are actually rooted at the repo root, so both commands were run from there instead (repo root `dotnet build`/`dotnet format --verify-no-changes`), which is the command form `docs/development/setup.md` documents. Functionally identical outcome (whole backend, all projects).
- All observed test failures across both suites (123 total: 110 Application + 13 Adapter) are attributable to this sandbox lacking Docker and lacking live Shoptet test-store secrets — neither is something this task or feature can or should fix; both are pre-existing, environment-only conditions, confirmed not to overlap with any file this feature's 7 prior tasks touched (except where the touched files' failures were independently traced to unrelated, pre-existing config-throw code via `git show`).
- No code was changed by this task.

## PR Summary
Ran the full-solution verification pass for the `IEshopOrderClient` → `IShoptetOrderTestClient` split: full backend build (0 errors), `dotnet format --verify-no-changes` (clean), the full Application unit test suite (all 7 named regression-sensitive test classes green; the 110 failures are unrelated Postgres-testcontainer tests failing because this sandbox has no Docker), a repo-wide grep confirming no stray `IEshopOrderClient` usage of the 4 relocated methods remains, and the adapter integration test project (DI resolves cleanly for both interfaces; the 13 failures are pre-existing live-Shoptet-credential/config gates, not regressions from this change). No code changes were needed or made.

### Changes
- None (verification-only task).

## Status
DONE
