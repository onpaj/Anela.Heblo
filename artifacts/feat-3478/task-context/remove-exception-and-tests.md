### task: remove-exception-and-tests

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/EmptyRetrievalException.cs`

After the three previous tasks, `EmptyRetrievalException` has zero remaining consumers in source or test code. Confirm that, then delete it.

- [ ] Step 1: Search the whole repository for any remaining reference:
  ```bash
  grep -rn "EmptyRetrievalException" backend/ --include="*.cs"
  ```
  Expect exactly one match: the type's own definition in `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/EmptyRetrievalException.cs`. If any other match appears, stop and fix it before continuing (it means an earlier task's edit was incomplete).
- [ ] Step 2: Delete the file:
  ```bash
  git rm backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/EmptyRetrievalException.cs
  ```
- [ ] Step 3: Build the backend to confirm nothing else referenced it:
  ```bash
  dotnet build Anela.Heblo.sln
  ```
- [ ] Step 4: Run the full backend test suite:
  ```bash
  dotnet test Anela.Heblo.sln
  ```
- [ ] Step 5: Re-run the grep to confirm zero remaining references anywhere in the repo (source and tests):
  ```bash
  grep -rn "EmptyRetrievalException" backend/ --include="*.cs"
  ```
  Expect no output.
- [ ] Step 6: Commit.
  ```bash
  git commit -m "#3478: delete dead EmptyRetrievalException type"
  ```

---
