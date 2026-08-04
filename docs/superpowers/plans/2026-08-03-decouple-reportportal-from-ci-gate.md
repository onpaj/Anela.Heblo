# Decouple ReportPortal Reporting From CI Deploy Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the `backend-tests` job on `main` from taking 1–2 hours by removing the live, per-test ReportPortal (RP) logger from its critical path, while still getting backend test results into ReportPortal via an async, non-blocking import job.

**Architecture:** `backend-tests` keeps producing trx + coverage as today, but drops the `ReportPortal.VSTest.TestLogger` (`--logger ReportPortal`) and the Tailscale connection it needed — that live logger makes one synchronous HTTP round-trip per test (avg 1.6–4.5s, some up to 20s, against 6,057 tests) to the self-hosted, NAS-based RP instance, which is what turns a 3.7-minute test run into a 79-minute job. Instead, `dotnet test` also emits a JUnit-format XML file (via `JunitXml.TestLogger`, in-process, no network calls) alongside the trx it already writes. A new `backend-report-portal` job — which nothing else `needs:`, so it can take as long as it wants without delaying `build-and-push`/`deploy-production` — downloads that JUnit XML as a build artifact and `POST`s it to ReportPortal's `junit/import` endpoint after the fact.

**Tech Stack:** GitHub Actions (`ci-main-branch.yml`), .NET/VSTest (`dotnet test`, `JunitXml.TestLogger` NuGet package), ReportPortal REST API (`POST /plugin/{project}/junit/import`), `curl`.

**Known risk (do not "fix" — just be aware):** ReportPortal's JUnit import endpoint has a documented history of timing out on files as small as ~1.3MB when the server is under-resourced (see `reportportal/reportportal#2474`). The self-hosted instance runs on a home NAS. With 6,057 tests the generated JUnit XML could exceed that. Per the existing "reporting is non-fatal by design" philosophy already baked into this repo's RP integration (`docs/testing/reportportal.md`), the new job is `continue-on-error: true` at every step and nothing downstream depends on it — if the import times out, you lose that run's RP data, nothing else breaks. Do not attempt to solve the import timeout in this plan; if it happens, that's a separate follow-up.

---

## File Structure

- Modify: `backend/test/Directory.Build.props` — add `JunitXml.TestLogger` package reference (alongside the existing `ReportPortal.VSTest.TestLogger` one, which stays for local opt-in use per existing docs).
- Modify: `.github/workflows/ci-main-branch.yml` — `backend-tests` job loses the Tailscale step and `--logger ReportPortal`, gains `--logger junit` + a JUnit artifact upload; new `backend-report-portal` job added after it.
- Modify: `docs/testing/reportportal.md` — update the "Backend — `dotnet test` (xUnit)" section to describe the new async-import flow instead of the live logger.

No other files change. Frontend RP reporting (Jest, `--reporters=./test/reportportal-jest.reporter.js`) is untouched — its job already finishes in ~3 minutes and isn't the problem.

---

### Task 1: Add JunitXml.TestLogger to the backend test build

**Files:**
- Modify: `backend/test/Directory.Build.props`
- Test: manual local run against `backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests` (smallest project, 6 tests — fast feedback)

- [ ] **Step 1: Confirm the logger is NOT currently available (expected failure)**

Run from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests --logger "junit;LogFilePath=/tmp/junit-check.xml"
```

Expected: the run either ignores the unknown logger silently or errors with something like `Invalid/Unsupported LoggerUri/FriendlyName` — either way, confirm `/tmp/junit-check.xml` does **not** exist afterward:

```bash
ls /tmp/junit-check.xml
```

Expected: `No such file or directory`.

- [ ] **Step 2: Add the package reference**

Open `backend/test/Directory.Build.props`. It currently looks like this:

```xml
<Project>

  <!--
    ReportPortal integration for all backend test projects (opt-in).

    The VSTest logger is only invoked when `dotnet test -l:ReportPortal` is passed AND
    reportportal_enabled=true is set in the environment (CI does both — see
    ci-main-branch.yml). Plain `dotnet test` / `dotnet build` ignore it entirely, so local
    runs are unaffected. The package is a test logger DLL; its mere presence does nothing.

    ReportPortal.config.json is shared across every test project and copied to each output
    directory next to the test assembly, where the logger looks for it. It ships with
    enabled=false; CI flips it on via reportportal_* environment variables.
  -->
  <ItemGroup>
    <PackageReference Include="ReportPortal.VSTest.TestLogger" Version="3.9.0" />
  </ItemGroup>

  <ItemGroup>
    <None Include="$(MSBuildThisFileDirectory)ReportPortal.config.json"
          Link="ReportPortal.config.json"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

