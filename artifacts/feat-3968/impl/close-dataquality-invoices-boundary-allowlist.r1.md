# Implementation: close-dataquality-invoices-boundary-allowlist

## What was implemented

Emptied the `DataQualityInvoicesAllowlist` in `ModuleBoundariesTests.cs`, replacing its 21-line
block (justification comment + 7 allowlisted `consumer -> provider` entries) with a single-line
empty `HashSet<string>` declaration and a new comment explaining why the allowlist is now closed —
mirroring the existing empty `LeafletAllowlist`/`ArticleAllowlist`/`SmartsuppKnowledgeBaseAllowlist`
pattern in the same file. This makes the `"DataQuality -> Invoices"` module-boundary rule (registered
in the `Rules` `MemberData`, itself untouched) a hard, zero-tolerance CI gate: the reflection-based
architecture test now fails immediately if any `Anela.Heblo.Application.Features.DataQuality.*` type
ever references `Anela.Heblo.Domain.Features.Invoices` / `Anela.Heblo.Application.Features.Invoices`
/ `Anela.Heblo.Persistence.Invoices` again. `DataQualityCatalogAllowlist` immediately above it (the
separate, out-of-scope `ProductPairingDqtComparer -> Catalog` violation) was left completely
untouched, as specified.

This closes out the escape hatch left by the previous task in this pipeline, which had already
rerouted `IInvoiceShoptetSource`/`IInvoiceErpClient`/`InvoiceDqtComparer` onto DataQuality-owned
`DqtInvoiceSnapshot`/`DqtInvoiceItem`/`DqtInvoiceSourceQuery` types and moved the
Invoices-domain-type mapping into `InvoiceShoptetSourceAdapter`/`InvoiceErpClientAdapter`
(`Invoices.Infrastructure`) via `InvoiceDqtSnapshotMapper` — leaving zero actual references from
DataQuality into the Invoices domain, with only the allowlist itself remaining to close.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — replaced the 7-entry
  `DataQualityInvoicesAllowlist` (plus its justification comment) with an empty `HashSet<string>`
  and a comment recording why it's now empty. No other part of the file (including the
  `DataQualityCatalogAllowlist` immediately above it and the `"DataQuality -> Invoices"`
  `ModuleBoundaryRule` registration further down) was changed.

## Tests

- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` —
  `Consumer_types_should_not_reference_provider_owned_namespaces` theory, all 32 `Rules` cases
  (including `"DataQuality -> Invoices"` now running against a fully empty allowlist).
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — full test suite (6721 tests), run to
  confirm no regressions from the change.

Actual results:

- `dotnet test ... --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces"`:
  `Passed! - Failed: 0, Passed: 32, Skipped: 0, Total: 32`
- `dotnet build`: `0 Error(s)` (94 pre-existing warnings, unrelated to this change).
- `dotnet format --verify-no-changes`: exit code 0, no formatting changes needed.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (full suite):
  `Failed: 105, Passed: 6612, Skipped: 4, Total: 6721`. All 105 failures are the identical
  `System.ArgumentException: Docker is either not running or misconfigured` error from
  Testcontainers, spread across 18 pre-existing integration/SQL-shape test classes (e.g.
  `LeafletRepositoryIntegrationTests`, `KnowledgeBaseRepositoryIntegrationTests`,
  `BankStatementImportRepositoryIntegrationTests`, `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests`,
  etc.) — a pre-existing sandbox environment limitation (no Docker daemon available here), not a
  regression from this change. Verified via `grep -c "Docker is either not running"` (15 matches in
  a truncated tail sample) and a full untruncated log confirming every one of the 105 failures shares
  that exact error message. No `ModuleBoundariesTests` failures appear anywhere in the full run.

## How to verify

```
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces"
dotnet build
dotnet format --verify-no-changes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expect: 32/32 module-boundary rule cases pass; build has 0 errors; format reports no changes; the
full suite shows the same 105 pre-existing Docker/Testcontainers failures (0 new failures) unless a
Docker daemon is available in the environment, in which case those should also pass.

## Notes

- No source-level references from `Anela.Heblo.Application.Features.DataQuality.*` into the Invoices
  namespaces existed at the start of this task (confirmed by the prior task's artifact and by this
  task's own clean test run) — this task was purely the mechanical allowlist closure plus
  verification, exactly as scoped.
- `DataQualityCatalogAllowlist` and the `"DataQuality -> Invoices"` `ModuleBoundaryRule` registration
  were left untouched, per the task's explicit instruction.
- `artifacts/feat-3968/state.json` shows as modified in `git status` (pipeline-tooling-managed file,
  same as noted by the previous task in this pipeline) and was deliberately excluded from staging/
  commit — only `ModuleBoundariesTests.cs` was added and committed.
- This sandbox environment is unusually slow for `dotnet build`/`dotnet test` (multi-minute NuGet
  restore/compile turnaround observed, consistent with the previous task's notes) — none of this
  affected correctness of the results, only wall-clock time.

## PR Summary

Closes the `DataQuality -> Invoices` allowlist in `ModuleBoundariesTests.cs` down to empty, turning
that module-boundary check into a hard CI gate (matching the already-closed Leaflet/Article/Smartsupp
allowlists). The 7 previously-allowlisted entries were all consequences of DataQuality's contracts
and `InvoiceDqtComparer` returning/consuming Invoices-domain types directly; a prior task in this
pipeline already rerouted them onto DataQuality-owned DTOs and moved the domain-type mapping into the
Invoices-side adapters, so this change is a pure allowlist removal plus verification — no production
code changed.

### Changes
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — `DataQualityInvoicesAllowlist` emptied (7 entries + comment removed, replaced with an empty set and an updated comment).

## Status
DONE
