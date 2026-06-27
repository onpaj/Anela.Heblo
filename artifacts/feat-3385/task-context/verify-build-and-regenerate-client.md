### task: verify-build-and-regenerate-client

**Files:**
- No files modified — verification only.

**Goal:** Confirm the backend compiles cleanly, no stray references to the old namespace remain, and the TypeScript OpenAPI client regenerates without errors.

**Steps:**
- [ ] Step 1: Build the backend from the repo root:
  ```bash
  dotnet build backend/Anela.Heblo.sln
  ```
  Expected: `Build succeeded` with 0 errors and 0 warnings related to these changes.

- [ ] Step 2: Verify no file in `backend/` references the old namespace for these DTOs:
  ```bash
  grep -r "BackgroundJobs\.Contracts\.RefreshTask" backend/
  ```
  Expected: no output (zero matches).

- [ ] Step 3: Run `dotnet format` to confirm formatting is clean:
  ```bash
  dotnet format backend/Anela.Heblo.sln --verify-no-changes
  ```
  Expected: exits with code 0.

- [ ] Step 4: Build the frontend to regenerate the TypeScript OpenAPI client and confirm no TypeScript errors:
  ```bash
  cd frontend && npm run build
  ```
  Expected: build completes without TypeScript errors.

- [ ] Step 5: Run the frontend linter:
  ```bash
  cd frontend && npm run lint
  ```
  Expected: exits with code 0.

**Acceptance criteria:**
- `dotnet build` exits with 0 errors.
- `grep` for old namespace returns no matches.
- `npm run build` exits with 0 errors.
- `npm run lint` exits with 0 errors.