Add a `JunitXml.TestLogger` package reference to the first `ItemGroup`, with a comment explaining why it's there:

```xml
<Project>

  <!--
    ReportPortal integration for all backend test projects (opt-in).

    The VSTest logger is only invoked when `dotnet test -l:ReportPortal` is passed AND
    reportportal_enabled=true is set in the environment (CI does both — see
    ci-main-branch.yml). Plain `dotnet test` / `dotnet build` ignore it entirely, so local
    runs are unaffected. The package is a test logger DLL; its mere presence does nothing.

    ReportPortal.config.json is shared across every test project and copied to each output
    directory next to the test assembly, where the logger looks for it. It ships with
    enabled=false; CI flips it on via reportportal_* environment variables.

    JunitXml.TestLogger produces a JUnit-format XML report used to import results into
    ReportPortal ASYNCHRONOUSLY after the test run (see ci-main-branch.yml's
    backend-report-portal job) instead of via ReportPortal.VSTest.TestLogger's live,
    per-test HTTP calls — the live logger was turning a 3.7-minute test run into a
    70+ minute one because of network round-trips per test. It's always attached via
    `--logger junit` in CI; unlike the RP logger it makes no network calls, so it's
    harmless to also run locally.
  -->
  <ItemGroup>
    <PackageReference Include="ReportPortal.VSTest.TestLogger" Version="3.9.0" />
    <PackageReference Include="JunitXml.TestLogger" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <None Include="$(MSBuildThisFileDirectory)ReportPortal.config.json"
          Link="ReportPortal.config.json"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Restore and verify the logger now works**

```bash
dotnet restore Anela.Heblo.sln
rm -f /tmp/junit-check.xml
dotnet test backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests --logger "junit;LogFilePath=/tmp/junit-check.xml"
cat /tmp/junit-check.xml
```

Expected: the test run passes (6 tests), and `/tmp/junit-check.xml` exists and contains a `<testsuites>`/`<testsuite>` XML structure with 6 `<testcase>` entries.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Directory.Build.props
git commit -m "feat: add JunitXml.TestLogger for async ReportPortal import"
```

---

### Task 2: Strip the live ReportPortal logger out of the gating `backend-tests` job

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml:113-227` (the `backend-tests` job)

- [ ] **Step 1: Remove the Tailscale connection step from `backend-tests`**

In `.github/workflows/ci-main-branch.yml`, find this block (currently right after "🏗️ Build all test projects" and before "🧪 Run tests with coverage"):

```yaml
      # See the frontend job for rationale — fail-safe Tailnet join for ReportPortal.
      - name: 🔗 Connect to Tailscale (for ReportPortal)
        if: vars.RP_ENABLE == 'true'
        continue-on-error: true
        uses: tailscale/github-action@v4
        with:
          # Tagged, reusable, ephemeral auth key (carries tag:ci itself, so no tags input).
          authkey: ${{ secrets.TS_AUTHKEY }}

```

Delete it entirely (the new `backend-report-portal` job in Task 3 will do its own Tailscale connect).

- [ ] **Step 2: Replace the "Run tests with coverage" step**

Find this step:

```yaml
      - name: 🧪 Run tests with coverage (excluding Playwright integration tests)
        run: |
          RP_LOGGER=""
          if [ "${RP_ENABLE}" = "true" ]; then
            RP_LOGGER="--logger ReportPortal"
            echo "📡 ReportPortal logger enabled for backend tests"
          fi
          dotnet test Anela.Heblo.sln --collect:"XPlat Code Coverage" --logger trx --logger "console;verbosity=normal" $RP_LOGGER --results-directory ./coverage --filter "Category!=Playwright&Category!=Integration" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
        env:
          ASPNETCORE_ENVIRONMENT: Automation
          # Shoptet integration test configuration
          StockClient__Url: ${{ secrets.SHOPTET_STOCK_URL }}
          ProductPriceOptions__ProductExportUrl: ${{ secrets.SHOPTET_PRODUCT_EXPORT_URL }}
          # FlexiBee integration test configuration
          FlexiBeeSettings__Server: ${{ secrets.FLEXIBEE_SERVER_URL }}
          FlexiBeeSettings__Login: ${{ secrets.FLEXIBEE_USERNAME }}
          FlexiBeeSettings__Password: ${{ secrets.FLEXIBEE_PASSWORD }}
          FlexiBeeSettings__Company: ${{ secrets.FLEXIBEE_COMPANY }}
          # ReportPortal (opt-in). The logger is only attached when RP_ENABLE=true; these
          # reportportal_* vars override backend/test/ReportPortal.config.json at runtime.
          RP_ENABLE: ${{ vars.RP_ENABLE }}
          reportportal_enabled: ${{ vars.RP_ENABLE }}
          reportportal_server_url: ${{ vars.RP_ENDPOINT }}
          reportportal_server_project: ${{ vars.RP_PROJECT }}
          reportportal_server_apikey: ${{ secrets.RP_API_KEY }}
          reportportal_launch_name: heblo-backend
          reportportal_launch_attributes: layer:backend;ci:${{ github.run_number }}
