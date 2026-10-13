# Specification: Move Azure Blob Print-Sink Config Out of PrintPickingListOptions

## Summary
`PrintPickingListOptions` (Application layer, `ExpeditionList` module) currently carries three properties — `PrintSink`, `BlobConnectionString`, `BlobContainerName` — that belong to the Azure Blob print-sink adapter, not to the Application layer. `PrintSink` is dead (read raw from `IConfiguration`, never through the options object), and the two Blob fields are consumed exclusively by `AzureAdapterModule` in the outer Adapters layer. This change extracts those three properties into a new `AzureBlobPrintSinkOptions` class owned by `Anela.Heblo.Adapters.Azure`, leaving `PrintPickingListOptions` holding only the fields its actual Application-layer consumers (`ExpeditionListService`, `PrintExpeditionOrderHandler`, `RunExpeditionListPrintFixHandler`) use.

## Background
`PrintPickingListOptions.ConfigurationKey` binds the whole `"ExpeditionList"` configuration section. Every property in that section currently lands on one class, even though three of them (`PrintSink`, `BlobConnectionString`, `BlobContainerName`) are read only by adapter-layer code (`AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure`) or, in the case of `PrintSink`, not read from the options object at all — `ServiceCollectionExtensions.AddPrintQueueSink` reads `configuration["ExpeditionList:PrintSink"]` directly to decide which adapter to wire up. This violates Clean Architecture layering (an Application-layer options class should not exist solely to serve an Infrastructure/Adapter concern) and creates a false impression that `PrintPickingListOptions.PrintSink` controls sink dispatch at runtime. It also puts a secret-shaped field (`BlobConnectionString`) in an Application-layer type, which would force every environment to satisfy it if options validation (`ValidateDataAnnotations`/`ValidateOnStart`) is ever added to `PrintPickingListOptions`, even environments with no blob storage configured.

A directly analogous decoupling was already done for `FileStorageOptions` (see `docs/superpowers/plans/2026-06-12-decouple-filestorage-from-expeditionlist-config.md`) and for `ExpeditionListArchiveOptions` (see `docs/superpowers/plans/2026-05-26-decouple-expeditionlistarchive-from-expeditionlist.md`), both of which gave a module its own configuration section and Key Vault secret. This change follows the same layering principle but, unlike those two, does **not** require a new configuration section or new Key Vault secrets — see FR-2 and the Open Questions / architecture-review discussion of the "same section" vs. "sub-section" choice.

## Functional Requirements

### FR-1: Trim `PrintPickingListOptions` to Application-layer concerns only
Remove `PrintSink`, `BlobConnectionString`, and `BlobContainerName` from `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`. The class keeps `ConfigurationKey`, `EmailSender`, `PrintQueueFolder`, `DefaultEmailRecipients`, `SourceStateId`, `FixSourceStateId`, `DesiredStateId`, `DesiredStateName`, `NoteStateId`, `SendToPrinterByDefault`, `ChangeOrderStateByDefault` — i.e. exactly the properties actually read by `ExpeditionListService`, `PrintExpeditionOrderHandler`, and `RunExpeditionListPrintFixHandler`.

**Acceptance criteria:**
- `PrintPickingListOptions.cs` no longer declares `PrintSink`, `BlobConnectionString`, or `BlobContainerName`.
- `git grep -n "PrintPickingListOptions" backend/` shows no remaining reference to those three property names anywhere in the codebase.
- The solution builds with no new compiler errors or warnings.

### FR-2: Introduce `AzureBlobPrintSinkOptions` in the Adapters.Azure layer
Add a new class `AzureBlobPrintSinkOptions` in `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/`, holding exactly `BlobConnectionString` (string, default empty) and `BlobContainerName` (string, default `"expedition-lists"` — same default as today). Bind it from the **same** `"ExpeditionList"` configuration section that `PrintPickingListOptions` already binds from (i.e. `configuration.GetSection(PrintPickingListOptions.ConfigurationKey)`), not a new sub-section.

**Acceptance criteria:**
- `AzureBlobPrintSinkOptions` exists under the `Anela.Heblo.Adapters.Azure.Features.ExpeditionList` namespace, as a class (not a record), matching this repo's DTO/options convention.
- No `appsettings*.json` file changes and no new/renamed Key Vault secret. The config keys `ExpeditionList:BlobConnectionString` and `ExpeditionList:BlobContainerName` keep their exact current names and current values in every environment.
- Binding two different options types (trimmed `PrintPickingListOptions` and new `AzureBlobPrintSinkOptions`) from the same `"ExpeditionList"` section is valid `IOptions` usage: each class only picks up the JSON keys matching its own declared properties, and unknown extra keys are ignored (default `ErrorOnUnknownConfiguration` behavior in .NET options binding — confirmed, this is not enabled anywhere in this codebase).

