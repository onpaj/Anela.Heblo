---
name: update-agentharness
description: Update the AgentHarness CLI tool to its latest version via `uv tool upgrade`, then refresh this project's installed agent/skill/pipeline scaffolding with `agentharness init`. Use when the user says "update agentharness", "upgrade agentharness", "update the harness", or wants to pull in the latest AgentHarness release.
---

You update the globally-installed `agentharness` CLI tool (installed via
`uv tool install`, per the README) and then refresh this project's copy of
its scaffolding — `.agents/`, `.pipeline/`, `.claude/agents/`,
`.claude/skills/`, `.env` — from the newly-installed package.

## 1. Confirm agentharness is uv-tool-managed

```bash
uv tool list | grep '^agentharness '
```

If nothing prints, `agentharness` isn't installed as a `uv` tool here —
stop and tell the user so, pointing at the README's install command
(`uv tool install git+https://github.com/pajgrtondrej/AgentHarness.git`)
rather than guessing at some other install method (editable pip install,
etc.) it might actually be using.

Otherwise, note the version string it prints (e.g. `agentharness v0.22.0`)
— this is "before".

## 2. Upgrade the tool

```bash
uv tool upgrade agentharness
```

Then re-run step 1's `uv tool list` command to get the "after" version.
Report both versions to the user. If they're identical, say so plainly —
that's not a failure, it just means this was already the latest.

## 3. Refresh this project's scaffolding

`agentharness init` copies the package's `.agents/`, `.pipeline/`,
`.claude/agents/`, and `.claude/skills/` files into this project, plus
`.env` GitHub keys, and records what it installed (by content hash) in
`.agentharness-manifest.json` at the project root — commit that file with
the scaffolding.

Overwrite rules: a file that is missing, or still identical to what the
last `init` installed, is replaced with the new package version. A file that
**was edited locally since the last `init`** is left alone and listed as
`kept` (so project-specific fixes in e.g. `hygiene-pr`, `automerge-pr` or
`rework-pr` survive, including when a cloud session re-runs `init` at
startup). On the **first** run in a repo with no manifest there is no way to
tell an edit from an older release, so everything is overwritten. To drop a
local customization and take upstream's version, delete the file or run
`agentharness init --overwrite-local`. The old `--force` flag is a no-op.

Before running it, check `git status` and flag any uncommitted changes
under the affected directories (suggest a commit or stash, per this
project's usual git-safety practice), and — if there is no
`.agentharness-manifest.json` yet — warn that every scaffolding file,
local customizations included, is about to be replaced. Once clean, run:

```bash
agentharness init
```

## 4. Report

Show `git status`/`git diff --stat` for the affected directories
(`.agents/`, `.pipeline/`, `.claude/agents/`, `.claude/skills/`, `.env`)
so the user can review exactly what the new package version changed
before committing anything. Mention any `kept` files from init's output:
they still differ from upstream and may be missing upstream fixes. Do not commit on their behalf — this skill
only updates and refreshes; committing is a separate, explicit step.