```

Replace it with:

```yaml
      - name: 🧪 Run tests with coverage (excluding Playwright integration tests)
        run: |
          dotnet test Anela.Heblo.sln --collect:"XPlat Code Coverage" --logger trx --logger "console;verbosity=normal" --logger "junit;LogFilePath=junit-results.xml" --results-directory ./coverage --filter "Category!=Playwright&Category!=Integration" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
        env:
          ASPNETCORE_ENVIRONMENT: Automation
          # Shoptet integration test configuration
          StockClient__Url: ${{ secrets.SHOPTET_STOCK_URL }}
          ProductPriceOptions__ProductExportUrl: ${{ secrets.SHOPTET_PRODUCT_EXPORT_URL }}
          # FlexiBee integration test configuration
          FlexiBeeSettings__Server: ${{ secrets.FLEXIBEE_SERVER_URL }}
          FlexiBeeSettings__Login: ${{ secrets.FLEXIBEE_USERNAME }}
          FlexiBeeSettings__Password: ${{ secrets.FLEXIBEE_PASSWORD }}
          FlexiBeeSettings__Company: ${{ secrets.FLEXIBEE_COMPANY }}
```

Note: `--results-directory ./coverage` means the JUnit file lands at `coverage/junit-results.xml` — same base directory the trx and cobertura files already use.

- [ ] **Step 3: Add a JUnit artifact upload step**

Immediately after the step from Step 2 (i.e. before "🔀 Merge coverage reports"), add:

```yaml
      - name: 📦 Persist JUnit results artifact (for async ReportPortal import)
        if: (success() || failure()) && vars.RP_ENABLE == 'true'
        uses: actions/upload-artifact@v4
        with:
          name: backend-junit-results
          path: coverage/junit-results.xml
          retention-days: 7

```

`if: success() || failure()` matters here — GitHub Actions bash steps run with `set -e`, so if `dotnet test` exits non-zero (test failures), later steps are skipped by default. We still want the JUnit file uploaded (and later imported into ReportPortal) even when tests fail, since failure history is exactly what RP is for.

- [ ] **Step 4: Validate the YAML still parses**

```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci-main-branch.yml')); print('valid YAML')"
```

Expected: `valid YAML`.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "fix: drop live ReportPortal logger from backend-tests critical path"
```

---

### Task 3: Add the async `backend-report-portal` job

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml` (add a new job after `backend-tests`, before `build-and-push`)

- [ ] **Step 1: Insert the new job**

In `.github/workflows/ci-main-branch.yml`, find the boundary between the `backend-tests` job and the `build-and-push` job:

```yaml
      - name: 🏗️ Build solution
        run: dotnet build Anela.Heblo.sln --configuration Release --no-restore

  # Build and Push Docker Image
  build-and-push:
```

Insert a new job between them, so it reads:

```yaml
      - name: 🏗️ Build solution
        run: dotnet build Anela.Heblo.sln --configuration Release --no-restore

  # Report backend test results to ReportPortal — deliberately NOT a dependency of
  # build-and-push/deploy-production. This is the async counterpart to the live
  # ReportPortal.VSTest.TestLogger that used to run inline in backend-tests: importing
  # 6000+ tests into the self-hosted, NAS-based RP instance can take over an hour, so it
  # runs off to the side here instead of blocking the deploy pipeline. Non-fatal by design
  # (continue-on-error everywhere) — same philosophy as the rest of the RP integration,
  # see docs/testing/reportportal.md.
  backend-report-portal:
    name: 📡 Report Backend Tests to ReportPortal
    runs-on: ubuntu-latest
    needs: [backend-tests]
    if: (success() || failure()) && vars.RP_ENABLE == 'true'
    steps:
      - name: 📥 Download JUnit results artifact
        continue-on-error: true
        uses: actions/download-artifact@v4
        with:
          name: backend-junit-results
          path: ./junit

      - name: 🔗 Connect to Tailscale (for ReportPortal)
        continue-on-error: true
        uses: tailscale/github-action@v4
        with:
          # Tagged, reusable, ephemeral auth key (carries tag:ci itself, so no tags input).
          authkey: ${{ secrets.TS_AUTHKEY }}

      - name: 📡 Import JUnit results into ReportPortal
        continue-on-error: true
        run: |
          curl --fail --silent --show-error \
            -X POST "${RP_ENDPOINT}/plugin/${RP_PROJECT}/junit/import" \
            -H "Authorization: Bearer ${RP_API_KEY}" \
            -F "file=@./junit/junit-results.xml;type=application/xml" \
            -F 'launchImportRq={"name":"heblo-backend","attributes":[{"key":"layer","value":"backend"},{"key":"ci","value":"'"${GITHUB_RUN_NUMBER}"'"}]};type=application/json'
        env:
          RP_ENDPOINT: ${{ vars.RP_ENDPOINT }}
          RP_PROJECT: ${{ vars.RP_PROJECT }}
          RP_API_KEY: ${{ secrets.RP_API_KEY }}

  # Build and Push Docker Image
  build-and-push:
