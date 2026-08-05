# Architecture review: Unify MCP tool JSON serialization

## Verdict

**Approve design-01.md as written.** I independently re-verified every factual claim it makes against the current tree (not trusting the artifact's own verification) and every one holds. No changes to the proposed structure. This document adds the invariant checks the design step doesn't itself frame as invariant-checking, and calls out two things implementers should watch for that aren't blocking.

## Re-verification (independent, against current tree)

| Claim | Checked how | Result |
|---|---|---|
| `ProductType` has `[JsonConverter(typeof(JsonStringEnumConverter))]` at type level | Read `backend/src/Anela.Heblo.Domain/Features/Catalog/ProductType.cs` | Confirmed — already string-serialized today, independent of this fix |
| `ManufactureOrderState` has no type-level converter | Read `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderState.cs` | Confirmed — plain enum, no attribute. Valid regression-test target |
| 23 `JsonSerializer.Serialize(` call sites across 6 tool files | `grep -rn` on `backend/src/Anela.Heblo.API/MCP/Tools/*.cs` | Confirmed — exactly 23, exactly those 6 files |
| `Program.cs:151-154` constructs `JsonStringEnumConverter` inline in `AddJsonOptions` | Read `Program.cs:151-154` | Confirmed verbatim |
| 7 test files deserialize MCP tool output | `grep -rln "JsonSerializer.Deserialize"` on `backend/test/Anela.Heblo.Tests/MCP/Tools/*.cs` | Confirmed — exactly the 7 named files |
| MCP tools are constructed with plain constructor injection, no DI container, in tests (`new CatalogMcpTools(mediatorMock.Object, ...)`) | Read `CatalogMcpToolsTests.cs:1-32` | Confirmed — supports the static-over-`IOptions<T>` rationale |
| `Infrastructure/<Area>/StaticClass.cs` is an established convention | Listed `Infrastructure/` subfolders: `Hangfire/`, `Telemetry/`, `ExceptionHandling/`, `Authentication/`, each holding static/DI-registration helpers | Confirmed — `Infrastructure/Json/McpJsonOptions.cs` fits the existing pattern exactly |
| No existing `Infrastructure/Json/` folder or naming collision | `ls backend/src/Anela.Heblo.API/Infrastructure/` | Confirmed empty — no collision |
| `MeetingTasksMcpTools.cs` serializes anonymous objects (4 sites) | `grep -n "JsonSerializer.Serialize" MeetingTasksMcpTools.cs` | Confirmed — `Serialize(new { ... })` at 4 call sites |
| `McpModule.cs` registers all 7 tool classes via `WithTools<T>()`, no other JSON-serialization surface hides in the MCP module itself | Read `McpModule.cs` | Confirmed — DI registration only, no serialization logic to route around |

Everything in design-01.md's "Verification of plan-01.md" section is itself correct — including its correction of plan-01's premise (the `ProductType`/`GetCatalogList` example in the original issue report doesn't reproduce on current `main`; `ManufactureOrderState` is the enum that actually proves the bug). I re-derived this independently rather than trusting the artifact's self-report, and it checks out.

## Invariants checked

**1. Single-converter invariant (the design's own acceptance criterion).**
Before: exactly one `new JsonStringEnumConverter()` in `backend/src/Anela.Heblo.API` (`Program.cs:153`). The design's end state: still exactly one, now in `McpJsonOptions.cs`, with `Program.cs` iterating `McpJsonOptions.Default.Converters` instead of constructing its own. This is mechanically correct — `JsonSerializerOptions.Converters` is a populated `IList<JsonConverter>`, not a settable property, so copying converter references via `foreach` (not reassignment) is the only available mechanism. Confirmed against the `System.Text.Json` API shape (`Converters` is `IList<JsonConverter>` with no init-only setter on `JsonSerializerOptions` after construction in this .NET version's ASP.NET Core JSON options pattern).

**2. No DTO becomes a record.** CLAUDE.md's hard rule ("DTOs are classes, never C# records") isn't touched by this change — `McpJsonOptions` is a static class holding a `JsonSerializerOptions` field, not a DTO, and no existing DTO shape changes. Non-issue, but worth stating explicitly since the review is invariant-focused.

**3. Vertical-slice / module-boundary invariant.** `docs/architecture/filesystem.md`'s component-placement convention is `Infrastructure/<CrossCuttingArea>/` for things that serve the whole API layer, not a single feature slice. JSON serialization policy for MCP (which spans all 7 feature-specific tool classes) is exactly this kind of cross-cutting concern, not a vertical slice. `Infrastructure/Json/` is the correct placement, matching `Infrastructure/Hangfire/`, `Infrastructure/Telemetry/`, `Infrastructure/ExceptionHandling/`, `Infrastructure/Authentication/`.

**4. REST/OpenAPI contract stability (FR-5).** The design's `Program.cs` change is a no-op for MVC output: MVC's `AddJsonOptions` still ends up with exactly one `JsonStringEnumConverter` in its `Converters` list, so the generated TypeScript client and existing REST contract tests are unaffected. Confirmed by reading the exact diff shape proposed — it replaces construction, not behavior.

**5. Thread-safety of the shared static instance.** `JsonSerializerOptions` becomes immutable after first use (`System.Text.Json` throws `InvalidOperationException` on mutation post-first-use, not silently unsafe) — the design's `static readonly` field, populated once at type-init and never touched again, satisfies this. No locking needed. This matters here specifically because the instance is shared across concurrent MCP tool invocations (ASP.NET Core request pipeline is multi-threaded) — confirmed this isn't a "probably fine" assumption but a documented framework guarantee.

## Points for implementation to watch (non-blocking)

- **`Program.cs`'s `foreach` copies converter *instances* by reference**, so `McpJsonOptions.Default.Converters[0]` and `MVC's options.JsonSerializerOptions.Converters[0]` become the *same* `JsonStringEnumConverter` object after wiring. That's fine (converters are stateless/immutable) but implementers should not "simplify" this later into assigning the whole list or constructing a second instance — that would silently reintroduce the two-source-of-truth problem this fix removes.
- **Test file count is 7, not 5** — plan-01's FR-4 acceptance criteria and scope section still say 5 (missing `KnowledgeBaseToolsTests.cs`, `LeafletToolsTests.cs`). design-01 already corrects this in its own body (§4, "Test updates (7 files)"); implementation should follow design-01's corrected list, not plan-01's original FR-4 text. Flagging so the discrepancy between the two artifacts doesn't cause an implementer to stop at 5 files.
- **Anonymous-object serialization in `MeetingTasksMcpTools.cs`** is asserted (not just assumed) to work identically under `JsonStringEnumConverter` because the converter resolves by the property's static declared type via reflection, which is true for anonymous types same as named DTOs. No special-casing needed, but the implementation step should include at least one assertion against one of these 4 call sites specifically (not just the named-DTO tools), since it's structurally the one call-site shape that differs from the rest of the codebase's pattern.

## Structural decisions endorsed as-is

- **Static class over `IOptions<JsonOptions>` injection** — correct call. Verified independently: MCP tools are plain POCOs constructed with `new T(...)` in every existing test, with no DI container in the test path. Injecting `IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>` would force a constructor change across 7 production classes and 7 test fixtures for a purely internal serialization detail, and would make MCP (a non-MVC transport) depend on an MVC-branded options type. The static field has no lifetime to manage and is process-wide by design (converters are stateless), so there's no scoping loss versus DI.
- **`ManufactureOrderState` as the regression-guard target instead of `ProductType`** — correct, and the reasoning is airtight: a `ProductType`-based assertion would pass on unpatched `main` (type already carries the attribute), so it wouldn't actually guard against the regression this task exists to prevent.
- **Mechanical, uniform call-site edit (`Serialize(x)` → `Serialize(x, McpJsonOptions.Default)`)** — matches the task's explicit "do not hand-fix per call site; centralize the options" instruction while still being a one-line diff at each site, not an abstraction that hides what's happening.

## Prerequisites before implementation begins

None outstanding. No open questions remain from plan-01 that design-01 left unresolved — the DI-vs-static question is answered with evidence, not by assumption. Implementation can proceed directly from design-01.md's "Rough implementation sequence" (§ steps 1–5), using the corrected 7-file test list from design-01 rather than plan-01's original 5-file FR-4 text.
