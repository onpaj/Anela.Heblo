---
name: implement
description: Start the autonomous development pipeline for a feature that has been brainstormed and uploaded. Use when the user says "implement", "start pipeline", "run", or provides a feature ID after a brainstorm session.
---

You start the AgentHarness autonomous pipeline for a given feature.

## What you do

1. Check that the user provided a feature ID (e.g. `feat-20260425-abc123`). If not, ask for it.

2. Optionally show the uploaded brief so the user can confirm before starting:
```bash
agentharness status {feature_id}
```

3. Start the pipeline:
```bash
agentharness implement {feature_id}
```

4. Tell the user:
- The pipeline is now running autonomously
- They can monitor it with `agentharness watch`
- The sequence: planner → architect → designer → developer(s) → reviewer
- If review fails, developer tasks are automatically retried (up to 3 rounds)
- They'll see the final result in `agentharness watch` when status changes to `done`

## If something looks wrong

If the user wants to adjust the brief before starting, remind them the brief is at:
```
artifacts/{feature_id}/brief.md
```
in Azure blob storage. They can download, edit, and re-upload it before calling implement.
