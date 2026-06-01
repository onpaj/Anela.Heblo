---
name: convertforagent
description: Convert an existing GitHub issue into an AgentHarness feature. Patches the issue in-place — adds agentharness-feature + feat:brainstormed labels and appends state JSON to the body. Detects GitHub sub-issue relationships and sets up epic branches automatically. Does NOT create a new issue. Usage: /convertforagent <issue-number>
---

# Convert GitHub Issue to AgentHarness Feature

Patches an existing GitHub issue so it becomes a harness-tracked feature in `brainstormed` state — identical to what `agentharness submit` produces, but the original issue is updated in-place instead of a new one being created. If the issue is a GitHub sub-issue of an epic, the epic branch is created automatically and this child branches off it.

## Steps

### 1. Load environment

```bash
set -a && source .env && set +a
```

### 2. Run the conversion

Replace `<issue-number>` with the actual issue number:

```bash
agentharness convert <issue-number>
```

### 3. Verify discoverability

```bash
agentharness list 2>/dev/null | grep feat-
```

### 4. Report to user

Tell the user:
- The **feature ID** printed by the command
- That the feature is now visible in `agentharness watch`
- Next command: `/implement <feature-id>` or `agentharness implement <feature-id>`

## What the command does

| Action | Details |
|--------|---------|
| Fetches issue title | Used to derive `feat-<slug>` feature ID (40 char max) |
| Detects epic parent | Calls GitHub sub-issues API + body-marker fallback (`Epic: #N`) |
| Creates branches | `epic-<slug>` off main (once, idempotent), then `feat-<slug>` off epic branch |
| Opens umbrella PR | Draft `epic-<slug> → main` PR with sub-issue checklist (idempotent) |
| Uploads brief | `artifacts/<feature-id>/brief.md` — original issue body |
| Patches issue body | Appends/replaces `\`\`\`agentharness-state` block |
| Adds labels | `agentharness-feature` + `feat:brainstormed` |

## Notes

- If the issue body already contains an `agentharness-state` block it is replaced, not duplicated.
- For epic children, the `feat-<slug>` branch is created off the epic branch, not main.
- Requires `GITHUB_TOKEN` in `.env` and a working `gh` CLI session.
