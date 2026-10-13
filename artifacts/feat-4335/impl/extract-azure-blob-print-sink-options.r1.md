# Implementation: extract-azure-blob-print-sink-options

## What was implemented

Moved the three infrastructure-only properties (`PrintSink`, `BlobConnectionString`,
`BlobContainerName`) out of the Application-layer `PrintPickingListOptions` class and into a
new `AzureBlobPrintSinkOptions` class owned by the Azure Adapters layer. Both classes still
bind from the same `"ExpeditionList"` configuration section, so no config/Key Vault changes
were needed. `PrintSink` needed no replacement — it was already read as a raw config string in
`ServiceCollectionExtensions.AddPrintQueueSink`.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintSinkOptions.cs` — new class holding `BlobConnectionString` and `BlobContainerName` (default `"expedition-lists"`, matching the pre-refactor default).
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` — `AddAzurePrintQueueSinkInfrastructure` now binds `AzureBlobPrintSinkOptions` from `PrintPickingListOptions.ConfigurationKey` and constructs `BlobContainerClient` from it instead of from `PrintPickingListOptions`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` — removed `PrintSink`, `BlobConnectionString`, `BlobContainerName`; added a class-level doc comment pointing to where those concerns now live.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintSinkOptionsBindingTests.cs` — new file, 3 tests.

## Tests

`AzureBlobPrintSinkOptionsBindingTests` (new, 3 tests):
- Binds `AzureBlobPrintSinkOptions` from the `"ExpeditionList"` section via `AddAzurePrintQueueSinkInfrastructure`.
- Constructs `BlobContainerClient` correctly from the new options type.
- Confirms the `BlobContainerName` default is still `"expedition-lists"`.

## How to verify

```
dotnet build
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionList|FullyQualifiedName~PrintQueueSink"
```

All 193 tests in that filter pass, including the 3 new ones. No code anywhere else referenced
the removed properties (verified by repo-wide grep) — `AzureAdapterModule` was the only consumer
of the two Blob fields, and `PrintSink` was dead on the `IOptions` pipeline as the issue described.

## Notes

**Pre-existing, unrelated build break found and NOT fixed (out of scope for this task):**
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:51`
calls `HasSeededFieldsChanged(existing, config)`, but `existing` is now a
`List<RecurringJobConfiguration>` (from a recent unrelated batch-load refactor, PR #4323/#4324) —
it should be `existingConfig` (the `TryGetValue` out-variable). This is a `CS1503` compile error
that breaks `dotnet build` for the whole solution, confirmed via `git stash` to be present on this
branch independent of this task's changes (i.e., it predates and is unrelated to feat-4335). I did
not fix it — it's a different module (BackgroundJobs) and outside this task's surgical scope. I
verified my own change compiles and its tests pass by applying that one-line fix locally,
building/testing, then reverting it (`git checkout --`) before committing, so it does not appear
in this task's diff. **This will keep blocking a plain `dotnet build` for every task on this repo
until someone fixes it** — worth a human's attention or a separate issue.

## PR Summary
Moves `BlobConnectionString`, `BlobContainerName`, and the dead `PrintSink` property out of the
Application-layer `PrintPickingListOptions` and into a new `AzureBlobPrintSinkOptions` class in
the Azure Adapters layer, fixing the Clean Architecture layering violation described in issue
#4335. Both option classes still bind from the same `"ExpeditionList"` config section, so no
config or Key Vault changes are required. Pure structural change — no business logic moved.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintSinkOptions.cs` — new options class
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` — binds and consumes the new options type
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` — trimmed to Application-only fields
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintSinkOptionsBindingTests.cs` — new binding/regression tests

## Status
DONE_WITH_CONCERNS
