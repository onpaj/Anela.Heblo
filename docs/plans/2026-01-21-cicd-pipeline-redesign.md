# CI/CD Pipeline Redesign

**Date:** 2026-01-21
**Status:** Design Approved
**Author:** System Design

## Overview

This document outlines the redesign of the CI/CD pipeline to improve deployment workflow, reduce CI execution time, and separate E2E testing from the critical path.

## Current State

**Existing Workflows:**
- `ci.yml` - PR CI with manual staging deployment and optional E2E tests
- `deploy-staging.yml` - Standalone staging deployment
- `deploy-production.yml` - Production deployment from main branch
- `deploy-test.yml` - Test environment deployment (obsolete)
- `e2e-staging-manual.yml` - Manual E2E test execution

**Current Issues:**
- Feature branches require manual approval to deploy to staging
- E2E tests in CI slow down feedback loop (30-45 minutes total)
- Main branch automatically deploys to production (no staging option)
- No regular E2E regression testing
- Multiple overlapping deployment workflows

## Goals

1. **Feature branches automatically deploy to staging** - Faster feedback, no manual approval needed
2. **Main branch offers manual deployment options** - Deploy to staging, production, or both in any order
3. **Remove E2E tests from CI** - Faster CI feedback (15-20 minutes instead of 30-45)
4. **Nightly E2E regression tests** - Comprehensive testing against staging every night
5. **Simplify workflow structure** - Remove obsolete and overlapping workflows

## New Architecture

### 1. Feature Branch Pipeline (`ci-feature-branch.yml`)

**Trigger:** Pull request opened, synchronized, reopened, or marked ready for review

**Flow:**
```
PR opened/updated
    ↓
┌───────────────────────────┐
│ Run Tests (Parallel)      │
│ • Frontend (Jest)         │
│ • Backend (.NET)          │
│ • Upload coverage         │
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ Build Docker Image        │
│ Tag: staging-PR{n}-{sha}  │
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ Deploy to Staging         │
│ • No manual approval      │
│ • Auto-deploy             │
│ • Health check            │
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ Post PR Comment           │
│ • Deployment URL          │
│ • Version info            │
│ • Testing instructions    │
└───────────────────────────┘
```

**Key Features:**
- **No E2E tests** - Faster feedback loop
- **No manual approval** - Automatic deployment after tests pass
- **PR-specific versioning** - `staging-PR{number}-{shortSha}`
- **Automatic PR comments** - Deployment notification with URL
- **Claude Code review** - Still available with `@claude` trigger

**Estimated Time:** 15-20 minutes (vs current 30-45 minutes)

**Test Scope:**
- ✅ Frontend Jest unit tests
- ✅ Backend .NET unit/integration tests (excluding `Category=Playwright`)
- ❌ No E2E Playwright tests

---

### 2. Main Branch Pipeline (`ci-main-branch.yml`)

**Trigger:** Push to main branch

**Flow:**
```
Push to main
    ↓
┌───────────────────────────┐
│ Run Tests (Parallel)      │
│ • Frontend (Jest)         │
│ • Backend (.NET)          │
│ • Upload coverage         │
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ Generate Version          │
│ • GitVersion semver       │
│ • Create version tag      │
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ Build Docker Image        │
│ • Generate changelog      │
│ • EN + CS translation     │
│ • Tag: v{x.y.z}, latest   │
│ • Push to Docker Hub      │
└───────────┬───────────────┘
            ↓
         ⏸️ STOP
    (Manual approval required)
            ↓
    ┌───────┴───────┐
    ↓               ↓
┌─────────┐   ┌─────────────┐
│ Staging │   │ Production  │
│ Deploy  │   │ Deploy      │
│         │   │             │
│ Manual  │   │ Manual      │
│ Approve │   │ Approve     │
└─────────┘   └─────────────┘
```

**Key Features:**
- **Manual deployment approvals** - Uses GitHub environments
- **Flexible deployment** - Deploy to staging only, production only, or both
- **Independent approvals** - Can approve at different times
- **Version tagging** - Automatic semantic versioning with GitVersion
- **Changelog generation** - English + Czech translations

**GitHub Environments:**
- `staging-approval` - Existing environment, reused for staging deployment
- `production` - Existing environment, reused for production deployment

**Deployment Options:**

**Option A: Deploy to Staging Only**
1. Main branch CI completes
2. Approve `staging-approval` environment
3. Staging deployment runs
4. Test in staging
5. Optionally approve `production` later

**Option B: Deploy to Production Only**
1. Main branch CI completes
2. Approve `production` environment
3. Production deployment runs
4. Staging remains on previous version

**Option C: Deploy to Both**
1. Main branch CI completes
2. Approve both `staging-approval` and `production`
3. Both deployments run in parallel
4. Both environments updated

**Option D: Deploy Sequentially**
1. Approve staging first, test
2. Later approve production after validation

