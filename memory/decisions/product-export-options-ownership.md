# Decision: ProductExportOptions Is Owned by the Catalog Module

**Decision:** `ProductExportOptions` and its sole consumer `ProductExportDownloadJob`
both live in the Catalog vertical slice (`Anela.Heblo.Application.Features.Catalog.Infrastructure`).
The DI binding `services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"))`
lives in `CatalogModule.AddCatalogModule` (currently `CatalogModule.cs:114`). FileStorage exposes
only generic download/upload primitives (`IBlobStorageService`, `FileDownloadOptions`) and
does **not** own anything specific to product exports. (ADR-004 in
`docs/architecture/development_guidelines.md`.)

**Why:** Two earlier plans considered this question and converged on the current placement:
`docs/superpowers/plans/2026-06-02-relocate-productexportoptions-to-filestorage.md` proposed
moving the options into FileStorage. That decision was superseded by
`docs/superpowers/plans/2026-06-12-relocate-productexportdownloadjob-to-catalog.md`, which
moved both the options type **and** the consuming job into Catalog. Reason: the consumer is a
catalog-data refresh job, not a generic file operation; under ADR-004 each vertical slice
owns the options its module reads. Co-locating the binding with the consumer prevents a
recurring cross-module wiring split — exactly the same principle as the repository-binding
ruling in `[[repository-di-in-feature-module]]`.

**How to apply:**
- The binding line stays at `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`
  (line 114 at time of writing — line number may drift, but the file is stable).
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` must **never** bind
  `ProductExportOptions`. If a future arch-review or audit recommends moving it there, reject
  the recommendation and link back to this memo.
- Future arch-review iterations that re-file a "FileStorage owns ProductExportOptions"
  finding should be closed by pointing at this memo and at
  `artifacts/feat-arch-review-filestorage-productexportopt/resolution.md`.
- Regression guard:
  `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs`
  fails closed if the binding is deleted from `CatalogModule` or repointed at the wrong
  configuration section.
- Companion to `[[repository-di-in-feature-module]]` — the same ADR-004 principle applied to
  options bindings rather than repository bindings.
- Follow-up (out of scope here): a cross-module convention test that scans every `*Module.cs`
  and asserts each `Configure<T>` call sits in the owning module is a worthwhile future
  investment but should be tracked as its own task.
