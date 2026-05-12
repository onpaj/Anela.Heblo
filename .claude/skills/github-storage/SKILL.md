---
name: github-storage
description: Manage GitHub Issues, branches, and artifacts for AgentHarness GitHub backend. Use for inspecting pipeline queues (issues), listing features, checking dead-letter issues, uploading briefs, and inspecting feature state. Trigger on: "setup github", "list issues", "check queue", "dead letter", "inspect artifact", "github storage", "github backend".
---

You manage the AgentHarness GitHub backend using the `gh` CLI.

The GitHub backend uses:
- **GitHub Issues** (with labels) as the work queue
- **Git branches** (`feat/{feature_id}`) as the artifact store
- **Issue labels + JSON body** as the feature state manager

Load the `.env` file before running any commands that need `GITHUB_TOKEN`:

```bash
set -a && source .env && set +a
```

The repository is auto-detected from the git remote, or set explicitly via `GITHUB_OWNER` and `GITHUB_RUNS_REPO` in `.env`.

---

## Pipeline config

`.pipeline/config.json` controls the backend and pipeline behaviour. Edit it directly or use the snippets below.

### Show current config
```bash
cat .pipeline/config.json
```

### Switch to GitHub backend
```bash
python3 -c "
import json, pathlib
p = pathlib.Path('.pipeline/config.json')
cfg = json.loads(p.read_text())
cfg['storage_backend'] = 'github'
p.write_text(json.dumps(cfg, indent=2))
"
```

### Adjust pipeline timeouts / limits
```bash
python3 -c "
import json, pathlib
p = pathlib.Path('.pipeline/config.json')
cfg = json.loads(p.read_text())
cfg['defaults']['dead_letter_threshold'] = 3   # retries before dead-letter
cfg['defaults']['max_revisions'] = 3           # review→dev revision rounds
cfg['defaults']['poll_interval_seconds'] = 5.0 # observer poll cadence
p.write_text(json.dumps(cfg, indent=2))
"
```

---

## Initial setup

The GitHub backend requires no queue creation — issues are created dynamically. You only need a valid `GITHUB_TOKEN` and the correct repo set in `.env`.

### Verify credentials and repo access
```bash
gh auth status
gh repo view
```

### Create required issue labels (first-time setup)
```bash
# Feature status labels
for label in feat:brainstorming feat:analyzing feat:architecting feat:designing feat:planning feat:developing feat:reviewing feat:dev_revision feat:done feat:failed; do
  gh label create "$label" --color "#0075ca" --force
done

# Task state labels
for label in state:queued state:in-progress state:completed state:failed state:dead-letter state:blocked; do
  gh label create "$label" --color "#e4e669" --force
done

# Queue routing labels
for label in queue:analyst queue:architect queue:designer queue:planner queue:developer queue:reviewer; do
  gh label create "$label" --color "#d93f0b" --force
done

# Marker labels
gh label create "agentharness-feature" --color "#bfd4f2" --force
gh label create "implement" --color "#c2e0c6" --force
```

---

## Queue inspection

Queues are GitHub Issues with a `queue:{role}` label and `state:queued` or `state:in-progress`.

### List all open issues per queue (peek)
```bash
for queue in analyst architect designer planner developer reviewer; do
  count=$(gh issue list --label "queue:$queue" --state open --json number --jq 'length')
  echo "queue:$queue — $count issue(s)"
done
```

### List queued issues for a specific queue
```bash
gh issue list --label "queue:analyst" --label "state:queued" --state open \
  --json number,title,labels,createdAt \
  --template '{{range .}}#{{.number}} {{.title}} ({{.createdAt}})\n{{end}}'
```

### Inspect a specific queue message (issue body contains JSON)
```bash
# Replace 42 with the issue number
gh issue view 42 --json number,title,body,labels | python3 -c "
import sys, json
issue = json.load(sys.stdin)
print('Labels:', [l['name'] for l in issue['labels']])
# Body contains the TaskMessage JSON between markers
body = issue['body']
start = body.find('\`\`\`json')
end = body.find('\`\`\`', start + 6)
if start != -1:
    print(json.dumps(json.loads(body[start+7:end]), indent=2))
else:
    print(body)
"
```

