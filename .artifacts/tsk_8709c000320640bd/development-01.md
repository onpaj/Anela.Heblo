# Development: fold Logistics→Purchase into the shared `Rules()` theory

## What changed

Single file: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
(11 insertions, 70 deletions — net removal of 59 lines).

1. **Added one `ModuleBoundaryRule` row** to `Rules()`, placed immediately after the
   "Logistics -> Catalog" row, keeping the two Logistics-as-consumer pairs adjacent:

   ```csharp
   new ModuleBoundaryRule(
       Name: "Logistics -> Purchase",
       InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Logistics",
       ForbiddenNamespacePrefixes: new[]
       {
           "Anela.Heblo.Domain.Features.Purchase",
           "Anela.Heblo.Application.Features.Purchase",
           "Anela.Heblo.Persistence.Purchase",
       },
       Allowlist: new HashSet<string>(StringComparer.Ordinal)),
   ```

   `InspectedAssembly` is omitted, defaulting to `"Anela.Heblo.Application"` — identical
   to what the deleted Fact hard-coded via `Assembly.Load("Anela.Heblo.Application")`.

2. **Deleted** the bespoke `[Fact] Logistics_types_should_not_reference_Purchase_owned_namespaces`
   method in full, including its local `forbiddenPrefixes` array, empty `logisticsAllowlist`,
   and the nested `IsLogisticsForbidden` local function (a verbatim duplicate of the shared
   `IsForbidden` helper).

No other method, allowlist, or the shared `Consumer_types_should_not_reference_provider_owned_namespaces`
theory / `IsForbidden` / `EnumerateReferencedTypes` / `ExpandGenerics` helpers were touched.
The Logistics→Purchase pair is now enforced by the same shared theory as the other ~35
module-pair rows, so any future strengthening of the shared algorithm (e.g. the
declaring-type fallback, added after the fact per the architecture review) automatically
applies to this pair too instead of silently skipping it.

## Verification performed

- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (251 pre-existing warnings,
  none introduced by this change). Note: a separate, unrelated post-build step
  (`Anela.Heblo.AccessMatrixGen`, invoked from `Anela.Heblo.API.csproj`) throws a
  `JsonException` and produces an `MSB3073` **warning** in this sandbox — this is a
  pre-existing environment issue (no `access-matrix.generated.json` / stdin content
  available here) unrelated to the test file change and does not fail the build.
- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
  → `Passed! - Failed: 0, Passed: 34, Skipped: 0, Total: 34`. The new "Logistics -> Purchase"
  theory case passed on first run, confirming behavioral equivalence with the deleted Fact
  (no violation exists today, matching the old Fact's empty allowlist / passing state).
- `grep -n "IsLogisticsForbidden\|Logistics_types_should_not_reference_Purchase" ModuleBoundariesTests.cs`
  → no matches, confirming full removal.
- `dotnet format Anela.Heblo.sln --include backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs --verify-no-changes`
  → exit code 0, no formatting drift.
- `git diff --stat` confirms the change is scoped to exactly the one file, as intended
  (test-only, no production code touched).

## How to verify

```bash
export PATH="$HOME/.dotnet:$PATH"
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Expect `Passed: 34, Failed: 0` with a "Logistics -> Purchase" theory case among them and
no `Logistics_types_should_not_reference_Purchase_owned_namespaces` Fact in the output.
