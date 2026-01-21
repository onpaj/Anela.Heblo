# GitHub Actions Workflows

This directory contains the CI/CD workflows for the Anela Heblo project.

## Workflow Overview

| Workflow | Purpose | Trigger | Estimated Time |
|----------|---------|---------|----------------|
| `ci-feature-branch.yml` | Feature branch CI + auto-deploy to staging | PR opened/updated | 15-20 min |
| `ci-main-branch.yml` | Main branch CI + manual deployments | Push to main | 15-20 min (CI)<br>+5-10 min per deployment |
| `e2e-nightly-regression.yml` | Nightly E2E regression tests | Scheduled (2 AM CET)<br>Manual trigger | 10-15 min |
| `claude.yml` | Claude Code integration | Manual trigger | Varies |
| `cleanup.yml` | Cleanup utility | Manual trigger | <1 min |
| `generate-changelog.yml` | Standalone changelog generator | Manual trigger | 1-2 min |

## Workflow Dependency Graph

```
┌─────────────────────────────────────┐
│  Feature Branch (Pull Request)      │
│                                     │
│  ┌──────────────────────────┐     │
│  │  ci-feature-branch.yml   │     │
│  │                          │     │
│  │  1. Frontend Tests       │     │
│  │  2. Backend Tests        │     │
│  │  3. Build Docker         │     │
│  │  4. Auto-deploy Staging  │     │
│  │  5. Post PR Comment      │     │
│  └──────────────────────────┘     │
│                                     │
│  Optional: @claude in commit       │
│  triggers Claude Code Review       │
└─────────────────────────────────────┘

┌─────────────────────────────────────┐
│  Main Branch (Push to main)         │
│                                     │
│  ┌──────────────────────────┐     │
│  │  ci-main-branch.yml      │     │
│  │                          │     │
│  │  1. Frontend Tests       │     │
│  │  2. Backend Tests        │     │
│  │  3. Generate Version     │     │
│  │  4. Build Docker         │     │
│  │  5. Generate Changelog   │     │
│  │  6. Push to Docker Hub   │     │
│  │                          │     │
│  │  ⏸️  STOP - Manual Approval  │
│  │                          │     │
│  │  7a. Deploy Staging     │     │
│  │     (manual approval)    │     │
│  │                          │     │
│  │  7b. Deploy Production  │     │
│  │     (manual approval)    │     │
│  │     + Smoke Tests        │     │
│  └──────────────────────────┘     │
└─────────────────────────────────────┘

┌─────────────────────────────────────┐
│  Nightly Regression                 │
│                                     │
│  ┌──────────────────────────┐     │
│  │ e2e-nightly-regression.yml │    │
│  │                          │     │
│  │  1. Health Checks        │     │
│  │  2. Run E2E Tests        │     │
│  │  3. Upload Artifacts     │     │
│  │  4. Create/Close Issue   │     │
│  │  5. Teams Notification   │     │
│  └──────────────────────────┘     │
│                                     │
│  Runs daily at 2:00 AM CET         │
│  or manual trigger                  │
└─────────────────────────────────────┘
```

## Detailed Workflow Descriptions

### 1. Feature Branch CI (`ci-feature-branch.yml`)

**Purpose:** Automatically test and deploy feature branches to staging environment

**When it runs:**
- Pull request opened
- Pull request synchronized (new commits pushed)
- Pull request reopened
- Pull request marked ready for review

**What it does:**
1. **Claude Code Review** (optional) - If commit message contains `@claude` or `@claude-review`
2. **Frontend Tests** - Jest unit tests with coverage
3. **Backend Tests** - .NET unit/integration tests with coverage (excluding Playwright)
4. **Build Docker Image** - Tagged as `staging-PR{number}-{shortSha}`
5. **Deploy to Staging** - Automatic deployment to `https://heblo.stg.anela.cz`
6. **PR Comment** - Post deployment info with URL and version

**Key Features:**
- ✅ Automatic deployment (no approval needed)
- ✅ Fast feedback (no E2E tests in CI)
- ✅ PR-specific versioning
- ✅ Deployment notification in PR comments

