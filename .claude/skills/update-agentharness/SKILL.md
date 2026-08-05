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

## 3. Refresh this project's scaffolding — ask before forcing

`agentharness init` copies the package's `.agents/`, `.pipeline/`,
`.claude/agents/`, and `.claude/skills/` files into this project, plus
`.env` GitHub keys. Run it once *without* `--force` first:

```bash
agentharness init
```

Without `--force` this only **adds missing files** — anything that
already exists here (including every skill this project has customized,
like the ones this very session edited) is left untouched and reported as
`skip`. This step alone is safe to always run.

Then **ask the user** (do not assume) whether they also want to re-run
with `--force`:

> `agentharness init --force` overwrites every scaffolding file with the
> newly-installed package's version — including any project-specific
> customizations already made to `.agents/`, `.pipeline/`,
> `.claude/agents/`, or `.claude/skills/` (this project has several: e.g.
> `hygiene-pr`, `automerge-pr`, `rework-pr` all carry local changes).
> Uncommitted local edits would be silently lost. Recommend committing or
> stashing first if there's anything uncommitted (`git status`).

If the user declines, stop here — step 2's upgrade and step 3's
non-forced `init` already happened and are the entire outcome. If they
agree, first check `git status` and flag any uncommitted changes under
the affected directories before proceeding (suggest a commit or stash,
per this project's usual git-safety practice); once clean, run:

```bash
agentharness init --force
```

## 4. Report

Show `git status`/`git diff --stat` for the affected directories
(`.agents/`, `.pipeline/`, `.claude/agents/`, `.claude/skills/`, `.env`)
so the user can review exactly what the new package version changed
before committing anything. Do not commit on their behalf — this skill
only updates and refreshes; committing is a separate, explicit step.
