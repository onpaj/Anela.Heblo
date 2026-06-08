# Architecture Review: Decouple CombinedPrintQueueSink from DI Keying Conventions

## Skip Design: true

## Architectural Fit Assessment

The refactor is a **layering correction** that aligns the print-sink slice with the Clean Architecture rules documented in `docs/architecture/filesystem.md` and already followed by every other sink:

- `IPrintQueueSink` is the Application-layer contract (`Anela.Heblo.Application.Shared.Printing`).
- `FileSystemPrintQueueSink` (a pure-app sink) lives in `Anela.Heblo.Application.Features.ExpeditionList.Services`.
- Infrastructure-bound sinks live in adapter assemblies: `Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`, `Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs`.
- Adapter wiring and sink selection are composition-root concerns owned by `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.AddPrintQueueSink`.

The current `CombinedPrintQueueSink` is the lone outlier: it sits in `Anela.Heblo.Application` *and* carries `[FromKeyedServices("azure"|"cups")]` attributes whose string keys are defined in `ServiceCollectionExtensions.cs:417–418`. Application code thus depends on a composition-root convention with no compile-time link — exactly the violation Clean Architecture forbids.

**Integration points (single PR, surgical):**
1. `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs` — delete after move.
2. `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` — new home (matches existing `Anela.Heblo.API/Features/Users/` vertical slice convention).
3. `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:413–420` — replace concrete-type registration with a factory delegate.
4. `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` — swap the `using` to the new namespace; constructor call (`new CombinedPrintQueueSink(_azureSink.Object, _cupsSink.Object)`) already uses plain parameters and needs no change.
5. New DI-resolution test (FR-5) added under `backend/test/Anela.Heblo.Tests/API/` or alongside the existing tests.

`Anela.Heblo.API.csproj:13` already has `<InternalsVisibleTo Include="Anela.Heblo.Tests" />`, so `internal sealed` visibility carries cleanly into the tests project — no spec amendment needed for accessibility.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.API (HTTP shell + composition root)                     │
│                                                                     │
│   Extensions/ServiceCollectionExtensions.cs                         │
│     AddPrintQueueSink(cfg)                                          │
│       case "Combined":                                              │
│         AddAzurePrintQueueSink(cfg)                                 │
│         AddKeyedScoped<IPrintQueueSink,AzureBlobPrintQueueSink>(...)│
│         AddKeyedScoped<IPrintQueueSink,CupsPrintQueueSink>(...)     │
│         AddScoped<IPrintQueueSink>(sp => {                          │
│            var azure = sp.GetRequiredKeyedService<...>("azure");    │
│            var cups  = sp.GetRequiredKeyedService<...>("cups");     │
│            return new CombinedPrintQueueSink(azure, cups);          │
│         });                                                         │
│                                                                     │
│   Features/ExpeditionList/CombinedPrintQueueSink.cs   ◄── NEW HOME  │
│      internal sealed class CombinedPrintQueueSink                   │
│        ctor(IPrintQueueSink azureSink, IPrintQueueSink cupsSink)    │
│        (no DI attributes; no Microsoft.Extensions.DependencyInjection│
│         using directive)                                            │
└─────────────────────────────────────────────────────────────────────┘
                │ (project ref: API → Application)
                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application                                             │
│   Shared/Printing/IPrintQueueSink                  (unchanged)      │
│   Features/ExpeditionList/Services/                                 │
│     FileSystemPrintQueueSink                       (unchanged)      │
│     CombinedPrintQueueSink                         ◄── DELETED      │
└─────────────────────────────────────────────────────────────────────┘
                ▲                              ▲
                │                              │
┌───────────────┴───────────────┐  ┌───────────┴──────────────────────┐
│ Anela.Heblo.Adapters.Azure    │  │ Anela.Heblo.Adapters.Cups        │
│   AzureBlobPrintQueueSink     │  │   CupsPrintQueueSink             │
└───────────────────────────────┘  └──────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Target location inside the API project
**Options considered:**
- `Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` (vertical slice — mirrors existing `Anela.Heblo.API/Features/Users/`).
- `Anela.Heblo.API/Extensions/CombinedPrintQueueSink.cs` (adjacent to its DI registration).
- `Anela.Heblo.API/Printing/CombinedPrintQueueSink.cs` (new top-level folder).