**Environment:** `https://heblo.stg.anela.cz` (Azure Web App `heblo-test`)

### 2. Main Branch CI (`ci-main-branch.yml`)

**Purpose:** Build versioned releases and provide manual deployment options

**When it runs:**
- Push to main branch

**What it does:**
1. **Frontend Tests** - Jest unit tests with coverage
2. **Backend Tests** - .NET unit/integration tests with coverage
3. **Generate Version** - GitVersion semantic versioning (v{major}.{minor}.{patch})
4. **Build Docker Image** - Multi-stage build with changelog generation
5. **Generate Changelog** - English version from git commits
6. **Translate Changelog** - Czech version via GPT-4o-mini
7. **Push to Docker Hub** - Tags: `v{x.y.z}`, `latest`, `sha-{commit}`
8. **⏸️ PAUSE** - Wait for manual approval
9. **Deploy to Staging** (manual) - Via `staging-approval` environment
10. **Deploy to Production** (manual) - Via `production` environment + smoke tests

**Key Features:**
- ✅ Manual deployment control
- ✅ Flexible deployment options (staging, production, or both)
- ✅ Independent approvals
- ✅ Automatic version tagging
- ✅ Changelog generation (bilingual)
- ✅ Smoke tests for production

**Environments:**
- Staging: `https://heblo.stg.anela.cz` (Azure Web App `heblo-test`)
- Production: `https://heblo.anela.cz` (Azure Web App `heblo`)

**Deployment Options:**

| Option | Description | When to use |
|--------|-------------|-------------|
| **Staging only** | Approve `staging-approval` → test → approve `production` later | Want to validate in staging first |
| **Production only** | Approve `production` directly | Staging already validated from previous PR |
| **Both at once** | Approve both environments simultaneously | Confident in changes |
| **Sequential** | Approve staging, validate, then approve production | Standard deployment process |

### 3. E2E Nightly Regression (`e2e-nightly-regression.yml`)

**Purpose:** Comprehensive end-to-end testing against live staging environment

**When it runs:**
- **Scheduled:** Daily at 2:00 AM CET (1:00 AM UTC)
- **Manual:** Via `workflow_dispatch` with optional test pattern filter

**What it does:**
1. **Health Checks** - Validate staging endpoints (`/health/live`, `/health/ready`)
2. **Run E2E Tests** - Full Playwright test suite against staging
3. **Upload Artifacts** - HTML report, screenshots, test logs (30 day retention)
4. **Create/Update Issue** - GitHub issue on failure (auto-closes on success)
5. **Teams Notification** - Optional webhook notification

**Key Features:**
- ✅ Nightly execution (results ready in morning)
- ✅ Full E2E coverage
- ✅ Health pre-checks
- ✅ Automatic issue tracking
- ✅ Manual trigger support with test pattern filter
- ✅ 30-day artifact retention

**Test Configuration:**
- **Environment:** `https://heblo.stg.anela.cz`
- **Browser:** Chromium (or manual selection: firefox, webkit, all)
- **Authentication:** Azure service principal
- **Retries:** 2 retries per test on failure

**Manual Trigger:**
```bash
# Run all tests
gh workflow run e2e-nightly-regression.yml

# Run specific test pattern
gh workflow run e2e-nightly-regression.yml -f test_pattern=catalog

# Run with different browser
gh workflow run e2e-nightly-regression.yml -f browser=firefox
```

## How to...

### Deploy a Feature Branch to Staging

**Automatic process:**
1. Create a PR from your feature branch to `main`
2. Wait for tests to pass (~15-20 minutes)
3. Staging deployment happens automatically
4. Check the PR comment for deployment URL

**Result:** Your changes are live at `https://heblo.stg.anela.cz`

### Deploy Main Branch to Staging

1. Merge PR to `main` branch
2. Wait for CI to complete (~15-20 minutes)
3. Go to **Actions** tab → Click on the running "CI - Main Branch" workflow
4. Click **Review deployments** button
5. Select `staging-approval` environment
6. Click **Approve and deploy**
7. Wait for deployment (~5-10 minutes)

