# Development: Unify MCP tool JSON serialization with the REST API's enum-as-string contract

Implemented design-01.md exactly as specified (approved without changes by architecture-01.md).

## Summary

Every MCP tool serialized its response with a bare `JsonSerializer.Serialize(x)` (System.Text.Json defaults → enums as numbers), while the REST API's MVC pipeline serialized enums as strings via a `JsonStringEnumConverter` registered in `Program.cs`. Centralized both surfaces onto one shared `JsonSerializerOptions` instance so they can no longer independently drift.

## Files created

- `backend/src/Anela.Heblo.API/Infrastructure/Json/McpJsonOptions.cs` — new static class exposing `McpJsonOptions.Default`, a `static readonly JsonSerializerOptions` with `JsonStringEnumConverter` in its converters list. Sole place in the codebase that constructs `new JsonStringEnumConverter()`.

## Files changed

**`backend/src/Anela.Heblo.API/Program.cs`** — the MVC `AddJsonOptions` callback now copies converters from `McpJsonOptions.Default.Converters` (via `foreach`, since `JsonSerializerOptions.Converters` has no settable wholesale-assignment) instead of constructing its own `JsonStringEnumConverter`. No behavior change for REST/MVC output — same converter type ends up in the same list.

**6 MCP tool files (23 call sites)** — every `JsonSerializer.Serialize(x)` → `JsonSerializer.Serialize(x, McpJsonOptions.Default)`, plus a `using Anela.Heblo.API.Infrastructure.Json;`:
- `CatalogMcpTools.cs` (8 sites)
- `KnowledgeBaseTools.cs` (2 sites)
- `LeafletTools.cs` (1 site)
- `ManufactureBatchMcpTools.cs` (4 sites)
- `ManufactureOrderMcpTools.cs` (3 sites)
- `MeetingTasksMcpTools.cs` (4 sites — anonymous-object `Serialize(new {...})` calls; closing `});` became `}, McpJsonOptions.Default);`)
- `UserManagementMcpTools.cs` (1 site)

**7 test files** — every `JsonSerializer.Deserialize<T>(json)` → `JsonSerializer.Deserialize<T>(json, McpJsonOptions.Default)`, plus the matching `using`:
`CatalogMcpToolsTests.cs`, `KnowledgeBaseToolsTests.cs`, `LeafletToolsTests.cs`, `ManufactureBatchMcpToolsTests.cs`, `ManufactureOrderMcpToolsTests.cs`, `MeetingTasksMcpToolsTests.cs`, `UserManagementMcpToolsTests.cs`.

**Regression guard (FR-4)** — `ManufactureOrderMcpToolsTests.GetManufactureOrders_ShouldMapParametersCorrectly` now seeds a `ManufactureOrderDto` with `State = ManufactureOrderState.Planned` and asserts both:
- the raw JSON contains `"state":"Planned"` (case-insensitive `Assert.Contains`), and
- the deserialized response's `Orders[0].State` round-trips to `ManufactureOrderState.Planned`.

`ManufactureOrderState` has no type-level `[JsonConverter]` attribute (unlike `ProductType`, which design-01 verified already serializes as a string today via a type-level attribute predating this issue — so a `ProductType`-based assertion would pass on unpatched `main` and prove nothing). This assertion fails without the fix and passes with it, per design-01/architecture-01's explicit test-target correction.

## Verification performed

- `dotnet build Anela.Heblo.sln` — 0 errors (251 pre-existing warnings, none touching changed files).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MCP.Tools"` — 67/67 passed.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Category!=Integration"` — 6046 passed, 2 failed, 4 skipped. Both failures are pre-existing timing-flaky tests unrelated to this change (`DbResiliencePipelineProviderTests.Pipeline_AbortsByTotalTimeBudget` — wall-clock timeout assertion; `CatalogMergeSchedulerTests.ScheduleMerge_BurstOfCalls_CollapseToSingleCallback` — debounce-timing assertion). Neither touches JSON serialization or MCP.
- Full unfiltered run additionally showed 80 failures, all `[Trait("Category","Integration")]` Postgres/Testcontainers-backed tests failing with `Docker API responded with status code=InternalServerError ... Disk quota exceeded` — a sandbox Docker/disk environment issue, not a code regression; confirmed by re-running with `Category!=Integration` (see above), which isolates the 2 real, pre-existing, change-unrelated failures.
- `dotnet format Anela.Heblo.sln --verify-no-changes --no-restore` — clean, no formatting diffs.
- Confirmed invariants by grep: exactly one `new JsonStringEnumConverter()` in the codebase (in `McpJsonOptions.cs`); zero bare `JsonSerializer.Serialize(` calls remain in `MCP/Tools/*.cs`; zero bare `JsonSerializer.Deserialize` calls remain in `test/.../MCP/Tools/*.cs`.

## How to verify

```
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Category!=Integration"
dotnet format Anela.Heblo.sln --verify-no-changes --no-restore
```

Manual check of the actual fix: call `ManufactureOrderMcpTools.GetManufactureOrders` (or any MCP tool returning a `ManufactureOrderState`/`ManufactureType`/`ErpManufactureType` field) via the MCP endpoint and confirm the field renders as a string (e.g. `"Planned"`) rather than a number.
