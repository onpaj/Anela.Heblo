# Architecture Review: Replace `IConfiguration` injection in `GetInvoiceImportStatisticsHandler` with typed options

## Skip Design: true

This is a backend-only refactor. There is no UI surface, no visual component, no API contract change. Nothing for a designer to weigh in on.

## Architectural Fit Assessment

The proposed change aligns cleanly with patterns already established across the Application project. A direct survey of the codebase confirms:

- **The Options pattern is the convention.** At least 20 modules already define a `*Options` POCO and bind it via `services.Configure<TOptions>(configuration.GetSection(...))`. Examples: `PrintPickingListOptions`, `ArticleOptions`, `MeetingTasksOptions`, `BankAccountSettings`, `CatalogCacheOptions`, `PhotobankTagsCacheOptions`, `ExpeditionListArchiveOptions`. The Analytics module is the outlier, not the proposal.
- **The "module that needs config takes `IConfiguration`" pattern is well-established.** Modules that bind options change their signature from `AddXModule(this IServiceCollection services)` to `AddXModule(this IServiceCollection services, IConfiguration configuration)`. `ApplicationModule.cs` lines 73–108 show both styles side by side (e.g., `services.AddArticleModule(configuration);` vs `services.AddAnalyticsModule();`).
- **`Microsoft.Extensions.Configuration.Binder` is already referenced** by `Anela.Heblo.Application.csproj`. `services.Configure<T>(IConfiguration)` works out of the box — no new package reference required. The spec's caveat about `Microsoft.Extensions.Options.ConfigurationExtensions` is unnecessary in this codebase.
- **Integration points are exactly two files** (`AnalyticsModule.cs`, `GetInvoiceImportStatisticsHandler.cs`) plus one call site (`ApplicationModule.cs:74`) plus the existing test file. No collateral surface.
- **No co-consumer to worry about.** Verified: the only other touch-point that might have read the same section — `InvoiceImportStatisticsTile` — does not read `InvoiceImport:*` at all (it only calls the repository). The refactor is fully isolated.

The change is a small, faithful application of an established convention.

## Proposed Architecture

### Component Overview

```
ApplicationModule.AddApplicationServices(IServiceCollection, IConfiguration)
        │
        └─► AnalyticsModule.AddAnalyticsModule(IServiceCollection, IConfiguration)   ◄── signature change
                │
                ├─► services.Configure<InvoiceImportOptions>(                          ◄── new line
                │       configuration.GetSection(InvoiceImportOptions.ConfigurationKey))
                │
                └─► [MediatR auto-scan registers handler]
                        │
                        └─► GetInvoiceImportStatisticsHandler(
                                IAnalyticsRepository,
                                IOptions<InvoiceImportOptions>)                        ◄── constructor change

InvoiceImportOptions  (new POCO, Application/Features/Analytics/)
        ├── ConfigurationKey = "InvoiceImport"  (const string)
        ├── MinimumDailyThreshold = 10
        └── DefaultDaysBack = 14
```

### Key Design Decisions

#### Decision 1: Options class location and namespace
**Options considered:**
- (a) `Features/Analytics/InvoiceImportOptions.cs` (flat, alongside `AnalyticsModule.cs` and `AnalyticsConstants.cs`)
- (b) `Features/Analytics/UseCases/GetInvoiceImportStatistics/InvoiceImportOptions.cs` (co-located with the handler)
- (c) `Features/Analytics/Configuration/InvoiceImportOptions.cs` (mirroring `Photobank/Configuration/`)

**Chosen approach:** (a) — `backend/src/Anela.Heblo.Application/Features/Analytics/InvoiceImportOptions.cs`, namespace `Anela.Heblo.Application.Features.Analytics`.

**Rationale:** Matches the dominant pattern in this codebase: `PrintPickingListOptions`, `ArticleOptions`, `MeetingTasksOptions`, `LeafletOptions`, `KnowledgeBaseOptions`, `ExpeditionListArchiveOptions`, `OrgChartOptions`, `ProductExportOptions` all sit at the module root next to their `*Module.cs`. The `Configuration/` subfolder is used only when a module has multiple options classes (Photobank, Marketing, Manufacture). Analytics has one. Co-locating with the use case (option b) would orphan the options if a second handler ever needed the same section.

#### Decision 2: Convention for the section-name constant
**Options considered:**
- (a) `public const string ConfigurationKey = "InvoiceImport";`
- (b) `public const string SectionName = "InvoiceImport";`
- (c) Magic string at the call site in `AnalyticsModule.cs`

**Chosen approach:** (a) `ConfigurationKey`.