**Result:** Main branch is live at `https://heblo.stg.anela.cz`

### Deploy Main Branch to Production

**Option 1: After staging validation**
1. Deploy to staging first (see above)
2. Test in staging environment
3. Return to the same workflow run
4. Click **Review deployments** button
5. Select `production` environment
6. Click **Approve and deploy**
7. Wait for deployment + smoke tests (~5-10 minutes)

**Option 2: Direct production deployment**
1. Merge PR to `main` branch
2. Wait for CI to complete (~15-20 minutes)
3. Go to **Actions** tab → Click on the running "CI - Main Branch" workflow
4. Click **Review deployments** button
5. Select `production` environment (skip staging)
6. Click **Approve and deploy**
7. Wait for deployment + smoke tests (~5-10 minutes)

**Result:** Main branch is live at `https://heblo.anela.cz`

### Run E2E Tests Manually

**Run all tests:**
```bash
gh workflow run e2e-nightly-regression.yml
```

**Run specific test pattern:**
```bash
# Run catalog tests only
gh workflow run e2e-nightly-regression.yml -f test_pattern=catalog

# Run auth tests only
gh workflow run e2e-nightly-regression.yml -f test_pattern=auth
```

**Run with different browser:**
```bash
gh workflow run e2e-nightly-regression.yml -f browser=firefox
```

**View results:**
1. Go to **Actions** tab
2. Click on the workflow run
3. Wait for completion
4. Download `e2e-nightly-test-results-{run_number}` artifact
5. Extract and open `playwright-report/index.html`

### Investigate E2E Test Failures

When E2E tests fail, a GitHub issue is automatically created with label `e2e-nightly-failure`.

**Investigation steps:**
1. **Find the issue:**
   - Go to **Issues** tab
   - Filter by label: `e2e-nightly-failure`
   - Open the issue

2. **Download test artifacts:**
   - Click the workflow run link in the issue
   - Scroll to **Artifacts** section
   - Download `e2e-nightly-test-results-{run_number}`

3. **Review test report:**
   - Extract the ZIP file
   - Open `playwright-report/index.html` in browser
   - Review failed tests, screenshots, and traces

4. **Check staging environment:**
   - Visit `https://heblo.stg.anela.cz`
   - Verify it's accessible
   - Check recent deployments

5. **Fix the issue:**
   - Create a PR with the fix
   - Tests will run nightly again
   - Issue will auto-close when tests pass

### Trigger Claude Code Review

Add `@claude` or `@claude-review` to your commit message:

```bash
git commit -m "feat: add new feature @claude"
git push
```

The Claude review will run automatically in the PR workflow.

### View Deployment History

**Staging deployments:**
```bash
gh run list --workflow=ci-feature-branch.yml --limit 10
gh run list --workflow=ci-main-branch.yml --limit 10
```

**Production deployments:**
```bash
gh run list --workflow=ci-main-branch.yml --limit 10
```

**Filter by status:**
```bash
gh run list --workflow=ci-main-branch.yml --status completed --limit 10
gh run list --workflow=ci-main-branch.yml --status success --limit 10
gh run list --workflow=ci-main-branch.yml --status failure --limit 10
```

## Troubleshooting

### CI Tests Failing

**Symptoms:** Tests fail in feature branch or main branch CI

**Solutions:**
1. **Check test output:**
   - Go to workflow run
   - Click on failed job
   - Review test logs

2. **Run tests locally:**
   ```bash
   # Frontend
   cd frontend && npm test

   # Backend
   dotnet test Anela.Heblo.sln
   ```

3. **Check for flaky tests:**
   - Review recent test runs
   - Look for intermittent failures

### Docker Build Failing

**Symptoms:** Docker image build fails

**Solutions:**
1. **Check build logs:**
   - Go to workflow run
   - Click on "Build & Push Docker Image" job
   - Review Docker build output

2. **Test Docker build locally:**
   ```bash
   docker build -t test-build .
   ```

3. **Check Dockerfile:**
   - Verify all paths are correct
   - Ensure all build args are provided

### Deployment Failing

**Symptoms:** Azure deployment fails

