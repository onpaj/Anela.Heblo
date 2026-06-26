# CI/CD Workflow Enhancements Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add GitHub releases creation, fix smoke test timeout issues, and implement Teams notifications for production deployments.

**Architecture:** Enhance existing CI/CD workflow (.github/workflows/ci-main-branch.yml) to:
1. Create GitHub releases with changelog after successful Docker build
2. Increase smoke test timeout and retries for reliable health checks
3. Send Teams webhook notifications after successful production deployment

**Tech Stack:** GitHub Actions, GitHub CLI (gh), curl for Teams webhook, bash scripting

---

## Task 1: Add GitHub Release Creation

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml:327-344` (after git tag creation)

### Step 1: Add GitHub Release creation step

Add a new step after "🏷️ Create and Push Git Tag" step to create a GitHub release using the generated changelog.

**Location:** After line 344 in `.github/workflows/ci-main-branch.yml`

```yaml
      - name: 📦 Create GitHub Release
        run: |
          VERSION="${{ steps.version.outputs.version }}"
          echo "📦 Creating GitHub release: $VERSION"

          # Read the English changelog
          if [ ! -f frontend/public/changelog.json ]; then
            echo "❌ Changelog file not found!"
            exit 1
          fi

          # Extract changelog for current version
          CURRENT_VERSION="${VERSION#v}"
          RELEASE_NOTES=$(jq -r --arg version "$CURRENT_VERSION" '
            .versions[] |
            select(.version == $version) |
            "## Changes\n\n" +
            (.changes | map(
              if .type == "feature" then "### ✨ Features\n- " + .title
              elif .type == "fix" then "### 🐛 Bug Fixes\n- " + .title
              elif .type == "improvement" then "### 📈 Improvements\n- " + .title
              elif .type == "docs" then "### 📚 Documentation\n- " + .title
              elif .type == "perf" then "### ⚡ Performance\n- " + .title
              elif .type == "refactor" then "### ♻️ Refactoring\n- " + .title
              elif .type == "test" then "### 🧪 Tests\n- " + .title
              elif .type == "ci" then "### 👷 CI/CD\n- " + .title
              else "### 📝 Other\n- " + .title
              end
            ) | join("\n"))
          ' frontend/public/changelog.json || echo "No changelog available for this version.")

          # If no release notes found, use a default message
          if [ -z "$RELEASE_NOTES" ] || [ "$RELEASE_NOTES" == "null" ]; then
            RELEASE_NOTES="## Changes\n\nSee [changelog](https://heblo.anela.cz) for detailed release notes."
          fi

          # Create GitHub release
          echo "$RELEASE_NOTES" | gh release create "$VERSION" \
            --title "Release $VERSION" \
            --notes-file - \
            --verify-tag

          echo "✅ GitHub release $VERSION created successfully!"
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

### Step 2: Verify the workflow syntax

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
# Use GitHub Actions workflow validation
gh workflow view ci-main-branch.yml
```

Expected: Workflow should be valid YAML and show the updated structure

### Step 3: Commit the changes

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "feat(ci): add GitHub release creation after build

- Extract changelog for current version from changelog.json
- Format release notes by change type (features, fixes, improvements, etc.)
- Create GitHub release using gh CLI
- Include fallback message if changelog is not available"
```

---

## Task 2: Fix Smoke Test Timeout Issues

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml:506-517` (production smoke tests)
- Modify: `.github/workflows/ci-main-branch.yml:415-426` (staging deployment wait)

### Step 1: Increase staging deployment wait time

Replace the staging wait step (lines 415-426) with more robust waiting logic:

```yaml
      - name: ⏳ Wait for deployment to be ready
        run: |
          echo "⏳ Waiting for deployment to be ready..."

          # Initial wait for container startup (increased from 30s to 60s)
          sleep 60

          # Health check with retries (increased from 10 to 20 attempts with 15s intervals = 5 minutes total)
          MAX_ATTEMPTS=20
          ATTEMPT=1

          while [ $ATTEMPT -le $MAX_ATTEMPTS ]; do
            echo "🔍 Health check attempt $ATTEMPT/$MAX_ATTEMPTS..."

            HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" https://${{ steps.deploy.outputs.hostname }}/health || echo "000")

            if [ "$HTTP_CODE" == "200" ]; then
              echo "✅ Deployment is ready! (attempt $ATTEMPT/$MAX_ATTEMPTS)"
              break
            fi

            if [ $ATTEMPT -eq $MAX_ATTEMPTS ]; then
              echo "❌ Deployment failed to become ready after $MAX_ATTEMPTS attempts"
              echo "Last HTTP status code: $HTTP_CODE"
              exit 1
            fi

            echo "⏳ Waiting for deployment... (HTTP $HTTP_CODE, attempt $ATTEMPT/$MAX_ATTEMPTS)"
            sleep 15
            ATTEMPT=$((ATTEMPT + 1))
          done
```

### Step 2: Increase production deployment wait time and improve smoke tests

Replace the production wait step (lines 506-517) and smoke tests (lines 519-547) with enhanced logic:

```yaml
      - name: ⏳ Wait for deployment to be ready
        run: |
          echo "⏳ Waiting for deployment to be ready..."

          # Initial wait for container startup (increased from 30s to 60s)
          sleep 60

          # Health check with retries (increased from 10 to 20 attempts with 15s intervals = 5 minutes total)
          MAX_ATTEMPTS=20
          ATTEMPT=1

          while [ $ATTEMPT -le $MAX_ATTEMPTS ]; do
            echo "🔍 Health check attempt $ATTEMPT/$MAX_ATTEMPTS..."

            HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" https://${{ steps.deploy.outputs.hostname }}/health || echo "000")

            if [ "$HTTP_CODE" == "200" ]; then
              echo "✅ Deployment is ready! (attempt $ATTEMPT/$MAX_ATTEMPTS)"
              break
            fi

            if [ $ATTEMPT -eq $MAX_ATTEMPTS ]; then
              echo "❌ Deployment failed to become ready after $MAX_ATTEMPTS attempts"
              echo "Last HTTP status code: $HTTP_CODE"
              exit 1
            fi

            echo "⏳ Waiting for deployment... (HTTP $HTTP_CODE, attempt $ATTEMPT/$MAX_ATTEMPTS)"
            sleep 15
            ATTEMPT=$((ATTEMPT + 1))
          done

      - name: 🧪 Run Smoke Tests
        run: |
          echo "🧪 Running smoke tests against production..."

          HOSTNAME="${{ steps.deploy.outputs.hostname }}"
          ALL_PASSED=true

          # Health check with timeout
          echo "🔍 Testing /health endpoint..."
          HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 30 "https://$HOSTNAME/health" || echo "000")
          if [ "$HTTP_CODE" == "200" ]; then
            echo "✅ Health check passed (HTTP $HTTP_CODE)"
          else
            echo "❌ Health check failed (HTTP $HTTP_CODE)"
            ALL_PASSED=false
          fi

          # Liveness check with timeout
          echo "🔍 Testing /health/live endpoint..."
          HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 30 "https://$HOSTNAME/health/live" || echo "000")
          if [ "$HTTP_CODE" == "200" ]; then
            echo "✅ Liveness check passed (HTTP $HTTP_CODE)"
          else
            echo "❌ Liveness check failed (HTTP $HTTP_CODE)"
            ALL_PASSED=false
          fi

          # Readiness check with timeout
          echo "🔍 Testing /health/ready endpoint..."
          HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 30 "https://$HOSTNAME/health/ready" || echo "000")
          if [ "$HTTP_CODE" == "200" ]; then
            echo "✅ Readiness check passed (HTTP $HTTP_CODE)"
          else
            echo "❌ Readiness check failed (HTTP $HTTP_CODE)"
            ALL_PASSED=false
          fi

          if [ "$ALL_PASSED" = true ]; then
            echo "✅ All smoke tests passed!"
          else
            echo "❌ Some smoke tests failed!"
            exit 1
          fi
