# Implementation: extend-invoice-classification-fixture

## What was implemented
Added a `companyVat` parameter (default `""`) to the shared `InvoiceClassificationFixtures.CreateInvoice` test helper, inserted immediately before the trailing `params string[] itemNames` (the only legal position for a parameter before a `params` array). The constructed `ReceivedInvoice` now sets `CompanyVat = companyVat`. This is additive test infrastructure enabling later tests (e.g. for `VatClassificationRule`) to build invoices with a specific `CompanyVat` value; existing call sites are unaffected since the new parameter has a default matching `ReceivedInvoice.CompanyVat`'s own default (`string.Empty`).

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/TestHelpers/InvoiceClassificationFixtures.cs` — `CreateInvoice` signature and body updated to accept and set `companyVat`. `CreateRule` and the file's `using`/namespace lines were left untouched.

## Tests
- `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — **Build succeeded**, 0 errors (244 pre-existing warnings unrelated to this change).
- `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — Total: 6677, Passed: 6568, Failed: 105, Skipped: 4.
  - All 105 failures are `System.ArgumentException: Docker is either not running or misconfigured` (testcontainers/Postgres integration tests — confirmed via `grep -c "Docker is either not running or misconfigured"` = 105, matching the total failure count exactly). This is a pre-existing sandbox limitation (no Docker daemon available here), unrelated to this change.
  - Filtered run `--filter "FullyQualifiedName~InvoiceClassification"`: Total 97, Passed 94, Failed 3 — the 3 failures are the same Docker/testcontainers issue (`ClassificationRuleRepositoryReorderIntegrationTests`, a Postgres-backed integration test class), not part of the fixture's unit-test call sites.
  - No test involving `InvoiceClassificationFixtures.CreateInvoice` (all four call-site classes: `AmountClassificationRuleTests`, `CompanyNameClassificationRuleTests`, `DescriptionClassificationRuleTests`, `ItemDescriptionClassificationRuleTests`, plus `RuleEvaluationEngineTests`) failed or changed behavior — confirming the new optional parameter is fully backward compatible.

## How to verify
```bash
cd backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"
```
Expect the build to succeed with 0 errors, and the filtered test run to show only Docker-related integration-test failures (if Docker is unavailable) with all `*ClassificationRuleTests` and `RuleEvaluationEngineTests` unit tests passing.

## Notes
No deviations from the task spec. The full (unfiltered) `dotnet test` run shows 105 pre-existing failures, all attributable to the sandbox lacking a Docker daemon (required by several unrelated Postgres-testcontainer integration test suites: Smartsupp, GridLayouts, TransportBoxRepository SQL-shape tests, ClassificationRuleRepositoryReorderIntegrationTests, KnowledgeBase, etc.) — none relate to this fixture change.

## Status
DONE