```

- [ ] **Step 2: Confirm `build-and-push` does NOT depend on the new job**

```bash
grep -A3 "^  build-and-push:" .github/workflows/ci-main-branch.yml
```

Expected output includes `needs: [frontend-tests, backend-tests]` — `backend-report-portal` must **not** appear in that list. If it does, remove it; the entire point of this job is that nothing waits on it.

- [ ] **Step 3: Validate the YAML still parses**

```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci-main-branch.yml')); print('valid YAML')"
```

Expected: `valid YAML`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "feat: add async backend-report-portal job, decoupled from deploy gate"
```

---

### Task 4: Update ReportPortal documentation

**Files:**
- Modify: `docs/testing/reportportal.md`

- [ ] **Step 1: Replace the backend layer-wiring section**

Find this section:

```markdown
### Backend — `dotnet test` (xUnit)

- `backend/test/Directory.Build.props` adds the `ReportPortal.VSTest.TestLogger` package to
  every backend test project and copies the shared `backend/test/ReportPortal.config.json`
  next to each test assembly.
- The config ships with `"enabled": false`. The logger only activates when the run passes
  `-l:ReportPortal` **and** `reportportal_enabled=true` is in the environment.
- `ci-main-branch.yml` adds `--logger ReportPortal` and the `reportportal_*` env overrides
  only when `RP_ENABLE=true`.
- Launch: `heblo-backend`, attributes `layer:backend`, `ci:<run-number>`.

Enable locally (against your own instance) with:

```bash
reportportal_enabled=true \
reportportal_server_url=https://rp.example/api/v1 \
reportportal_server_project=heblo \
reportportal_server_apikey=<key> \
dotnet test Anela.Heblo.sln -l:ReportPortal
```
```

Replace it with:

```markdown
### Backend — `dotnet test` (xUnit)

Backend reporting is **asynchronous**, decoupled from the `backend-tests` job that gates
`build-and-push`/`deploy-production`. This is different from every other layer in this
doc, for a specific reason: `ReportPortal.VSTest.TestLogger` reports live, making one HTTP
round-trip per test. Against 6,000+ backend tests and a NAS-hosted RP instance reachable
only over Tailscale, that took the `backend-tests` job from ~4 minutes to 1–2 **hours** —
and because GitHub Actions `needs:` waits for job completion (not just success), that
latency blocked every deploy on `main`.

Instead:

- `backend/test/Directory.Build.props` adds `JunitXml.TestLogger` to every backend test
  project. `ci-main-branch.yml`'s `backend-tests` job always passes
  `--logger "junit;LogFilePath=junit-results.xml"` (no network calls, safe to run locally
  too) and uploads the resulting `coverage/junit-results.xml` as a build artifact when
  `RP_ENABLE=true`.
- A separate `backend-report-portal` job — which nothing else `needs:` — downloads that
  artifact and `POST`s it to ReportPortal's `junit/import` endpoint
  (`POST {RP_ENDPOINT}/plugin/{RP_PROJECT}/junit/import`) after `backend-tests` finishes.
  It runs in parallel with `build-and-push`/`deploy-production`, so however long the
  import takes has zero effect on deploy latency.
- `ReportPortal.VSTest.TestLogger` (the live logger) is still present in
  `Directory.Build.props` for local, single-project debugging against your own instance —
  it's just no longer wired into CI. Enable it locally with:

```bash
reportportal_enabled=true \
reportportal_server_url=https://rp.example/api/v1 \
reportportal_server_project=heblo \
reportportal_server_apikey=<key> \
dotnet test Anela.Heblo.sln -l:ReportPortal
```

- Launch: `heblo-backend`, attributes `layer:backend`, `ci:<run-number>` — same as before,
  just arriving asynchronously instead of live.
- Known risk: ReportPortal's JUnit import endpoint has timed out on files as small as
  ~1.3MB on under-resourced servers (see `reportportal/reportportal#2474`). The import
  job is `continue-on-error: true` at every step and nothing downstream depends on it, so
  a timeout just means that run's data doesn't make it into RP — consistent with the
  "reporting is non-fatal by design" principle above.
