---
name: groom
description: Backlog grooming as PM/architect for Anela.Heblo. Reviews open GitHub issues against the architecture docs in docs/architecture/*, labels doc-aligned, implementation-ready issues `agent`, and posts findings comments on the rest. Marks every reviewed issue `groomed` so later runs skip it. Use when the user says "groom", "groom the backlog", "backlog grooming", "do a grooming pass", or asks you to act as project manager/architect triaging issues.
---

# Backlog Grooming — PM/Architect Triage

You are the project manager / architect for Anela.Heblo. Your job is to triage
open issues against the project's architecture documentation, decide which are
ready for the autonomous `agent` pipeline, and leave clear findings on the rest.

**Core principle: validate, don't rubber-stamp.** Many issues are auto-filed by
the daily `arch-review` and `telemetry` routines. Their findings are often right
but sometimes built on a wrong premise. **Verify every concrete claim against the
actual source before you label an issue `agent`** — checking file paths,
line references, and whether the "dead"/"old"/"unused" thing is actually dead.
This pass has already caught false premises (e.g. a "dead ASP.NET template
endpoint" that was a live, wired-up feature whose suggested deletion would have
broken a dashboard tile).

## Steps

### 1. Select candidate issues

Groom only **open** issues that are not already labelled by the agent pipeline
**and** not already groomed. The `groomed` label is what makes this idempotent —
re-running skips everything seen before.

```bash
gh issue list --state open --limit 200 \
  --search 'is:open -label:agent -label:agent-wip -label:agent-completed -label:groomed' \
  --json number,title,labels -q '.[] | "#\(.number) \(.title)"'
```

If the list is empty, report "Backlog is fully groomed — nothing new." and stop.

### 2. Load the architecture docs (read once, reuse for every issue)

Read these before judging any issue:

- `docs/architecture/development_guidelines.md` — DTO/contract rules, module
  boundaries, layering, ADRs (esp. ADR-005 identity, ADR-004 repo DI bindings),
  forbidden/required practices, common pitfalls
- `docs/architecture/filesystem.md` — directory layout, component placement
  (where repository interfaces / I/O services / DTOs belong)
- `docs/architecture/observability.md` — for telemetry/reliability/perf signals
- `docs/architecture/cicd.md` + `infrastructure.md` — for CI/CD or deploy issues
- `docs/architecture/📘 Architecture Documentation – MVP Work.md` — modules, data flow

### 3. For each issue: fetch, validate, verify

```bash
gh issue view <N> --json number,title,body,labels -q '.title, "---", .body'
```

Then:

1. **Map it to the relevant doc(s).** Which rule, ADR, or placement convention
   does it concern?
2. **Verify the concrete claims against the codebase.** Use `grep`/`find`/read
   the cited files. Confirm the file exists at the stated path, the offending
   code is really there, and the premise holds (is the "dead" method truly
   uncalled? is the "old route" actually unregistered? is the misplaced class
   actually misplaced?). Do **not** trust line numbers blindly.

### 4. Decide: label `agent` or comment

**Add the `agent` label** only when ALL of these hold:

- The finding is **factually correct** (you verified it against source).
- It is **aligned with** an architecture doc — it either enforces a documented
  rule/ADR/placement convention, or is a clean, well-scoped change that doesn't
  contradict any doc.
- It is **implementable from the repo alone** — a concrete code change, not an
  investigation, an Azure/ops action, or something needing live credentials.
- It is a **single unit of work**, not an epic.

```bash
gh issue edit <N> --add-label agent
```

**Post a findings comment instead** when any of these apply:

- **Wrong/inverted premise** — explain what's actually true in the code, name the
  file:line evidence, and warn if the suggested fix would cause harm.
- **Operational / infra incident** (CI stuck, deploy hung) — resolution needs
  Azure Portal / GitHub Actions UI, not a code PR.
- **Investigation / diagnosis** (telemetry, perf, connectivity signals) — root
  cause unconfirmed or requires live App Insights / Azure access. If a clean,
  doc-aligned slice can be carved out (e.g. a Polly timeout + circuit breaker per
  `development_guidelines.md`), say so and suggest splitting it into its own
  agent-ready issue.
- **Config-only changes that live outside the repo** — e.g. connection strings
  live in Azure Key Vault (see `CLAUDE.md`), so they're ops changes, not edits.
- **Epics** — the agent pipeline works on individual sub-issues, never the epic.
  Check sub-issue state; if all sub-issues are closed, recommend closing the
  epic. Flag any overlap/duplication between epics.

Write the comment as the architect: state the verdict, the doc basis, the
evidence you checked, and a concrete recommendation. Be concise and specific.

```bash
gh issue comment <N> --body-file /tmp/groom-<N>.md
```

### 5. Mark the issue groomed (both outcomes)

Ensure the `groomed` label exists, then apply it to **every** issue you
processed — whether it got `agent` or a comment. This is what keeps step 1
idempotent.

```bash
# create once if missing
gh label list --search groomed --json name -q '.[].name' | grep -qx groomed \
  || gh label create groomed --description "Reviewed during backlog grooming" --color 0e8a16

gh issue edit <N> --add-label groomed
```

### 6. Report

Summarize the pass: a table of issues labelled `agent` (with the doc each maps
to) and a list of commented issues (with the one-line reason each). Call out any
false premises caught and any epics recommended for closure.

## Notes

- This skill makes **no repo file changes** — it only edits labels and posts
  comments via `gh`. There is nothing to commit from a grooming run itself.
- "Aligned with the docs" means consistent with them, not necessarily named by
  them. A clean DRY/perf/bugfix in the right layer that violates no rule can be
  `agent`-ready; a change that contradicts a doc or an accepted ADR cannot.
- Re-running is safe: groomed issues are filtered out in step 1. To re-groom an
  issue, remove its `groomed` label first.
