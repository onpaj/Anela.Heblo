### task: full-verification

**Files:** none (verification-only task).

**Depends on:** `add-marginleveldto-factory`, `replace-inline-construction-in-get-product-margins-handler`, `replace-inline-construction-in-get-catalog-detail-handler` (all three must be complete).

- [ ] **Step 1: Confirm zero remaining occurrences of the duplicated pattern project-wide**

Run: `grep -rn "new MarginLevelDto" backend/src/`
Expected: no output (0 matches) — every one of the original 12 inline constructions is gone.

- [ ] **Step 2: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln` (or the solution file used by this repo's CI)
Expected: Build succeeds, 0 errors, 0 new warnings.

- [ ] **Step 3: Format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: no formatting diffs reported. If it reports diffs, run `dotnet format backend/Anela.Heblo.sln` and re-run this check, then include the formatting fix in this task's commit.

- [ ] **Step 4: Full backend test run**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS, 0 failures — same pass count as `main` before this change plus the 2 new `MarginLevelDtoTests` cases.

- [ ] **Step 5: Confirm no frontend/OpenAPI client changes were triggered**

Run: `git status --short frontend/`
Expected: no output — `MarginLevelDto`'s public shape didn't change, so no generated TypeScript client diff should appear from a build. (No `npm run build` is required by this task; the DTO's public members and `[JsonPropertyName]` attributes are untouched, so there is nothing for the generator to pick up.)

- [ ] **Step 6: Commit (if Step 3 produced a formatting fix; otherwise skip — nothing to commit)**

```bash
git add -A
git commit -m "chore(catalog): dotnet format after MarginLevelDto factory refactor"
```
