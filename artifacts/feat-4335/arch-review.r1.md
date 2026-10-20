# Architecture Review: Move Azure Blob Print-Sink Config Out of PrintPickingListOptions

## Skip Design: true

## Architectural Fit Assessment
This is a pure configuration-ownership refactor inside the existing Clean Architecture / Vertical Slice layering already used throughout this codebase. It aligns with two precedents already merged in this repository:

- `docs/superpowers/plans/2026-06-12-decouple-filestorage-from-expeditionlist-config.md` — gave `FileStorage` its own `FileStorageOptions` class and config section, removing a cross-module read of `ExpeditionList:BlobConnectionString`.
- `docs/superpowers/plans/2026-05-26-decouple-expeditionlistarchive-from-expeditionlist.md` — gave `ExpeditionListArchive` its own `ExpeditionListArchiveOptions` class and `"ExpeditionListArchive"` config section.

This finding is the third and final leg of that same decoupling effort: the Adapters.Azure layer's print-sink configuration (`BlobConnectionString`, `BlobContainerName`) is still riding on the Application-layer `PrintPickingListOptions`, plus one fully dead property (`PrintSink`). The fix is structurally identical to the two precedents — introduce a narrower options class owned by the consuming layer — with one deliberate difference explained in Decision 2 below.

The main integration point is `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure` (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`), which is called from two places in `ServiceCollectionExtensions.AddPrintQueueSink` (the `"AzureBlob"` and `"Combined"` switch arms). Both call sites are unaffected by this change — they pass the same `IConfiguration` in, and the module's public method signature does not change.

## Proposed Architecture

### Component Overview
```
Before:
┌─────────────────────────────── Application layer ───────────────────────────────┐
│  PrintPickingListOptions  (bound from "ExpeditionList" section)                  │
│    ├─ EmailSender, PrintQueueFolder, *StateId, DesiredStateName,                 │
│    │  DefaultEmailRecipients, SendToPrinterByDefault,                            │
│    │  ChangeOrderStateByDefault        ← read by ExpeditionListService,          │
│    │                                     PrintExpeditionOrderHandler,            │
│    │                                     RunExpeditionListPrintFixHandler        │
│    ├─ PrintSink                        ← read by NOBODY (dead)                   │
│    ├─ BlobConnectionString             ← read by AzureAdapterModule (Adapters!)  │
│    └─ BlobContainerName                ← read by AzureAdapterModule (Adapters!)  │
└────────────────────────────────────────────────────────────────────────────────┘
                                    ▲
                                    │ IOptions<PrintPickingListOptions>  (crosses layer the wrong way)
                                    │
┌─────────────────────── Adapters.Azure layer ──────────────────────┐
│  AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure           │
└─────────────────────────────────────────────────────────────────-─┘

After:
┌────────────── Application layer ──────────────┐   ┌────────── Adapters.Azure layer ───────────┐
│  PrintPickingListOptions                       │   │  AzureBlobPrintSinkOptions (NEW)           │
│  (bound from "ExpeditionList" section)         │   │  (bound from the SAME "ExpeditionList"     │
│    EmailSender, PrintQueueFolder, *StateId,    │   │   section — see Decision 2)                │
│    DesiredStateName, DefaultEmailRecipients,   │   │    BlobConnectionString                    │
│    SendToPrinterByDefault,                     │   │    BlobContainerName                       │
│    ChangeOrderStateByDefault                   │   │                                            │
│  ← read by ExpeditionListService,              │   │  ← read by AzureAdapterModule ONLY         │
│    PrintExpeditionOrderHandler,                │   │                                            │
│    RunExpeditionListPrintFixHandler            │   │                                            │
└─────────────────────────────────────────────---┘   └────────────────────────────────────────────┘

  PrintSink: removed from PrintPickingListOptions entirely, no replacement.
  ServiceCollectionExtensions.AddPrintQueueSink keeps reading
  configuration["ExpeditionList:PrintSink"] raw — unchanged, unaffected.
```

### Key Design Decisions

#### Decision 1: A new `AzureBlobPrintSinkOptions` class, not a shared adapter-agnostic options type
**Options considered:**
- (a) Create `AzureBlobPrintSinkOptions` in `Anela.Heblo.Adapters.Azure.Features.ExpeditionList`, as the issue's suggested fix proposes.
- (b) Fold the two Blob fields into a more generic "print adapter options" abstraction shared across FileSystem/Cups/Azure adapters.

**Chosen approach:** (a). Create the narrow, adapter-specific `AzureBlobPrintSinkOptions` class exactly where the issue suggests: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintSinkOptions.cs`.

**Rationale:** Option (b) is speculative generality with no current second consumer — the `FileSystemPrintQueueSink` and `CupsPrintQueueSink` adapters have entirely different configuration shapes (a folder path; a CUPS server URL, respectively, the latter already living in its own `Cups:ServerUrl` section per `CombinedPrintQueueSinkRegistrationTests`). YAGNI applies. Matching the issue's suggested fix also keeps this change minimal and reviewable, and mirrors the `FileStorageOptions` / `ExpeditionListArchiveOptions` precedents exactly (one options class per adapter/module, owned where it's consumed).

