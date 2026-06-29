### task: fix-coverage-merge

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml`
- Modify: `.github/workflows/ci-feature-branch.yml`

---

#### ci-main-branch.yml — exact changes

The `backend-tests` job currently has this sequence (lines 111–160):

```
🧪 Run tests with coverage   (line 111)
📊 Process coverage files for CodeCov   (line 124)
📊 Prepare coverage file list   (line 138)
📊 Upload coverage reports   (line 144)
📦 Persist backend coverage artifact   (line 155)
```

Make three edits in order.

---

- [ ] **Step 1 — Insert the merge step between "Run tests" and "Process coverage files" in `ci-main-branch.yml`.**

  Locate the exact block (lines 124–136 are the "Process" step). Insert the new step immediately before it, after the closing env block of the test step.

  Find this text:

  ```yaml
        - name: 📊 Process coverage files for CodeCov
          run: |
            # Find all coverage.cobertura.xml files and fix paths for CodeCov
            find ./coverage -name "coverage.cobertura.xml" -type f | while read file; do
              sed -i 's|filename="|filename="backend/src/|g' "$file"
            done

            # Verify coverage files exist
            COVERAGE_COUNT=$(find ./coverage -name "coverage.cobertura.xml" -type f | wc -l)
            if [ "$COVERAGE_COUNT" -eq "0" ]; then
              echo "❌ ERROR: No coverage files found!"
              exit 1
            fi
  ```

  Replace it with:

  ```yaml
        - name: 🔀 Merge coverage reports
          run: |
            dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.4.3 || true
            reportgenerator \
              -reports:"coverage/**/*.cobertura.xml" \
              -targetdir:"coverage/merged" \
              -reporttypes:Cobertura

        - name: 📊 Process coverage files for CodeCov
          run: |
            # Fix paths in merged file for CodeCov
            sed -i 's|filename="|filename="backend/src/|g' coverage/merged/Cobertura.xml

            # Verify merged coverage file exists
            if [ ! -f coverage/merged/Cobertura.xml ]; then
              echo "❌ ERROR: Merged coverage file not found!"
              exit 1
            fi
  ```

---

- [ ] **Step 2 — Update "Prepare coverage file list" to point at the merged file in `ci-main-branch.yml`.**

  Find:

  ```yaml
        - name: 📊 Prepare coverage file list
          id: coverage-files
          run: |
            COVERAGE_FILES=$(find ./coverage -name "coverage.cobertura.xml" -type f | tr '\n' ',' | sed 's/,$//')
            echo "files=$COVERAGE_FILES" >> $GITHUB_OUTPUT
  ```

  Replace with:

  ```yaml
        - name: 📊 Prepare coverage file list
          id: coverage-files
          run: |
            echo "files=coverage/merged/Cobertura.xml" >> $GITHUB_OUTPUT
  ```

---

- [ ] **Step 3 — Update the artifact upload to include the merged file in `ci-main-branch.yml`.**

  Find:

  ```yaml
        - name: 📦 Persist backend coverage artifact
          uses: actions/upload-artifact@v4
          with:
            name: coverage-backend
            path: coverage/**/*.cobertura.xml
            retention-days: 7
  ```

  Replace with:

  ```yaml
        - name: 📦 Persist backend coverage artifact
          uses: actions/upload-artifact@v4
          with:
            name: coverage-backend
            path: |
              coverage/**/*.cobertura.xml
              coverage/merged/Cobertura.xml
            retention-days: 7
  ```

---

#### ci-feature-branch.yml — exact changes

The `backend-tests` job currently has this sequence (lines 92–152):

```
🧪 Run tests with coverage   (line 92)
📊 Process coverage files for CodeCov   (line 105)
📊 Prepare coverage file list   (line 141)
📊 Upload coverage reports   (line 149)   ← already a no-op stub
```

There is no artifact upload step in this workflow (the feature branch workflow does not persist a coverage artifact). Make two edits.

---

- [ ] **Step 4 — Insert the merge step between "Run tests" and "Process coverage files" in `ci-feature-branch.yml`.**

  Find this text (the entire "Process coverage files" step, lines 105–139):

  ```yaml
        - name: 📊 Process coverage files for CodeCov
          run: |
            # Find all coverage.cobertura.xml files and fix paths for CodeCov
            find ./coverage -name "coverage.cobertura.xml" -type f | while read file; do
              echo "Processing coverage file: $file"

              # First show original paths for debugging
              echo "Original paths in $file:"
              grep -o 'filename="[^"]*"' "$file" | head -5

              # Replace relative paths with paths relative to repository root for CodeCov
              # The original paths are like: filename="Anela.Heblo.Application/ApplicationModule.cs"
              # We need: filename="backend/src/Anela.Heblo.Application/ApplicationModule.cs"
              sed -i 's|filename="|filename="backend/src/|g' "$file"

              # Show processed paths for debugging
              echo "Processed paths in $file:"
              grep -o 'filename="[^"]*"' "$file" | head -5
              echo "---"
            done

            # Verify coverage files exist
            echo "=== Coverage files found ==="
            find ./coverage -name "coverage.cobertura.xml" -type f -exec echo "Found: {}" \;

            # Count total coverage files
            COVERAGE_COUNT=$(find ./coverage -name "coverage.cobertura.xml" -type f | wc -l)
            echo "Total coverage files: $COVERAGE_COUNT"

            if [ "$COVERAGE_COUNT" -eq "0" ]; then
              echo "❌ ERROR: No coverage files found!"
              exit 1
            fi
          env:
            ASPNETCORE_ENVIRONMENT: Automation
  ```

  Replace with:

  ```yaml
        - name: 🔀 Merge coverage reports
          run: |
            dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.4.3 || true
            reportgenerator \
              -reports:"coverage/**/*.cobertura.xml" \
              -targetdir:"coverage/merged" \
              -reporttypes:Cobertura

        - name: 📊 Process coverage files for CodeCov
          run: |
            # Fix paths in merged file for CodeCov
            echo "Processing merged coverage file..."
            echo "Original paths:"
            grep -o 'filename="[^"]*"' coverage/merged/Cobertura.xml | head -5

            sed -i 's|filename="|filename="backend/src/|g' coverage/merged/Cobertura.xml

            echo "Processed paths:"
            grep -o 'filename="[^"]*"' coverage/merged/Cobertura.xml | head -5

            # Verify merged coverage file exists
            if [ ! -f coverage/merged/Cobertura.xml ]; then
              echo "❌ ERROR: Merged coverage file not found!"
              exit 1
            fi
            echo "✅ Merged coverage file ready."
          env:
            ASPNETCORE_ENVIRONMENT: Automation
  ```

---

- [ ] **Step 5 — Update "Prepare coverage file list" to point at the merged file in `ci-feature-branch.yml`.**

  Find:

  ```yaml
        - name: 📊 Prepare coverage file list
          id: coverage-files
          run: |
            # Find all coverage files and create a comma-separated list
            COVERAGE_FILES=$(find ./coverage -name "coverage.cobertura.xml" -type f | tr '\n' ',' | sed 's/,$//')
            echo "files=$COVERAGE_FILES" >> $GITHUB_OUTPUT
            echo "Found coverage files: $COVERAGE_FILES"
  ```

  Replace with:

  ```yaml
        - name: 📊 Prepare coverage file list
          id: coverage-files
          run: |
            echo "files=coverage/merged/Cobertura.xml" >> $GITHUB_OUTPUT
            echo "Merged coverage file: coverage/merged/Cobertura.xml"
  ```

---

- [ ] **Step 6 — Commit.**

  ```bash
  git add .github/workflows/ci-main-branch.yml .github/workflows/ci-feature-branch.yml
  git commit -m "fix(ci): merge Cobertura XMLs before coverage-gap routine reads them

  dotnet test produces one coverage.cobertura.xml per test project.
  Without merging, the coverage-gap routine may read a single project
  XML and miss lines exercised by other projects, causing false low
  coverage readings (e.g. ScanPackingOrderHandler at 32.9% vs true ~65%).

  Add a reportgenerator merge step after the test run in both CI
  workflows. All downstream steps now consume coverage/merged/Cobertura.xml."
  ```

---

## Verification

After the CI run completes on the branch with these changes:

1. In the Actions log for the `backend-tests` job, confirm the `🔀 Merge coverage reports` step runs without error and prints a line like `Successfully created Cobertura report`.
2. Confirm the `📊 Process coverage files for CodeCov` step processes exactly one file (`coverage/merged/Cobertura.xml`).
3. Confirm no coverage-threshold failure is reported for `ScanPackingOrderHandler.cs` — it should appear at ≥ 60% in the gap routine's next weekly run.
4. Confirm no existing tests were removed or disabled (the `dotnet test` command and filter are unchanged).
