Plan saved to `docs/superpowers/plans/2026-06-02-relocate-productexportoptions-to-filestorage.md`.

**Summary of plan**

Twelve sequential tasks executing a pure-refactor relocation of the `ProductExportOptions` POCO from `Anela.Heblo.Domain/Features/Configuration/` to `Anela.Heblo.Application/Features/FileStorage/`. Key decisions baked in from the architecture review:

- **Flat placement** (no `Configuration/` subfolder) — matches every other single-file options class in the Application project.
- **Namespace** `Anela.Heblo.Application.Features.FileStorage` — folder-default, consistent with siblings.
- **DI registration stays** on `ServiceCollectionExtensions.cs:363`; only a `using` is potentially added. The file-level `using Anela.Heblo.Domain.Features.Configuration;` is **preserved** because `ConfigurationConstants` references it elsewhere in the same file.
- **Section key string** `"ProductExportOptions"` and all class member defaults are byte-for-byte preserved (FR-5).
- **5 test files** (added per arch-review amendment 1) get their `using` swapped; the 3 production consumers simply drop the stale Domain `using` and resolve `ProductExportOptions` via parent-namespace scope.
- **Final commit** stages exactly 11 file changes (1 add, 1 delete, 9 modify); steps 9 and 12 explicitly verify nothing else slipped in (NFR-4).
- **Out-of-scope follow-ups** are listed but not executed: moving the `Configure<>` call into `FileStorageModule`, adding a `SectionName` constant, renaming the misleading `Tests/.../Configuration/` folder.