**Rationale:** `ConfigurationKey` is used by more existing options (`PrintPickingListOptions`, `ExpeditionListArchiveOptions`, `BankAccountSettings`). `SectionName` is used by some Photobank options. The spec doesn't mandate either — but defining a constant on the type (rather than a magic string in `AnalyticsModule`) is the project default and removes one source of typos. Choose `ConfigurationKey` for majority consistency.

#### Decision 3: `IOptions<T>` vs `IOptionsSnapshot<T>` vs `IOptionsMonitor<T>`
**Options considered:** all three.
**Chosen approach:** `IOptions<InvoiceImportOptions>`, store `.Value` in a field.
**Rationale:** The spec already calls this correctly. The values are static operational thresholds — they don't change at runtime, no hot-reload is required, and the handler is request-scoped (registered by MediatR scan). `IOptionsSnapshot` would add scoped-DI overhead for zero functional benefit; `IOptionsMonitor` would add a change-token subscription for zero functional benefit. Storing `.Value` matches what most consumers do across this codebase.

#### Decision 4: Update `AnalyticsModule` signature vs. inject `IConfiguration` into the handler/tile
**Options considered:**
- (a) Change `AddAnalyticsModule()` → `AddAnalyticsModule(IConfiguration)` and call `services.Configure<InvoiceImportOptions>(...)` there.
- (b) Leave the module signature alone and register the options binding at a higher level (e.g., `ApplicationModule`).

**Chosen approach:** (a).

**Rationale:** Every module in this codebase that owns options binds them in its own module — this is the vertical-slice composition pattern. Hoisting the binding into `ApplicationModule` would leak Analytics-internal config keys upward and break the "each slice owns its DI" convention. The signature change cost is one extra parameter and one updated call site.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Analytics/
├── AnalyticsModule.cs                    ← MODIFIED: accept IConfiguration; add services.Configure<…>
├── AnalyticsConstants.cs
├── InvoiceImportOptions.cs               ← NEW
├── Contracts/
├── DashboardTiles/
│   └── InvoiceImportStatisticsTile.cs    ← UNCHANGED (does not read InvoiceImport:* — verified)
├── Services/
├── Validators/
└── UseCases/
    └── GetInvoiceImportStatistics/
        ├── GetInvoiceImportStatisticsHandler.cs   ← MODIFIED: IOptions<InvoiceImportOptions>
        ├── GetInvoiceImportStatisticsRequest.cs
        └── GetInvoiceImportStatisticsResponse.cs

backend/src/Anela.Heblo.Application/
└── ApplicationModule.cs                  ← MODIFIED line 74: AddAnalyticsModule(configuration)

backend/test/Anela.Heblo.Tests/Features/Analytics/
└── GetInvoiceImportStatisticsHandlerTests.cs   ← MODIFIED: 4 tests; replace ConfigurationBuilder with Options.Create
```

### Interfaces and Contracts

```csharp
// InvoiceImportOptions.cs
namespace Anela.Heblo.Application.Features.Analytics;

public class InvoiceImportOptions
{
    public const string ConfigurationKey = "InvoiceImport";

    public int MinimumDailyThreshold { get; set; } = 10;
    public int DefaultDaysBack { get; set; } = 14;
}
```

```csharp
// AnalyticsModule.cs — new signature
public static IServiceCollection AddAnalyticsModule(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.Configure<InvoiceImportOptions>(
        configuration.GetSection(InvoiceImportOptions.ConfigurationKey));

    // ... existing registrations unchanged
}
```

```csharp
// GetInvoiceImportStatisticsHandler.cs — new constructor
public GetInvoiceImportStatisticsHandler(
    IAnalyticsRepository analyticsRepository,
    IOptions<InvoiceImportOptions> invoiceImportOptions)
{
    _analyticsRepository = analyticsRepository;
    _options = invoiceImportOptions.Value;   // store .Value — singleton-effective
}
```

Replace the two reads:
```csharp
var minimumThreshold = _options.MinimumDailyThreshold;
var defaultDaysBack  = _options.DefaultDaysBack;
```

### Data Flow

```
appsettings.json / Azure Key Vault
        │
        │  Section "InvoiceImport" → { MinimumDailyThreshold, DefaultDaysBack }
        ▼
IConfiguration (built in composition root)
        │
        ▼
AnalyticsModule.AddAnalyticsModule(services, configuration)
        │
        │  services.Configure<InvoiceImportOptions>(section)
        ▼
IOptions<InvoiceImportOptions> (DI container)
        │
        ▼
GetInvoiceImportStatisticsHandler ctor → _options = options.Value
        │
        ▼