**Solutions:**
1. **Check Azure credentials:**
   - Verify `AZURE_CREDENTIALS_TEST` (staging)
   - Verify `AZURE_CREDENTIALS` (production)

2. **Check Docker image:**
   - Verify image exists on Docker Hub
   - Check image tag is correct

3. **Check Azure Web App:**
   - Visit Azure Portal
   - Check Web App logs
   - Verify container configuration

### E2E Tests Failing

**Symptoms:** Nightly E2E tests fail

**Solutions:**
1. **Download test artifacts:**
   - Get HTML report from workflow artifacts
   - Review screenshots and traces

2. **Check staging environment:**
   - Verify `https://heblo.stg.anela.cz` is accessible
   - Check recent deployments

3. **Run tests locally:**
   ```bash
   cd frontend
   npx playwright test
   ```

4. **Check authentication:**
   - Verify `E2E_CLIENT_ID` and `E2E_CLIENT_SECRET` secrets
   - Test Azure service principal authentication

### Changelog Generation Failing

**Symptoms:** Changelog generation or translation fails

**Solutions:**
1. **Check script execution:**
   - Review `./scripts/generate-changelog.sh` logs
   - Verify Git history is accessible

2. **Check OpenAI API:**
   - Verify `OPENAI_API_KEY` secret
   - Check API quota and limits
   - Review translation error messages

3. **Fallback behavior:**
   - If translation fails, English version is used as fallback
   - Check both `changelog.json` and `changelog.cs.json`

## Workflow Secrets

| Secret | Used By | Purpose |
|--------|---------|---------|
| `AZURE_CREDENTIALS_TEST` | Feature/Main (staging) | Azure login for staging deployments |
| `AZURE_CREDENTIALS` | Main (production) | Azure login for production deployments |
| `DOCKER_USERNAME` | Feature/Main | Docker Hub login |
| `DOCKER_PASSWORD` | Feature/Main | Docker Hub password |
| `E2E_CLIENT_ID` | E2E Nightly | Azure service principal for E2E tests |
| `E2E_CLIENT_SECRET` | E2E Nightly | Azure service principal secret |
| `REACT_APP_AZURE_CLIENT_ID` | Feature/Main | React app Azure client ID |
| `REACT_APP_AZURE_AUTHORITY` | Feature/Main | React app Azure authority |
| `REACT_APP_AZURE_BACKEND_CLIENT_ID` | Feature/Main | React app backend client ID |
| `REACT_APP_AZURE_TENANT_ID` | Feature/Main | React app tenant ID |
| `OPENAI_API_KEY` | Main | GPT-4o-mini for changelog translation |
| `CODECOV_TOKEN` | Feature/Main | Code coverage upload |
| `TEAMS_WEBHOOK_URL` | E2E Nightly (optional) | Teams notifications |
| `CLAUDE_CODE_OAUTH_TOKEN` | Feature | Claude Code review integration |
| `SHOPTET_*` | Feature/Main | Shoptet integration test credentials |
| `FLEXIBEE_*` | Feature/Main | FlexiBee integration test credentials |

## Migration Notes

This CI/CD pipeline replaces the previous workflows:
- ❌ `ci.yml` → ✅ `ci-feature-branch.yml` + `ci-main-branch.yml`
- ❌ `deploy-staging.yml` → ✅ Integrated into `ci-feature-branch.yml` and `ci-main-branch.yml`
- ❌ `deploy-production.yml` → ✅ Integrated into `ci-main-branch.yml`
- ❌ `deploy-test.yml` → ✅ Removed (obsolete test environment)
- ❌ `e2e-staging-manual.yml` → ✅ `e2e-nightly-regression.yml`

**Key Changes:**
- Feature branches now deploy automatically (no manual approval)
- Main branch deployments are manual (staging and/or production)
- E2E tests moved from CI to nightly regression
- Faster CI feedback (15-20 min vs 30-45 min)
- More flexible deployment options

## Support

For questions or issues with CI/CD:
1. Check this README first
2. Review workflow run logs in GitHub Actions
3. Check relevant documentation in `/docs`
4. Create an issue with label `ci-cd`