**Estimated Time:**
- CI: 15-20 minutes (build + tests)
- Deployment: 5-10 minutes per environment (when approved)

---

### 3. Nightly E2E Regression (`e2e-nightly-regression.yml`)

**Trigger:**
- **Scheduled:** Every night at 2:00 AM CET (1:00 AM UTC)
- **Manual:** `workflow_dispatch` with optional test pattern filter

**Flow:**
```
Scheduled (2 AM CET) or Manual Trigger
    ↓
┌───────────────────────────┐
│ Validate Staging Health   │
│ • /health/live            │
│ • /health/ready           │
│ • Fail fast if down       │
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ Run Full E2E Suite        │
│ • Target: staging         │
│ • Browser: Chromium       │
│ • Auth: Azure SP          │
│ • All Playwright tests    │
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ Upload Results            │
│ • Playwright HTML report  │
│ • Screenshots (failures)  │
│ • 30 day retention        │
└───────────┬───────────────┘
            ↓
┌───────────────────────────┐
│ Notify Results            │
│ • GitHub issue (if fail)  │
│ • Teams webhook           │
│ • Workflow summary        │
└───────────────────────────┘
```

**Key Features:**
- **Nightly execution** - Runs every night at 2 AM CET (results ready in morning)
- **Full E2E coverage** - All Playwright tests against live staging
- **Health pre-check** - Validates staging is healthy before running tests
- **Automated issue creation** - Creates GitHub issue on failure (auto-closes on success)
- **Manual trigger support** - Can run ad-hoc with optional test pattern filter
- **Artifact retention** - 30 days for investigation

**Test Configuration:**
- **Base URL:** `https://heblo.stg.anela.cz`
- **Authentication:** Azure service principal (E2E credentials)
- **Browser:** Chromium headless
- **Timeout:** 30 seconds per test
- **Retries:** 2 retries on failure

**Estimated Time:** 10-15 minutes

---

## Workflow Comparison

| Aspect | Current | New |
|--------|---------|-----|
| **Feature Branch CI Time** | 30-45 min | 15-20 min |
| **Feature Branch Staging Deploy** | Manual approval | Automatic |
| **Main Branch Deployment** | Auto to production | Manual to staging/production |
| **E2E Testing** | Optional in CI | Nightly regression |
| **Deployment Flexibility** | Limited | High (staging/production/both) |
| **Total Workflows** | 9 files | 6 files |

---

## Workflow File Changes

### New Workflows (Create)
1. ✅ `ci-feature-branch.yml` - Feature branch CI + auto-deploy to staging
2. ✅ `ci-main-branch.yml` - Main branch CI + manual deployment approvals
3. ✅ `e2e-nightly-regression.yml` - Scheduled + manual E2E testing

### Workflows to Delete
1. ❌ `ci.yml` - Replaced by `ci-feature-branch.yml` and `ci-main-branch.yml`
2. ❌ `deploy-staging.yml` - Staging deployment now part of main workflow
3. ❌ `deploy-production.yml` - Production deployment now part of main workflow
4. ❌ `deploy-test.yml` - Obsolete test environment
5. ❌ `e2e-staging-manual.yml` - Replaced by `e2e-nightly-regression.yml`

### Workflows to Keep (No Changes)
1. ✅ `claude.yml` - Claude Code integration
2. ✅ `cleanup.yml` - Cleanup utility
3. ✅ `generate-changelog.yml` - Standalone changelog generator

**Before:** 9 workflow files
**After:** 6 workflow files (3 new, 3 kept, 5 deleted)

---

## Documentation Updates Required

### 1. **CLAUDE.md**
Update CI/CD section:
- Feature branch: auto-deploy to staging on PR open/update
- Main branch: manual deployment approvals using GitHub environments
- E2E tests: nightly regression only, not in CI
- New workflow file names and purposes
- Updated deployment process instructions

### 2. **docs/architecture/application_infrastructure.md**
Update deployment strategy:
- CI/CD pipeline flow diagrams
- Deployment triggers and approval process
- Environment promotion strategy (feature → staging → production)
- Testing strategy (unit/integration in CI, E2E nightly)
- Workflow file organization

### 3. **docs/architecture/environments.md**
Update environment deployment details:
- **Staging:** Deployed from feature branches (auto) + main branch (manual)
- **Production:** Deployed from main branch (manual approval only)
- **Test environment:** Removed/obsolete
- Environment URLs and configurations
- GitHub environment approval settings

### 4. **README.md**
Update if it contains:
- CI/CD badges or workflow status
- Deployment instructions
- Development workflow documentation

### 5. **`.github/workflows/README.md`**
Create or update workflow documentation:
- Purpose and trigger for each workflow
- How to manually trigger deployments from main branch
- How to interpret E2E nightly regression results
- Troubleshooting common CI/CD issues
- Workflow dependency graph