**Chosen approach:** `Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`, namespace `Anela.Heblo.API.Features.ExpeditionList`.

**Rationale:** Matches the only existing precedent for feature folders inside the API project (`Anela.Heblo.API/Features/Users/`). The composite is feature-specific (ExpeditionList print fan-out), not a cross-cutting primitive. Keeping `Extensions/` reserved for `IServiceCollection`/`IApplicationBuilder` extension methods preserves its semantics, and avoiding a new top-level `Printing/` folder keeps the API project surface small.

#### Decision 2: Inline factory vs. private static helper method
**Options considered:**
- Inline lambda inside the `"Combined"` switch arm.
- Private static `CreateCombinedSink(IServiceProvider)` helper next to `AddPrintQueueSink`.

**Chosen approach:** Inline lambda (exactly as the spec mandates in FR-3).

**Rationale:** The factory is three statements long; extracting it adds indirection without removing duplication. Cohesion stays with the switch arm that owns the keying decision. If a third composite ever appears, refactor then.

#### Decision 3: Keep `CombinedPrintQueueSink` `internal sealed`
**Options considered:**
- Promote to `public` for "registration simplicity".
- Keep `internal sealed` and rely on `InternalsVisibleTo`.

**Chosen approach:** Keep `internal sealed`.

**Rationale:** The class is registered in the same assembly (`Anela.Heblo.API`) — internal access is sufficient. `Anela.Heblo.API.csproj` already exposes internals to `Anela.Heblo.Tests` for the FR-5 DI-resolution test. Promoting to public would broaden the API surface without need.

#### Decision 4: Do not generalise into a `CompositeSink<T>`
**Options considered:**
- Build a reusable composite wrapper.
- Keep `CombinedPrintQueueSink` as a single-purpose class.

**Chosen approach:** Single-purpose (spec marks generalisation as out of scope).

**Rationale:** YAGNI. Two consumers (Azure + CUPS) is below the threshold for an abstraction. The existing class already enforces the desired semantics (sequential, fail-fast, single materialisation via `.ToList()`); a generic wrapper would either dilute or duplicate that.

## Implementation Guidance

### Directory / Module Structure
```
backend/src/
├── Anela.Heblo.API/
│   ├── Extensions/ServiceCollectionExtensions.cs        ← edit "Combined" case (replace AddScoped<T,T> with factory)
│   └── Features/ExpeditionList/                         ← NEW folder (mirror Features/Users/)
│       └── CombinedPrintQueueSink.cs                    ← NEW FILE (relocated, stripped of [FromKeyedServices])
└── Anela.Heblo.Application/
    └── Features/ExpeditionList/Services/
        ├── FileSystemPrintQueueSink.cs                  ← unchanged
        └── CombinedPrintQueueSink.cs                    ← DELETE

backend/test/Anela.Heblo.Tests/
└── Features/ExpeditionList/
    ├── CombinedPrintQueueSinkTests.cs                   ← edit using (only); constructor call unchanged
    └── CombinedPrintQueueSinkRegistrationTests.cs       ← NEW (FR-5; DI resolution test)
```

### Interfaces and Contracts

**Public contracts — unchanged.** `IPrintQueueSink` and `AddPrintQueueSink(this IServiceCollection, IConfiguration)` keep their signatures.

**Relocated class canonical form:**

```csharp
using Anela.Heblo.Application.Shared.Printing;

namespace Anela.Heblo.API.Features.ExpeditionList;

internal sealed class CombinedPrintQueueSink : IPrintQueueSink
{
    private readonly IPrintQueueSink _azureSink;
    private readonly IPrintQueueSink _cupsSink;

    public CombinedPrintQueueSink(IPrintQueueSink azureSink, IPrintQueueSink cupsSink)
    {
        _azureSink = azureSink;
        _cupsSink = cupsSink;
    }

    public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var paths = filePaths.ToList();
        await _azureSink.SendAsync(paths, cancellationToken);
        await _cupsSink.SendAsync(paths, cancellationToken);
    }
}
```