### List in-progress issues (claimed by a worker)
```bash
gh issue list --label "state:in-progress" --state open \
  --json number,title,labels,updatedAt \
  --template '{{range .}}#{{.number}} {{.title}} (updated: {{.updatedAt}})\n{{end}}'
```

### Clear a stuck in-progress issue (release claim)
```bash
# Replace 42 with the issue number
ISSUE=42
gh issue edit $ISSUE --remove-label "state:in-progress"
gh issue edit $ISSUE --add-label "state:queued"
# Also remove any claimed-by label
gh issue view $ISSUE --json labels --jq '.labels[].name' | grep "^claimed-by:" | while read label; do
  gh issue edit $ISSUE --remove-label "$label"
done
```

---

## Feature listing

Features live on branches named `feat/{feature_id}` and are tracked by issues with the `agentharness-feature` label.

### List all features (via issues)
```bash
gh issue list --label "agentharness-feature" --state all \
  --json number,title,labels,createdAt \
  --template '{{range .}}#{{.number}} {{.title}}\n  Labels: {{range .labels}}{{.name}} {{end}}\n  Created: {{.createdAt}}\n\n{{end}}'
```

### List active features (not done/failed)
```bash
gh issue list --label "agentharness-feature" --state open \
  --json number,title,labels,createdAt \
  --template '{{range .}}#{{.number}} {{.title}}\n  Status: {{range .labels}}{{.name}} {{end}}\n\n{{end}}'
```

### List feature branches
```bash
git branch -r | grep "feat/" | sed 's|origin/||'
```

### List artifacts for a feature (files on its branch)
```bash
FEATURE_ID=feat-20260425-abc123
git ls-tree -r --name-only "origin/$FEATURE_ID" -- artifacts/ 2>/dev/null || \
  gh api "repos/{owner}/{repo}/git/trees/refs/heads/$FEATURE_ID?recursive=1" \
    --jq '.tree[] | select(.type == "blob") | .path' | grep "^artifacts/"
```

---

## Artifact inspection

Artifacts are files committed to the feature branch under `artifacts/{feature_id}/`.

### Read an artifact from a feature branch
```bash
FEATURE_ID=feat-20260425-abc123
ARTIFACT=brief.md

git show "origin/$FEATURE_ID:artifacts/$FEATURE_ID/$ARTIFACT" 2>/dev/null || \
  gh api "repos/{owner}/{repo}/contents/artifacts/$FEATURE_ID/$ARTIFACT?ref=$FEATURE_ID" \
    --jq '.content' | base64 -d
```

### Read state from the feature issue body
```bash
# Find the feature issue by title (feature ID is in the title)
FEATURE_ID=feat-20260425-abc123
gh issue list --label "agentharness-feature" --state all \
  --json number,title,body \
  --jq ".[] | select(.title | contains(\"$FEATURE_ID\")) | .body" | \
  python3 -c "
import sys, json
body = sys.stdin.read()
start = body.find('\`\`\`json')
end = body.find('\`\`\`', start + 6)
if start != -1:
    print(json.dumps(json.loads(body[start+7:end].strip()), indent=2))
else:
    print(body)
"
```

### List all artifacts on a feature branch
```bash
FEATURE_ID=feat-20260425-abc123
git fetch origin "$FEATURE_ID" 2>/dev/null
git ls-tree -r --name-only "FETCH_HEAD" -- "artifacts/$FEATURE_ID/"
```

---

## Brief upload

Upload a brief to the feature branch to seed the pipeline.

### Upload brief.md for a new feature
```bash
FEATURE_ID=feat-$(date +%Y%m%d)-$(openssl rand -hex 3)
BRANCH="$FEATURE_ID"

# Create the branch from main
git fetch origin main
git push origin "origin/main:refs/heads/$BRANCH"

# Encode the brief content and upload via API
CONTENT=$(base64 -i brief.md)
OWNER=$(gh repo view --json owner --jq '.owner.login')
REPO=$(gh repo view --json name --jq '.name')

gh api "repos/$OWNER/$REPO/contents/artifacts/$FEATURE_ID/brief.md" \
  --method PUT \
  --field message="feat: upload brief for $FEATURE_ID" \
  --field content="$CONTENT" \
  --field branch="$BRANCH"

echo "Feature branch created: $FEATURE_ID"
```

