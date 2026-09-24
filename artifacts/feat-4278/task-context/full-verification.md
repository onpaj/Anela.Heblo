### task: full-verification

**Files:** none (verification-only task; no new files, no code changes)

- [ ] **Step 1: Full backend build**

Run: `dotnet build Anela.Heblo.sln` (repo root — this is the only solution file in the repo)
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Format check**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: No formatting changes required. If it reports changes, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to apply them, then re-stage and amend the relevant commit from whichever of the three prior tasks introduced the unformatted code.

- [ ] **Step 3: Full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: All tests pass, including the new `ImportBankStatementRequestValidatorTests` and the modified `ImportBankStatementHandlerTests`.

- [ ] **Step 4: Manual sanity check of the new behavior (optional, no server changes needed beyond what's already built)**

This step is descriptive only — there is no new script to run. If a local API instance is available, verify:
- `POST /api/bank-statements/import` with `{"accountName": "DOES-NOT-EXIST", "dateFrom": "2024-01-01", "dateTo": "2024-01-31"}` now returns HTTP 400 with a `ProblemDetails` body containing `errors: [{ propertyName: "AccountName", ... }]`, not HTTP 500.
- `POST /api/bank-statements/import` with `dateFrom` later than `dateTo` for a known account now returns HTTP 400 with `errors: [{ propertyName: "DateFrom", errorMessage: "DateFrom must not be later than DateTo" }]`.
- `POST /api/bank-statements/import` with a known account and a valid date range still behaves exactly as before (HTTP 200 with `BankStatementImportResultDto`).

No commit for this task — it only verifies the work already committed in the three prior tasks.
