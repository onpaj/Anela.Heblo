# Architecture Review: Invert ExpeditionList → Logistics Picking Dependency

## Skip Design: true

This is a backend-only structural refactor. No UI, screens, or visual components are added or changed.

## Architectural Fit Assessment

The proposal aligns with the established consumer-owned-contract pattern (`docs/architecture/development_guidelines.md`, `ILeafletKnowledgeSource`, `ILogisticsStockOperationQueryService`, `LogisticsCatalogTransportSourceAdapter`). The boundary test scaffold in `ModuleBoundariesTests.cs` already supports a new rule at the same shape used at line 327.

However, **the brief and spec mischaracterize who "the provider" is**, and this materially affects implementation:

- `Anela.Heblo.Application.Features.Logistics.Picking` (`IPickingListSource`, `PrintPickingListRequest`, `PrintPickingListResult`) is a namespace-only home for these types. **No Logistics-internal code consumes any of them** (verified by grep). They are interface + DTOs with no module-side implementation.
- The single concrete implementation lives in the **Shoptet adapter**: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`.
- DI for `IPickingListSource` is registered in `ShoptetApiAdapterServiceCollectionExtensions.cs:76`, **not** in `LogisticsModule.cs` (which contains no picking-related registrations at all).
- The spec's FR-5 ("DI registration appears in LogisticsModule") and FR-4 ("Adapter lives under Features/Logistics/") therefore describe a topology that doesn't match reality. We need to choose explicitly between two architectures.

**Additional gap discovered during exploration:** The spec missed two of the four files that import the Logistics.Picking namespace:

| File | Spec mentions? | Imports Logistics.Picking |
|---|---|---|
| `Services/ExpeditionListService.cs` | Yes | Yes |
| `Infrastructure/Jobs/PrintPickingListJob.cs` | Yes | Yes |
| `Services/IExpeditionListService.cs` | **No** | Yes — `PrintPickingListRequest`/`Result` appear in the public method signature |
| `UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs` | **No** | Yes |

Without fixing the two missing files, the architecture test (FR-6) will fail and the acceptance criterion in FR-3 (`grep` returns zero) cannot be met.

## Proposed Architecture

### Component Overview

```
+-------------------------------------------------------------+
|  ExpeditionList module (consumer, owns the contract)        |
|                                                             |
|  Contracts/                                                 |
|    IExpeditionPickingSource     <----+                      |
|    ExpeditionPickingRequest          |                      |
|    ExpeditionPickingResult           |                      |
|                                       |                      |
|  Services/                            |                      |
|    IExpeditionListService  ----consumes (no Logistics ref)  |
|    ExpeditionListService     ----+                          |
|  Infrastructure/Jobs/             |                          |
|    PrintPickingListJob       ----+                          |
|  UseCases/RunExpeditionListPrintFix/                        |
|    RunExpeditionListPrintFixHandler  --+                    |
+-----------------------------------------|-------------------+
                                          |
                                          | (DI resolves to)
                                          v
+-------------------------------------------------------------+
|  Logistics module (Application/Features/Logistics)          |
|                                                             |
|  Infrastructure/                                            |
|    LogisticsExpeditionPickingAdapter  --- implements        |
|       IExpeditionPickingSource                              |
|       (translates ExpeditionPicking* <-> PrintPickingList*) |
|       (depends on Logistics.Picking.IPickingListSource)     |
|                                                             |
|  Picking/  (unchanged, per "Out of Scope")                  |
|    IPickingListSource (Logistics-namespaced)                |
|    PrintPickingListRequest / Result                         |
|                                                             |
|  LogisticsModule.cs                                         |
|    services.AddScoped<IExpeditionPickingSource,             |
|                       LogisticsExpeditionPickingAdapter>(); |
+-------------------------------------------------------------+
                                          ^
                                          | (still implements)
                                          |
