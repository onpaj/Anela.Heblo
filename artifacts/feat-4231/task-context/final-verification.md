### task: final-verification

**Files:** none (verification only)

- [ ] **Step 1: Full backend build**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors, 0 new warnings.

- [ ] **Step 2: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting changes required. If it reports changes, run `dotnet format` (without `--verify-no-changes`) and re-run Step 1.

- [ ] **Step 3: Full ExpeditionListArchive + Architecture test suite**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListArchive|FullyQualifiedName~ModuleBoundariesTests"`
Expected: PASS, 0 failed (covers all 4 migrated handler test files, `BlobPathValidatorTests.cs` (unchanged, no dependency on `IBlobStorageService`), and the architecture guard).

- [ ] **Step 4: Full backend test suite (regression check)**

Run: `cd backend && dotnet test`
Expected: PASS, 0 failed. In particular, confirm `FileStorage` module's own tests (`AzureBlobStorageServiceTests.cs`, `DownloadFromUrlHandlerTests.cs`, `MockBlobStorageService.cs`, `Pipeline/FileStorageValidationPipelineTests.cs`, `AzureAdapterModuleTests.cs`) are unaffected — they mock/test `IBlobStorageService` directly and this refactor does not touch that interface or its other consumers.

- [ ] **Step 5: Verify no stray references remain**

Run: `cd backend && grep -rl "IBlobStorageService\|BlobItemInfo" src/Anela.Heblo.Application/Features/ExpeditionListArchive/`
Expected: No output (empty result) — zero files under `ExpeditionListArchive` reference `IBlobStorageService` or `BlobItemInfo` any more. If any file is listed, it was missed by an earlier task — go back and fix it before proceeding.

- [ ] **Step 6: Final commit (only if Step 2's `dotnet format` produced changes)**

```bash
git add -A backend
git commit -m "chore(expedition-list-archive): apply dotnet format after IExpeditionListArchiveBlobStore migration"
```

If Step 2 required no changes, skip this commit — there is nothing to commit.

---

## Self-Review

**1. Spec coverage:**
- FR-1 (narrow contract) → `contracts-and-dto` Step 2.
- FR-2 (`ExpeditionBlobItem` DTO) → `contracts-and-dto` Step 1.
- FR-3 (adapter) → `adapter-and-di` Step 1.
- FR-4 (DI registration) → `adapter-and-di` Step 2.
- FR-5 (migrate all four handlers, including the `ReprintExpeditionListHandler` manual factory) → `migrate-download-handler`, `migrate-reprint-handler` (Steps 1–2 cover both the handler and the factory), `migrate-get-lists-by-date-handler`, `migrate-get-dates-handler`.
- FR-6 (update existing unit tests) → each `migrate-*` task's Step 2/3 (test file rewrite), plus `final-verification` Step 4 confirms `FileStorage`-owned tests are untouched.
- FR-7 (module-boundary guard) → `module-boundary-guard`.
- NFR-1 (zero behavioral change) → every handler rewrite explicitly preserves the `Handle()` body; `final-verification` Step 4 is the regression check.
- NFR-2 (no API/contract surface change) → no `*Request`/`*Response`/`ExpeditionListItemDto` file is touched anywhere in this plan.
- NFR-3 / arch-review Decision 3 (Singleton lifetime) → `adapter-and-di` Step 2 registers `AddSingleton`.
- Arch-review Decisions 1–4 (adapter placement, binding site, lifetime, factory update) → all directly reflected in `adapter-and-di` and `migrate-reprint-handler`.

**2. Placeholder scan:** No TBD/TODO/"add appropriate"/"similar to Task N" patterns — every step shows full file contents or exact diffs.

**3. Type consistency:** `IExpeditionListArchiveBlobStore` and `ExpeditionBlobItem` signatures are identical across `contracts-and-dto`, `adapter-and-di`, and all four `migrate-*` tasks. Field name `_blobStore` (not `_blobStorageService` or `_store`) is used consistently in every handler and its test's mock variable name `_blobStoreMock`. Adapter class name `ExpeditionListArchiveBlobStoreAdapter` and namespace `Anela.Heblo.Application.Features.FileStorage.Infrastructure` are consistent between `adapter-and-di` and the `module-boundary-guard` allowlist comment.