### Overwrite an existing artifact
```bash
FEATURE_ID=feat-20260425-abc123
ARTIFACT_PATH="artifacts/$FEATURE_ID/brief.md"
OWNER=$(gh repo view --json owner --jq '.owner.login')
REPO=$(gh repo view --json name --jq '.name')

# Get current SHA (required for updates)
SHA=$(gh api "repos/$OWNER/$REPO/contents/$ARTIFACT_PATH?ref=$FEATURE_ID" --jq '.sha')
CONTENT=$(base64 -i brief.md)

gh api "repos/$OWNER/$REPO/contents/$ARTIFACT_PATH" \
  --method PUT \
  --field message="chore: update brief for $FEATURE_ID" \
  --field content="$CONTENT" \
  --field branch="$FEATURE_ID" \
  --field sha="$SHA"
```

---

## Dead-letter inspection

Failed tasks are GitHub Issues with the `state:dead-letter` label.

### List all dead-letter issues
```bash
gh issue list --label "state:dead-letter" --state open \
  --json number,title,labels,createdAt,updatedAt \
  --template '{{range .}}#{{.number}} {{.title}}\n  Labels: {{range .labels}}{{.name}} {{end}}\n  Updated: {{.updatedAt}}\n\n{{end}}'
```

### Inspect a dead-letter issue
```bash
# Replace 42 with the issue number
gh issue view 42
```

### Re-queue a dead-letter issue (after fixing the issue)
```bash
ISSUE=42

# Remove dead-letter label, add back to the appropriate queue
gh issue edit $ISSUE --remove-label "state:dead-letter" --remove-label "state:failed"
gh issue edit $ISSUE --add-label "state:queued"

echo "Issue #$ISSUE re-queued. The observer will pick it up on next poll."
```

### Close a dead-letter issue permanently
```bash
gh issue close 42 --comment "Closing dead-letter issue — feature abandoned."
```

---

## State inspection

### Check feature state via issue labels
```bash
FEATURE_ID=feat-20260425-abc123
gh issue list --label "agentharness-feature" --state all \
  --json number,title,labels \
  --jq ".[] | select(.title | contains(\"$FEATURE_ID\")) | {number: .number, labels: [.labels[].name]}"
```

### Check all features and their pipeline phase
```bash
gh issue list --label "agentharness-feature" --state all --limit 50 \
  --json number,title,labels \
  --jq '.[] | {
    id: (.title | capture("(feat-[0-9]+-[a-f0-9]+)").capture),
    issue: .number,
    status: ([.labels[].name | select(startswith("feat:"))] | first // "unknown")
  }'
```

### Watch pipeline progress (poll every 10s)
```bash
watch -n 10 'gh issue list --label "agentharness-feature" --state open \
  --json number,title,labels \
  --jq ".[] | \"\(.number) \(.title) — \([.labels[].name | select(startswith(\"feat:\"))] | first)\""'
```

---

## Common patterns

### Full pipeline health check
```bash
echo "=== Queue depths ===" && \
for queue in analyst architect designer planner developer reviewer; do
  queued=$(gh issue list --label "queue:$queue" --label "state:queued" --state open --json number --jq 'length' 2>/dev/null || echo "?")
  inprog=$(gh issue list --label "queue:$queue" --label "state:in-progress" --state open --json number --jq 'length' 2>/dev/null || echo "?")
  echo "  queue:$queue — queued: $queued, in-progress: $inprog"
done

echo ""
echo "=== Dead-letter ===" && \
count=$(gh issue list --label "state:dead-letter" --state open --json number --jq 'length')
[ "$count" -gt 0 ] && echo "  $count dead-letter issue(s) need attention" || echo "  None"

echo ""
echo "=== Active features ===" && \
gh issue list --label "agentharness-feature" --state open --limit 20 \
  --json number,title,labels \
  --jq '.[] | "  #\(.number) \(.title) [\([.labels[].name | select(startswith("feat:"))] | first)]"'
```

### Find a stuck feature
```bash
# Issues in in-progress for more than 30 minutes
gh issue list --label "state:in-progress" --state open \
  --json number,title,updatedAt,labels \
  --jq '.[] | select((.updatedAt | fromdateiso8601) < (now - 1800)) |
    "#\(.number) \(.title) — stalled since \(.updatedAt)"'
```
