### task: full-verification

**Run the project's full backend validation suite before declaring the change done**

**Files:** none (verification only, per CLAUDE.md's "Validation before completion" checklist)

- [ ] **Step 1: Full backend build**

Run: `dotnet build`
Expected: Build succeeds with 0 errors, 0 new warnings introduced by this change.

- [ ] **Step 2: Format check**

Run: `dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports violations, run `dotnet format` (without `--verify-no-changes`) to fix them, then re-run the verify command and re-commit the formatting fix.

- [ ] **Step 3: Run the full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: All tests PASS, including every test in `RecurringJobConfigurationTests.cs` and `RecurringJobConfigurationRepositoryTests.cs` touched by this change, and no unrelated test regresses.

- [ ] **Step 4: Confirm no other reference to the removed EF mapping remains**

Run: `grep -rn "JobName" backend/src --include=*.cs | grep -v Migrations`
Expected output: only in-memory (non-query) references remain — `RecurringJobConfiguration.cs`'s computed property definition, `IRecurringJobConfigurationRepository.cs`'s `GetByJobNameAsync` parameter name, `RecurringJobSeeder.cs`'s `config.JobName` read, and unrelated types (`RecurringJobMetadata.JobName`, `BackgroundJobInfo.JobName`, job-specific `Metadata.JobName` usages in `Anela.Heblo.Adapters.*`) that are out of scope for this issue. No LINQ query against `_context.RecurringJobConfigurations` should reference `JobName`.

- [ ] **Step 5: Note the manual migration-apply step in the PR description**

Per `CLAUDE.md` ("Database migrations are manual, not automated in deployment" for Development/Test/Staging) and `docs/architecture/infrastructure.md` §5 (migrations auto-apply on Production startup), add a line to the PR description noting: "This PR includes an EF Core migration (`DropRedundantJobNameColumn`) that drops a column and a unique index. It applies automatically on Production startup; for Staging/Test/local, run `dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API` manually after deploy." No code change for this step — it is a documentation/communication step for whoever reviews or deploys the PR.

No commit for this task — it is a verification pass over the commits made in the previous two tasks. If any step fails, fix the issue in the relevant file from Task 1 or Task 2, re-run the failing step, and amend that earlier task's commit only if instructed to do so by the execution process in use; otherwise add a small follow-up commit.