### 6. **docs/processes/deployment-guide.md** (New - Recommended)
Create comprehensive deployment guide:
- **Feature Branch Deployment:** Automatic process and what to expect
- **Main Branch to Staging:** Step-by-step manual approval process
- **Main Branch to Production:** Step-by-step manual approval process
- **Emergency Rollback:** How to rollback failed deployments
- **E2E Test Failures:** How to investigate and resolve nightly failures
- **Manual E2E Runs:** When and how to trigger manual E2E tests

---

## Implementation Plan

### Phase 1: Create New Workflows
1. Create `ci-feature-branch.yml` based on current `ci.yml` without E2E tests and manual approvals
2. Create `ci-main-branch.yml` with manual deployment jobs using GitHub environments
3. Create `e2e-nightly-regression.yml` based on `e2e-staging-manual.yml` with schedule trigger

### Phase 2: Test New Workflows
1. Test feature branch workflow on a test PR
2. Test main branch workflow on a test branch
3. Test nightly E2E workflow manually
4. Validate GitHub environment approvals work correctly

### Phase 3: Update Documentation
1. Update CLAUDE.md with new CI/CD process
2. Update architecture documentation (application_infrastructure.md, environments.md)
3. Create deployment guide (deployment-guide.md)
4. Create workflow README in .github/workflows/

### Phase 4: Remove Old Workflows
1. Delete `ci.yml`
2. Delete `deploy-staging.yml`
3. Delete `deploy-production.yml`
4. Delete `deploy-test.yml`
5. Delete `e2e-staging-manual.yml`

### Phase 5: Validation
1. Run complete feature branch flow (PR → staging deployment)
2. Run complete main branch flow (push → manual staging → manual production)
3. Verify nightly E2E schedule is configured correctly
4. Validate all documentation is up-to-date

---

## Benefits

### Developer Experience
- ✅ **Faster feedback** - 15-20 min CI instead of 30-45 min
- ✅ **Automatic staging deployments** - No waiting for manual approval on PRs
- ✅ **Flexible production deployments** - Deploy staging first, test, then production
- ✅ **Better visibility** - Clear deployment status in GitHub UI

### Quality Assurance
- ✅ **Comprehensive E2E testing** - Full suite runs nightly against staging
- ✅ **Early issue detection** - Nightly regression catches issues before production
- ✅ **Automated reporting** - GitHub issues for failures, artifacts for investigation

### Operations
- ✅ **Controlled production deployments** - Always manual approval required
- ✅ **Independent environment deployments** - Deploy staging without touching production
- ✅ **Audit trail** - GitHub environment approvals tracked
- ✅ **Simplified workflow structure** - 6 files instead of 9

### Cost Optimization
- ✅ **Reduced CI minutes** - No E2E tests in every CI run
- ✅ **Efficient E2E testing** - Single nightly run instead of per-PR
- ✅ **Parallel test execution** - Frontend and backend tests run in parallel

---

## Risk Mitigation

### Risk: E2E issues discovered late (nightly instead of per-PR)
**Mitigation:**
- Manual E2E trigger available for critical PRs
- Feature branches deploy to staging automatically for manual testing
- Unit/integration tests still catch most issues in CI

### Risk: Automatic staging deployment of broken feature branches
**Mitigation:**
- Unit/integration tests run before deployment
- Staging is a testing environment, failures are acceptable
- Multiple PRs can coexist on staging (latest wins)
- Main branch still requires manual approval for production

### Risk: Manual approval process adds friction
**Mitigation:**
- Approvals only required for main branch deployments
- GitHub UI makes approval process quick and easy
- Can approve multiple environments at once
- Staging can be skipped if already validated

---

## Success Metrics

After implementation, track:
- **CI execution time:** Target 15-20 minutes for feature branches
- **Deployment frequency:** Number of staging/production deployments per week
- **E2E test stability:** Nightly pass rate and flakiness
- **Time to production:** Time from main branch push to production deployment
- **Issue detection:** Issues caught by nightly E2E vs production

---

## Open Questions (Resolved)

✅ Feature branch deployment trigger: **PR open/update** (Option B)
✅ Nightly E2E schedule: **2:00 AM CET** (Option B)
✅ Main branch deployment flow: **Separate manual approvals for each environment** (Option A with flexibility)
✅ Test organization: **Keep unit/integration in CI, move E2E to nightly** (Option A)
✅ Existing GitHub environments: **Reuse `staging-approval` and `production`**
✅ Manual E2E workflow: **Delete `e2e-staging-manual.yml`, use nightly workflow manual trigger**
✅ Test environment: **Delete `deploy-test.yml`, environment is obsolete**

---

## Conclusion

This redesign streamlines the CI/CD pipeline by:
1. Automating feature branch deployments to staging
2. Providing flexible manual deployment options for main branch
3. Moving E2E tests to nightly regression for comprehensive coverage
4. Simplifying workflow structure from 9 to 6 files
5. Reducing CI execution time by ~50%

The new architecture balances speed, quality, and control while maintaining audit trails and deployment safety.
