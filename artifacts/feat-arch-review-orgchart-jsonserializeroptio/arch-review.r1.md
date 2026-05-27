# Architecture Review: Reuse `JsonSerializerOptions` in OrgChartService

## Skip Design: true

## Architectural Fit Assessment

This change aligns perfectly with established codebase conventions. A `grep` for `static readonly JsonSerializerOptions` returns **14+ existing call sites** that already use exactly this pattern — including the direct analog `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/OpenMeteoWeatherForecastClient.cs:20`, which is structurally identical to `OrgChartService` (HTTP client + JSON deserialization in a feature service). `OrgChartService` is the outlier; this brings it into compliance.

Integration points are zero. The change is internal to a single class, behind the `IOrgChartService` interface. No DI registration, MediatR handler (`GetOrganizationStructureHandler`), or DTO contract (`OrgChartResponse`, `OrganizationDto`, `PositionDto`, `EmployeeDto`) is affected.

Note one path discrepancy from the spec: the actual file lives at `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` (lowercase `backend/`, `Services/` subfolder), not `Backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartService.cs` as the spec states. Lines 39–42 match the cited block.

## Proposed Architecture

### Component Overview

```
GetOrganizationStructureHandler (MediatR)
        │ depends on
        ▼
IOrgChartService  ──implemented by──►  OrgChartService
                                          │
                                          ├─ HttpClient (injected, scoped)
                                          ├─ OrgChartOptions (IOptions<>)
                                          ├─ ILogger<OrgChartService>
                                          └─ JsonOptions  ◄── NEW: private static readonly
                                                              (process-lifetime singleton,
                                                               independent of DI lifetime)
```

No new components. The static field is a CLR-level cache attached to the type, not a DI registration.

### Key Design Decisions

#### Decision 1: Field scope — `private static readonly` on `OrgChartService` (no shared registry)

**Options considered:**
- (A) `private static readonly` on `OrgChartService` itself (matches every other adapter/service in the codebase).
- (B) Centralized `JsonSerializerOptions` registry in `Anela.Heblo.Application.Common` (e.g., a `JsonDefaults` static class).
- (C) `internal static` to allow tests in the same assembly to assert option configuration.

**Chosen approach:** (A) — `private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };`

**Rationale:** The spec explicitly excludes a centralized registry from scope (Out of Scope §3). Every existing call site in the codebase uses option (A). Introducing a new shared abstraction would (1) violate YAGNI, (2) require touching 14 unrelated files, (3) couple unrelated features through a global, and (4) exceed the surgical-change directive in `CLAUDE.md`. Option (C) is unnecessary because no tests for `OrgChartService` currently exist and the spec does not require adding any.

#### Decision 2: Field naming — `JsonOptions` (PascalCase)

**Options considered:** `JsonOptions`, `_jsonOptions`, `s_jsonOptions`, `SerializerOptions`.

**Chosen approach:** `JsonOptions` — PascalCase, no underscore prefix.

**Rationale:** 12 of 14 existing call sites use `JsonOptions` (PascalCase). This is the dominant convention. Two outliers (`ClaudeMeetingSummaryExplainer._jsonOptions`, `GraphApiHelpers.JsonOptions` as `public static`) do not justify deviating from the prevailing pattern.

#### Decision 3: Initialization — inline `new()` expression, no static constructor

**Options considered:** Inline field initializer vs. explicit `static` constructor.

**Chosen approach:** Inline `new() { PropertyNameCaseInsensitive = true }`.

**Rationale:** Single option, no ordering or conditional logic. Inline initialization is the idiom used by all existing call sites and avoids the BeforeFieldInit semantic differences that come with explicit static constructors.

## Implementation Guidance

### Directory / Module Structure

Single file modified: `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs`. No new files, no module changes, no DI registration changes in `OrgChartModule.cs` or `ApplicationModule.cs`.

### Interfaces and Contracts

No interface or contract changes. `IOrgChartService.GetOrganizationStructureAsync` signature, `OrgChartResponse` shape, and all DTO contracts remain byte-identical.

### Data Flow

Unchanged. The only behavioral difference is that the `JsonSerializerOptions` instance referenced on the `JsonSerializer.Deserialize<OrgChartResponse>(content, JsonOptions)` call is the same heap object on every invocation, instead of a freshly allocated one. The deserializer's internal reflection metadata cache (attached to the options instance) is built once on first call and reused thereafter.

Concretely, replace lines 39–44 of `OrgChartService.cs`:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

var orgChart = JsonSerializer.Deserialize<OrgChartResponse>(content, options);
```

with a usage of the new field, and add at the top of the class (alongside the other `private readonly` fields, but before them since it is `static`):

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true
};
```

Then: `var orgChart = JsonSerializer.Deserialize<OrgChartResponse>(content, JsonOptions);`

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A future contributor adds a mutating call (e.g., `JsonOptions.Converters.Add(...)`) and triggers `InvalidOperationException` because options become read-only on first use. | LOW | `readonly` field + `private` scope confines mutation surface to this one file. Microsoft's `JsonSerializerOptions` itself throws if mutated after first use, surfacing the bug immediately at runtime. No additional guard required. |
| The static field outlives DI scopes, so if anyone later adds DI-resolved converters they'd be confused by the lifetime mismatch. | LOW | Out of scope per spec (Out of Scope §4 — no converters). Document in the field declaration only if a converter is added later. |
| Static field initialization racing with type-load could theoretically delay first request marginally. | NEGLIGIBLE | This is true of every other `JsonOptions` field in the codebase; the cost is amortized and one-time, identical to the previous per-call cost on the first invocation. |
| Tests inadvertently rely on a fresh options instance (e.g., asserting option identity). | NONE | No existing tests for `OrgChartService` (verified via `find backend/test -iname "*orgchart*"` — zero results). |

## Specification Amendments

1. **File path correction.** Spec references `Backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartService.cs`. The actual path is `backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` (lowercase `backend`, additional `Services/` subfolder). Update the spec's Background and Dependencies sections to match.

2. **Test coverage statement is misleading.** Spec FR-2 acceptance criterion says "Existing unit/integration tests covering `OrgChartService.GetOrganizationStructureAsync` continue to pass without modification." No such tests exist. Reword to: "No existing tests cover `OrgChartService` directly; the existing `backend` test suite (`dotnet test`) must continue to pass without modification, and no new tests are required for this change."

3. **Field naming clarification (non-blocking).** Spec uses `JsonOptions` in the example, which matches the codebase convention. Recommend the spec explicitly call out `JsonOptions` (PascalCase, no underscore) as the required name to remove any ambiguity at review time.

## Prerequisites

None. No migrations, configuration changes, infrastructure work, or upstream merges are required. Implementation can begin immediately on the current branch.