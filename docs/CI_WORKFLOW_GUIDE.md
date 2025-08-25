# 🔄 CI Workflow Guide

This guide explains the enhanced CI/CD workflow configuration with optional UI tests and automated merge capabilities.

## 🎯 Workflow Overview

Our CI pipeline consists of the following jobs:

### Required Jobs (Must Pass)
1. **Frontend CI** - Tests and builds the React frontend
2. **Backend CI** - Tests and builds the .NET backend
3. **Docker Build** - Creates production Docker image

### Optional Jobs (Non-blocking)
4. **UI Tests (Playwright)** - End-to-end UI testing

### Automation Jobs
5. **Quality Gate** - Validates all required jobs passed
6. **Auto-merge** - Automatically merges PRs when conditions are met

## 🎭 UI Tests (Playwright)

UI tests are **optional** and won't block PR merging if they fail.

### When UI Tests Run
- ✅ On all non-draft PRs (by default)
- ❌ Skipped when PR has `skip-ui-tests` label
- ❌ Skipped on draft PRs

### UI Test Features
- Uses the existing `./scripts/run-playwright-tests.sh` script
- Runs in automation environment (ports 5001/3001)
- 10-minute timeout to prevent hanging
- Uploads test reports and results as artifacts
- Comments results summary on PR

### UI Test Configuration
```yaml
env:
  CI: true
  PLAYWRIGHT_REPORTER: list  # Prevents HTML server timeouts
```

## 🤖 Auto-Merge Configuration

Auto-merge enables automatic PR merging when all required checks pass.

### How to Enable Auto-Merge

Add one of these labels to your PR:
- `auto-merge` - Squash and merge (default)
- `auto-squash` - Squash and merge (explicit)
- `auto-rebase` - Rebase and merge

### Auto-Merge Requirements
1. ✅ PR must not be in draft mode
2. ✅ All **required** CI jobs must pass:
   - Frontend CI
   - Backend CI
   - Docker Build
3. ✅ PR must have one of the auto-merge labels

### Merge Strategies

| Label | Strategy | Description |
|-------|----------|-------------|
| `auto-merge` | Squash | Default - creates single commit |
| `auto-squash` | Squash | Explicit squash and merge |
| `auto-rebase` | Rebase | Maintains commit history |

### Auto-Merge Features
- 🗑️ Automatically deletes branch after merge
- 💬 Comments on PR with status and instructions
- 📊 Shows UI test status (optional/non-blocking)
- ✏️ Updates existing comments instead of creating new ones

## 🏷️ Available Labels

### Control Labels
- `auto-merge` - Enable auto-merge with squash
- `auto-squash` - Enable auto-merge with squash (explicit)
- `auto-rebase` - Enable auto-merge with rebase
- `skip-ui-tests` - Skip UI tests entirely

### Example Usage
```bash
# Enable auto-merge with squash
gh pr edit 123 --add-label "auto-merge"

# Skip UI tests and enable auto-merge
gh pr edit 123 --add-label "skip-ui-tests,auto-merge"

# Use rebase merge strategy
gh pr edit 123 --add-label "auto-rebase"
```

## 📊 Quality Gate

The Quality Gate ensures only required jobs must pass:

### Required Jobs (Must Pass ✅)
- Frontend CI (tests + build)
- Backend CI (tests + build)  
- Docker Build

### Optional Jobs (Can Fail ⚠️)
- UI Tests (Playwright)

### Quality Gate Output
- Detailed status of all jobs
- Clear distinction between required/optional
- Comprehensive CI summary in GitHub Actions

## 🔧 Workflow Triggers

The CI workflow runs on:
- Pull request opened
- Pull request synchronized (new commits)
- Pull request reopened
- Pull request ready for review (from draft)

Target branch: `main`

## 📈 Status Reporting

### PR Comments
Auto-merge adds detailed comments showing:
- ✅ All required checks status
- ⚠️ Optional checks status
- 📋 Available labels and their effects
- 🔄 Merge strategy being used

### GitHub Actions Summary
Each workflow run includes:
- 📊 Job status breakdown (required vs optional)
- 📝 Commit and branch information
- 🎯 Clear pass/fail indicators

## 🚨 Troubleshooting

### UI Tests Failing
```bash
# Skip UI tests if they're blocking development
gh pr edit 123 --add-label "skip-ui-tests"
```

### Auto-merge Not Working
Check that:
1. PR is not in draft mode
2. All required jobs passed (not just UI tests)
3. PR has correct auto-merge label
4. No merge conflicts exist

### Workflow Debugging
- Check GitHub Actions logs for detailed error messages
- UI test artifacts are uploaded for debugging
- Quality Gate shows clear status of all jobs

## 📚 Best Practices

1. **Use UI tests for UI changes** - Remove `skip-ui-tests` for frontend PRs
2. **Label PRs early** - Add auto-merge labels when creating PR
3. **Review UI test failures** - Even though optional, they indicate real issues
4. **Choose merge strategy wisely**:
   - Use `auto-squash` for feature branches
   - Use `auto-rebase` for hotfixes to preserve history

## 🔮 Future Enhancements

Potential improvements:
- Conditional UI tests based on changed files
- Integration with branch protection rules
- Automatic label assignment based on PR content
- Slack/Teams notifications for auto-merge events