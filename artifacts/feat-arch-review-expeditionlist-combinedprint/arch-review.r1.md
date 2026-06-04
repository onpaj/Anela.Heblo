I have enough grounding. Verified: spec aligns with reality — `CombinedPrintQueueSink.cs` only exists under API (not yet in Application), peer `FileSystemPrintQueueSink` is at the proposed target, `InternalsVisibleTo("Anela.Heblo.Tests")` is wired in `AssemblyInfo.cs:3` and `.csproj:46`, and `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2` is already a direct Application dependency (so `[FromKeyedServices]` works without new package refs). One detail to flag: `ServiceCollectionExtensions.cs:27` already has `using Anela.Heblo.Application.Features.ExpeditionList.Services;` — so after relocation the `using Anela.Heblo.API.Features.ExpeditionList;` at line 24 becomes dead and must be removed.

```markdown
# Architecture Review: Relocate CombinedPrintQueueSink to Application Layer

## Skip Design: true

## Architectural Fit Assessment

This change is a **pure structural correction** that restores Clean Architecture compliance for the print-sink slice. It is fully aligned with existing patterns — in fact, it eliminates the only deviation from them.

**Existing convention (verified in repo):**
- Pure Application-layer sinks (filesystem I/O, composition) live in `Anela.Heblo.Application/Features/ExpeditionList/Services/` (e.g. `FileSystemPrintQueueSink.cs`).
- Infrastructure-bound sinks live in dedicated adapter projects: `Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`, `Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs`.
- The `IPrintQueueSink` contract is owned by `Anela.Heblo.Application.Shared.Printing`.
- The API project's role is HTTP shell + composition root (registration in `Extensions/ServiceCollectionExtensions.AddPrintQueueSink`).

**The outlier:** `CombinedPrintQueueSink` — a composite that holds no infrastructure dependency, only Application abstractions and DI metadata — sits in the API project. It is the single sink implementation that violates the layering rule. Moving it removes that violation without touching any other slice.

**Integration points:**
1. `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:420` — DI registration of the concrete type. The required `using Anela.Heblo.Application.Features.ExpeditionList.Services;` is **already present** at line 27 (it imports `FileSystemPrintQueueSink`), so no new `using` is needed; only the now-dead `using Anela.Heblo.API.Features.ExpeditionList;` at line 24 must be removed.
2. `Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` — single `using` swap; `InternalsVisibleTo("Anela.Heblo.Tests")` is already configured on `Anela.Heblo.Application` via both `AssemblyInfo.cs:3` and `Anela.Heblo.Application.csproj:46`, so `internal sealed` visibility carries over cleanly.

No call site beyond DI exists; the type is only constructed by the container.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.API (HTTP shell + composition root)                │
│                                                                │
│   Extensions/ServiceCollectionExtensions.cs                    │
│     AddPrintQueueSink(cfg)                                     │
│       switch (cfg["ExpeditionList:PrintSink"])                 │
│         "Combined" → registers CombinedPrintQueueSink ─────────┼──┐
└────────────────────────────────────────────────────────────────┘  │
                                                                    │
┌────────────────────────────────────────────────────────────────┐  │
│ Anela.Heblo.Application                                        │  │
│                                                                │  │
│   Shared/Printing/                                             │  │
│     IPrintQueueSink                  ◄─── implemented by ──┐   │  │
│                                                            │   │  │
│   Features/ExpeditionList/Services/                        │   │  │
│     FileSystemPrintQueueSink (existing)            ────────┤   │  │
│     CombinedPrintQueueSink   (MOVED HERE)          ────────┼───┼──┘
│       │                                                    │   │
│       ├─ [FromKeyedServices("azure")] IPrintQueueSink ─────┤   │
│       └─ [FromKeyedServices("cups")]  IPrintQueueSink ─────┤   │
└────────────────────────────────────────────────────────────────┘  
                                                            ▲  ▲
            ┌───────────────────────────────────────────────┘  │
            │                                                  │
┌───────────┴────────────────┐    ┌────────────────────────────┴─┐
│ Anela.Heblo.Adapters.Azure │    │ Anela.Heblo.Adapters.Cups    │
│   AzureBlobPrintQueueSink  │    │   CupsPrintQueueSink         │
└────────────────────────────┘    └──────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Target namespace

**Options considered:**
- `Anela.Heblo.Application.Features.ExpeditionList.Services` (peer of `FileSystemPrintQueueSink`).
- `Anela.Heblo.Application.Features.ExpeditionList` (parent, one level up).
- `Anela.Heblo.Application.Shared.Printing` (next to the interface).

**Chosen approach:** `Anela.Heblo.Application.Features.ExpeditionList.Services`.

**Rationale:** Mirrors `FileSystemPrintQueueSink` exactly — same folder, same namespace, same vertical slice. The composite is feature-specific (it expresses the ExpeditionList print strategy), not a cross-cutting primitive, so it belongs in the feature slice, not in `Shared/Printing`.

#### Decision 2: Keep `internal sealed` visibility

**Options considered:**
- Keep `internal sealed` (current).
- Promote to `public` to make registration cleaner.

**Chosen approach:** Keep `internal sealed`.

**Rationale:** Visibility broadening is explicitly out of scope per the spec. The DI registration in `ServiceCollectionExtensions.AddPrintQueueSink` lives in the same solution and can reference internals of `Anela.Heblo.Application` either via the existing project reference (concrete-type registration only requires the type to be accessible at the registration call site — and since the API project references Application, `internal` types of Application are **not** visible to API). **This is a real concern** — see the Risks section. Resolution: add `InternalsVisibleTo("Anela.Heblo.API")` to the Application project, **or** widen the class to `internal` → `public sealed` only in the unlikely event that approach 1 is unacceptable. The brief explicitly suggests keeping `internal`; the cleanest fix is therefore the `InternalsVisibleTo` grant.

#### Decision 3: Keep DI registration in API project

**Options considered:**
- Move `AddPrintQueueSink` extension to Application (e.g., an `ExpeditionListModule`).
- Leave it in `Anela.Heblo.API.Extensions.ServiceCollectionExtensions`.

**Chosen approach:** Leave it in API.

**Rationale:** Out of scope per the brief ("Refactoring `AddPrintQueueSink` is not addressed here"). The composition root is the conventional home for sink-selection logic that mixes adapter packages (`AddAzurePrintQueueSink`, `AddCupsPrinting`) — moving it would force Application to take a dependency on adapter projects, inverting the layering. Status quo is correct.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/
├── Anela.Heblo.API/
│   ├── Extensions/ServiceCollectionExtensions.cs        ← edit (remove using, keep registration)
│   └── Features/ExpeditionList/                         ← DELETE the file; remove the folder if empty
│       └── CombinedPrintQueueSink.cs                    ← REMOVED
└── Anela.Heblo.Application/
    ├── AssemblyInfo.cs                                  ← ADD InternalsVisibleTo("Anela.Heblo.API") (see Risk #1)
    └── Features/ExpeditionList/Services/
        ├── FileSystemPrintQueueSink.cs                  ← unchanged (reference template)
        └── CombinedPrintQueueSink.cs                    ← NEW FILE (relocated)

backend/test/Anela.Heblo.Tests/
└── Features/ExpeditionList/
    └── CombinedPrintQueueSinkTests.cs                   ← edit using only
```

