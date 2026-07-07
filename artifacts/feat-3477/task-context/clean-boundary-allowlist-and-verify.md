### task: clean-boundary-allowlist-and-verify

[Remove the 4 resolved LeafletAllowlist entries, run full backend test suite, dotnet format, final build]

- [ ] Step 1: Confirm the `"Leaflet -> KnowledgeBase"` boundary rule already passes with the current (non-empty) allowlist, isolating a genuine pass from an allowlist-editing mistake.

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ModuleBoundariesTests" \
    2>&1 | tail -60
  ```
  Expect all `ModuleBoundariesTests` theory cases to pass, including `Consumer_types_should_not_reference_provider_owned_namespaces(rule: "Leaflet -> KnowledgeBase")`.

- [ ] Step 2: Remove the four resolved allowlist entries and their justification comment.

  File: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

  Change:
  ```csharp
  // Pre-existing allowlist for Leaflet → KnowledgeBase. Each entry needs a comment with the
  // justification. Entries should be removed as the underlying violations are fixed.
  //
  // Entry format: "{ConsumerFullyQualifiedTypeName} -> {ProviderTypeFullName}"
  //
  // Compiler-generated types (e.g. DisplayClasses for closures, state machines for async
  // methods) are automatically handled by matching against the declaring type's namespace
  // prefix below.
  private static readonly HashSet<string> LeafletAllowlist = new(StringComparer.Ordinal)
  {
      // Pre-existing dependency: UploadLeafletHandler and IndexLeafletHandler consume
      // IDocumentTextExtractor, which currently lives in
      // Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting this is out of
      // scope for the 2026-05-15 Leaflet decoupling. Track separately and remove these
      // entries when IDocumentTextExtractor is relocated to a shared namespace.
      "Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet.UploadLeafletHandler -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor",
      "Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet.IndexLeafletHandler -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor",

      // Pre-existing dependency: LeafletIngestionJob consumes IOneDriveService, which
      // currently lives in Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting
      // this is out of scope for the 2026-05-15 Leaflet decoupling. Track separately and
      // remove these entries when IOneDriveService is relocated to a shared namespace.
      "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IOneDriveService",
      "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.OneDriveFile",
  };
  ```
  to:
  ```csharp
  // Allowlist for Leaflet → KnowledgeBase. Empty — IDocumentTextExtractor and IOneDriveService
  // were relocated to Anela.Heblo.Application.Shared.Rag, closing the compile-time dependency.
  private static readonly HashSet<string> LeafletAllowlist = new(StringComparer.Ordinal);
  ```

  Do not touch any other allowlist in this file (`ArticleAllowlist`, `LogisticsAllowlist`, `CatalogLogisticsAllowlist`, `CatalogPurchaseAllowlist`, `CatalogManufactureAllowlist`, `DataQualityCatalogAllowlist`, `DataQualityInvoicesAllowlist`, `ManufactureCatalogAllowlist`, `ExpeditionListLogisticsAllowlist`, `ShoptetApiAdaptersCatalogAllowlist`, `ShoptetApiAdaptersLogisticsAllowlist`, `PackagingShoptetOrdersAllowlist`) — all out of scope per the spec.

- [ ] Step 3: Run `ModuleBoundariesTests` again and confirm the `"Leaflet -> KnowledgeBase"` case still passes with the now-empty allowlist.

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ModuleBoundariesTests" \
    2>&1 | tail -60
  ```
  Expect all cases green, including `"Leaflet -> KnowledgeBase"`. If it fails, the failure output lists the exact `Consumer -> Provider` violation still remaining — cross-check it against the file inventory at the top of this plan for a missed consumer, fix the missed `using`, and re-run this step before proceeding.

- [ ] Step 4: Confirm zero remaining references to the relocated types under the old namespace anywhere in the codebase (FR-3 acceptance criterion).

  ```bash
  grep -rn "Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor\|Anela.Heblo.Application.Features.KnowledgeBase.Services.IOneDriveService\|Anela.Heblo.Application.Features.KnowledgeBase.Services.OneDriveFile\|Anela.Heblo.Application.Features.KnowledgeBase.Services.GraphOneDriveService\|Anela.Heblo.Application.Features.KnowledgeBase.Services.MockOneDriveService" \
    backend/src backend/test --include=*.cs
  ```
  Expect no output. Also confirm the old physical files are gone:
  ```bash
  ls backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ | grep -i "DocumentTextExtractor\|OneDriveService\|GraphOneDriveService\|MockOneDriveService\|GraphFolderResolver\|GraphApiHelpers\|DocumentExtractors"
  ```
  Expect no output (the `DocumentExtractors/` subfolder should no longer exist under `Features/KnowledgeBase/Services/`, and none of the listed filenames should remain there).

