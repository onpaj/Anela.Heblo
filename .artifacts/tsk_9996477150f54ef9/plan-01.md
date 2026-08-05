# Plan: Unify MCP tool JSON serialization with the REST API's enum-as-string contract

## Summary

Every MCP tool method serializes its MediatR response via a bare `JsonSerializer.Serialize(x)`, which uses `System.Text.Json` defaults (enums as numbers). The REST API registers a global `JsonStringEnumConverter` in `Program.cs:151-154`, so the two surfaces disagree: REST/generated-client consumers see `"type": "Product"`, MCP consumers (AI clients with no human to decode the mapping) see `"type": 8`. Fix: introduce one shared `JsonSerializerOptions` instance carrying the same `JsonStringEnumConverter`, and have both the MVC pipeline and all MCP tool call sites use it, so there is a single source of truth instead of two independently-configured places that can drift again.

## Context

Confirmed by direct inspection of the current tree:
- 23 `JsonSerializer.Serialize(...)` call sites across 6 files use zero-arg (default-options) serialization: `CatalogMcpTools.cs` (8), `KnowledgeBaseTools.cs` (2), `LeafletTools.cs` (1), `ManufactureBatchMcpTools.cs` (4), `ManufactureOrderMcpTools.cs` (3), `MeetingTasksMcpTools.cs` (4, serializing anonymous objects), `UserManagementMcpTools.cs` (1).
- `Program.cs:151-154` adds `JsonStringEnumConverter` only to the MVC `AddJsonOptions` pipeline, which controllers use but MCP tools (registered separately via `AddMcpServices()` in `McpModule.cs`, not part of the MVC pipeline) never touch.
- Every affected DTO carries at least one enum with non-sequential explicit values (e.g. `ProductType`: `Product=8, Goods=1, Material=3, SemiProduct=7, Set=99, UNDEFINED=0`), so the numeric output is not even guessable ordinally — it requires reading source to decode.
- Existing unit tests for these tool classes (`backend/test/Anela.Heblo.Tests/MCP/Tools/*Tests.cs`) round-trip the tool's JSON string back through `JsonSerializer.Deserialize<T>(jsonResult)` **with no options**. Once tool output switches to string enums, that bare deserialize call will throw `JsonException` on the first enum-typed property it hits, regardless of whether the test asserts on that field — this is a hard dependency of the fix, not an incidental cleanup.

## Functional requirements

**FR-1 — Single shared enum-aware `JsonSerializerOptions`.**
Introduce one `JsonSerializerOptions` instance (or one `IOptions<T>`-backed source) containing `JsonStringEnumConverter`, referenced by both the MVC `AddJsonOptions` configuration in `Program.cs` and every MCP tool's serialization calls, so the two surfaces cannot independently drift again.
- Acceptance: grep for `new JsonStringEnumConverter()` in `backend/src/Anela.Heblo.API` returns exactly one occurrence (today: one, in `Program.cs`).
- Acceptance: `Program.cs`'s `AddJsonOptions` call and the MCP serialization path both reference the same options object/definition (not two independently-constructed instances with equivalent-but-separate converter lists).

**FR-2 — All MCP tool serialization calls use the shared options.**
Replace all 23 `JsonSerializer.Serialize(x)` call sites in `CatalogMcpTools.cs`, `KnowledgeBaseTools.cs`, `LeafletTools.cs`, `ManufactureBatchMcpTools.cs`, `ManufactureOrderMcpTools.cs`, `MeetingTasksMcpTools.cs`, `UserManagementMcpTools.cs` with `JsonSerializer.Serialize(x, <sharedOptions>)`.
- Acceptance: `grep -rn "JsonSerializer.Serialize(" backend/src/Anela.Heblo.API/MCP/Tools/*.cs` shows every call passing the shared options argument; zero bare (single-argument) calls remain.
- Acceptance: for a representative tool (`GetCatalogList`), a manual/integration invocation returns `"type": "Product"` (string), not `"type": 8`.

