Plan saved to `docs/superpowers/plans/2026-06-12-relocate-productexportdownloadjob-to-catalog.md`.

## Summary

The plan decomposes the relocation into **13 bite-sized tasks**, ordered so the solution builds and the test suite stays green at every commit boundary:

1. **Tasks 1–2** — introduce `FileDownloadOptions` in FileStorage and register it.
2. **Tasks 3–4** — migrate the two FileStorage consumers (`DownloadFromUrlHandler`, `DownloadResilienceService`) to use `FileDownloadOptions`. The arch review missed that `DownloadResilienceService` also injects `ProductExportOptions` — confirmed via grep, handled in Task 4.
3. **Tasks 5–7** — replace `ProductExportOptionsTests` with `FileDownloadOptionsTests`, slim `ProductExportOptions` to `Url`+`ContainerName`, and repair `FileStorageModuleTests`.
4. **Task 8** — rename `ProductExportDownloadClientName` → `FileDownloadClientName` across 4 source + 3 test files.
5. **Tasks 9–11** — relocate `ProductExportOptions` and `ProductExportDownloadJob` to the Catalog namespace; move DI registration from `ServiceCollectionExtensions` to `CatalogModule`.
6. **Task 12** — relocate the job tests to mirror the source.
7. **Task 13** — final greps, build, full test suite, manual Hangfire dashboard smoke check, and a PR-description template.

Key facts verified against the codebase before writing:
- `IRecurringJob` is auto-discovered via assembly scan (`ServiceCollectionExtensions.cs:373`), so the job needs no explicit registration in `CatalogModule`.
- Config section `ProductExportOptions:` is already at the root, not under `FileStorage:` — no rename, no Key Vault migration.
- `DownloadResilienceService` is a second hidden consumer of the options class (only the handler was flagged in the arch review).