```

### Step 3: Verify the workflow syntax

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
# Validate YAML syntax
yamllint .github/workflows/ci-main-branch.yml || echo "yamllint not installed, skipping"
# Verify workflow
gh workflow view ci-main-branch.yml
```

Expected: Workflow should be valid YAML with updated timeout logic

### Step 4: Commit the changes

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "fix(ci): improve deployment health check reliability

- Increase initial wait from 30s to 60s for container startup
- Increase max attempts from 10 to 20 (5 minutes total timeout)
- Increase interval between checks from 10s to 15s
- Add detailed HTTP status code logging for failed checks
- Improve smoke tests with explicit timeout and better error reporting
- Show attempt number and HTTP codes for debugging"
```

---

## Task 3: Add Teams Webhook Notification

**Files:**
- Modify: `.github/workflows/ci-main-branch.yml:549-559` (after production deployment summary)

### Step 1: Add Teams notification step

Add a new step after the "📝 Deployment Summary" step in the production deployment job:

**Location:** After line 559 in `.github/workflows/ci-main-branch.yml`

```yaml
      - name: 📢 Notify Teams about Production Deployment
        if: success()
        run: |
          VERSION="${{ needs.build-and-push.outputs.version }}"
          HOSTNAME="${{ steps.deploy.outputs.hostname }}"

          echo "📢 Sending Teams notification for version $VERSION..."

          # Create Teams message payload
          TEAMS_MESSAGE=$(cat <<EOF
          {
            "@type": "MessageCard",
            "@context": "https://schema.org/extensions",
            "summary": "Heblo ${VERSION} úspěšně nasazeno",
            "themeColor": "00AA00",
            "title": "🚀 Heblo ${VERSION} úspěšně nasazeno",
            "sections": [
              {
                "activityTitle": "Produkční nasazení dokončeno",
                "activitySubtitle": "$(date '+%Y-%m-%d %H:%M:%S UTC')",
                "facts": [
                  {
                    "name": "Verze:",
                    "value": "${VERSION}"
                  },
                  {
                    "name": "Prostředí:",
                    "value": "Production"
                  },
                  {
                    "name": "URL:",
                    "value": "https://${HOSTNAME}"
                  },
                  {
                    "name": "Workflow:",
                    "value": "${GITHUB_WORKFLOW}"
                  }
                ],
                "markdown": true
              }
            ],
            "potentialAction": [
              {
                "@type": "OpenUri",
                "name": "Zobrazit aplikaci",
                "targets": [
                  {
                    "os": "default",
                    "uri": "https://${HOSTNAME}"
                  }
                ]
              },
              {
                "@type": "OpenUri",
                "name": "Zobrazit workflow run",
                "targets": [
                  {
                    "os": "default",
                    "uri": "${GITHUB_SERVER_URL}/${GITHUB_REPOSITORY}/actions/runs/${GITHUB_RUN_ID}"
                  }
                ]
              }
            ]
          }
          EOF
          )

          # Send to Teams webhook
          HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" \
            -H "Content-Type: application/json" \
            -d "$TEAMS_MESSAGE" \
            "${{ secrets.TEAMS_WEBHOOK_URL }}")

          if [ "$HTTP_CODE" == "200" ]; then
            echo "✅ Teams notification sent successfully!"
          else
            echo "⚠️ Failed to send Teams notification (HTTP $HTTP_CODE)"
            echo "Note: This is not a critical failure, continuing workflow..."
          fi
        env:
          GITHUB_WORKFLOW: ${{ github.workflow }}
          GITHUB_SERVER_URL: ${{ github.server_url }}
          GITHUB_REPOSITORY: ${{ github.repository }}
          GITHUB_RUN_ID: ${{ github.run_id }}