Note: the `using Microsoft.Extensions.DependencyInjection;` directive disappears (no symbol from that namespace remains in the file).

**Edited "Combined" switch arm in `ServiceCollectionExtensions.cs:413–420`:**

```csharp
case "Combined":
    // AddAzurePrintQueueSink registers a non-keyed IPrintQueueSink as a side effect;
    // it is unused here — the factory below registers the resolved IPrintQueueSink last and wins.
    services.AddAzurePrintQueueSink(configuration);
    services.AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure");
    services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
    services.AddScoped<IPrintQueueSink>(provider =>
    {
        var azure = provider.GetRequiredKeyedService<IPrintQueueSink>("azure");
        var cups  = provider.GetRequiredKeyedService<IPrintQueueSink>("cups");
        return new CombinedPrintQueueSink(azure, cups);
    });
    break;
```

Add a single `using Anela.Heblo.API.Features.ExpeditionList;` at the top of `ServiceCollectionExtensions.cs` (the existing `using Anela.Heblo.Application.Features.ExpeditionList.Services;` stays — it still resolves `FileSystemPrintQueueSink`).

### Data Flow

Unchanged at runtime. With `ExpeditionList:PrintSink=Combined`:
1. Startup → `Program.cs:122` → `AddPrintQueueSink(builder.Configuration)`.
2. The `"Combined"` arm registers keyed `"azure"` → `AzureBlobPrintQueueSink`, keyed `"cups"` → `CupsPrintQueueSink`, and a non-keyed `IPrintQueueSink` factory that constructs `CombinedPrintQueueSink` from the two keyed resolutions.
3. At request time, `ExpeditionListService` is injected with the factory-produced `CombinedPrintQueueSink`. Constructor parameters are now plain `IPrintQueueSink` — no DI attribute lookup happens during instantiation.
4. `SendAsync(paths)` materialises `paths` via `.ToList()`, awaits `_azureSink.SendAsync` then `_cupsSink.SendAsync` — fail-fast on Azure exception (verified by existing test `SendAsync_AzureThrows_CupsNeverCalledAndExceptionPropagates`).

