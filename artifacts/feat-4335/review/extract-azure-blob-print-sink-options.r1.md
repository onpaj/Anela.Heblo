# Code Review: extract-azure-blob-print-sink-options

## Summary
The implementation matches spec.r1.md exactly: `PrintPickingListOptions` is trimmed to its
Application-layer fields, `AzureBlobPrintSinkOptions` is added in the Adapters.Azure layer bound
from the same `"ExpeditionList"` section, `AzureAdapterModule` now resolves the new type, and a
regression-guard test file was added. All 193 tests in the `ExpeditionList|PrintQueueSink` filter
pass, including the 5 `CombinedPrintQueueSinkRegistrationTests` (FR-3's explicit acceptance
criterion) and the 3 new `AzureBlobPrintSinkOptionsBindingTests`.

## Review Result: PASS

### task: extract-azure-blob-print-sink-options
**Status:** PASS

Checked against spec.r1.md's FR-1 through FR-5 and NFR-1 through NFR-3:
- FR-1: `PrintPickingListOptions.cs` no longer declares `PrintSink`, `BlobConnectionString`, or
  `BlobContainerName`; repo-wide grep confirms no remaining reference to those three property
  names anywhere.
- FR-2: `AzureBlobPrintSinkOptions` exists under `Anela.Heblo.Adapters.Azure.Features.ExpeditionList`,
  is a class (not a record, per this repo's DTO/options convention), defaults match
  (`BlobContainerName = "expedition-lists"`), binds from the same `"ExpeditionList"` section via
  `PrintPickingListOptions.ConfigurationKey` — no new section or Key Vault secret introduced.
- FR-3: `AzureAdapterModule.cs` no longer resolves `IOptions<PrintPickingListOptions>` anywhere;
  `BlobContainerClient` is now built from `IOptions<AzureBlobPrintSinkOptions>` with the same
  `(connectionString, containerName)` pair. `CombinedPrintQueueSinkRegistrationTests` (5 tests)
  passes unmodified.
- FR-4: `ServiceCollectionExtensions.AddPrintQueueSink` untouched, confirmed by diff — `PrintSink`
  correctly needed no replacement class.
- FR-5: New `AzureBlobPrintSinkOptionsBindingTests` (3 tests) seeds an in-memory `"ExpeditionList"`
  config, runs `AddAzurePrintQueueSinkInfrastructure`, and asserts both the bound options values
  and the resulting `BlobContainerClient`'s `AccountName`/`Name`.
- NFR-1/NFR-3: pure structural change, no dispatch/business logic touched; compiler enforces the
  layering split (Application no longer references the Blob fields; Adapters no longer references
  the removed properties on `PrintPickingListOptions`).
- NFR-2: no `appsettings*.json` or Key Vault changes in the diff.

## Docs to Update
(none — this is an internal DI/config-binding refactor with no public API, CLI, or operational
surface change; the class-level XML doc comments added to both options classes already capture
the layering rationale for future readers)

## Overall Notes
While validating this task's build, I found a **pre-existing, unrelated** compile error in
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:51`
(`HasSeededFieldsChanged(existing, config)` should be `HasSeededFieldsChanged(existingConfig, config)`
— `existing` was recently changed to a `List<RecurringJobConfiguration>` by an unrelated batch-load
refactor). Confirmed via `git stash` that this error is present on the branch independent of this
task's diff, so it is not this task's regression and is out of scope for this PR to fix — it's a
different module (BackgroundJobs) with no relation to ExpeditionList/PrintPickingListOptions. It
currently breaks a plain `dotnet build` of the whole solution on this branch; flagging for human
attention (a separate issue) since it will keep blocking full-solution builds for every subsequent
task here until fixed.
