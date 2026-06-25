### task: fix-leaflet-generator-timeout

#### Raise RESULT_TIMEOUT_MS in leaflet-generator.spec.ts

**Files:**
- Modify: `frontend/test/e2e/marketing/leaflet-generator.spec.ts`

**Context:**  
Line 7 defines `RESULT_TIMEOUT_MS = 30_000`. The test waits at line 36 for a `.prose` container to appear after clicking "Vygenerovat leták". The LLM call behind this can take longer than 30 s. Raise to `90_000` since we cannot verify staging in this automated pipeline.

- [ ] **Step 1: Read the current file**  
  Read `frontend/test/e2e/marketing/leaflet-generator.spec.ts` and confirm line 7 has `RESULT_TIMEOUT_MS = 30_000`.

- [ ] **Step 2: Raise the timeout**  
  On line 7, change:
  ```ts
  const RESULT_TIMEOUT_MS = 30_000;
  ```
  to:
  ```ts
  const RESULT_TIMEOUT_MS = 90_000;
  ```

- [ ] **Step 3: Verify only line 7 changed**  
  Run `git diff frontend/test/e2e/marketing/leaflet-generator.spec.ts` and confirm exactly one line was modified.

- [ ] **Step 4: Commit**  
  ```
  git add frontend/test/e2e/marketing/leaflet-generator.spec.ts
  git commit -m "fix(e2e): raise RESULT_TIMEOUT_MS to 90_000 for LLM generation wait"
  ```
