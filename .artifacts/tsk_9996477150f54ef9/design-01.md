# Design: Unify MCP tool JSON serialization with the REST API's enum-as-string contract

No UI surface is involved — this is a backend serialization fix. UX/UI section omitted.

## Verification of plan-01.md against current code

Before finalizing the design, I re-read the current tree (not the plan's snapshot) since CLAUDE.md requires verifying claims before building on them. Two corrections to plan-01.md:

1. **`ProductType` already carries a type-level `[JsonConverter(typeof(JsonStringEnumConverter))]` attribute** (`backend/src/Anela.Heblo.Domain/Features/Catalog/ProductType.cs:5`, present since the `feat: refactoring` commit — predates this issue). System.Text.Json's converter-resolution order is: property-level attribute → `JsonSerializerOptions.Converters` list → **type-level attribute** → built-ins. Because MCP tools call `JsonSerializer.Serialize(x)` with an empty converters list, `ProductType` already resolves to its type-level attribute and serializes as `"Product"`, not `8`, **today**, before this fix. The task's headline evidence (`GetCatalogList` emitting `"type": 8`) does not reproduce against the current tree.
2. **7 test files touch `JsonSerializer.Deserialize`, not 5** — `KnowledgeBaseToolsTests.cs` and `LeafletToolsTests.cs` also deserialize tool output and are equally exposed once other enums switch to string output.

Neither correction changes the plan's diagnosis or direction — `ManufactureOrderState`, `ManufactureType`, `ErpManufactureType`, `ProposedTaskStatus`, and `MeetingTranscriptStatus` (checked directly) all lack any `[JsonConverter]` attribute, so every MCP response carrying one of those still emits raw numbers, and the underlying architecture problem (no centralized options; correctness depends on someone remembering to decorate each new enum) is real and worth fixing centrally. It does change **what a correct regression test must assert on**: a test that round-trips `ProductType` through `GetCatalogList` proves nothing about this fix, because it already passes on `main`. Verification must use an enum with no type-level attribute — `ManufactureOrderState` via `ManufactureOrderMcpTools.GetManufactureOrders` is the chosen target (see Interfaces).

## Component design

### 1. Shared options — `Anela.Heblo.API.Infrastructure.Json.McpJsonOptions`

New static class, placed alongside the existing `Infrastructure/` cross-cutting helpers (`Infrastructure/Hangfire/`, `Infrastructure/Telemetry/`, etc. follow the same static-class-under-`Infrastructure/<Area>/` convention):

```
backend/src/Anela.Heblo.API/Infrastructure/Json/McpJsonOptions.cs
```

```csharp
namespace Anela.Heblo.API.Infrastructure.Json;

public static class McpJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
```

Responsibility: single source of truth for "how MCP tool responses are serialized." Nothing else — no DI, no configuration binding, no per-tool overrides. A `readonly static JsonSerializerOptions` is thread-safe for concurrent `Serialize` calls (documented `System.Text.Json` guarantee once the instance is no longer mutated after first use), so no locking or per-call construction is needed.

**Why static over `IOptions<JsonOptions>` injection (resolves plan-01's open question):** the MCP tool classes are resolved via `WithTools<T>()` and constructed with plain constructor injection (`IMediator`, `ICurrentUserService` today — confirmed in `CatalogMcpTools` and its test `CatalogMcpToolsTests.cs:21-31`, which builds the tool directly with `new CatalogMcpTools(mediatorMock.Object, userServiceMock.Object)`, no DI container). Injecting `IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>` would require:
- adding a constructor parameter to all 7 tool classes,
- updating all 7 test fixtures to construct and pass a mock/real `IOptions<JsonOptions>`,
- and would couple the MCP surface's serialization to the *MVC* options type, which is conceptually backwards — MCP is not an MVC concern.

A static field is simpler, has no lifetime/DI concerns (there is nothing to scope — the options are immutable and process-wide), and is trivially referenceable from both `Program.cs` and every tool file with one `using`.

### 2. `Program.cs` wiring

Change the MVC `AddJsonOptions` callback to reuse the same converter list instead of constructing its own `JsonStringEnumConverter`, so there is exactly one `new JsonStringEnumConverter()` in the codebase (today: one, in `Program.cs:153`; after: one, in `McpJsonOptions`):

```csharp
.AddJsonOptions(options =>
{
    foreach (var converter in McpJsonOptions.Default.Converters)
    {
        options.JsonSerializerOptions.Converters.Add(converter);
    }
});
```

`JsonSerializerOptions.Converters` cannot be assigned wholesale after the MVC options object is constructed by the framework (it's a pre-populated collection, not a settable property), so the loop — copying converter instances by reference — is the mechanism, not a stylistic choice. This keeps MVC's own defaults (camelCase property naming, etc., already configured elsewhere in the ASP.NET Core JSON pipeline) untouched; only the converter list gains the same `JsonStringEnumConverter` instance the MCP surface uses. Functionally this is a no-op for REST output (FR-5): MVC still ends up with a `JsonStringEnumConverter` in its converters list, same as before.

### 3. MCP tool call sites (6 files, 23 call sites)

Every `JsonSerializer.Serialize(x)` becomes `JsonSerializer.Serialize(x, McpJsonOptions.Default)`, plus a `using Anela.Heblo.API.Infrastructure.Json;` in each of the 6 files. No other change to any tool method — parameters, `[Description]` attributes, request-side mapping, and error-throwing (`McpException`) branches are untouched, per FR-3.

Mechanical, uniform edit — same shape at every site:
```diff
- return JsonSerializer.Serialize(response);
+ return JsonSerializer.Serialize(response, McpJsonOptions.Default);
```
including the four anonymous-object call sites in `MeetingTasksMcpTools.cs` (`JsonSerializer.Serialize(new { ... })` → `JsonSerializer.Serialize(new { ... }, McpJsonOptions.Default)`) — `JsonStringEnumConverter` resolves enum-typed properties on anonymous types the same way as named DTOs (reflection over the property's declared type), so no special-casing is needed there.

### 4. Test updates (7 files)

Each `JsonSerializer.Deserialize<T>(jsonResult)` becomes `JsonSerializer.Deserialize<T>(jsonResult, McpJsonOptions.Default)`. `JsonStringEnumConverter` is bidirectional and also accepts the numeric form on deserialize by default, so this change is safe regardless of ordering, but doing it in the same commit as the call-site change keeps the two in sync and avoids a red build in between.

Affected files: `CatalogMcpToolsTests.cs`, `KnowledgeBaseToolsTests.cs`, `LeafletToolsTests.cs`, `ManufactureBatchMcpToolsTests.cs`, `ManufactureOrderMcpToolsTests.cs`, `MeetingTasksMcpToolsTests.cs`, `UserManagementMcpToolsTests.cs`.

One additional assertion (FR-4's regression guard) goes into **`ManufactureOrderMcpToolsTests`**, not `CatalogMcpToolsTests` — see Interfaces below for why.

## Data schemas

No schema changes. Summarizing the shapes actually touched (traced by direct inspection, not the plan's estimate):

**New type** — `Anela.Heblo.API.Infrastructure.Json.McpJsonOptions` (static class, one public field `Default: JsonSerializerOptions`). No DTO, no persisted shape.

**Response payload shape change** (the actual "data schema" affected) — every MCP tool response DTO with an enum-typed property switches that property's JSON representation from number to string. Concretely, confirmed by direct grep, the enums reachable from MCP responses today with **no** type-level `[JsonConverter]` (i.e., the ones this fix actually changes the wire format for):

| Enum | No type-level attribute? | Reached via |
|---|---|---|
| `ManufactureOrderState` | yes | `GetManufactureOrdersResponse`, `GetManufactureOrderResponse` (`ManufactureOrderMcpTools`) |
| `ManufactureType` | yes | Manufacture batch/order responses (`ManufactureBatchMcpTools`, `ManufactureOrderMcpTools`) |
| `ErpManufactureType` | yes | Manufacture responses |
| `ProposedTaskStatus` | yes | `MeetingTasksMcpTools` anonymous-object responses |
| `MeetingTranscriptStatus` | yes | `MeetingTasksMcpTools` responses |
| `ProductType` | **no — already string** | `CatalogMcpTools` (unaffected by this fix; already correct) |

Example before/after for `ManufactureOrderState` (`GetManufactureOrders`):
```jsonc
// before
{ "state": 3, ... }
// after
{ "state": "Planned", ... }
```

No request-side shape changes (FR-3): MCP tool input parameters (e.g. `ProductType[]? productTypes`) are bound by the `ModelContextProtocol` SDK's own parameter binder, independent of these `JsonSerializer.Serialize`/`Deserialize` call sites, and are out of scope.

## Interfaces

- **MCP tool responses** (`Task<string>` JSON from every `[McpServerTool]` method): enum-typed fields switch number → string, as above. This is the fix.
- **MCP tool requests**: unchanged.
- **REST API / OpenAPI contract**: unchanged externally (FR-5); internally, `Program.cs` now references `McpJsonOptions.Default.Converters` instead of constructing its own `JsonStringEnumConverter`.
- **Regression-guard test target**: add one assertion to `ManufactureOrderMcpToolsTests` (e.g. on `GetManufactureOrders`) asserting the raw JSON contains a string state value (`Assert.Contains("\"state\":\"", jsonResult)` or an equivalent deserialized-string check on `ManufactureOrderState`) rather than the numeric form. `ManufactureOrderState` has no type-level attribute, so this assertion fails on `main` today and passes only once the shared options are wired through — making it an actual regression guard, unlike a `ProductType`-based assertion which would pass either way.

## Non-functional notes carried from plan-01

- No new dependencies — `JsonStringEnumConverter` is already in use.
- No per-call-site divergence — one `McpJsonOptions.Default` instance referenced everywhere; a new MCP tool follows the existing `JsonSerializer.Serialize(x, McpJsonOptions.Default)` pattern by copying any neighboring call site, not by remembering a rule.
- No performance concern — `Default` is a `static readonly` field, constructed once per process.

## Rough implementation sequence

1. Add `Infrastructure/Json/McpJsonOptions.cs`.
2. Update `Program.cs`'s `AddJsonOptions` callback to add `McpJsonOptions.Default`'s converters instead of constructing a new one.
3. Update all 23 call sites across the 6 MCP tool files (`using` + pass `McpJsonOptions.Default`).
4. Update all 7 MCP tool test files' `Deserialize` calls to pass `McpJsonOptions.Default`; add the `ManufactureOrderMcpToolsTests` string-enum assertion.
5. `dotnet build`, `dotnet format`, `dotnet test` (full `Anela.Heblo.Tests` run — MCP tool tests plus any REST JSON-contract tests, e.g. `ArticleStatusWireFormatTests`, `ArticlesControllerTests`, which already assert enum-as-string REST output and must remain green per FR-5).
