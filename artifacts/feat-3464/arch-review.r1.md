# Architecture Review: Extract `ITimeWindowParser` interface in Analytics module

## Skip Design: true

## Architectural Fit Assessment

This is a textbook DIP-compliance fix, not a new capability. It aligns perfectly with the existing convention in `backend/src/Anela.Heblo.Application/Features/Analytics/Services/`: every service in that folder except `TimeWindowParser` already ships as an `I{Name}` interface implemented by a same-named concrete class, both colocated in one file (verified in `MarginCalculator.cs`, which defines `IMarginCalculator` and `MarginCalculator` side by side, and in `AnalyticsModule.cs`, which registers `IProductFilterService`, `IReportBuilderService`, `IMarginCalculator`, and `IMonthlyBreakdownGenerator` as interface-to-implementation pairs). `TimeWindowParser` is registered as `services.AddScoped<TimeWindowParser>();` — the sole concrete-type registration in the module — and is the sole concrete-type constructor dependency in `GetProductMarginSummaryHandler`.

This is an **intra-module** concern, not a cross-module contract. `docs/architecture/development_guidelines.md` describes a heavier consumer-defines-the-contract pattern (interface in consumer's `Contracts/` folder, implementation in the owning module) for cross-module dependencies — that pattern does not apply here. `TimeWindowParser` is private to Analytics, consumed only by a handler inside the same module, so the correct and consistent placement is exactly what the spec proposes: the interface colocated with the implementation in `Services/TimeWindowParser.cs`, matching the `MarginCalculator.cs` precedent, not moved into a `Contracts/` folder.

No other module, adapter, or test fixture references `TimeWindowParser` outside the three sites named in the spec (handler, module registration, one test file) — confirmed by inspecting the `Services/` directory and the handler/test source directly. There is no risk of breaking an external consumer.

## Proposed Architecture

### Component Overview

```
AnalyticsModule.cs (DI composition root)
        │
        │  services.AddScoped<ITimeWindowParser, TimeWindowParser>();
        ▼
GetProductMarginSummaryHandler
        │  private readonly ITimeWindowParser _timeWindowParser;
        ▼
ITimeWindowParser  (interface, Features/Analytics/Services/TimeWindowParser.cs)
        △
        │  implements
TimeWindowParser  (concrete class, same file, depends on TimeProvider — unchanged)
```

No new components, no new files, no change to the dependency direction of anything outside this module. The only structural change is the insertion of an interface between the handler and the concrete class, matching the shape already used for `IMarginCalculator`, `IMonthlyBreakdownGenerator`, `IProductFilterService`, `IReportBuilderService`.

### Key Design Decisions

#### Decision 1: Interface location — colocated file vs. separate file vs. `Contracts/` folder
**Options considered:**
1. Colocate `ITimeWindowParser` in the existing `TimeWindowParser.cs` file (as `MarginCalculator.cs` does for `IMarginCalculator`).
2. Create a new `ITimeWindowParser.cs` file, interface-only.
3. Move it to a module-level `Contracts/` folder, treating it as a cross-module contract.

**Chosen approach:** Option 1 — colocate in `TimeWindowParser.cs`.

**Rationale:** This matches the codebase's own precedent (`MarginCalculator.cs`) exactly, requires the smallest diff, and is what the spec (FR-1) and brief both explicitly call for. Option 3 is wrong here — `Contracts/` is reserved for interfaces exposed *across* module boundaries per `development_guidelines.md`; `TimeWindowParser` has exactly one internal consumer and must stay a module-private implementation detail. Option 2 adds file-count noise with no benefit over Option 1 given the established one-file-per-service-pair pattern in this folder.

#### Decision 2: DI lifetime and registration style
**Options considered:** Keep `AddScoped<TimeWindowParser>()` and add a second `AddScoped<ITimeWindowParser>(sp => sp.GetRequiredService<TimeWindowParser>())` forwarding registration; vs. replace the registration outright with `AddScoped<ITimeWindowParser, TimeWindowParser>()`.

**Chosen approach:** Replace outright, one line, no forwarding registration.

**Rationale:** Nothing resolves `TimeWindowParser` by its concrete type after this change (confirmed: only consumer is the handler, which will depend on the interface; the test constructs `TimeWindowParser` directly via `new`, not via DI). A forwarding registration would be unnecessary indirection. `Scoped` lifetime is preserved unchanged, consistent with every other service in this module.

#### Decision 3: Test strategy — keep real object vs. introduce a mock
**Options considered:** (a) keep constructing the real `TimeWindowParser` via `new TimeWindowParser(timeProvider)` in the test and pass it where `ITimeWindowParser` is expected; (b) introduce a `Mock<ITimeWindowParser>` using the already-referenced Moq package.

**Chosen approach:** (a) for this change; note (b) as a natural but explicitly out-of-scope follow-up.

**Rationale:** The spec (FR-4) is explicit that this is a structural refactor with no required behavior change to the test, and the brief's stated pain point (interface makes the handler "easier to unit-test") is satisfied the moment the interface exists — the *option* to mock is unlocked, but exercising that option is a separate, optional improvement correctly deferred per the spec's Out of Scope section. Since `TimeWindowParser` implements `ITimeWindowParser`, the existing `new TimeWindowParser(timeProvider)` call keeps compiling with zero test-body changes beyond (at most) a field type annotation.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Modify exactly three existing files:

- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs` — add `ITimeWindowParser` above `TimeWindowParser`; change `public class TimeWindowParser` to `public class TimeWindowParser : ITimeWindowParser`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — line 47, `services.AddScoped<TimeWindowParser>();` → `services.AddScoped<ITimeWindowParser, TimeWindowParser>();`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — field, constructor parameter type `TimeWindowParser` → `ITimeWindowParser`.

Optionally touch (not required to compile, but keeps the test's intent legible):

- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — field type may be updated to `ITimeWindowParser` for consistency with the other collaborator fields in the same test (`_marginCalculator`, `_monthlyBreakdownGenerator` are declared as their concrete types there too, so leaving `TimeWindowParser` as-is is equally consistent — implementer's call, either compiles).

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface ITimeWindowParser
{
    (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow);
}

public class TimeWindowParser : ITimeWindowParser
{
    // constructor(TimeProvider) and ParseTimeWindow body: unchanged
}
```

This is the entire contract surface. No versioning, no OpenAPI impact — `ITimeWindowParser` is not part of any MediatR request/response DTO and never crosses the API boundary, so it is invisible to the generated TypeScript client.

### Data Flow

Unchanged. `GetProductMarginSummaryHandler.Handle` still calls `_timeWindowParser.ParseTimeWindow(request.TimeWindow)` as its first step to derive `(fromDate, toDate)`, which flows into `DateRange`, the repository stream query, and the margin/monthly-breakdown calculators exactly as today. The only difference is the compile-time type of `_timeWindowParser` (`ITimeWindowParser` instead of `TimeWindowParser`); the runtime object graph resolved by DI is identical.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed usage site outside the three known ones, causing a build break | Low | Repo-wide search for `TimeWindowParser` (already done in spec authoring and re-verified during this review: only `TimeWindowParser.cs`, `AnalyticsModule.cs`, the handler, and the test reference it) before editing; re-run after edit to confirm zero remaining concrete-type references outside the implementation file. |
| `dotnet build` / `dotnet format` catches nothing but a stray using or unused import after the type swap | Low | Standard pre-completion validation (`dotnet build`, `dotnet format`) already mandated by project rules covers this. |
| Scope creep into mocking `ITimeWindowParser` with Moq in the test, expanding the diff beyond the fix | Low | Explicitly called out as out-of-scope in the spec; implementer should leave the test's `new TimeWindowParser(timeProvider)` construction as-is. |

No medium/high risks identified. This change has no runtime behavior surface, no data model, no API contract, and a single, already-covered call site.

## Specification Amendments

None. The spec's FR-1 through FR-4 and the API/Interface Design section match the codebase precedent exactly (verified against `MarginCalculator.cs` and `AnalyticsModule.cs` directly) and require no architectural correction. One clarifying note for the implementer, not a spec change: leave the test's field type exactly as convenient — both `TimeWindowParser` and `ITimeWindowParser` compile post-change, and neither choice is "more correct" than the other given the test's existing mixed style (concrete types used for `_marginCalculator`/`_monthlyBreakdownGenerator` fields already).

## Prerequisites

None. No migrations, no config, no infrastructure changes. This can be implemented immediately in a single small commit/PR touching the three files listed above.