### FR-3: `AzureAdapterModule` reads Blob settings via the new options type
Update `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure` (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`) to:
1. Register/bind `AzureBlobPrintSinkOptions` from `configuration.GetSection(PrintPickingListOptions.ConfigurationKey)`.
2. Build the `BlobContainerClient` singleton factory from `IOptions<AzureBlobPrintSinkOptions>` instead of `IOptions<PrintPickingListOptions>`.

**Acceptance criteria:**
- `AzureAdapterModule.cs` no longer resolves `IOptions<PrintPickingListOptions>` anywhere.
- `BlobContainerClient` is constructed with the same `(connectionString, containerName)` pair it received before this change, for the same input configuration — i.e. this is a pure refactor with no behavior change to the "AzureBlob" or "Combined" print-sink modes.
- Existing test `CombinedPrintQueueSinkRegistrationTests` (`backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`) continues to pass unmodified — it already seeds `ExpeditionList:BlobConnectionString` / `ExpeditionList:BlobContainerName` in its in-memory configuration and exercises the full `AddPrintQueueSink` → `AddAzurePrintQueueSinkInfrastructure` path for both the `"AzureBlob"` and `"Combined"` sink modes.

### FR-4: `PrintSink` needs no replacement class
`PrintSink` is not moved into `AzureBlobPrintSinkOptions` or any other options class. `ServiceCollectionExtensions.AddPrintQueueSink` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:437`) keeps reading `configuration["ExpeditionList:PrintSink"]` directly, exactly as it does today. This requirement exists only to make explicit that FR-1's removal of the dead `PrintSink` property from `PrintPickingListOptions` is not a functional regression — nothing ever read it through the options pipeline.

**Acceptance criteria:**
- `ServiceCollectionExtensions.AddPrintQueueSink` is unmodified by this change.
- `git grep -n 'configuration\["ExpeditionList:PrintSink"\]' backend/` still finds exactly the one existing call site.

### FR-5: Add a regression guard test
Add a unit test asserting `AzureBlobPrintSinkOptions`, when bound from a configuration section containing `BlobConnectionString` and `BlobContainerName`, produces an options instance with those exact values — proving the rebind is wired correctly end-to-end (configuration → `AzureAdapterModule` → `BlobContainerClient`).

**Acceptance criteria:**
- A new test (in `backend/test/Anela.Heblo.Tests/`, colocated with existing `ExpeditionList` / Azure adapter tests) seeds an in-memory configuration under the `"ExpeditionList"` key with a connection string and container name, runs `AddAzurePrintQueueSinkInfrastructure`, and asserts the resolved `BlobContainerClient`'s `AccountName`/container name (or the bound `IOptions<AzureBlobPrintSinkOptions>.Value`) reflect the seeded values.
- The full existing `CombinedPrintQueueSinkRegistrationTests` suite and all other `ExpeditionList`-module tests continue to pass.

## Non-Functional Requirements

### NFR-1: Zero behavior change
This is a structural/layering refactor only. No print-sink dispatch logic, no blob upload logic, no email/state-transition logic in `ExpeditionListService`, `PrintExpeditionOrderHandler`, or `RunExpeditionListPrintFixHandler` changes. The "FileSystem", "AzureBlob", "Cups", and "Combined" print-sink modes must behave identically before and after this change, for identical configuration input.

### NFR-2: No deployment prerequisites
Because `AzureBlobPrintSinkOptions` binds from the same `"ExpeditionList"` configuration section (FR-2), this change requires **no** new or renamed Key Vault secret and **no** `appsettings*.json` edits in any environment (Development, Staging, Production). This is a deliberate deviation from the precedent set by the `FileStorageOptions` extraction (which did introduce a new section/secret) — see the architecture review for the explicit trade-off.

### NFR-3: Layering
No Application-layer code (anything under `Anela.Heblo.Application`) may reference `AzureBlobPrintSinkOptions`. No Adapters-layer code may reference the removed `PrintPickingListOptions.PrintSink` / `.BlobConnectionString` / `.BlobContainerName` properties (they no longer exist, so this is enforced by the compiler).

## Data Model
No persisted data model changes. This affects only two in-memory configuration POCOs bound via the .NET Options pattern:

- `PrintPickingListOptions` (Application layer) — trimmed, same class, same `ConfigurationKey = "ExpeditionList"`.
- `AzureBlobPrintSinkOptions` (new, Adapters.Azure layer) — `BlobConnectionString: string = ""`, `BlobContainerName: string = "expedition-lists"`, bound from the same `"ExpeditionList"` section.

## API / Interface Design
No public API, controller, or MediatR request/response contract changes. No frontend changes. The only "interface" change is the DI wiring inside `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure`, which is internal to backend startup composition and not exposed to any consumer.

## Dependencies
- `Microsoft.Extensions.Options` / `Microsoft.Extensions.Options.ConfigurationExtensions` (already a dependency of `Anela.Heblo.Adapters.Azure` via existing `IOptions<PrintPickingListOptions>` usage).
- `Azure.Storage.Blobs` (already referenced by `Anela.Heblo.Adapters.Azure`).
- No new NuGet packages.

## Out of Scope
- Renaming or relocating the `"ExpeditionList"` configuration section itself.
- Any change to `ExpeditionListArchiveOptions` (a separate, already-decoupled options class in the `ExpeditionListArchive` module — confirmed by inspection to be unrelated, despite sharing a `BlobContainerName` property name).
- Any change to `FileStorageOptions` or the `FileStorage` module.
- Any change to `PrintQueueFolder` (the `FileSystem` print-sink's own adapter-specific field) — the issue does not flag it, and the analogous filesystem adapter registration already documents that `PrintPickingListOptions` is bound in the Application layer for this field on purpose (`FileSystemAdapterServiceCollectionExtensions.AddFileSystemPrintQueueSink` XML doc). Reclassifying it is not requested and is left for a future, separate arch-review finding if warranted.
- Adding options validation (`ValidateDataAnnotations`/`ValidateOnStart`) to either options class — the issue's "why it matters" section cites this only as a future risk this change forecloses, not as new work to do now.
- Any change to the `"Cups"` or `"FileSystem"` print-sink code paths.

## Open Questions
None.

## Status: COMPLETE