- [ ] Step 5: Run `dotnet format` once across the whole solution (repository validation standard) and review the diff it produces.

  ```bash
  cd backend && dotnet format Anela.Heblo.sln 2>&1 | tail -40
  git diff --stat
  ```
  If `dotnet format` changes any file outside the ones touched by this plan, review the change — it should only be whitespace/using-order normalization. If it reformats unrelated pre-existing code, revert those specific unrelated hunks with `git checkout -- <file>` to keep this change surgical, per this repo's "touch only what the task requires" rule, and re-run `dotnet format` scoped to the touched projects only if needed:
  ```bash
  dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```

- [ ] Step 6: Final full build.

  ```bash
  cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -40
  ```
  Expect: `Build succeeded.` with 0 errors, 0 new warnings.

- [ ] Step 7: Run the full backend test suite.

  ```bash
  cd backend && dotnet test Anela.Heblo.sln 2>&1 | tail -100
  ```
  Expect 0 failed tests. (Full-suite run, not just the filtered subsets used in earlier tasks — this is the final NFR-2 gate: "All existing tests that reference the relocated types must be updated in the same change set.")

- [ ] Step 8: Commit.

  ```bash
  git add -A
  git commit -m "Remove resolved Leaflet->KnowledgeBase allowlist entries; dotnet format pass"
  ```

---

## Self-review checklist (completed while writing this plan)

- FR-1 (relocate `IDocumentTextExtractor` + 3 impls): covered by `relocate-document-extractors`, Steps 1–2, 8.
- FR-2 (relocate `IOneDriveService`/`OneDriveFile` + `GraphOneDriveService`/`MockOneDriveService`/`GraphFolderResolver`, plus the arch-review-amended `GraphApiHelpers.cs`→`GraphDriveModels.cs`): covered by `relocate-onedrive-services`, Steps 1–3, 10.
- FR-3 (update all consumers, keep non-relocated-type usings intact): covered across both relocation tasks' Steps 3–7/9/11, verified in `clean-boundary-allowlist-and-verify` Step 4. `IndexDocumentHandler.cs`/`IndexDocumentHandlerTests.cs` were deliberately excluded after confirming by direct file read that they only use the non-relocated `IDocumentIndexingService`.
- FR-4 (DI registration ownership + `IConfiguration` signature change): covered by `move-di-registration-to-sharedragmodule`, all steps.
- FR-5 (remove allowlist entries, verify boundary test): covered by `clean-boundary-allowlist-and-verify`, Steps 1–3.
- NFR-1 (zero behavioral change): enforced by moving code verbatim (no logic edits beyond namespace/using lines) in every step; Decision 3 from the arch review (keep the `"KnowledgeBase"`-only config section check, do not generalize to `"Leaflet"`) is preserved verbatim in `SharedRagModule.cs` Step 1.
- NFR-2 (build/test integrity, no non-compiling intermediate commit): each task ends with a `dotnet build` + targeted `dotnet test` step before its commit; the final task runs the full suite.
- NFR-3 (no new cross-module coupling in the wrong direction): `SharedRagModule.cs`'s new dependency on `Anela.Heblo.Application.Features.KnowledgeBase` (for `KnowledgeBaseOptions`) and `Anela.Heblo.Domain.Shared` (for `InfrastructureConfigurationKeys`) was cross-checked against `ModuleBoundariesTests.cs`'s `Rules()` table — no rule inspects `Anela.Heblo.Application.Shared.Rag` as a consumer namespace, so this does not trip any boundary test; it also matches the arch review's own explicit Decision 2 code sample.
- Type/name consistency: `IDocumentTextExtractor`, `IOneDriveService`, `OneDriveFile`, `GraphOneDriveService`, `MockOneDriveService`, `GraphFolderResolver`, `GraphDriveModels`, `Anela.Heblo.Application.Shared.Rag`, `Anela.Heblo.Application.Shared.Rag.DocumentExtractors`, and `Anela.Heblo.Application.Shared.Rag.OneDrive` are spelled identically everywhere they appear across all four tasks.
- No placeholders: every step names exact files, exact before/after code, and exact commands.