+-------------------------------------------------------------+
|  Anela.Heblo.Adapters.ShoptetApi (unchanged)                |
|    ShoptetApiExpeditionListSource : IPickingListSource      |
|    (binds Logistics-namespaced IPickingListSource only)     |
+-------------------------------------------------------------+
```

### Key Design Decisions

#### Decision 1: Bridging adapter inside Logistics, vs. retargeting the Shoptet adapter directly

**Options considered:**

- **A.** Retarget `ShoptetApiExpeditionListSource` to implement the new ExpeditionList-owned interface; delete the Logistics-namespaced picking types (they have no other consumer).
- **B.** Keep the Logistics-namespaced picking types untouched; add a thin `LogisticsExpeditionPickingAdapter` in `Logistics.Infrastructure` that implements ExpeditionList's interface and delegates to the existing Logistics-namespaced `IPickingListSource` (still implemented by `ShoptetApiExpeditionListSource`).

**Chosen approach:** **B**, because the spec's "Out of Scope" section explicitly forbids touching the Logistics-namespaced types. B is a strictly additive refactor: nothing existing is renamed, no test assemblies' resolution paths change.

**Rationale:** A is architecturally cleaner (one fewer hop, no duplicate near-identical DTOs) and is the structure I would recommend if the constraint were lifted — see "Specification Amendments" below. B respects the stated scope and lets the team revisit cleanup later. The adapter is sub-millisecond DTO translation; no I/O or latency cost.

#### Decision 2: Interface naming — `IPickingListSource` vs. `IExpeditionPickingSource`

**Options considered:**
- Keep the name `IPickingListSource` in the new namespace (spec's choice).
- Rename to `IExpeditionPickingSource` to mirror the `ExpeditionPicking*` DTO naming.

**Chosen approach:** Rename to `IExpeditionPickingSource`.

**Rationale:** C# permits two interfaces with the same simple name in different namespaces, but the bridging adapter in Logistics will need to *depend on both* (it consumes the Logistics-namespaced one and implements the ExpeditionList-namespaced one). Two `IPickingListSource` symbols in the same file force `using` aliases or fully-qualified type references and add no value. The DTO rename (`ExpeditionPickingRequest`/`Result`) is already paying this cost; finish the job.

#### Decision 3: Adapter location and visibility

**Chosen approach:** `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs`, declared `internal sealed`.

**Rationale:** Matches the precedent set by `LogisticsCatalogTransportSourceAdapter` (same folder, same `internal sealed` visibility). `internal` makes it impossible for any consumer to depend on the concrete type by mistake — it can only be reached via the interface.

#### Decision 4: DTO shape — minimal vs. full parity

**Chosen approach:** Minimal — only the fields ExpeditionList actually reads or writes today.

ExpeditionList currently uses (verified against `ExpeditionListService.cs`, `PrintPickingListJob.cs`, `RunExpeditionListPrintFixHandler.cs`):

Request inputs: `Carriers`, `SourceStateId`, `DesiredStateId`, `ChangeOrderState`, `SendToPrinter`.
Result outputs: `ExportedFiles` (for cleanup), `TotalCount` (for logging/response). `OrderIds` on `PrintPickingListResult` is **not consumed** by ExpeditionList — exclude it.
Statics on `PrintPickingListRequest` (`DefaultCarriers`, `DefaultSourceStateId`, `DefaultDesiredStateId`): these are consumed by `PrintPickingListJob` and `RunExpeditionListPrintFixHandler`. Move them onto `ExpeditionPickingRequest`.

**Rationale:** YAGNI. The whole point of the inversion is that the consumer states what it needs; copying unused fields recreates the coupling we're removing.

#### Decision 5: `Carriers` enum ownership

**Issue not raised in the spec:** `PrintPickingListRequest.Carriers` is `IList<Carriers>` where `Carriers` is `Anela.Heblo.Domain.Features.Logistics.Carriers`. If `ExpeditionPickingRequest` keeps that property typed as `IList<Carriers>`, ExpeditionList still imports a Domain Logistics type, and the new boundary rule will flag it.

**Options considered:**
- Allowlist `Anela.Heblo.Domain.Features.Logistics.Carriers` in the new rule.
- Define an ExpeditionList-owned `ExpeditionCarrier` enum.
- Type the field as `IList<string>` or `IList<int>`.

**Chosen approach:** Allowlist `Carriers` (and only `Carriers`) in the new ExpeditionList → Logistics rule, with a comment justifying it.

**Rationale:** `Carriers` is a stable Domain enum (`Zasilkovna`, `GLS`, `PPL`, `Osobak`) used by many parts of the system; duplicating it into ExpeditionList would create a synchronization burden far worse than the dependency it removes. The boundary test pattern already supports per-rule allowlists with documented entries (`LeafletAllowlist`, `LogisticsAllowlist`). Use that mechanism rather than re-typing the field.

## Implementation Guidance

### Directory / Module Structure

New files:
```
backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/
    IExpeditionPickingSource.cs
    ExpeditionPickingRequest.cs
    ExpeditionPickingResult.cs

backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/
    LogisticsExpeditionPickingAdapter.cs
