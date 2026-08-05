# Review: Unify MCP tool JSON serialization with the REST API's enum-as-string contract

## Verdict: done

## What was checked

1. **Diff inspection** (`git show dcd92d05`) — read every changed hunk in full:
   - New `Anela.Heblo.API.Infrastructure.Json.McpJsonOptions` static class with a single `JsonSerializerOptions` instance carrying `JsonStringEnumConverter`.
   - `Program.cs`'s MVC `AddJsonOptions` now copies converters from `McpJsonOptions.Default` instead of constructing its own `JsonStringEnumConverter` — REST/MVC behavior is unchanged (same converter type ends up registered), and the two surfaces can no longer independently drift.
   - All 23 `JsonSerializer.Serialize(...)` call sites across the 6 MCP tool files (`CatalogMcpTools.cs`, `KnowledgeBaseTools.cs`, `LeafletTools.cs`, `ManufactureBatchMcpTools.cs`, `ManufactureOrderMcpTools.cs`, `MeetingTasksMcpTools.cs`, `UserManagementMcpTools.cs`) now pass `McpJsonOptions.Default`, including the anonymous-object multi-line calls in `MeetingTasksMcpTools.cs`.
   - All corresponding test-side `JsonSerializer.Deserialize<T>(...)` calls across the 7 test files updated to use `McpJsonOptions.Default` symmetrically.
2. **Grep verification** — confirmed zero remaining bare `JsonSerializer.Serialize(` in `MCP/Tools/*.cs` and zero bare `JsonSerializer.Deserialize` in the corresponding test folder; both are fully covered.
3. **Regression guard correctness** — `ManufactureOrderMcpToolsTests.GetManufactureOrders_ShouldMapParametersCorrectly` was reviewed directly: it seeds `ManufactureOrderState.Planned` (an enum with no type-level `[JsonConverter]`, unlike `ProductType`, which design-01 correctly identified as already serializing as a string via a pre-existing type-level attribute and therefore useless as a regression target). The test asserts the raw JSON contains `"state":"Planned"` and that round-tripping preserves the enum. This is a real regression guard: it fails on unpatched code (numeric `0`) and passes with the fix.
4. **Ran the build and tests myself** rather than trusting the development step's report:
   - `dotnet build Anela.Heblo.sln` → 0 errors, 251 pre-existing warnings (none in changed files), matching the claimed baseline.
   - `dotnet test .../Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MCP.Tools"` → **67/67 passed**.
   - `dotnet format Anela.Heblo.sln --verify-no-changes --no-restore` → exit 0, clean.

## Conformance to spec/design/architecture

- Matches design-01.md's approved approach exactly: single static `McpJsonOptions.Default`, both Program.cs and all MCP call sites route through it, no per-call-site hand-fixing — this was an explicit requirement in the original issue ("Do not hand-fix per call site; centralize the options"), satisfied.
- Fixes the concrete bug described in the issue: MCP payloads now serialize enums (e.g. `ManufactureOrderState`) as strings, matching the REST/generated-client contract, instead of opaque non-sequential numeric values.
- No scope creep — changes are confined to the serialization/deserialization call sites and the new options class; no unrelated refactoring.

## Non-blocking notes

- The `AccessMatrixGen` tool throws an unhandled `JsonException` during the build's post-build step in this sandbox (unrelated pre-existing environment issue, not caused by this change — build still reports 0 errors and completes).
- Consistent with architecture-01.md's one flagged note, the test-file list matches design-01's corrected 7 files (not plan-01's original 5), which the implementation followed correctly.

No functional requirement is unmet, no architecture conflict, no missing required tests, no correctness bug found.

```json
{"outcome": "done", "summary": "Verified the full diff (McpJsonOptions.cs, Program.cs, all 23 MCP serialize call sites across 6 files, all 20 deserialize call sites across 7 test files, and the ManufactureOrderState regression guard) against design-01/architecture-01, and independently ran build (0 errors), MCP.Tools tests (67/67 passed), and dotnet format --verify-no-changes (clean). Implementation matches the approved design, centralizes enum serialization as required, and the regression test is well-targeted and meaningful."}
```
