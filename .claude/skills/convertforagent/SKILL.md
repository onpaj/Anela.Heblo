---
name: convertforagent
description: Convert an existing GitHub issue into an AgentHarness feature. Patches the issue in-place — adds agentharness-feature + feat:brainstormed labels and appends state JSON to the body. Does NOT create a new issue. Usage: /convertforagent <issue-number>
---

# Convert GitHub Issue to AgentHarness Feature

Patches an existing GitHub issue so it becomes a harness-tracked feature in `brainstormed` state — identical to what `agentharness submit` produces, but the original issue is updated in-place instead of a new one being created.

## Steps

### 1. Load environment

```bash
set -a && source .env && set +a
```

### 2. Run the conversion script

Replace `<issue-number>` with the actual issue number, then run this single Python script — it handles everything atomically:

```bash
python3 - <issue-number> <<'PYEOF'
import subprocess, sys, json, re, base64
from datetime import datetime, timezone

# ── helpers ──────────────────────────────────────────────────────────────────

def gh(*args):
    r = subprocess.run(["gh", *args], capture_output=True, text=True, check=True)
    return r.stdout.strip()

def gh_json(*args):
    return json.loads(gh(*args))

def slug(title: str) -> str:
    s = re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")
    return s[:40]

# ── fetch issue ──────────────────────────────────────────────────────────────

issue_number = sys.argv[1]
issue = gh_json("issue", "view", issue_number, "--json", "number,title,body,url")
title   = issue["title"]
body    = (issue["body"] or "").strip()
url     = issue["url"]
feature_id = f"feat-{slug(title)}"

print(f"Issue     : #{issue_number} — {title}")
print(f"Feature ID: {feature_id}")

# ── detect repo owner/name ───────────────────────────────────────────────────

owner = gh("repo", "view", "--json", "owner", "--jq", ".owner.login")
repo  = gh("repo", "view", "--json", "name",  "--jq", ".name")

# ── create feature branch from default branch ────────────────────────────────

default_branch = gh("repo", "view", "--json", "defaultBranchRef", "--jq", ".defaultBranchRef.name")
# Check if branch already exists
existing = subprocess.run(
    ["gh", "api", f"repos/{owner}/{repo}/git/ref/heads/{feature_id}"],
    capture_output=True
)
if existing.returncode != 0:
    # Get SHA of default branch tip
    sha = gh("api", f"repos/{owner}/{repo}/git/ref/heads/{default_branch}",
             "--jq", ".object.sha")
    gh("api", f"repos/{owner}/{repo}/git/refs",
       "--method", "POST",
       "--field", f"ref=refs/heads/{feature_id}",
       "--field", f"sha={sha}")
    print(f"Branch    : created {feature_id}")
else:
    print(f"Branch    : {feature_id} already exists — skipping creation")

# ── upload brief.md to feature branch ────────────────────────────────────────

brief_path = f"artifacts/{feature_id}/brief.md"
brief_content = body  # keep the original issue body unchanged as the brief
encoded = base64.b64encode(brief_content.encode()).decode()

# Check if file already exists on branch (need its SHA for update)
existing_file = subprocess.run(
    ["gh", "api", f"repos/{owner}/{repo}/contents/{brief_path}?ref={feature_id}"],
    capture_output=True, text=True
)
put_args = [
    "api", f"repos/{owner}/{repo}/contents/{brief_path}",
    "--method", "PUT",
    "--field", f"message=feat: upload brief for {feature_id}",
    "--field", f"content={encoded}",
    "--field", f"branch={feature_id}",
]
if existing_file.returncode == 0:
    file_sha = json.loads(existing_file.stdout)["sha"]
    put_args += ["--field", f"sha={file_sha}"]
gh(*put_args)
print(f"Artifact  : uploaded {brief_path}")

# ── build FeatureState JSON ───────────────────────────────────────────────────

now = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"
config_json = gh("api", f"repos/{owner}/{repo}/contents/.pipeline/config.json?ref={default_branch}",
                 "--jq", ".content")
raw_cfg = base64.b64decode(config_json.replace("\n","")).decode()
cfg = json.loads(raw_cfg)
max_revisions = cfg.get("defaults", {}).get("max_revisions", 3)
feature_marker = cfg.get("github", {}).get("feature_marker", "agent")

state = {
    "feature_id": feature_id,
    "status": "brainstormed",
    "created_at": now,
    "updated_at": now,
    "brief_submitted_by": None,
    "phases": {},
    "tasks": [],
    "history": [{"timestamp": now, "event": "brief_uploaded",
                 "phase": None, "task_id": None, "worker_id": None, "details": None}],
    "config": {"max_revisions": max_revisions, "current_revision_round": 0},
    "worktree_path": None,
    "branch_name": feature_id,
    "cleanup_warning": None,
    "state_issue_number": int(issue_number),
    "pr_number": None,
    "pr_url": None,
}

state_block = "```agentharness-state\n" + json.dumps(state) + "\n```"

# ── ensure required labels exist ─────────────────────────────────────────────

for label in (feature_marker, "feat:brainstormed"):
    subprocess.run(
        ["gh", "label", "create", label, "--color", "0075ca", "--force"],
        capture_output=True
    )

# ── patch issue: add labels ───────────────────────────────────────────────────

gh("issue", "edit", issue_number,
   "--add-label", feature_marker,
   "--add-label", "feat:brainstormed")
print(f"Labels    : {feature_marker}, feat:brainstormed added")

# ── patch issue: append state block to body ───────────────────────────────────

STATE_RE = re.compile(r"```agentharness-state\s*\n.*?\n```", re.DOTALL)
if STATE_RE.search(body):
    new_body = STATE_RE.sub(state_block, body)
else:
    new_body = body + "\n\n" + state_block + "\n"

gh("issue", "edit", issue_number, "--body", new_body)
print("Body      : state JSON block appended")

print()
print(f"Done. Feature '{feature_id}' is now discoverable in TUI.")
print(f"Next step : agentharness implement {feature_id}")
PYEOF
```

### 3. Verify discoverability

```bash
agentharness list 2>/dev/null | grep "feat-" || \
  gh issue list --label "agentharness-feature" --state open \
    --json number,title,labels \
    --jq '.[] | "#\(.number) \(.title) [\([.labels[].name | select(startswith("feat:"))] | first)]"'
```

### 4. Report to user

Tell the user:
- The **feature ID** (printed by the script)
- The issue URL (unchanged — same issue, now tracked)
- That the feature is now visible in `agentharness watch`
- Next command: `/implement <feature-id>` or `agentharness implement <feature-id>`

## What the script does

| Action | Details |
|--------|---------|
| Derives feature ID | `feat-<slug-of-issue-title>` (40 char max, same algorithm as harness) |
| Creates branch | `feat-<slug>` from default branch, skipped if already exists |
| Uploads brief | `artifacts/<feature-id>/brief.md` — the original issue body, unmodified |
| Builds FeatureState | `status: brainstormed`, `state_issue_number` = this issue, `branch_name` = feature ID |
| Adds labels | `agentharness-feature` + `feat:brainstormed` (creates labels if missing) |
| Patches issue body | Appends (or replaces) the `\`\`\`agentharness-state` block; description untouched |

## Notes

- The issue description is **never modified** — only the state JSON block at the end of the body is added.
- If the issue body already contains an `agentharness-state` block it is replaced, not duplicated.
- Feature ID is derived from the issue **title**. Duplicate titles → same feature ID; warn if this seems likely.
- Requires `GITHUB_TOKEN` in `.env` and a working `gh` CLI session.
