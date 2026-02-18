# CI Test Summary Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add GitHub Actions Job Summary to both CI workflows showing pass/fail totals and names of failed tests for BE (.NET xUnit) and FE (Jest) test jobs.

**Architecture:** `dorny/test-reporter@v1` reads TRX files (already emitted by `dotnet test --logger trx`) for BE and JUnit XML (new, via `jest-junit`) for FE, then writes a summary to the GitHub Actions workflow run summary page.

**Tech Stack:** `dorny/test-reporter@v1`, `jest-junit` npm package, GitHub Actions Job Summary (`$GITHUB_STEP_SUMMARY`)

---

### Task 1: Install jest-junit frontend dependency

**Files:**
- Modify: `frontend/package.json` (devDependencies + jest config)

**Step 1: Add jest-junit to devDependencies**

In `frontend/package.json`, add to the `"devDependencies"` block:

```json
"jest-junit": "^16.0.0"
```

Final devDependencies should look like:
```json
"devDependencies": {
  "@playwright/test": "^1.56.1",
  "@types/react-router-dom": "^5.3.3",
  "eslint": "^8.57.0",
  "husky": "^9.1.7",
  "jest-junit": "^16.0.0",
  "lint-staged": "^15.2.10"
}
```

**Step 2: Commit**

```bash
git add frontend/package.json
git commit -m "chore: add jest-junit for CI test reporting"
```

---

### Task 2: Add test reporter steps to ci-feature-branch.yml

**Files:**
- Modify: `.github/workflows/ci-feature-branch.yml`

**Step 1: Add `checks: write` to top-level permissions**

Current permissions block (line 13-16):
```yaml
permissions:
  contents: read
  pull-requests: write
  packages: read
```

Replace with:
```yaml
permissions:
  contents: read
  pull-requests: write
  packages: read
  checks: write
```

**Step 2: Update FE test command to also output JUnit XML**

Current step "🧪 Run tests with coverage" in `frontend-tests` job (line 105-110):
```yaml
      - name: 🧪 Run tests with coverage
        working-directory: ./frontend
        run: npm test -- --coverage --watchAll=false
        env:
          REACT_APP_USE_MOCK_AUTH: true
          CI: true
```

Replace with:
```yaml
      - name: 🧪 Run tests with coverage
        working-directory: ./frontend
        run: npm test -- --coverage --watchAll=false --reporters=default --reporters=jest-junit
        env:
          REACT_APP_USE_MOCK_AUTH: true
          CI: true
          JEST_JUNIT_OUTPUT_DIR: ./test-results
          JEST_JUNIT_OUTPUT_NAME: junit.xml
```

**Step 3: Add dorny/test-reporter step after FE codecov upload**

After the "📊 Upload coverage reports" step in the `frontend-tests` job (after line 125), insert:

```yaml
      - name: 📋 Frontend Test Report
        uses: dorny/test-reporter@v1
        if: success() || failure()
        with:
          name: Frontend Tests
          path: frontend/test-results/junit.xml
          reporter: jest-junit
          fail-on-error: false
```

**Step 4: Add dorny/test-reporter step after BE codecov upload**

After the "📊 Upload coverage reports" step in the `backend-tests` job (after line 236), insert:

```yaml
      - name: 📋 Backend Test Report
        uses: dorny/test-reporter@v1
        if: success() || failure()
        with:
          name: Backend Tests
          path: coverage/**/*.trx
          reporter: dotnet-trx
          fail-on-error: false
```

**Step 5: Commit**

```bash
git add .github/workflows/ci-feature-branch.yml
git commit -m "ci: add test summary reports to feature branch workflow"
```

---

### Task 3: Add test reporter steps to ci-main-branch.yml

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml`

**Step 1: Add `checks: write` to top-level permissions**

Current permissions block (line 24-26):
```yaml
permissions:
  contents: write
  packages: write
```

Replace with:
```yaml
permissions:
  contents: write
  packages: write
  checks: write
```

**Step 2: Update FE test command to also output JUnit XML**

Current step "🧪 Run tests with coverage" in `frontend-tests` job (line 52-57):
```yaml
      - name: 🧪 Run tests with coverage
        working-directory: ./frontend
        run: npm test -- --coverage --watchAll=false
        env:
          REACT_APP_USE_MOCK_AUTH: true
          CI: true
```

Replace with:
```yaml
      - name: 🧪 Run tests with coverage
        working-directory: ./frontend
        run: npm test -- --coverage --watchAll=false --reporters=default --reporters=jest-junit
        env:
          REACT_APP_USE_MOCK_AUTH: true
          CI: true
          JEST_JUNIT_OUTPUT_DIR: ./test-results
          JEST_JUNIT_OUTPUT_NAME: junit.xml
```

**Step 3: Add dorny/test-reporter step after FE codecov upload**

After the "📊 Upload coverage reports" step in the `frontend-tests` job (after line 68), insert:

```yaml
      - name: 📋 Frontend Test Report
        uses: dorny/test-reporter@v1
        if: success() || failure()
        with:
          name: Frontend Tests
          path: frontend/test-results/junit.xml
          reporter: jest-junit
          fail-on-error: false
```

**Step 4: Add dorny/test-reporter step after BE codecov upload**

After the "📊 Upload coverage reports" step in the `backend-tests` job (after line 143), insert:

```yaml
      - name: 📋 Backend Test Report
        uses: dorny/test-reporter@v1
        if: success() || failure()
        with:
          name: Backend Tests
          path: coverage/**/*.trx
          reporter: dotnet-trx
          fail-on-error: false
```

**Step 5: Commit**

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "ci: add test summary reports to main branch workflow"
```

---

### Task 4: Install jest-junit locally and update package-lock.json

**Files:**
- Modify: `frontend/package-lock.json` (auto-generated)

**Step 1: Run npm install in frontend directory**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend && npm install --legacy-peer-deps
```

Expected: jest-junit appears in node_modules and package-lock.json is updated.

**Step 2: Commit**

```bash
git add frontend/package-lock.json
git commit -m "chore: update lockfile after adding jest-junit"
```
