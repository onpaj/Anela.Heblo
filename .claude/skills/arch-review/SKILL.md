---
name: arch-review
description: Rolling architecture audit for any repository. Picks one part of a module map at random, runs a scoped read-only senior-architect review of just that part, deduplicates against existing issues, and files one GitHub issue per genuine finding. Run it inside the repo you want audited and it needs no arguments — the module map is auto-detected; pass a map path to audit a different repository, a part number to review that exact part, and --dry-run to review without filing anything. Filing nothing is a correct outcome. Use when the user says "arch review", "architecture review", "audit the architecture", "review a module", "arch-review", or asks for a rolling/scheduled architecture audit. Read-only against code — never modifies code, opens a PR, or commits.
---

You are the rolling architecture audit. Every run reviews **one randomly chosen part** of
a module map — not the whole codebase, and not a feature.

The picking and the review rigor are generic; the **module map is the input** that makes a
run specific to a repository. Pass a different map and the same skill audits a different
project.

**Zero findings is a correct, expected result.** A clean part costs one review and files
nothing. Nothing in this skill may pressure you toward inventing findings.

## Arguments

```
/arch-review [map-path] [part-number] [--dry-run]
```

- **`map-path`** — the module map to draw from. **Optional.** With no argument the picker
  discovers the map in the repository you are standing in: `docs/architecture/module-map.md`
  if it is there, otherwise the single `module-map.md` it can find near the repo root.
  Running the skill inside the repo you want audited therefore needs no arguments at all.
  Pass a path only to audit a **different** repository — findings are filed against the
  repo the **map** lives in, not the one you invoked from.

  If discovery finds several maps it stops and lists them rather than guessing; if it finds
  none it says so and points at `/arch-map`. Both are fail-closed on purpose.
- **`part-number`** — review this exact part instead of drawing one at random. This is a
  **normal, full run**: the named part is reviewed and its findings are filed, exactly as
  a random draw would be. Naming a part chooses *which* part, nothing else.

- **`--dry-run`** — review and write the artifact, but **file nothing**. Off by default;
  every run is a full run unless this flag is present. Combine it with a part number when
  you want to inspect what a specific part would produce before letting it file.

All are optional and may arrive in any order — a bare integer is a part number,
`--dry-run` is the flag, anything else is a path. If the user named no part, draw at
random.

If the map path does not exist, stop and say so. Do not fall back to a different map and
do not invent parts.

## 1. Pick the part

Inside the repository being audited, no arguments are needed — the map is discovered:

```bash
.claude/skills/arch-review/pick-module.sh
```

Pass the map only when auditing another repository, and add `ARCH_PART=<n>` when a part
was named:

```bash
ARCH_PART=7 .claude/skills/arch-review/pick-module.sh <map-path>
```

Expected: exactly one line on stdout, carrying the part **and the map it came from**:

```
Architecture review of module map part #7 — Monitoring TUI (map: /abs/path/module-map.md)
```

**Non-zero exit, or empty stdout → stop.** Report that no review ran and why (the script
prints the reason on stderr). Do not improvise a part; the picker fails closed on purpose,
and a review of a part that does not exist is worse than no review.

## 2. Load the reviewer persona

The persona is the single source of truth for how to review — do not restate or improvise
it here. Read whichever of these exists, in order, and follow its instructions in full:

1. `.agents/arch-review.md` (a consumer repo, installed by `agentharness init`)
2. `agentharness/data/agents/arch-review.md` (this repository)

If neither exists, stop and tell the user to run `agentharness init`.

Pass the picker's line to the persona verbatim as its request. It carries the map path, so
the persona can resolve the part's boundary and the repository's normative documents on its
own.

## 3. Run the review

Follow the persona. In short, and without replacing what it says:

- Resolve the part's `Owns:` paths from the map. **Every finding must live under those
  paths.**
- Read the normative documents the map declares — and verify their claims against the
  code, since documents go stale.
- Deduplicate against existing `arch-review` issues, **open and closed**, before drafting
  anything.

You are read-only for this entire step: no edits, no commits, no branches, no PR.

If the map is missing the structure the persona needs — no `Owns:` for the drawn part, no
per-part section at all — stop and report the map as unusable, pointing at
`.claude/skills/arch-map/map-contract.md`. A review bounded by a guess is worse than no
review.

## 4. Write the review artifact

Write your full review next to the map, so the record lives with the repo under review:

```
<map-dir>/../arch-reviews/{YYYY-MM-DD}-part-{N}.md
```

For the default map that is `docs/arch-reviews/`. Create the directory if it does not
exist. The artifact must end with the fenced `json` array of drafts the persona specifies
— **`[]` when the part is clean.**

**If your review has prose but no fenced json block, that is a failure, not a clean
result.** Fix the artifact before continuing; the empty array is what distinguishes
"nothing to file" from "the contract was dropped".

## 5. File one issue per draft

If the array is empty, skip to step 6.

**If `--dry-run` was passed, file nothing.** Instead print, for each draft, the exact title,
the exact label set that *would* be applied (required labels plus the filtered draft
labels), and the body's first few lines — then go to step 6 and say plainly that this was a
dry run and nothing was created. Do not create labels either; `gh label create` is a write.