### Interfaces and Contracts

No interface changes. The contract `IPrintQueueSink` in `Anela.Heblo.Application.Shared.Printing` is untouched. The class's public surface (constructor signature with two `[FromKeyedServices]` parameters and `SendAsync(IEnumerable<string>, CancellationToken)`) is byte-identical.

New file canonical form:

```csharp
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

internal sealed class CombinedPrintQueueSink : IPrintQueueSink
{
    private readonly IPrintQueueSink _azureSink;
    private readonly IPrintQueueSink _cupsSink;

    public CombinedPrintQueueSink(
        [FromKeyedServices("azure")] IPrintQueueSink azureSink,
        [FromKeyedServices("cups")] IPrintQueueSink cupsSink)
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

### Data Flow

Unchanged at runtime. For `ExpeditionList:PrintSink = "Combined"`:

1. App startup → `Program.cs` → `AddPrintQueueSink(IConfiguration)`.
2. The `"Combined"` branch registers: keyed `"azure"` → `AzureBlobPrintQueueSink`; keyed `"cups"` → `CupsPrintQueueSink`; non-keyed `IPrintQueueSink` → `CombinedPrintQueueSink`.
3. At request time, an `IPrintQueueSink` consumer (e.g., `ExpeditionListService`) is injected with `CombinedPrintQueueSink`, whose constructor resolves the two keyed children.
4. `SendAsync(paths)` materializes `paths` once via `.ToList()`, awaits `azureSink.SendAsync` then `cupsSink.SendAsync` — fail-fast on Azure, no try/catch around the second call.

The IL emitted and the DI resolution graph are identical pre- and post-move.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **API project cannot see `internal sealed` type in Application.** Today the class is `internal` in API itself, so DI registration compiles. After the move, `services.AddScoped<IPrintQueueSink, CombinedPrintQueueSink>()` in API will fail to compile because `CombinedPrintQueueSink` becomes `internal` to Application. | **High** — build break | Add `[assembly: InternalsVisibleTo("Anela.Heblo.API")]` to `backend/src/Anela.Heblo.Application/AssemblyInfo.cs` and add `<InternalsVisibleTo Include="Anela.Heblo.API" />` to `Anela.Heblo.Application.csproj` (mirrors the existing `Anela.Heblo.Tests` entry). This is the cleanest fix and preserves the spec's "stays `internal sealed`" constraint. **The spec misses this prerequisite — see Spec Amendment #1.** |
| Stale dead `using Anela.Heblo.API.Features.ExpeditionList;` left at `ServiceCollectionExtensions.cs:24` after the move. | Low — `dotnet build` warning at most; `dotnet format` may auto-remove. | Explicitly delete line 24. No other type from that namespace is referenced (verified — the only inhabitant was `CombinedPrintQueueSink`). |
| Empty folder `backend/src/Anela.Heblo.API/Features/ExpeditionList/` left behind. | Low — cosmetic only. | Delete the directory after the file removal (the spec mandates this in FR-1). |
| Test assembly references the old namespace via more than the import (e.g., a `[assembly:]` attribute, snapshot, or generated file). | Low — none found by grep; only one `using` and the type name itself appear in the test file. | Single-namespace `using` swap is sufficient. Confirmed by `grep -rn CombinedPrintQueueSink backend/`. |
| Behavioral drift introduced "incidentally" during the move (e.g., reordering of `await`, dropping `.ToList()`). | Medium — silent regression in print fan-out. | The four existing unit tests (`SendAsync_BothSucceed_*`, `_AzureThrows_*`, `_AzureSucceedsCupsThrows_*`, `_SinglePassEnumerable_*`) cover the exact contract; they remain unmodified and must pass post-move. Treat any test edit as a code smell. |
| `ExpeditionListServicePrintSinkTests` (which exercises the registration path) regresses. | Low | Spec already lists it as a required gate (NFR-4). Re-run after the change. |

## Specification Amendments

### Amendment 1 (REQUIRED): Add `InternalsVisibleTo("Anela.Heblo.API")` to Application

The spec implicitly assumes that `internal sealed CombinedPrintQueueSink` will be accessible from `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs::AddPrintQueueSink` after the move. **It will not** — `internal` types in Application are invisible to API. Today this compiles only because the class lives in the API assembly.

Add a new functional requirement:

> **FR-6: Grant `InternalsVisibleTo` for API project.**
> Add `[assembly: InternalsVisibleTo("Anela.Heblo.API")]` to `backend/src/Anela.Heblo.Application/AssemblyInfo.cs` and `<InternalsVisibleTo Include="Anela.Heblo.API" />` to the `<ItemGroup>` containing the existing `Anela.Heblo.Tests` entry in `Anela.Heblo.Application.csproj`.
>
> **Acceptance criteria:**
> - `Anela.Heblo.API` compiles after the relocation without changing the visibility of `CombinedPrintQueueSink`.
> - The class remains `internal sealed`.
> - Both `AssemblyInfo.cs` (for the source-level attribute) and the `.csproj` (for the MSBuild SDK form) are updated to mirror the existing pattern for `Anela.Heblo.Tests`.

### Amendment 2 (clarifying, non-binding): FR-3 wording

The spec says the `using Anela.Heblo.API.Features.ExpeditionList;` line "is removed if no other type from that namespace is referenced." This is verified — `grep` shows `CombinedPrintQueueSink` is the only inhabitant of that namespace and the only API reference to it is the DI registration. The conditional language can be hardened to **"must be removed"** since the prerequisite is satisfied.

## Prerequisites

Before opening the PR:

1. **InternalsVisibleTo grant** (per Amendment 1) must be applied in the **same commit** as the file move, or the API project will not compile. This is the only non-trivial prerequisite.
2. No migrations, no configuration changes, no infrastructure changes, no Azure Key Vault changes.
3. No new NuGet packages (verified — `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2` is already a direct dependency of `Anela.Heblo.Application` per `Anela.Heblo.Application.csproj:18`, so `[FromKeyedServices]` resolves without a new reference).
4. Validation gates: `dotnet build`, `dotnet format` (zero changes), `dotnet test` (full `Anela.Heblo.Tests` suite — at minimum `CombinedPrintQueueSinkTests` and `ExpeditionListServicePrintSinkTests` must be green). No FE or E2E work in scope.
```