```

### Step 2: Verify the workflow syntax

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
# Validate YAML syntax
gh workflow view ci-main-branch.yml
```

Expected: Workflow should be valid YAML with Teams notification step

### Step 3: Test Teams webhook payload format

Create a test script to validate the JSON payload format:

```bash
cat > /tmp/test-teams-payload.sh <<'EOF'
#!/bin/bash

VERSION="v1.2.3"
HOSTNAME="heblo.azurewebsites.net"

TEAMS_MESSAGE=$(cat <<PAYLOAD
{
  "@type": "MessageCard",
  "@context": "https://schema.org/extensions",
  "summary": "Heblo ${VERSION} úspěšně nasazeno",
  "themeColor": "00AA00",
  "title": "🚀 Heblo ${VERSION} úspěšně nasazeno",
  "sections": [
    {
      "activityTitle": "Produkční nasazení dokončeno",
      "activitySubtitle": "$(date '+%Y-%m-%d %H:%M:%S UTC')",
      "facts": [
        {
          "name": "Verze:",
          "value": "${VERSION}"
        },
        {
          "name": "Prostředí:",
          "value": "Production"
        },
        {
          "name": "URL:",
          "value": "https://${HOSTNAME}"
        }
      ],
      "markdown": true
    }
  ]
}
PAYLOAD
)

echo "$TEAMS_MESSAGE" | jq .
EOF

chmod +x /tmp/test-teams-payload.sh
/tmp/test-teams-payload.sh
```