**FR-3 — Enum request-side parsing is unaffected.**
`JsonStringEnumConverter` is bidirectional; MCP tool *inputs* (e.g. `ProductType[]? productTypes` parameters, already accepted as strings per the tool's own `[Description]` text) must continue to parse correctly. This is a serialization-output-only change — no request DTOs or `[McpServerTool]` method signatures change.
- Acceptance: existing request-mapping tests (e.g. `CatalogMcpToolsTests.GetCatalogList_ShouldMapParametersCorrectly`, which asserts `req.ProductTypes[0] == ProductType.Material`) continue to pass unmodified.

**FR-4 — Update existing MCP tool tests to deserialize with the same options.**
Every test in `backend/test/Anela.Heblo.Tests/MCP/Tools/*Tests.cs` that does `JsonSerializer.Deserialize<T>(jsonResult)` on a tool's output must pass the same shared `JsonSerializerOptions` (or an equivalent test-local instance with `JsonStringEnumConverter`), or those tests will start throwing `JsonException` the moment enum output becomes string-typed.
- Acceptance: `dotnet test` for `Anela.Heblo.Tests` — all MCP tool test files (`CatalogMcpToolsTests`, `ManufactureBatchMcpToolsTests`, `ManufactureOrderMcpToolsTests`, `MeetingTasksMcpToolsTests`, `UserManagementMcpToolsTests`) pass.
- Acceptance: at least one test per affected tool class asserts the *string* form of an enum value appears in the raw JSON (e.g. `Assert.Contains("\"type\":\"Product\"", jsonResult)` or equivalent deserialized-string assertion), so a future regression back to numeric output is caught.

**FR-5 — No behavior change to REST API output.**
The REST/MVC JSON contract (`Program.cs` `AddJsonOptions`) must serialize identically before and after this change — this is a refactor of *where* the options come from, not a change to MVC behavior.
- Acceptance: any existing REST API integration/contract test suite that asserts enum-as-string output continues to pass unmodified.

## Non-functional requirements

- **No new dependencies.** `JsonStringEnumConverter` is already part of `System.Text.Json.Serialization`; no package changes.
- **No per-call-site divergence.** The fix must be centralized (one options definition, referenced everywhere) — per the task's explicit instruction not to hand-fix call sites piecemeal. A future new MCP tool or new enum should get correct behavior "for free" by following the established pattern (e.g. injecting the shared options / a base class), not by remembering to pass a options object correctly by hand each time.
- **No performance concern.** `JsonSerializerOptions` should be a cached singleton/static (not re-constructed per call) — this is already implied by "shared options" but worth stating so implementation doesn't accidentally allocate a new `JsonStringEnumConverter` per request.

## Data model

No data model changes. This is a serialization-layer fix; no entities, DTOs, or enum definitions change. The relevant existing shapes:
- `JsonSerializerOptions` (framework type) — the object being centralized.
- MCP tool classes (`CatalogMcpTools`, `ManufactureOrderMcpTools`, `ManufactureBatchMcpTools`, `KnowledgeBaseTools`, `LeafletTools`, `MeetingTasksMcpTools`, `UserManagementMcpTools`) — the 6 files whose serialization call sites change.
- Enums affected transitively (confirmed to exist, not exhaustively audited here — architecture/dev step should do the full inventory): `ProductType`, `ManufactureOrderState`, `ManufactureType`, `ErpManufactureType`, `ProposedTaskStatus`, `MeetingTranscriptStatus`, and any other enum reachable from a response DTO returned by an `[McpServerTool]` method.

## Interfaces

- **MCP tool responses** (`[McpServerTool]` methods returning `Task<string>` of JSON): output shape changes for every enum-typed property, from numeric (`8`) to string (`"Product"`). This is the entire point of the fix — MCP clients that may have started depending on numeric values (undocumented, unintended) will see a breaking change in that undocumented behavior. Since MCP is consumed by AI clients that read field names/values contextually rather than hardcoded numeric parsing, this is expected to be a pure improvement, but worth noting as a discoverable behavior change (see Open questions).
- **MCP tool requests**: unchanged — string enum input already works today via the SDK's own parameter binding (independent of the `JsonSerializer.Serialize` calls being fixed).
- **REST API**: unchanged externally; internally, `Program.cs`'s `AddJsonOptions` call now references the shared options definition instead of constructing its own inline `JsonStringEnumConverter`.

## Dependencies and scope

**In scope:**
- Introducing the shared `JsonSerializerOptions` (or equivalent shared configuration point).
- Updating `Program.cs` to reference it.
- Updating all 23 MCP tool serialization call sites across the 6 tool files.
- Updating the 5 existing MCP tool test files to deserialize with matching options where they currently don't.

**Out of scope:**
- Changing any enum definitions or their explicit numeric values.
- Changing MCP tool method signatures, parameter names, or `[Description]` text.
- Auditing/fixing other JSON serialization surfaces outside MCP + REST (e.g. Hangfire job payloads, external API clients) — not mentioned in the reported issue and not touched by `Program.cs:151-154` today either.
- Adding new tests beyond what's needed to verify the enum-as-string contract (FR-4) — this is a targeted fix, not a test-coverage expansion project.

**Depends on:**
- `System.Text.Json.Serialization.JsonStringEnumConverter` (already in use, no new dependency).
- `ModelContextProtocol` SDK's `[McpServerToolType]`/`[McpServerTool]` attribute model (unchanged, just consumed).

## Rough plan

1. **Design the shared options.** Decide the concrete mechanism (see Open questions — recommendation: a small static class, e.g. `Anela.Heblo.API.Infrastructure.Json.AppJsonSerializerOptions`, exposing a single cached `JsonSerializerOptions` instance with `JsonStringEnumConverter`, since MVC's `AddJsonOptions` accepts a raw `JsonSerializerOptions` and MCP tools can reference the same static instance without needing DI plumbing).
2. **Wire `Program.cs`** to add the converter to that shared instance rather than constructing an inline anonymous one (or, if the static-instance approach is chosen, register the converter once in the static initializer and have `Program.cs` add the same instance's converters to the MVC options — whichever keeps exactly one `new JsonStringEnumConverter()` in the codebase).
3. **Update all 23 call sites** in the 6 MCP tool files to pass the shared options into `JsonSerializer.Serialize(...)`.
4. **Update the 5 existing MCP tool test files** to deserialize tool output with the same shared options, and add/adjust at least one assertion per tool class that verifies string (not numeric) enum output, per FR-4.
5. **Validate:** `dotnet build`, `dotnet format`, full `dotnet test` run for `Anela.Heblo.Tests` (with particular attention to the MCP tool test files and any REST API JSON contract tests), and a manual MCP tool invocation (e.g. via the MCP inspector or an integration test) confirming a `ProductType` field round-trips as `"Product"` rather than `8`.

## Open questions

- **Mechanism for sharing the options instance.** Two viable approaches:
  - (a) A static/singleton `JsonSerializerOptions` (e.g. `AppJsonSerializerOptions.Default`) referenced by both `Program.cs` and all MCP tools directly, no DI needed.
  - (b) Inject `IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>` into each MCP tool class constructor and use `.Value.JsonSerializerOptions`, guaranteeing MCP always uses whatever the MVC pipeline is actually configured with, at the cost of adding a constructor dependency (and mock setup) to 6 tool classes and their test fixtures.
  - **Default assumption for this plan: approach (a)** — a static shared instance — since it avoids touching 6 constructors and their existing test setups, and the task's own suggested direction says "a shared options instance," which reads as (a). Flag for the architecture step to confirm or override.
- **Anonymous object serialization in `MeetingTasksMcpTools.cs`.** Four call sites serialize `new { ... }` anonymous types rather than named DTOs. `JsonStringEnumConverter` works identically on anonymous types (it operates on the property's static enum type via reflection), so no special-casing is expected, but worth a explicit test since this file's shape differs from the rest.
- **Backward compatibility for existing MCP consumers.** If any current AI client integration has hardcoded numeric enum parsing against the current (buggy) MCP output, this fix breaks it silently from that client's perspective. Given the task frames the numeric output as the bug and MCP is described as feeding machine consumers that expect the same contract as the REST API, this plan treats the string-enum output as strictly correct and does not add a transition/compat path. Flag if any known external MCP consumer needs coordination before this ships.