#### Decision 2: Bind `AzureBlobPrintSinkOptions` from the SAME `"ExpeditionList"` section, not a new sub-section or new top-level section
**Options considered:**
- (a) Bind from the existing `"ExpeditionList"` section (same keys: `ExpeditionList:BlobConnectionString`, `ExpeditionList:BlobContainerName` — unchanged).
- (b) Bind from a new sub-section, e.g. `"ExpeditionList:AzureBlob"` (the issue's parenthetical alternative).
- (c) Bind from a brand-new top-level section, e.g. `"AzureBlobPrintSink"` (mirroring the `FileStorageOptions` precedent's top-level `"FileStorage"` section exactly).

**Chosen approach:** (a).

**Rationale:** This is the one place this review deliberately diverges from the `FileStorageOptions` precedent, and it's worth being explicit about why. Both (b) and (c) rename the effective configuration key path from `ExpeditionList:BlobConnectionString` to something else (`ExpeditionList:AzureBlob:BlobConnectionString` or `AzureBlobPrintSink:BlobConnectionString`). Per `docs/architecture/environments.md` § "Module-owned Key Vault Secrets", the current value is delivered in Staging and Production via the Key Vault secret `ExpeditionList--BlobConnectionString` in `kv-heblo-stg` (and the equivalent production vault), using the project's `--`-for-`:` naming convention (see root `CLAUDE.md`). Renaming the key path means that secret no longer resolves the new key, which means:
- A new Key Vault secret must be provisioned in every non-Development environment *before* deploying the code change (see the `FileStorageOptions` plan's Prerequisites P1–P4 for exactly how heavy that process is — confirming the vault name, provisioning two secrets, verifying them, deciding on the Test-slot approach).
- Until the old and new secrets are both correct, `AzureBlob`/`Combined` print-sink mode silently gets an empty connection string in Staging/Production — a live-store-adjacent adapter (Production `ExpeditionList:PrintSink` is `"Combined"`, i.e. this path is active in production today, per `appsettings.Production.json`).

None of that operational risk is justified here: this issue is filed purely as a **layering** finding ("an Application-layer class should not hold configuration properties that exist solely for an Infrastructure/Adapter adapter"), not a "these two modules must never share a namespace" finding. Binding two different, independently-scoped C# options classes from the same configuration section is ordinary, supported `IOptions` usage — each class's binder only populates the properties it declares and silently ignores the rest (this repository does not enable `ErrorOnUnknownConfiguration` anywhere; confirmed by inspection of all `.Bind(...)`/`Configure<T>(...)` call sites). Choosing (a) fully satisfies the issue's stated concerns (dead property removed, Blob fields no longer live on an Application-layer type, secret surface area for `PrintPickingListOptions` specifically shrinks to zero) with a zero-line change to any `appsettings*.json` file and zero new Key Vault secrets. If a future finding specifically wants `ExpeditionList` to stop being a shared configuration namespace across the Application and Adapters.Azure layers, that is a separate, larger piece of work with its own deployment-prerequisite plan (as the `FileStorageOptions` migration was) — not part of this fix.

**Amendment to the issue's suggested fix:** the issue says "bound to the same config section (**or** a sub-section like `ExpeditionList:AzureBlob`)" — treat the "same config section" branch as the required approach, not an equally-weighted alternative.

#### Decision 3: `AzureAdapterModule` calls `services.Configure<AzureBlobPrintSinkOptions>(...)` itself, not `ExpeditionListModule`
**Options considered:**
- (a) `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure` calls `services.Configure<AzureBlobPrintSinkOptions>(configuration.GetSection(PrintPickingListOptions.ConfigurationKey))` inline, at the same place it currently resolves `IOptions<PrintPickingListOptions>`.
- (b) `ExpeditionListModule.AddExpeditionListModule` (Application layer) registers the binding for `AzureBlobPrintSinkOptions` even though it never consumes it, "because that's where the section is configured today."

**Chosen approach:** (a).

**Rationale:** (b) would reintroduce exactly the layering violation this issue is about, just moved one level — an Application-layer module configuring an Adapters-layer type. `Anela.Heblo.Adapters.Azure` already has a project reference to `Anela.Heblo.Application` (it already consumes `IOptions<PrintPickingListOptions>` and `PrintPickingListOptions.ConfigurationKey` today), so referencing the `ConfigurationKey` constant to point at the same section is not a new dependency — it is narrowing an existing one. `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure` is only ever called once per process (from the `"AzureBlob"` or `"Combined"` switch arms in `ServiceCollectionExtensions.AddPrintQueueSink`), so calling `services.Configure<AzureBlobPrintSinkOptions>(...)` there is a single, idempotent registration point with no risk of double-binding.

## Implementation Guidance

### Directory / Module Structure
- **New file:** `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintSinkOptions.cs` — a plain class (not a record, per this repo's DTO convention in `CLAUDE.md`), two properties: `BlobConnectionString` (`string`, default `string.Empty`) and `BlobContainerName` (`string`, default `"expedition-lists"` — must match today's default in `PrintPickingListOptions` exactly, since Development has no explicit value for this key and relies on the class default).
- **Modified file:** `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` — delete the three lines for `PrintSink`, `BlobConnectionString`, `BlobContainerName`. `ConfigurationKey` stays (still `"ExpeditionList"`).
- **Modified file:** `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` — in `AddAzurePrintQueueSinkInfrastructure`: add `services.Configure<AzureBlobPrintSinkOptions>(configuration.GetSection(PrintPickingListOptions.ConfigurationKey));` and change the `BlobContainerClient` factory to resolve `IOptions<AzureBlobPrintSinkOptions>` instead of `IOptions<PrintPickingListOptions>`. No other method in this file (`AddAzurePrintQueueSink`, `AddAzureBlobStorageService`) touches `PrintPickingListOptions` — they are unaffected.
- **No changes** to `ExpeditionListModule.cs`, `ServiceCollectionExtensions.cs`, `FileSystemAdapterServiceCollectionExtensions.cs`, `ExpeditionListService.cs`, `PrintExpeditionOrderHandler.cs`, `RunExpeditionListPrintFixHandler.cs`, `AzureBlobPrintQueueSink.cs`, `CombinedPrintQueueSink.cs`, `ExpeditionListArchiveOptions.cs`, any `appsettings*.json`, or any Key Vault secret.

### Interfaces and Contracts
```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintSinkOptions.cs
namespace Anela.Heblo.Adapters.Azure.Features.ExpeditionList;

public class AzureBlobPrintSinkOptions
{
    public string BlobConnectionString { get; set; } = string.Empty;
    public string BlobContainerName { get; set; } = "expedition-lists";
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs  (trimmed)
namespace Anela.Heblo.Application.Features.ExpeditionList;

public class PrintPickingListOptions
{
    public const string ConfigurationKey = "ExpeditionList";

    public string EmailSender { get; set; } = string.Empty;
    public string PrintQueueFolder { get; set; } = string.Empty;
    public List<string> DefaultEmailRecipients { get; set; } = new();
    public int SourceStateId { get; set; } = -2;
    public int FixSourceStateId { get; set; } = 73;
    public int DesiredStateId { get; set; } = 26;
    public string DesiredStateName { get; set; } = "Balí se";
    public int NoteStateId { get; set; } = 35;
    public bool SendToPrinterByDefault { get; set; } = false;
    public bool ChangeOrderStateByDefault { get; set; } = true;
}
```

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs  (AddAzurePrintQueueSinkInfrastructure, changed body)
public static IServiceCollection AddAzurePrintQueueSinkInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.Configure<AzureBlobPrintSinkOptions>(
        configuration.GetSection(PrintPickingListOptions.ConfigurationKey));

    services.AddSingleton(provider =>
    {
        var options = provider.GetRequiredService<IOptions<AzureBlobPrintSinkOptions>>().Value;
        return new BlobContainerClient(options.BlobConnectionString, options.BlobContainerName);
    });

    services.AddSingleton<AzureBlobPrintQueueSink>();

    return services;
}
```
(`AzureBlobPrintSinkOptions` is in the same file's existing `using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;` namespace, so no new `using` is required in `AzureAdapterModule.cs`. The existing `using Anela.Heblo.Application.Features.ExpeditionList;` stays, since `PrintPickingListOptions.ConfigurationKey` is still referenced.)

### Data Flow
Unchanged at runtime. `ServiceCollectionExtensions.AddPrintQueueSink` reads `configuration["ExpeditionList:PrintSink"]` (a raw string, never through any options object) to pick `"AzureBlob"` / `"Cups"` / `"Combined"` / default `"FileSystem"`. For `"AzureBlob"` and `"Combined"`, it calls into `AzureAdapterModule`, which now binds `AzureBlobPrintSinkOptions` from `"ExpeditionList"` and constructs `BlobContainerClient(options.BlobConnectionString, options.BlobContainerName)` — identical values, identical config keys, just read through a narrower typed options class scoped to the adapter that needs them. `ExpeditionListService` and the two MediatR handlers continue to resolve `IOptions<PrintPickingListOptions>` exactly as today, now seeing a smaller (but functionally identical, for the fields they use) options object.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A future config-binding call somewhere accidentally enables `ErrorOnUnknownConfiguration` for one of the two options types bound from the shared `"ExpeditionList"` section, which would then throw on the other type's keys. | Low | Confirmed by inspection that no `.Bind(...)`/`Configure<T>(...)` call in this codebase currently sets `BinderOptions.ErrorOnUnknownConfiguration = true`. FR-5's regression test exercises the real binding path so any future opt-in would be caught by CI, not production. |
| A developer re-adds `PrintSink`, `BlobConnectionString`, or `BlobContainerName` to `PrintPickingListOptions` later without realizing they were deliberately moved. | Low | The removed properties have no replacement property names to collide with (Adapters.Azure's class is named distinctly, `AzureBlobPrintSinkOptions`). A one-line XML-doc comment on the trimmed `PrintPickingListOptions` class (see Specification Amendments) makes the intent discoverable without requiring a new test. |
| Missing a call site that still reads `PrintPickingListOptions.BlobConnectionString`/`.BlobContainerName`/`.PrintSink` (incomplete removal). | Low | Verified by repo-wide grep during this review (Implementation Guidance's "No changes" list is exhaustive) — the only consumer of those three properties anywhere in `backend/` was `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure`. The task plan's verification task re-runs this grep as a build-time check. |
| Confusing `AzureBlobPrintSinkOptions.BlobContainerName` (Adapters.Azure, print-sink) with `ExpeditionListArchiveOptions.BlobContainerName` (Application, ExpeditionListArchive module) — same property name, different classes, different sections, both defaulting to `"expedition-lists"`. | Low | Pre-existing ambiguity (both classes already had this property name before this change); not introduced or worsened by this fix. No action needed beyond what FR-5's test and this review's naming already do. |

## Specification Amendments
- Add a short XML-doc `<remarks>` comment on the trimmed `PrintPickingListOptions` class noting that Azure Blob print-sink configuration (`BlobConnectionString`, `BlobContainerName`) and print-sink selection (`PrintSink`) live in `Anela.Heblo.Adapters.Azure.Features.ExpeditionList.AzureBlobPrintSinkOptions` and `ServiceCollectionExtensions.AddPrintQueueSink` respectively, even though all three keys still live under the same `"ExpeditionList"` configuration section. This directly forecloses the "Risks" row above about a well-intentioned but incorrect future re-addition, and costs nothing (no behavior, no test dependency).
- No other amendment to `spec.r1.md` — the spec's FR-1 through FR-5 are directly buildable as written.

## Prerequisites
None. Unlike the `FileStorageOptions` precedent, this change requires no Key Vault provisioning, no `appsettings*.json` edits, and no environment-specific sequencing — see Decision 2. Implementation can start immediately from `spec.r1.md` and this review.
