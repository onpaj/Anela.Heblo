All tasks complete. Here is the output artifact:

---

# Implementation: Reuse `JsonSerializerOptions` in OrgChartService

## What was implemented

Extracted the per-call `JsonSerializerOptions` allocation in `OrgChartService.GetOrganizationStructureAsync` into a `private static readonly JsonSerializerOptions JsonOptions` field. The field is initialized once at type-load time and reused on every request, eliminating a per-invocation allocation and allowing the deserializer's internal reflection metadata cache to be built once and reused.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` — added `private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };` as the first class member; removed the 5-line local `var options` block; updated `JsonSerializer.Deserialize<OrgChartResponse>(content, options)` → `(content, JsonOptions)`.

## Tests

No tests were added or modified. The spec explicitly states none are required for this class (no existing tests cover `OrgChartService` directly). The full backend test suite (`dotnet test`) passed: 61 passed, 1 skipped (pre-existing Shoptet DI issue unrelated to this change).

## How to verify

```bash
# Confirm build is clean
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj

# Confirm format is clean
dotnet format backend/Anela.Heblo.sln --verify-no-changes \
  --include backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs

# Confirm test suite passes
dotnet test backend/Anela.Heblo.sln --no-restore

# Inspect the diff
git diff HEAD~1 -- backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs
```

## Notes

- Field naming (`JsonOptions`, PascalCase, no underscore prefix) matches the dominant convention used across 12+ existing call sites in this codebase.
- Static field is placed before instance fields with one blank line separator, consistent with the pattern at `OpenMeteoWeatherForecastClient.cs`.
- Commit: `c3e7eae1` — `refactor(orgchart): reuse static JsonSerializerOptions in OrgChartService`.

## PR Summary

Eliminated a per-call `JsonSerializerOptions` allocation in `OrgChartService` by extracting it to a `private static readonly` field. `JsonSerializerOptions` is expensive to construct because it lazily builds internal reflection metadata caches; constructing one per request means rebuilding that cache on every call. The fix aligns `OrgChartService` with the same pattern already used by 12+ other services and adapters in this codebase (e.g., `OpenMeteoWeatherForecastClient`).

### Changes
- `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` — added `private static readonly JsonSerializerOptions JsonOptions` field, removed per-call local variable, updated `Deserialize` call to reference the shared instance.

## Status
DONE