Expected: Valid JSON output with properly escaped Czech characters

### Step 4: Commit the changes

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "feat(ci): add Teams notification for production deployment

- Send webhook notification to Teams after successful production deployment
- Include version, environment, URL, and workflow details
- Use Czech language for notification message: 'Heblo v{verze} úspěšně nasazeno'
- Add action buttons to open app and view workflow run
- Non-blocking notification (failure won't stop deployment)"
```

---

## Task 4: Update Workflow Documentation

**Files:**
- Create: `docs/ci-cd-enhancements.md` (optional documentation)

### Step 1: Add inline documentation to workflow

Update the workflow file header comment to document new features:

**Location:** After line 6 in `.github/workflows/ci-main-branch.yml`

```yaml
# Features:
# - Parallel frontend and backend tests
# - GitVersion-based semantic versioning
# - Changelog generation (EN + CS translation via OpenAI)
# - Docker image build and push to Docker Hub
# - Git tag creation
# - GitHub release creation with formatted changelog
# - Manual approval gates for staging and production deployments
# - Azure Web App deployment with health checks
# - Comprehensive smoke tests with retries
# - Teams webhook notification on successful production deployment
```

### Step 2: Verify final workflow

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
# Check workflow structure
gh workflow view ci-main-branch.yml --yaml | head -50
```

Expected: Complete workflow with all enhancements visible

### Step 3: Final commit

```bash
git add .github/workflows/ci-main-branch.yml
git commit -m "docs(ci): update workflow documentation with new features

- Document GitHub release creation
- Document improved health check timeouts
- Document Teams notification integration"
```

---

## Task 5: Verification and Testing

### Step 1: Push changes to a test branch

```bash
# Create a test branch
git checkout -b ci-enhancements-test

# Push all commits
git push origin ci-enhancements-test
```

Expected: Branch pushed successfully

### Step 2: Verify workflow file in GitHub UI

Visit: `https://github.com/onpaj/Anela.Heblo/blob/ci-enhancements-test/.github/workflows/ci-main-branch.yml`

Expected:
- Green checkmark (valid workflow syntax)
- All new steps visible in diff

### Step 3: Create a pull request

```bash
gh pr create \
  --title "feat(ci): enhance CI/CD workflow with releases, better timeouts, and Teams notifications" \
  --body "## Changes

- **GitHub Releases**: Automatically create GitHub releases with formatted changelog after successful build
- **Improved Health Checks**: Increase timeout from 100s to 300s (5 minutes) with better retry logic
- **Teams Notifications**: Send webhook notification to Teams after successful production deployment

## Testing

- Validated YAML syntax
- Tested JSON payload formats
- Verified workflow structure

## Breaking Changes

None - all changes are additive

## Related Issues

None" \
  --base main
```

Expected: Pull request created successfully

### Step 4: Merge to main and monitor first run

After PR approval and merge:

```bash
# Switch to main
git checkout main
git pull origin main

# Monitor the workflow run
gh run watch
```

Expected:
- Tests pass
- Docker build succeeds
- Git tag created
- **NEW**: GitHub release created with changelog
- Staging deployment with improved health checks
- Production deployment with improved health checks
- **NEW**: Teams notification sent with "Heblo v{version} úspěšně nasazeno"

### Step 5: Verify GitHub Release

Visit: `https://github.com/onpaj/Anela.Heblo/releases/latest`

Expected:
- Release created with version tag
- Changelog formatted by type (features, fixes, improvements)
- Release notes readable and properly formatted

### Step 6: Verify Teams Notification

Check Teams channel configured with `TEAMS_WEBHOOK_URL` secret.

Expected:
- Message received with Czech text: "🚀 Heblo v{version} úspěšně nasazeno"
- Version, environment, and URL displayed
- Action buttons working (open app, view workflow)

---

## Summary

This plan implements three key enhancements to the CI/CD workflow:

1. **GitHub Releases** - Automatically creates releases with formatted changelogs extracted from changelog.json
2. **Robust Health Checks** - Increases timeout from ~100s to 300s (5 minutes) with 60s initial wait + 20 retries × 15s intervals
3. **Teams Notifications** - Sends Czech-language webhook notifications ("Heblo v{verze} úspěšně nasazeno") after successful production deployments

All changes are non-breaking and enhance the existing workflow without modifying core functionality.
