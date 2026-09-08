### task: validate-full-solution

**Files:** none (verification only — no code changes expected in this task).

**Depends on:** `add-daily-counts-repository-method`, `rewire-adapter-to-repository`.

This task runs the full project-level validation required by this repository's contribution rules (`CLAUDE.md` → "Validation before completion") before the change is considered done.

- [ ] **Step 1: Full solution build**

Run from the repository root (the solution file `Anela.Heblo.sln` lives there, not under `backend/`):

```bash
dotnet build Anela.Heblo.sln 2>&1 | tail -30
```

Expected: `Build succeeded.` with 0 errors. No new warnings attributable to the three changed files (`IIssuedInvoiceRepository.cs`, `IssuedInvoiceRepository.cs`, `InvoiceImportStatisticsSourceAdapter.cs`).

- [ ] **Step 2: Code formatting check**

Run:
```bash
dotnet format Anela.Heblo.sln --verify-no-changes 2>&1 | tail -40
```
Expected: no formatting violations reported for the three changed production files or the two changed test files. If it reports violations, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to apply fixes, review the diff to confirm it only touches files this plan changed, then re-run the `--verify-no-changes` check to confirm it is now clean.

- [ ] **Step 3: Run the full affected test project**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Invoices" 2>&1 | tail -60
```
Expected: `Passed!` — every test under the `Invoices` namespace passes, including `IssuedInvoiceRepositoryTests`, `InvoiceImportStatisticsSourceAdapterTests`, `InvoiceConsumptionSourceAdapterTests`, `InvoiceImportServiceTests`, `InvoiceImportRealChangeTrackerTests`, `GetIssuedInvoicesListHandlerPaginationTests`, and `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests` (this confirms the other `IssuedInvoiceRepository` methods were not disturbed by the edits in this plan).

- [ ] **Step 4: Run the architecture boundary tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests" 2>&1 | tail -40
```
Expected: `Passed!` — the existing cross-module boundary rules (e.g. "Analytics (Application) -> Invoices") still pass; this change does not add or remove any cross-module reference, only changes what a single class within the Invoices module depends on internally.

- [ ] **Step 5: Run the full test suite as a final sanity check**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -20
```
Expected: `Passed!` with the same total test count as `main` plus the net new/changed tests from this plan (5 new `IssuedInvoiceRepositoryTests` tests added; 5 old `InvoiceImportStatisticsSourceAdapterTests` tests replaced by 3 new ones — net change: +3 tests overall). No unrelated failures.

- [ ] **Step 6: If `dotnet format` made changes in Step 2, commit them**

```bash
git status --short
```
If this shows modified files (from an auto-fix in Step 2), stage and commit them:
```bash
git add -u
git commit -m "chore(invoices): apply dotnet format"
```
If `git status --short` is empty, no commit is needed for this task.
