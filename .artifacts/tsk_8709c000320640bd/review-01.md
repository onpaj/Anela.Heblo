# Review: fold Logistics→Purchase into the shared `Rules()` theory

## Verdict: done

## What was checked

Read `plan-01.md`, `design-01.md`, `architecture-01.md`, `development-01.md`, and the
actual diff (`git diff main -- backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`).

Diff (verified directly, not just from the development report):
- Adds one `ModuleBoundaryRule` row, `Name: "Logistics -> Purchase"`, with
  `InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Logistics"`, the same
  three forbidden prefixes (`Domain/Application/Persistence.Features.Purchase`), and
  an inline empty `HashSet<string>(StringComparer.Ordinal)` allowlist — placed
  immediately after the "Logistics -> Catalog" row.
- Deletes the `[Fact] Logistics_types_should_not_reference_Purchase_owned_namespaces`
  method in full (forbidden-prefix array, empty `logisticsAllowlist`, the nested
  `IsLogisticsForbidden` duplicate of `IsForbidden`, the enumeration/violation loop,
  and the assertion).
- No other rule, allowlist, or shared helper (`IsForbidden`, `EnumerateReferencedTypes`,
  `ExpandGenerics`, the theory method) is touched. `git diff main --stat` confirms the
  only non-artifact file touched is `ModuleBoundariesTests.cs` (11 insertions / 70
  deletions) — no production code.

This matches the task's "Suggested direction" exactly: one data row replacing the
bespoke Fact + duplicated helper, enforced by the pre-existing shared theory.

## Independent verification performed this session

- `dotnet build Anela.Heblo.sln` → **0 errors** (pre-existing warning count unchanged;
  the one new warning line is the known unrelated `Anela.Heblo.AccessMatrixGen`
  sandbox `MSB3073` issue, not caused by this change).
- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
  → **34/34 passed**, including the new theory case
  `Consumer_types_should_not_reference_provider_owned_namespaces(rule: ModuleBoundaryRule
  { Name = Logistics -> Purchase, ... })`. No `Logistics_types_should_not_reference_Purchase_owned_namespaces`
  Fact appears in the run — confirms clean removal and that the new row reproduces a
  passing result (no violation exists today, same as before).
- `grep -n "IsLogisticsForbidden\|Logistics_types_should_not_reference_Purchase"` over
  the file → no matches.
- Read the surrounding `Rules()` rows (lines ~403–460) directly: the new row's
  formatting (indentation, trailing comma, blank-line separation) matches the
  established convention identically to adjacent rows.
- `dotnet format --verify-no-changes` repeatedly hung in this sandbox (MSBuild
  workspace load stalled at near-zero CPU across three attempts, even after
  `dotnet build-server shutdown`) — an environment issue, not a code issue. The
  development step's own log reports this same command returned exit 0 with no
  formatting drift, and direct visual inspection of the diff shows no formatting
  deviation from house style, so this is not blocking.

## Conformance to plan/design/architecture

- FR-1 (remove Fact + duplicated helper): met.
- FR-2 (add equivalent `Rules()` row, same placement guidance): met.
- FR-3 (preserve enforcement strength — same prefixes, same assembly default, same
  empty allowlist): met and verified via passing test run.
- Out-of-scope items (other rules, shared algorithm, production code) were not
  touched, per `git diff --stat`.
- No real violation was newly surfaced (all 34 cases pass), so no allowlist-workaround
  or scope creep occurred.

No functional requirement is unmet, no architecture conflict, no required test is
missing (this is itself test-infrastructure consolidation — the shared theory now
covers the case), and no correctness bug was found.