Identical DI resolution graph and identical IL inside `SendAsync` before and after.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| DI registration breaks silently if either string key (`"azure"`/`"cups"`) is later renamed in only one place. | Medium | After this change the keys exist in exactly one file (`ServiceCollectionExtensions.cs`) and in exactly one switch arm. Any rename is a same-file edit. Optional hardening: lift the keys to `private const string AzureSinkKey = "azure";` / `CupsSinkKey = "cups";` near `AddPrintQueueSink`. Treat as a follow-up, not a blocker — the spec doesn't require it. |
| Behavioural drift introduced incidentally during the move (await ordering, `.ToList()` dropped, `CancellationToken` not threaded). | Medium | Existing four unit tests in `CombinedPrintQueueSinkTests.cs` lock the exact contract: `SendAsync_BothSucceed_CallsBothSinksWithSamePaths`, `SendAsync_AzureThrows_CupsNeverCalledAndExceptionPropagates`, `SendAsync_AzureSucceedsCupsThrows_ExceptionPropagates`, `SendAsync_SinglePassEnumerable_BothSinksReceiveAllPaths`. They must remain unmodified (only the `using` swaps) and must pass post-move. |
| `ExpeditionListServicePrintSinkTests` regresses. | Low | Test depends only on `IPrintQueueSink` mock — unaffected by sink relocation. Re-run as part of FR-4 verification. |
| `AddAzurePrintQueueSink(configuration)` side-effect (registers a non-keyed `AzureBlobPrintQueueSink` as `IPrintQueueSink`) interacts poorly with the new factory registration. | Low | The factory is the *last* non-keyed `IPrintQueueSink` registration in the `"Combined"` arm; standard ServiceCollection "last-wins" semantics give us the same behaviour as today. The existing inline comment at `ServiceCollectionExtensions.cs:414–415` already documents this; copy it forward verbatim. |
| Empty folder `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/` remains after the move (it won't — `FileSystemPrintQueueSink.cs` still lives there). | None | n/a — verified the folder retains a peer file. |
| FR-5 test for `Combined` configuration accidentally trips `AddCupsPrinting(configuration)` requirements (real CUPS endpoint, real config keys). | Medium | Build the test's `IConfiguration` in-memory with the minimal keys the Cups/Azure adapter registrations need to construct (not connect). If a sink's `Add…` extension performs eager validation at registration time, stub via `IConfigurationBuilder.AddInMemoryCollection(...)` rather than loading `appsettings.json`. Inspect `AddCupsPrinting` and `AddAzurePrintQueueSink` before writing the test to confirm what config they read. |

## Specification Amendments

### Amendment 1 (clarifying, REQUIRED): Pin the relocation target

The spec offers `Anela.Heblo.API.Printing` *or* "adjacent to `ServiceCollectionExtensions`" as the destination. Pin it to `Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` with namespace `Anela.Heblo.API.Features.ExpeditionList`. Reason: matches the only existing precedent (`Anela.Heblo.API/Features/Users/`) and keeps `Extensions/` reserved for extension-method classes.

### Amendment 2 (clarifying): FR-5 test placement and `InternalsVisibleTo` note

The spec mentions "use `InternalsVisibleTo` or a wrapper if the type is internal". Clarify: `Anela.Heblo.API.csproj:13` already declares `<InternalsVisibleTo Include="Anela.Heblo.Tests" />`, so no wrapper or new attribute is required. Place the new test at `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` (mirrors the existing `backend/test/Anela.Heblo.Tests/API/` directory).

The test should assert:
- `IPrintQueueSink` resolves to `CombinedPrintQueueSink` when `ExpeditionList:PrintSink=Combined`.
- The keyed `"azure"` resolution is `AzureBlobPrintQueueSink`.
- The keyed `"cups"` resolution is `CupsPrintQueueSink`.
- Resolving `IPrintQueueSink` under `ExpeditionList:PrintSink=FileSystem` continues to yield `FileSystemPrintQueueSink` (regression guard).

### Amendment 3 (clarifying, non-binding): Carry forward the existing comment

Preserve the inline comment at `ServiceCollectionExtensions.cs:414–415` ("AddAzurePrintQueueSink registers a non-keyed IPrintQueueSink as a side effect; it is unused here — the last non-keyed registration wins") in the rewritten arm. Without it the next reader will wonder why a non-keyed Azure registration is present alongside the factory.

## Prerequisites

1. None at the project-reference or visibility level: `Anela.Heblo.API` already references `Anela.Heblo.Application` (so `IPrintQueueSink` is in scope), and `Anela.Heblo.API.csproj` already grants `InternalsVisibleTo("Anela.Heblo.Tests")` (so the new DI-resolution test can assert on the `internal` type).
2. No new NuGet packages — the API project already depends on `Microsoft.Extensions.DependencyInjection.Abstractions` transitively.
3. No configuration, migration, Azure Key Vault, environment-variable, or deployment changes.
4. Validation gates before merge: `dotnet build`, `dotnet format` (zero diff), full `Anela.Heblo.Tests` suite — at minimum `CombinedPrintQueueSinkTests`, `ExpeditionListServicePrintSinkTests`, and the new `CombinedPrintQueueSinkRegistrationTests` must be green.
5. Final grep gate (asserts the architectural invariant after the change): `grep -rn 'FromKeyedServices' backend/src/Anela.Heblo.Application/` must return zero matches; `grep -rn '"azure"\|"cups"' backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` must show those literals appearing *only* inside `AddPrintQueueSink`.