```

Modified files:
```
backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IExpeditionListService.cs
backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs
backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs
backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs
backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs
backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs
```

Unchanged (per "Out of Scope"):
```
backend/src/Anela.Heblo.Application/Features/Logistics/Picking/* (all 3 files)
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs
backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs
```

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface IExpeditionPickingSource
{
    Task<ExpeditionPickingResult> CreatePickingListAsync(
        ExpeditionPickingRequest request,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken = default);
}
```

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public class ExpeditionPickingRequest
{
    public const int DefaultSourceStateId = -2;
    public const int DefaultDesiredStateId = 26;

    public IList<Carriers> Carriers { get; set; } = new List<Carriers>();
    public int SourceStateId { get; set; } = DefaultSourceStateId;
    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
    public bool ChangeOrderState { get; set; }
    public bool SendToPrinter { get; set; }

    public static IList<Carriers> DefaultCarriers { get; } = new List<Carriers>
    {
        Anela.Heblo.Domain.Features.Logistics.Carriers.Zasilkovna,
        Anela.Heblo.Domain.Features.Logistics.Carriers.GLS,
        Anela.Heblo.Domain.Features.Logistics.Carriers.PPL,
        Anela.Heblo.Domain.Features.Logistics.Carriers.Osobak,
    };
}
```

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public class ExpeditionPickingResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
}
```

Both DTOs are `class`, not `record` (per project DTO rule — these are returned through the MediatR/service layer and any future OpenAPI surface).

Adapter skeleton:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Infrastructure;

internal sealed class LogisticsExpeditionPickingAdapter
    : ExpeditionList.Contracts.IExpeditionPickingSource
{
    private readonly Picking.IPickingListSource _inner;

    public LogisticsExpeditionPickingAdapter(Picking.IPickingListSource inner) =>
        _inner = inner;

    public async Task<ExpeditionList.Contracts.ExpeditionPickingResult> CreatePickingListAsync(
        ExpeditionList.Contracts.ExpeditionPickingRequest request,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken = default)
    {
        var logisticsRequest = new Picking.PrintPickingListRequest
        {
            Carriers = request.Carriers,
            SourceStateId = request.SourceStateId,
            DesiredStateId = request.DesiredStateId,
            ChangeOrderState = request.ChangeOrderState,
            SendToPrinter = request.SendToPrinter,
        };

        var inner = await _inner.CreatePickingList(logisticsRequest, onBatchFilesReady, cancellationToken);

        return new ExpeditionList.Contracts.ExpeditionPickingResult
        {
            ExportedFiles = inner.ExportedFiles,
            TotalCount = inner.TotalCount,
        };
    }
}
```

DI registration added to `LogisticsModule.AddTransportModule`:

```csharp
services.AddScoped<
    ExpeditionList.Contracts.IExpeditionPickingSource,
    LogisticsExpeditionPickingAdapter>();
```

### Data Flow

For `PrintPickingListJob` (recurring):

1. Hangfire fires job → `PrintPickingListJob.ExecuteAsync`.
2. Job builds `ExpeditionPickingRequest` (ExpeditionList type) from `PrintPickingListOptions`.
3. Job calls `_expeditionListService.PrintPickingListAsync(request, emailList, ct)`.
4. `ExpeditionListService` builds batch callback, calls `_pickingSource.CreatePickingListAsync(request, callback, ct)` (resolved via DI → `LogisticsExpeditionPickingAdapter`).
5. Adapter translates request → `PrintPickingListRequest`, calls Logistics-namespaced `IPickingListSource` (resolved → `ShoptetApiExpeditionListSource`).
6. Shoptet adapter executes existing logic, invokes batch callback as before.
7. Adapter translates `PrintPickingListResult` → `ExpeditionPickingResult`, returns.
8. `ExpeditionListService` cleans up files, returns to job. Same observable behavior.

For `RunExpeditionListPrintFixHandler`: same flow with `SourceStateId = FixSourceStateId`.

### Module Boundary Rule

Add to `ModuleBoundariesTests.cs` Rules array, modeled exactly on the line 327 rule:

```csharp
new ModuleBoundaryRule(
    Name: "ExpeditionList -> Logistics",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionList",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Logistics",
        "Anela.Heblo.Application.Features.Logistics",
        "Anela.Heblo.Persistence.Logistics",
    },
    Allowlist: ExpeditionListLogisticsAllowlist),
```

With (in the allowlist section near other declared lists):

```csharp
// Allowlist for ExpeditionList -> Logistics.
// Carriers is a Domain enum (Zasilkovna/GLS/PPL/Osobak) shared widely across the codebase.
// Re-defining it in ExpeditionList would create a synchronization burden far worse than
// the dependency it removes.
private static readonly HashSet<string> ExpeditionListLogisticsAllowlist = new(StringComparer.Ordinal)
{
    "Anela.Heblo.Application.Features.ExpeditionList.Contracts.ExpeditionPickingRequest -> Anela.Heblo.Domain.Features.Logistics.Carriers",
};
```

Verify the test name encoding matches `EnumerateReferencedTypes` output by running the test once with a deliberate extra violation, observing the printed `Found:` block, and copying the exact entry string.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `Carriers` Domain reference leaks more boundary violations than just `ExpeditionPickingRequest.Carriers` (e.g. via the static `DefaultCarriers` getter or callers) | Medium | Run the boundary test after first compile. Any unexpected entries surface immediately; add them to the allowlist with justification or refactor the leak. |
| Two test files (`ExpeditionListServiceOrderStateTests`, `ExpeditionListServicePrintSinkTests`) currently `Mock<IPickingListSource>` on the Logistics-namespaced interface — left untouched they will fail to compile if the production code stops accepting it | High | Update both test files to mock `IExpeditionPickingSource` and pass `ExpeditionPickingResult`. This is part of FR-7 implicitly but should be explicit in the implementation checklist. |
| Boundary test passes vacuously when ExpeditionList types are entirely scrubbed of Logistics references but `EnumerateReferencedTypes` misses something (it only inspects member signatures, not method bodies — see the docstring at `ModuleBoundariesTests.cs:660`) | Low | Combine the test with the existing `grep` acceptance criterion in FR-3 — both must pass. The grep catches `using` imports even when the type only appears in method bodies. |
| DI resolution order: `LogisticsExpeditionPickingAdapter` depends on `Picking.IPickingListSource`, which is registered by `AddShoptetApiAdapter`. If a test container or alternate composition omits the Shoptet adapter, resolving the ExpeditionList interface throws | Medium | Existing `ExpeditionListService` already depends transitively on this binding; the risk is unchanged. Document the dependency in the adapter file with a one-line comment. |
| `IExpeditionListService` change is a public Application-layer signature change. Anything outside `ExpeditionList` that consumes it would break | Low | grep confirms only ExpeditionList-internal callers (`PrintPickingListJob`, `RunExpeditionListPrintFixHandler`, the two test files). Safe. |
| `Func<IList<string>, Task>?` callback parameter shape preserved across the adapter — easy to drop accidentally | Low | Include this parameter in `IExpeditionPickingSource`. Required for the existing per-batch upload/email/printer behavior. The minimal-fields rule does **not** apply to the operation signature — only to the DTOs. |

## Specification Amendments

1. **FR-3 acceptance criterion is incomplete.** Add `IExpeditionListService.cs` and `RunExpeditionListPrintFixHandler.cs` to the file list. The grep check already covers them, but the prose lists only `ExpeditionListService` and `PrintPickingListJob`. Fix the prose to match the grep.

2. **FR-4 / FR-5 location is wrong.** The brief and spec describe Logistics as the implementation provider. Verify with the team and update to one of:
   - **(B)** Keep types in Logistics namespace; add `LogisticsExpeditionPickingAdapter` in `Application/Features/Logistics/Infrastructure/`; register in `LogisticsModule`. **The Shoptet adapter is the actual sink** — the Logistics-side adapter is purely a DTO translator. This is the option this review recommends.
   - **(A — out-of-scope per spec, but architecturally cleaner)** Have `ShoptetApiExpeditionListSource` implement `IExpeditionPickingSource` directly; delete `Anela.Heblo.Application.Features.Logistics.Picking/*` (verified no other consumers). DI moves to `AddShoptetApiAdapter`. No Logistics-side adapter exists. Recommend revisiting the out-of-scope as a follow-up — keeping a dead namespace just to avoid touching it is the kind of decay this boundary rule exists to prevent.

3. **Interface name.** Rename to `IExpeditionPickingSource` for consistency with `ExpeditionPickingRequest`/`Result` and to avoid two `IPickingListSource` symbols in the bridging adapter file.

4. **Add an explicit FR for the `Carriers` allowlist entry.** Without it, the boundary test fails on the first run. Document the justification in the allowlist comment block.

5. **FR-7 should explicitly include test-file updates.** `ExpeditionListServiceOrderStateTests.cs` and `ExpeditionListServicePrintSinkTests.cs` both depend on the Logistics-namespaced interface today and must be retargeted. They live in `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/`.

6. **`PrintPickingListResult.OrderIds` is unused by ExpeditionList** — confirm and exclude from `ExpeditionPickingResult` (FR-2 says "only fields ExpeditionList reads," and the codebase confirms no reader).

7. **Adapter visibility.** Mark `LogisticsExpeditionPickingAdapter` as `internal sealed` (precedent: `LogisticsCatalogTransportSourceAdapter`). Acceptance: type cannot be referenced from outside `Anela.Heblo.Application`.

## Prerequisites

None. This refactor is purely a code reorganization:

- No DB migration.
- No config/secrets changes (no Key Vault entries to add).
- No infrastructure changes.
- No NuGet additions.
- No external API or contract changes.

Standard validation gates apply per `CLAUDE.md`:
- `dotnet build` and `dotnet format` must pass.
- `dotnet test` must pass — specifically the ExpeditionList unit tests, the new architecture test, and `PickingListIntegrationTests` in the Shoptet adapter tests (which continues to resolve the Logistics-namespaced `IPickingListSource` and is unaffected by Option B).