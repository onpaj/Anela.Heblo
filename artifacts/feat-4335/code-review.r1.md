## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature-branch diff (`c2cbcc14...HEAD`, merge-base with `origin/main`) against
`spec.r1.md` for issue #4335 (move Azure Blob print-sink config out of `PrintPickingListOptions`).

- `PrintPickingListOptions` (`backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`)
  now holds exactly the properties FR-1 lists — `PrintSink`, `BlobConnectionString`,
  `BlobContainerName` are gone, and a repo-wide `grep` for those names on `PrintPickingListOptions`
  turns up nothing but doc-comment cross-references.
- New `AzureBlobPrintSinkOptions`
  (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintSinkOptions.cs`)
  is a class (not a record, per project convention), carries the same two properties with the
  same defaults (`BlobContainerName = "expedition-lists"`), and is bound from the same
  `"ExpeditionList"` section (FR-2) — confirmed correct: two options types binding from one
  section is standard `IOptions` behavior with no `ErrorOnUnknownConfiguration`.
- `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure` now resolves
  `IOptions<AzureBlobPrintSinkOptions>` to build the `BlobContainerClient`, and no longer resolves
  `IOptions<PrintPickingListOptions>` anywhere in the file (FR-3) — verified with `grep`.
- `ServiceCollectionExtensions.AddPrintQueueSink` is untouched and still reads
  `configuration["ExpeditionList:PrintSink"]` directly (FR-4) — the one call site is unchanged.
- New test `AzureBlobPrintSinkOptionsBindingTests` (FR-5) exercises the real DI registration path
  end-to-end: binds from an in-memory `"ExpeditionList"` section, asserts the bound
  `AzureBlobPrintSinkOptions` values, asserts the constructed `BlobContainerClient`'s
  `AccountName`/`Name` reflect the seeded connection string/container, and pins the
  `BlobContainerName` default. `CombinedPrintQueueSinkRegistrationTests` is unmodified, and this
  and prior task reviews (`review/full-verification.r1.md`) confirm it and the rest of the
  `ExpeditionList`/`PrintQueueSink` suite still pass.
- No other Application- or Adapters-layer consumer references the removed properties;
  `ExpeditionListModule`, `ExpeditionListService`, `PrintExpeditionOrderHandler`,
  `RunExpeditionListPrintFixHandler`, `PrintPickingListJob`, and `FileSystemPrintQueueSink` all
  continue to resolve `IOptions<PrintPickingListOptions>` for fields that still exist on the
  trimmed class.
- No `appsettings*.json` changes, no config-key renames — matches NFR-2.
- Pre-existing, unrelated compile error at `RecurringJobSeeder.cs:51` (`CS1503`, from PR #4324,
  already on `origin/main` — reproduced independently against `origin/main` in isolation during
  this review, outside this feature's diff and outside `ExpeditionList`/Azure adapter code) blocks
  a literal whole-solution `dotnet build`/`dotnet test`. This is not a regression introduced by
  this branch and is out of this issue's scope; already flagged in
  `artifacts/feat-4335/review/full-verification.r1.md`. Building the touched projects directly
  (`Anela.Heblo.Adapters.Azure.csproj`, which pulls in `Anela.Heblo.Application.csproj`) hits only
  that same pre-existing error — nothing new from this diff.

This is a pure, well-scoped structural refactor with zero behavior change, matching the spec's
NFR-1/NFR-3 exactly. No blocking findings.