```

- [ ] **Step 2: Commit**

```bash
git add docs/testing/reportportal.md
git commit -m "docs: describe async ReportPortal import flow for backend tests"
```

---

### Task 5: Final review and live verification checklist

This task has no code changes — it's the checklist for confirming the fix actually works once it lands on `main`, since `ci-main-branch.yml` only triggers on `push: branches: [main]` and there's no safe way to dry-run the full workflow (including the real Tailscale/RP secrets) from a branch or PR.

- [ ] **Step 1: Re-read the diff end to end**

```bash
git diff main --stat
git diff main -- .github/workflows/ci-main-branch.yml backend/test/Directory.Build.props docs/testing/reportportal.md
```

Confirm: `backend-tests` no longer references `ReportPortal`, `RP_LOGGER`, or Tailscale anywhere; `backend-report-portal` exists as a job nothing `needs:`; `Directory.Build.props` has both package references; docs match the new flow.

- [ ] **Step 2: Merge to `main` through the normal PR flow**

This is a real, hard-to-reverse action (it triggers the production deploy pipeline) — do not push directly to `main`. Open a PR, let `ci-feature-branch.yml` run (note: that workflow has its own independent, RP-free `backend-tests` job definition — it validates the build/test mechanics but does **not** exercise the new `backend-report-portal` job or the RP import call, since RP is only wired into `ci-main-branch.yml`). Get it reviewed and merged normally.

- [ ] **Step 3: Watch the first real run on `main`**

```bash
gh run list --repo onpaj/Anela.Heblo --workflow=ci-main-branch.yml --limit 1
```

Take the run ID and watch it:

```bash
gh run watch <run-id> --repo onpaj/Anela.Heblo
```

Expected: `backend-tests` completes in single-digit minutes (not 1+ hour); `build-and-push` starts shortly after `frontend-tests`/`backend-tests` finish, without waiting on `backend-report-portal`.

- [ ] **Step 4: Confirm the ReportPortal import actually landed data**

Check the `backend-report-portal` job's logs for the `curl` step:

```bash
gh run view <run-id> --repo onpaj/Anela.Heblo --job <backend-report-portal-job-id> --log
```

Expected: the `curl --fail` call returns a 2xx response (curl prints nothing extra on success beyond the response body, and exits 0). If it fails (non-2xx, timeout, or connection error via Tailscale), the job step will show the error — this is the known, accepted risk from the "Known risk" section above. Confirm it's non-fatal: the job is allowed to be red without blocking `build-and-push`/`deploy-production`, which is the whole point of this change.

- [ ] **Step 5: Spot-check ReportPortal itself**

Open the RP UI (`${RP_ENDPOINT}` minus `/api/v1`, e.g. `http://nas.tail0cdb23.ts.net:8080/ui/#<project>/launches/all`) and confirm a `heblo-backend` launch appears with attributes `layer:backend` and `ci:<run-number>` matching the run from Step 3, with a test count matching what `backend-tests` reported (~6,057 at time of writing, will drift as tests are added).

---

## Self-Review Notes

- **Spec coverage:** every element of the design agreed in conversation (backend-only scope, split into a fast gating job + async import job, JUnit-format intermediate file, `continue-on-error` non-fatal philosophy, docs update) has a corresponding task above.
- **Placeholder scan:** no TBDs — every step has literal file paths, exact YAML/XML/bash to write, and a concrete pass/fail expectation.
- **Type/name consistency:** artifact name `backend-junit-results` and file path `coverage/junit-results.xml` are used identically in Task 2 (producer) and Task 3 (consumer). Env var names (`RP_ENDPOINT`, `RP_PROJECT`, `RP_API_KEY`) match the existing repo variables/secrets used elsewhere in this same workflow file.
- **Known open question, not a blocker:** the exact JSON shape ReportPortal's `junit/import` expects for `launchImportRq` was inferred from public documentation/search results, not verified against a live call (no test credentials in this environment). If the `curl` call in Task 3 fails with a 4xx on first real run (Task 5, Step 4), inspect the response body for a schema error and adjust the `-F 'launchImportRq=...'` JSON shape accordingly — this is exactly the kind of "deal with failures as they come" risk flagged upfront, not a sign the plan was wrong.
