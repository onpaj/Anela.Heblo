---
name: submit
description: Use when the user says "submit", "submit to pipeline", or wants to send the last brainstorming spec to the AgentHarness pipeline. Finds the spec from context or disk and calls agentharness submit.
---

# Submit — Send Spec to AgentHarness Pipeline

Takes the last spec written by `superpowers:brainstorming` and submits it to the AgentHarness autonomous development pipeline.

## Steps

### 1. Locate the spec

Check in this order:

1. **Current context** — was a spec file path mentioned or just written this session?
2. **Project spec directories** — find the most recently modified spec on disk:

```bash
ls -t docs/features/*.md 2>/dev/null | head -1
ls -t docs/superpowers/specs/*.md 2>/dev/null | head -1
```

Use whichever returns a result. If both are empty, tell the user no spec was found and stop.

### 2. Confirm with user

Show the spec path and a one-line summary (first H1 heading from the file). Ask:

> "Ready to submit `<path>` to the pipeline. Shall I go ahead?"

Wait for confirmation before proceeding.

### 3. Submit

```bash
agentharness submit <spec-path>
```

This uploads the spec and returns a **feature ID** (e.g. `feat-20260425-abc123`).

### 4. Tell the user

- The feature ID
- They can review or edit the spec at `artifacts/<feature-id>/brief.md` before starting
- When ready: `/implement <feature-id>` or `agentharness implement <feature-id>`
