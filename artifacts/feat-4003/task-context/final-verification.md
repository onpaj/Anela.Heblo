### task: final-verification

**Files:** none (verification only — no new file changes)

- [ ] **Step 1: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: 0 errors, 0 warnings introduced.

- [ ] **Step 2: Full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: All tests PASS (no regressions anywhere in the solution, not just the Journal module).

- [ ] **Step 3: Full frontend build**

Run: `cd frontend && npm run build`
Expected: 0 errors.

- [ ] **Step 4: Full frontend lint**

Run: `cd frontend && npm run lint`
Expected: 0 errors.

- [ ] **Step 5: Repository-wide grep confirms zero remaining references**

Run: `grep -rn "SearchJournalEntryDto\|ToSearchDto" backend frontend`
Expected: No output (no matches) anywhere in the repository.

- [ ] **Step 6: `dotnet format` verify (no diffs)**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: Exits 0.
