# Resolution: ProductExportOptions ownership (FileStorage arch-review finding)

**Source:** daily arch-review routine, 2026-06-05, FileStorage module.
The brief claimed `ProductExportOptions` was being bound in
`Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:364` — outside the owning module —
and suggested moving the binding into `FileStorageModule`.

**Current state (verified 2026-06-14):**
- `ProductExportOptions` is bound exactly once, in
  `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:114`.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` contains
  zero references to `ProductExportOptions`. Line 364 currently binds `HangfireOptions`.
- `ProductExportOptions` lives in `Anela.Heblo.Application.Features.Catalog.Infrastructure`.
- The sole consumer, `ProductExportDownloadJob`, lives in
  `Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs`.

**Conclusion:** Not applicable — already resolved by the
`docs/superpowers/plans/2026-06-12-relocate-productexportdownloadjob-to-catalog.md` plan,
which moved both `ProductExportOptions` and `ProductExportDownloadJob` into Catalog. The
brief's suggested fix (move the binding into `FileStorageModule`) would **reintroduce** the
ADR-004 violation it claims to fix, because the option type and its consumer both belong to
Catalog — not to FileStorage.

**Durable trail markers added in this branch:**
- Regression guard:
  `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs`
- Decision record: `memory/decisions/product-export-options-ownership.md`

**For future arch-review runs:** any re-filing of this same finding should be closed by
linking to this resolution and to the decision memo. The guard test will fail closed if
someone moves the binding back to the API layer or to `FileStorageModule`.