Otherwise, for each draft:

**5a. Target the right repository.** Issues belong to the repo the **map** lives in, not
the one this skill was invoked from — they are different whenever a map is passed in from
outside. Resolve it once and pass it explicitly to every `gh` call:

```bash
REPO="$(cd "<map-dir>" && gh repo view --json nameWithOwner --jq .nameWithOwner 2>/dev/null \
        || git -C "<map-dir>" remote get-url origin)"
```

**If `USE_GH_API` is set in the environment**, every `gh` invocation shown below is
routed through `.claude/skills/_lib/gh_api.sh` instead -- a curl+REST equivalent for
environments where the `gh` CLI itself is not permitted.

**5b. Make sure the labels exist** (idempotent — safe to re-run):

```bash
for L in arch-review architecture tech-debt maintainability design-patterns \
         antipattern code-quality duplication documentation \
         critical major moderate minor; do
  if [ -n "${USE_GH_API:-}" ]; then
    GH_REPO="$REPO" .claude/skills/_lib/gh_api.sh label-create "$L" ededed "" >/dev/null 2>&1 || true
  else
    gh label create "$L" --color ededed --force --repo "$REPO" >/dev/null 2>&1 || true
  fi
done
```

**5c. Build the label set structurally.** This is the part that must not be left to
judgement:

- `REQUIRED_LABELS` = `arch-review` plus the repo's pipeline-routing label, if it has one
  (`agent` in AgentHarness-managed repos). **Always applied by you, on the command line,
  to every issue, whatever the draft says.** Never read them from the draft.
- The draft's own labels are filtered against this allowlist, and anything else is
  dropped rather than passed through:
  `architecture`, `tech-debt`, `maintainability`, `design-patterns`, `antipattern`,
  `code-quality`, `duplication`, `documentation`, `critical`, `major`, `moderate`,
  `minor`.

  A hallucinated label would make `gh` fail and lose the whole issue; dropping it loses
  nothing.

> **Why this is structural and not an instruction.** In the process this skill is ported
> from, the house label was merely *instructed* — and the reviewer omitted it on two
> consecutive live runs, including once with a strengthened, example-backed prompt.
> Anything the model merely *should* do is not a guarantee, and restating it more
> forcefully does not make it one. So you weld the required labels on here, in the
> command, where the model cannot forget them.

**5d. Create the issue:**

```bash
if [ -n "${USE_GH_API:-}" ]; then
  GH_REPO="$REPO" .claude/skills/_lib/gh_api.sh issue-create \
    --title "[arch-review] <Area>: <headline>" \
    --label "arch-review,agent,<topical>,<severity>" \
    --body-file <path-to-body>
else
  gh issue create --repo "$REPO" \
    --title "[arch-review] <Area>: <headline>" \
    --label "arch-review,agent,<topical>,<severity>" \
    --body-file <path-to-body>
fi
```

Write the body to a temp file rather than passing it inline — bodies carry code fences,
backticks, and newlines that do not survive shell quoting reliably.

> **Blast radius.** Where the routing label is applied (`agent` in an AgentHarness repo),
> `/chopchop` will pick the issue up autonomously and drive it through `/oneshot` to a
> pull request. One agent's architectural opinion can therefore become a merged PR with no
> human gate. That is why the bar for a finding is set high. To file findings *without*
> auto-pickup, drop the routing label from `REQUIRED_LABELS` and keep only `arch-review`.

## 6. Report

Tell the user, briefly:

- which map and which part were reviewed (number and name);
- the verdict — `findings` or `clean`;
- for each issue filed: number, title, and severity — or, on a dry run, say **"dry run —
  nothing filed"** and list what would have been created;
- where the artifact was written.

For a clean part, say so plainly — *"part #N is sound, nothing filed"*. That is a
successful run, not an absence of one.

## Notes

- **The map is the substrate.** It partitions a repository into numbered parts and is what
  makes the audit iterable. Its required structure — summary-table rows, per-part `Owns:`,
  stable numbers, the `RETIRED` convention, and the `## Normative documents` section that
  grounds findings in what the project actually decided — is specified in
  `.claude/skills/arch-map/map-contract.md`. There is a worked example in this
  repository at `docs/architecture/module-map.md`.
- **No map yet?** Write one first, following the contract. Do not audit without a map: the
  `Owns:` boundary is the only thing keeping each review scoped, and without it the
  reviewer wanders across the codebase and refiles the same findings every cycle.
- **Selection is uniform random with replacement.** Expect roughly two thirds of the parts
  to be touched over a full cycle of runs, with some drawn several times before others are
  drawn once. Uniform in the limit, uneven in any given week.
- **Cross-run dedup rests on an instruction, not a guarantee.** There is no stable
  per-finding marker across runs, so the persona's own search of open *and closed* issues
  is the only defence against a duplicate. Expect it to fail occasionally; if duplicates
  start appearing, that is the signal to add a stable marker to the issue body.
- Only **one** part is reviewed per invocation. Run the skill again to review another.