Handle() reads _options.MinimumDailyThreshold / DefaultDaysBack
```

Behavior is identical for: (a) both keys present, (b) one key present, (c) section absent — the `InvoiceImportOptions` defaults (10, 14) take effect whenever the configuration provider supplies no value, matching the prior `GetValue<int>(key, default)` semantics.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `AddAnalyticsModule()` signature change breaks the existing call site at `ApplicationModule.cs:74`. | High (build break) | Update the call to `services.AddAnalyticsModule(configuration);` in the same PR. Verified `configuration` is in scope at that line (it's the parameter of `AddApplicationServices`). |
| Other call sites of `AddAnalyticsModule()` (e.g., test fixtures, integration-test factories) fail to compile. | Medium | Run `grep -rn "AddAnalyticsModule" backend/` before merge and update all call sites. Initial scan shows only `ApplicationModule.cs:74` — but verify exhaustively. |
| Configuration section absent in `appsettings*.json` triggers a different code path than before. | Low | Both old (`GetValue<int>(key, 10)`) and new (`InvoiceImportOptions { = 10 }`) yield `10` when the section is missing. Equivalent. Test FR-4 adds an explicit assertion for this case. |
| `services.Configure<T>(IConfiguration)` requires a package the Application project doesn't have. | None | Verified: `Microsoft.Extensions.Configuration.Binder` is already in `Anela.Heblo.Application.csproj`. No new package reference needed. The spec's hedge about `Microsoft.Extensions.Options.ConfigurationExtensions` can be dropped. |
| Existing tests (4 of them) construct `IConfigurationRoot` in `ConfigurationBuilder` — they all need updating in lockstep with the constructor change. | Low | All four tests are in one file (`GetInvoiceImportStatisticsHandlerTests.cs`). The change is mechanical: replace `ConfigurationBuilder().Add… .Build()` with `Options.Create(new InvoiceImportOptions { MinimumDailyThreshold = …, DefaultDaysBack = … })`. |
| `InvoiceImportStatisticsTile` is silently affected. | None | Verified by reading the tile: it never reads `InvoiceImport:*` config. Refactor is isolated to the handler. |

## Specification Amendments

1. **FR-1 — confirm options class location.** The spec says "or the conventional location for module-level options in this codebase — match existing examples if any." Confirmed convention: **module root** (`Features/Analytics/InvoiceImportOptions.cs`), not under `UseCases/GetInvoiceImportStatistics/`. Update the spec to remove the ambiguity.

2. **FR-1 — add `ConfigurationKey` constant.** Add a `public const string ConfigurationKey = "InvoiceImport";` to the options class to match `PrintPickingListOptions`, `ExpeditionListArchiveOptions`, `BankAccountSettings`. Use this constant in `AnalyticsModule` instead of the magic string `"InvoiceImport"`.

3. **FR-2 — `AddAnalyticsModule` signature change is required.** The spec says "If the module registration method does not already accept `IConfiguration`, follow the established pattern." It does not currently accept it. Make it explicit: the method signature changes to `AddAnalyticsModule(this IServiceCollection services, IConfiguration configuration)`, and `ApplicationModule.cs:74` must change from `services.AddAnalyticsModule();` to `services.AddAnalyticsModule(configuration);`. List both edits in the acceptance criteria.

4. **FR-4 — existing tests already exist.** The spec phrases this conditionally ("any existing unit tests"). Confirmed: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` exists with 4 tests, all of which mock `IConfiguration` via `ConfigurationBuilder`. All 4 must be ported. The "default values" test the spec asks for already exists in spirit (`Handle_ShouldUseDefaultThresholdWhenNotConfigured` at line 69) — adapt rather than add.

5. **Dependencies — remove the hedge.** The spec says "If the Application project does not yet reference [`Microsoft.Extensions.Options.ConfigurationExtensions`] directly, add a `PackageReference`." Verified `Microsoft.Extensions.Configuration.Binder` is already referenced and `services.Configure<T>(IConfiguration)` is in active use across other modules without any new package reference. Drop the hedge — no package edit required.

6. **NFR-4 — clarify scope.** The spec says the Application project "must not gain (and ideally should lose…) a direct dependency on `Microsoft.Extensions.Configuration.Abstractions` for this handler." Clarify: removing the dep from the **handler** is the goal; the **project** will continue to reference `Microsoft.Extensions.Configuration.Abstractions` because `AnalyticsModule` (and many other modules) still legitimately consume `IConfiguration` at the module's composition seam. That is correct per existing patterns and not a violation.

## Prerequisites

None blocking. Specifically:

- **No new package references.** Verified `Microsoft.Extensions.Configuration.Binder` already in `Anela.Heblo.Application.csproj`.
- **No configuration changes.** Section `"InvoiceImport"` and key names `MinimumDailyThreshold` / `DefaultDaysBack` remain identical. `appsettings*.json` and Azure Key Vault entries continue to bind without edit.
- **No migration, no infrastructure work, no feature flag.**
- **No deployment-time coupling.** Single PR contains all changes.

Ready for implementation as-is.