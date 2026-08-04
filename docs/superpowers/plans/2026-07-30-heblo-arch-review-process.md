# `heblo-arch-review` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A harness_v2 Process that every two hours picks one random part of `docs/architecture/module-map.md`, runs a scoped senior-architect review of it, and files one `harness:todo`-labelled GitHub issue per architectural finding — filing nothing when the part is sound.

**Architecture:** A `command` check runs a picker script that prints one line naming a random part; that line becomes the task's request. The task runs a one-step `arch-review` workflow whose `open-issue` finisher parses the persona's artifact for a fenced JSON array of issue drafts and files 0..N issues. No change to harness_v2 core.

**Tech Stack:** bash (picker + tests), JSON (harness process/workflow/agent definitions), harness_v2 1.5.1, `gh` CLI, Python 3.11 (validation only, via the harness tool's own interpreter).

**Spec:** `docs/superpowers/specs/2026-07-30-heblo-arch-review-process-design.md`

## Global Constraints

- **Every filed issue must carry `harness:todo`.** Guaranteed structurally by making it the finisher's scope `label` (`GithubIssueTracker` welds the scope label onto every issue). Never demote it to `allowed_labels`.
- **Zero findings is a correct, expected outcome.** No step, prompt, or test may imply a target count.
- **The persona is read-only.** It never edits, commits, pushes, opens a PR, or creates a branch or worktree.
- **The agent must be named `arch-review`, never `architecture`.** `~/harness-root/agents/architecture.json` is the `development` workflow's design step; overwriting it breaks that pipeline.
- **The process must use `dedup: per-interval`.** `per-state` would permanently suppress any re-picked part and silently kill the process.
- **A non-empty artifact with no fenced JSON block fails the task** (`harness.issue_drafts.parse_drafts` raises `DraftError`). A clean review must still emit `[]`.
- **`~/harness-root` is not a git repository.** Files written there are unversioned machine config; their content is recorded in the spec.
- Repo paths are relative to `/Users/rem/Anela.Heblo`. Harness paths are absolute under `/Users/rem/harness-root`.
- The interpreter that matches the running service is `/Users/rem/.local/share/uv/tools/harness/bin/python`.

---

## File Structure

| File | Responsibility |
|---|---|
| `scripts/pick-module.sh` (create) | Print one random live part of the module map, or exit non-zero. The only piece with logic worth testing. |
| `scripts/test-pick-module.sh` (create) | Shell test suite for the picker. No new tooling — plain bash asserts. |
| `scripts/prune-harness-worktrees.sh` (create) | G1 mitigation: delete worktrees of terminal tasks older than N days. |
| `/Users/rem/harness-root/agents/arch-review.json` (create) | The reviewer persona. |
| `/Users/rem/harness-root/workflows/arch-review.json` (create) | One step, two terminal outcomes, the `open-issue` finisher binding. |
| `/Users/rem/harness-root/processes/heblo-arch-review.json` (create) | Cadence, action, target, repository. |
| `/Users/rem/harness-root/processes/worktree-janitor.json` (create) | Cadence for the pruner. |

Task 1 is the only one with real logic. Tasks 2–4 are configuration whose "test" is compile validation. Tasks 5–6 are the dry runs that must pass before anything reaches GitHub on a schedule. Task 7 goes live. Task 8 is the disk mitigation and is independent — it can be done before or after 7, but must not be skipped.

---

### Task 1: The picker script

**Files:**
- Create: `scripts/pick-module.sh`
- Test: `scripts/test-pick-module.sh`

**Interfaces:**
- Consumes: `docs/architecture/module-map.md` (summary tables, rows of the form `| <n> | <name> | …`).
- Produces: a single stdout line, exit 0:
  `Architecture review of module map part #<n> — <name>`
  Task 4's process invokes it as `/Users/rem/Anela.Heblo/scripts/pick-module.sh`.
- Env overrides for testability: `HEBLO_MAP` (path to the map, default `<repo>/docs/architecture/module-map.md`), `HEBLO_NO_PULL` (any non-empty value skips the `git pull`).

- [ ] **Step 1: Write the failing test**

Create `scripts/test-pick-module.sh`:

```bash
#!/usr/bin/env bash
# Tests for pick-module.sh. Run: ./scripts/test-pick-module.sh
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PICK="$HERE/pick-module.sh"
export HEBLO_NO_PULL=1

pass=0
fail=0
check() { # check <description> <condition-exit-code>
  if [ "$2" -eq 0 ]; then pass=$((pass + 1)); echo "  ok   $1"
  else fail=$((fail + 1)); echo "  FAIL $1"; fi
}

echo "== real map =="
out="$("$PICK")"; rc=$?
check "exits 0 on the real map" "$rc"
check "prints exactly one line" "$([ "$(printf '%s' "$out" | wc -l | tr -d ' ')" = "0" ] && echo 0 || echo 1)"
check "matches the expected format" \
  "$(printf '%s' "$out" | grep -qE '^Architecture review of module map part #[0-9]+ — .+$' && echo 0 || echo 1)"

echo "== 200 draws =="
tmp="$(mktemp)"
for _ in $(seq 200); do "$PICK" >> "$tmp" || echo "PICKER-FAILED" >> "$tmp"; done
check "no draw failed" "$(grep -q 'PICKER-FAILED' "$tmp" && echo 1 || echo 0)"
check "every draw is well-formed" \
  "$(grep -cvE '^Architecture review of module map part #[0-9]+ — .+$' "$tmp" | grep -q '^0$' && echo 0 || echo 1)"
distinct="$(sed -E 's/^.*part #([0-9]+) .*$/\1/' "$tmp" | sort -un | wc -l | tr -d ' ')"
echo "  (distinct parts drawn in 200: $distinct)"
check "draws are spread over many parts (>30 distinct)" "$([ "$distinct" -gt 30 ] && echo 0 || echo 1)"
check "never draws a RETIRED part" "$(grep -qi 'RETIRED' "$tmp" && echo 1 || echo 0)"
rm -f "$tmp"

echo "== synthetic map: retired rows are skipped =="
syn="$(mktemp)"
cat > "$syn" <<'EOF'
# Application Module Map
| # | Part | Approx. size |
|---|------|--------------|
| 1 | Alpha | BE ~1k |
| 2 | RETIRED — merged into #1 | — |
| 3 | Gamma | BE ~2k |
EOF
res="$(HEBLO_MAP="$syn" bash -c 'for _ in $(seq 60); do "$0"; done' "$PICK" | sed -E 's/^.*part #([0-9]+) .*$/\1/' | sort -un | tr '\n' ' ')"
check "only live rows drawn (got: $res)" "$([ "$res" = "1 3 " ] && echo 0 || echo 1)"
rm -f "$syn"

echo "== failure modes =="
HEBLO_MAP=/nonexistent/map.md "$PICK" >/dev/null 2>&1
check "missing map exits non-zero" "$([ $? -ne 0 ] && echo 0 || echo 1)"
out="$(HEBLO_MAP=/nonexistent/map.md "$PICK" 2>/dev/null)"
check "missing map prints nothing on stdout" "$([ -z "$out" ] && echo 0 || echo 1)"

empty="$(mktemp)"; printf '# Nothing here\n' > "$empty"
HEBLO_MAP="$empty" "$PICK" >/dev/null 2>&1
check "map with no rows exits non-zero" "$([ $? -ne 0 ] && echo 0 || echo 1)"
rm -f "$empty"

echo
echo "passed: $pass  failed: $fail"
[ "$fail" -eq 0 ]
```

Make it executable:

```bash
chmod +x scripts/test-pick-module.sh
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./scripts/test-pick-module.sh`
Expected: FAIL — every check errors because `scripts/pick-module.sh` does not exist yet (`bash: .../pick-module.sh: No such file or directory`), and the final line reports a non-zero `failed:` count with exit status 1.

- [ ] **Step 3: Write the picker**

Create `scripts/pick-module.sh`:

```bash
#!/usr/bin/env bash
#
# Print one randomly chosen live part of the application module map, as a single
# line the harness `command` check turns into an architecture-review task:
#
#   Architecture review of module map part #17 — Manufacture Inventory, Lots & Settings
#
# Exits non-zero and prints nothing on stdout if anything is wrong: the harness
# treats a non-zero exit as "no observation", so a broken picker costs no agent
# call. Fail closed.
#
# Env:
#   HEBLO_MAP      override the map path (tests)
#   HEBLO_NO_PULL  any non-empty value skips the git pull (tests)
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MAP="${HEBLO_MAP:-$REPO/docs/architecture/module-map.md}"

# Prefer to review current main, but NEVER fail on the pull. The pull is an
# optimisation; the map on disk is the actual input. A fail-closed pull would
# mean one unpushed local commit — or any diverged clone — silently stops the
# whole process forever, with no task, no issue and no error anywhere. A review
# of a slightly stale tree is enormously better than that.
if [ -z "${HEBLO_NO_PULL:-}" ]; then
  git -C "$REPO" pull --ff-only -q >&2 \
    || echo "pick-module: git pull failed, reviewing the tree as it stands" >&2
fi

[ -f "$MAP" ] || { echo "pick-module: no module map at $MAP" >&2; exit 1; }

# Summary-table rows only: "| <n> | <name> | ...". The per-part "## N." headings
# are deliberately not parsed — a retired part keeps its heading.
# Retired rows are dropped; they exist only so old references stay resolvable.
rows="$(
  grep -E '^\|[[:space:]]*[0-9]+[[:space:]]*\|' "$MAP" \
    | grep -viE '\|[^|]*RETIRED' \
    | awk -F'|' '{
        num = $2; name = $3;
        gsub(/^[ \t]+|[ \t]+$/, "", num);
        gsub(/^[ \t]+|[ \t]+$/, "", name);
        if (num != "" && name != "") print num "\t" name;
      }'
)"

count="$(printf '%s\n' "$rows" | grep -c . || true)"
[ "$count" -gt 0 ] || { echo "pick-module: no parts parsed from $MAP" >&2; exit 1; }

index=$(( RANDOM % count + 1 ))
line="$(printf '%s\n' "$rows" | sed -n "${index}p")"
num="${line%%$'\t'*}"
name="${line#*$'\t'}"

printf 'Architecture review of module map part #%s — %s\n' "$num" "$name"
```

Make it executable:

```bash
chmod +x scripts/pick-module.sh
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `./scripts/test-pick-module.sh`
Expected: PASS — `failed: 0`, exit status 0. The informational line `(distinct parts drawn in 200: N)` should read somewhere around 40–48 of the 52 parts; that is the expected shape of random-with-replacement over 200 draws, not a defect.

- [ ] **Step 5: Sanity-check the output by eye**

Run: `for i in 1 2 3 4 5; do ./scripts/pick-module.sh; done`
Expected: five lines, each naming a real part. Cross-check two of the numbers against `docs/architecture/module-map.md` to confirm the number and name belong to the same row.

- [ ] **Step 6: Commit**

```bash
git add scripts/pick-module.sh scripts/test-pick-module.sh docs/superpowers/specs/2026-07-30-heblo-arch-review-process-design.md docs/superpowers/plans/2026-07-30-heblo-arch-review-process.md
git commit -m "feat(arch-review): module-map picker for the scheduled architecture review"
```

---

### Task 2: The reviewer persona

**Files:**
- Create: `/Users/rem/harness-root/agents/arch-review.json`

**Interfaces:**
- Consumes: the task's request line produced by Task 1 (`data.title`), which `behaviors/agent.py` renders into the prompt.
- Produces: a step artifact ending in a fenced ```json array of `{title, body, labels}` drafts, and an outcome of `findings` or `clean` — both consumed by Task 3's workflow and finisher.

- [ ] **Step 1: Verify the name is free**

Run: `ls /Users/rem/harness-root/agents/`
Expected: `architecture.json` exists (the `development` workflow's design step — **do not touch it**), and `arch-review.json` does **not**. If `arch-review.json` already exists, stop and inspect it before overwriting.

- [ ] **Step 2: Write the persona**

The prompt contains quotes, newlines and em-dashes, so write it through Python rather than hand-escaping JSON. Run:

```bash
/Users/rem/.local/share/uv/tools/harness/bin/python - <<'PYEOF'
import json, pathlib

prompt = """You are a very senior software architect reviewing EXISTING architecture in the Anela.Heblo codebase. You run non-interactively in an automated pipeline. You are READ-ONLY.

== ABSOLUTE RULES ==
- Never edit, create or delete any source file. Never commit, never push, never open a pull request.
- Do not create a git worktree and do not create or switch branches - the harness owns this working directory.
- The only file you write is your step artifact.
- Never wait for interactive input.

== 1. YOUR SCOPE IS ONE PART, AND ONLY ONE PART ==
Your request names one part of the application module map, e.g.

  Architecture review of module map part #17 - Manufacture Inventory, Lots & Settings

Open docs/architecture/module-map.md, find that part by its NUMBER, and read its entry in full: Purpose, Owns, Key entry points, Depends on, Consumed by, Analysis notes.

The `Owns:` paths are your scope boundary. EVERY finding you report must live in a file under one of those paths. This is the most important rule in this prompt:
- A defect in a part this one DEPENDS ON is OUT OF SCOPE. Report it only if THIS part misuses that dependency - and then the finding is about this part's misuse, located in this part's own file.
- Do not review the whole application. Do not follow an interesting thread out of your part.
- The `Analysis notes`, where present, usually point straight at a known soft spot. Start there.

== 2. READ THE NORMATIVE DOCUMENTS BEFORE YOU JUDGE ==
A finding must be grounded in what this codebase has decided, not in generic opinion. Read the ones relevant to what your part actually does, skim the rest:
- docs/architecture/development_guidelines.md - THIS IS WHERE THE ADRs LIVE (ADR-001 onwards). There is no docs/adr/ directory. Never report "no ADRs found".
- docs/(book emoji) Architecture Documentation - MVP Work.md (glob for `docs/*Architecture Documentation*.md`)
- docs/architecture/filesystem.md - where code is supposed to live
- docs/architecture/testing-strategy.md
- docs/architecture/DateTime_StandardizationGuide.md and docs/architecture/Dev_Guidelines_time.md
- docs/architecture/localization.md
- docs/architecture/observability.md
- CLAUDE.md at the repository root

== 3. WHAT COUNTS AS A FINDING ==
Report:
- a violation of a documented rule or an ADR;
- an outlier from the architecture the rest of the application follows - vertical slice organisation, MediatR use cases, the generated API client, the established persistence and layering patterns;
- a misuse of a technology or framework (EF Core, MediatR, Hangfire, React Query, the typed API client);
- an architectural best-practice error whose consequence you can state concretely.

Do NOT report:
- style, formatting or naming preference;
- missing tests, unless the gap is itself architectural;
- speculative refactors, or "this could be more generic";
- anything already covered by an existing issue (see section 4);
- anything whose consequence you cannot state concretely.

== FINDING NOTHING IS A CORRECT RESULT ==
There is no target number of findings. Parts of this codebase are well written, and reporting zero findings for those parts is exactly what you should do. A review that invents a marginal finding to look productive is a FAILED review: every issue you file is picked up automatically by the development pipeline, so a weak finding costs real work. When in doubt, leave it out - report `clean` and stop.

== 4. DO NOT REFILE WHAT IS ALREADY KNOWN ==
Before drafting anything, run:

  gh issue list --repo onpaj/Anela.Heblo --label arch-review --state all --limit 200

Read the titles. Drop any finding that matches one - whether that issue is OPEN or CLOSED.
- OPEN means it is already filed and being worked on.
- CLOSED means it was already fixed, or it was reviewed and REJECTED. Refiling a rejected finding is worse than missing a real one.
If a title looks close, read it with `gh issue view <number>` before you decide.

== 5. YOUR OUTPUT ==
Write your step artifact: which part you reviewed, which documents you read, what you checked, and the evidence for each finding.

Then END THE ARTIFACT with a fenced json block holding an ARRAY of issue drafts.

THE JSON BLOCK IS MANDATORY EVEN WHEN YOU FOUND NOTHING - in that case write an empty array. An artifact that contains prose but no fenced json block FAILS THE TASK.

Each draft is an object:
  {"title": "[arch-review] <Area>: <one-line headline>",
   "body": "<markdown>",
   "labels": ["<topical>", "<severity>"]}

- <Area> is a short name for the part, in the house style of the existing issues (e.g. Catalog, Manufacture, Photobank, ShoptetOrders, Configuration, Logistics).
- The body must carry: evidence with `file:line` references; the rule or ADR violated, quoted; why it matters, concretely; and a suggested direction. Do not write the fix yourself.
- labels: pick exactly ONE topical label from architecture, tech-debt, maintainability, design-patterns, antipattern, code-quality, duplication - and exactly ONE severity from critical, major, moderate, minor. Any other label is silently dropped.
- Do NOT add harness:todo. The harness attaches it to every issue automatically.

Typically 0-5 drafts.

== 6. YOUR VERDICT ==
- `findings` - you drafted one or more issues.
- `clean` - the array is empty. This is a good outcome, not a failure.
"""

spec = {
    "prompt": prompt,
    "model": "opus",
    "fallback_model": None,
    "allowed_tools": ["Read", "Grep", "Glob", "Bash"],
    # NOT ["findings","clean"] — fs_agents.py:53 validates this field against
    # {done, request_changes} only and would raise AgentNotFound at load. The
    # workflow's edges are the live vocabulary (invariant #42); this is just the
    # workflow-less fallback, exactly as agents/heal.json and dedup.json do it.
    "allowed_outcomes": ["done"],
    "timeout": None,
}

path = pathlib.Path("/Users/rem/harness-root/agents/arch-review.json")
path.write_text(json.dumps(spec, indent=2, ensure_ascii=False) + "\n")
print(f"wrote {path} ({len(prompt)} chars of prompt)")
PYEOF
```

Expected output: `wrote /Users/rem/harness-root/agents/arch-review.json (~4600 chars of prompt)`

- [ ] **Step 3: Verify the harness can load it**

```bash
/Users/rem/.local/share/uv/tools/harness/bin/python -c "
from harness.drivers.fs_agents import FilesystemAgentCatalog
from pathlib import Path
spec = FilesystemAgentCatalog(Path('/Users/rem/harness-root/agents')).get('arch-review')
print('outcomes (workflow-less fallback only):', spec.allowed_outcomes)
print('tools:', spec.allowed_tools)
print('model:', spec.model)
assert spec.allowed_outcomes == ('done',)  # see the correction note in Task 2
assert 'Write' not in spec.allowed_tools and 'Edit' not in spec.allowed_tools
print('OK')
"
```

Expected: prints the outcomes, tools and model, then `OK`. A `Write`/`Edit` tool sneaking into the list is a real defect — the persona is read-only.

- [ ] **Step 4: Verify the existing `architecture` agent is untouched**

```bash
git -C /Users/rem/Anela.Heblo diff --quiet 2>/dev/null; ls -la /Users/rem/harness-root/agents/architecture.json
/Users/rem/.local/share/uv/tools/harness/bin/python -c "
import json; p=json.load(open('/Users/rem/harness-root/agents/architecture.json'))
assert p['prompt'].startswith('You are a senior software architect. From the brief'), 'architecture.json was modified!'
print('architecture.json intact')
"
```

Expected: `architecture.json intact`.

---

### Task 3: The workflow

**Files:**
- Create: `/Users/rem/harness-root/workflows/arch-review.json`

**Interfaces:**
- Consumes: the `arch-review` agent from Task 2 (step name == agent name).
- Produces: a workflow named `arch-review`, which Task 4's process targets.

- [ ] **Step 1: Write the workflow**

```bash
cat > /Users/rem/harness-root/workflows/arch-review.json <<'EOF'
{
  "name": "arch-review",
  "start": "arch-review",
  "transitions": [
    {
      "from": "arch-review",
      "on": "findings",
      "to": "end",
      "hint": "one or more real violations found and drafted"
    },
    {
      "from": "arch-review",
      "on": "clean",
      "to": "end",
      "hint": "the part is sound - nothing worth filing"
    }
  ],
  "descriptions": {
    "arch-review": "review one module-map part against the documented architecture; draft an issue per finding"
  },
  "finishers": {
    "arch-review": {
      "kind": "open-issue",
      "label": "harness:todo",
      "allowed_labels": [
        "arch-review",
        "architecture",
        "tech-debt",
        "maintainability",
        "design-patterns",
        "antipattern",
        "code-quality",
        "duplication",
        "critical",
        "major",
        "moderate",
        "minor"
      ]
    }
  }
}
EOF
```

- [ ] **Step 2: Verify it parses and the outcome vocabulary is right**

```bash
/Users/rem/.local/share/uv/tools/harness/bin/python -c "
from harness.drivers.fs_workflows import FilesystemWorkflowRepository
from pathlib import Path
wf = FilesystemWorkflowRepository(Path('/Users/rem/harness-root/workflows')).get('arch-review')
print('start:', wf.start)
print('outcomes for arch-review:', wf.outcomes_for('arch-review'))
assert set(wf.outcomes_for('arch-review')) == {'findings', 'clean'}
print('OK')
"
```

Expected: `start: arch-review`, the two outcomes, then `OK`.

**Why this matters:** harness_v2 invariant #42 — the workflow's edges, not the agent's `allowed_outcomes`, are the authoritative vocabulary. If these two sets disagree, the persona is steered by the workflow and the agent file's list is only a fallback.

- [ ] **Step 3: Verify the finisher binding is accepted**

```bash
/Users/rem/.local/share/uv/tools/harness/bin/python -c "
from harness.drivers.fs_workflows import FilesystemWorkflowRepository
from pathlib import Path
wf = FilesystemWorkflowRepository(Path('/Users/rem/harness-root/workflows')).get('arch-review')
b = wf.finishers['arch-review']
print('kind:', b.kind)
print('config:', b.config)
assert b.kind == 'open-issue'
assert b.config['label'] == 'harness:todo', 'harness:todo MUST be the scope label'
assert 'from_step' not in b.config, 'from_step must be absent (wrapping shape)'
assert 'arch-review' in b.config['allowed_labels']
print('OK')
"
```

Expected: `kind: open-issue`, the config dict, then `OK`.

The two assertions are the plan's guard on the Global Constraints: `label` must be `harness:todo` (structural guarantee), and `from_step` must be absent (so the persona runs and *then* its artifact is filed, rather than the step being replaced).

---

### Task 4: The process

**Files:**
- Create: `/Users/rem/harness-root/processes/heblo-arch-review.json`

**Interfaces:**
- Consumes: Task 1's script path, Task 3's workflow name, and the `Anela.Heblo` entry in `/Users/rem/harness-root/repos.json`.
- Produces: a compiled `ScheduledTrigger` firing every 7200s.

- [ ] **Step 1: Write the process**

```bash
cat > /Users/rem/harness-root/processes/heblo-arch-review.json <<'EOF'
{
  "name": "heblo-arch-review",
  "trigger": {
    "interval": "7200s"
  },
  "action": {
    "check": "command",
    "params": {
      "command": "/Users/rem/Anela.Heblo/scripts/pick-module.sh",
      "timeout": 120
    }
  },
  "target": {
    "workflow": "arch-review"
  },
  "repository": "Anela.Heblo",
  "dedup": "per-interval",
  "sink": {
    "kind": "none"
  }
}
EOF
```

- [ ] **Step 2: Verify it compiles**

```bash
/Users/rem/.local/share/uv/tools/harness/bin/python -c "
import json
from harness.drivers.fs_processes import compile_process
from harness.drivers.system_clock import SystemClock
raw = json.load(open('/Users/rem/harness-root/processes/heblo-arch-review.json'))
t = compile_process(
    'heblo-arch-review', raw,
    clock=SystemClock(),
    known_workflows={'arch-review'},
    known_steps={'arch-review'},
    known_repositories={'Anela.Heblo', 'harness_v2', 'personal_assistant'},
)
print('compiled:', t.kind)
print('dedup:', t._dedup)
assert t._dedup == 'per-interval', 'per-state would permanently suppress a re-picked part'
print('OK')
"
```

Expected: `compiled: …`, `dedup: per-interval`, `OK`. A `ProcessValidationError` here names the offending field — fix that field and rerun rather than guessing.

- [ ] **Step 3: Verify the check actually runs the picker**

```bash
/Users/rem/.local/share/uv/tools/harness/bin/python -c "
from harness.drivers.checks import CommandCheck
c = CommandCheck(command='/Users/rem/Anela.Heblo/scripts/pick-module.sh', timeout=120)
obs = c.evaluate()
print('observations:', len(obs))
print('title:', obs[0].data['title'])
assert len(obs) == 1, 'the picker must print exactly one line'
assert obs[0].data['title'].startswith('Architecture review of module map part #')
print('OK')
"
```

Expected: `observations: 1`, the title line, then `OK`.

This is the seam where the picker meets the harness. If the picker prints a trailing blank line or a stray diagnostic on stdout, this is where it shows up as two observations — and two observations under `per-interval` dedup collide on the same key.

---

### Task 5: Dry run — the clean path

No new files. This is the run that must not be skipped: a clean part is the common case, and the `DraftError` trap makes it the easiest one to get wrong.

- [ ] **Step 1: Pick a small, likely-sound part to aim at**

Run: `grep -A 20 '^## 36\.' docs/architecture/module-map.md`
Expected: the entry for *Feature Flags, Configuration & Grid Layouts* (BE ~1.1k / FE ~0.7k) — one of the smallest parts, so the review is cheap and a clean verdict is plausible.

- [ ] **Step 2: Submit one task by hand**

```bash
/Users/rem/.local/bin/harness submit \
  --root /Users/rem/harness-root \
  --workflow arch-review \
  --repo Anela.Heblo \
  --data '{"title":"Architecture review of module map part #36 — Feature Flags, Configuration & Grid Layouts"}'
```

Expected: the command prints the new task id. Note it — call it `$TASK`.

- [ ] **Step 3: Watch it run**

```bash
tail -f /Users/rem/harness-root/harness.log
```

Expected: the task moves into the `arch-review` step, the agent runs (several minutes on `opus`), then the task settles. Stop tailing with Ctrl-C once it settles.

- [ ] **Step 4: Read the artifact and confirm the JSON block exists**

```bash
find /Users/rem/harness-root/worktrees -path '*/.artifacts/*arch-review*' -name '*.md' -newermt '-1 hour' | head
```

Then read the file that lists. Expected: a review of part #36 ending in a fenced ```json block. **If the artifact has prose but no JSON block, the task will have failed** — that is the persona bug this step exists to catch; strengthen the output-contract section of the prompt in Task 2 and rerun.

- [ ] **Step 5: Confirm the outcome and that nothing was filed**

```bash
grep -i "$TASK" /Users/rem/harness-root/harness.log | tail -20
gh issue list --repo onpaj/Anela.Heblo --label arch-review --state open --limit 5
```

Expected: the task's last outcome is `clean` (or `findings` with a small array — both are legitimate, this part just happened to have something). If `clean`, the summary reads `no issues to file` and the GitHub list is unchanged from before the run.

- [ ] **Step 6: If the outcome was `clean`, verify the task ended in `done/`, not `failed/`**

```bash
ls /Users/rem/harness-root/done/ | grep "$TASK" || ls /Users/rem/harness-root/failed/ | grep "$TASK"
```

Expected: the task file is in `done/`. A clean review landing in `failed/` means the empty-array path is broken — stop and diagnose before Task 6.

---

### Task 6: Dry run — the findings path

No new files. Proves an issue is actually created, carries both required labels, and is ingested.

- [ ] **Step 1: Submit against a part with a documented soft spot**

Part #3 (*Product Costing & Margin Calculation*) carries the analysis note *"four competing cost providers — worth checking which one actually wins in which scenario"*, so it is the most likely part to yield a genuine finding.

```bash
/Users/rem/.local/bin/harness submit \
  --root /Users/rem/harness-root \
  --workflow arch-review \
  --repo Anela.Heblo \
  --data '{"title":"Architecture review of module map part #3 — Product Costing & Margin Calculation"}'
```

- [ ] **Step 2: Wait for it to settle, then check what was filed**

```bash
gh issue list --repo onpaj/Anela.Heblo --label arch-review --state open --limit 5 \
  --json number,title,labels
```

Expected: zero or more new `[arch-review] …` issues. Zero is a legitimate result — if so, rerun against another part (#24 Photobank or #29 Customer Support are the two largest) rather than weakening the persona's bar.

- [ ] **Step 3: Verify the labels on a filed issue — the hard requirement**

```bash
gh issue view <number> --repo onpaj/Anela.Heblo --json labels --jq '.labels[].name'
```

Expected: the list includes **`harness:todo`** and **`arch-review`**, plus one topical and one severity label. `harness:todo` missing is a blocking defect — recheck that `label` (not `allowed_labels`) is `harness:todo` in Task 3.

- [ ] **Step 4: Verify the idempotency marker is embedded**

```bash
gh issue view <number> --repo onpaj/Anela.Heblo --json body --jq '.body' | grep -o '<!-- harness-issue:[^>]*>'
```

Expected: one `<!-- harness-issue:tsk_…:xxxxxxxx -->` comment. This is what stops a re-run of the *same* task from filing twice, and what the autoheal brake reads.

- [ ] **Step 5: Verify ingestion by the development pipeline**

Wait ~60s, then:

```bash
gh issue view <number> --repo onpaj/Anela.Heblo --json labels --jq '.labels[].name'
```

Expected: `harness:todo` has been swapped for `harness:queued` by `processes/harness-todo.json`, confirming the finding is now flowing into the `development` workflow. **This is the point of no return** — from here the issue is on its way to an automerge-eligible PR.

---

### Task 7: Go live

- [ ] **Step 1: Restart the service so it picks up the new files**

```bash
launchctl kickstart -k gui/$(id -u)/com.harness
```

- [ ] **Step 2: Confirm a clean startup**

```bash
sleep 20 && grep -iE 'warning|error|ProcessValidationError|UnknownFinisherKind' /Users/rem/harness-root/harness.log | tail -20
```

Expected: no warning naming `arch-review` or `heblo-arch-review`. A `warning:` naming the workflow means the finisher binding was rejected and the workflow was dropped — the process would then be skipped too.

- [ ] **Step 3: Confirm the process is scheduled**

```bash
grep -i 'heblo-arch-review' /Users/rem/harness-root/harness.log | tail -5
```

Expected: evidence the process compiled at startup.

- [ ] **Step 4: Watch the first two fires**

Over the next ~4 hours, confirm two tasks are born ~2h apart, that they name **different** parts, and that neither fails. Record the parts drawn.

```bash
grep -i 'Architecture review of module map part' /Users/rem/harness-root/harness.log | tail -10
```

Expected: two distinct request lines. Two fires within the same 2h bucket would mean the dedup key is wrong; no second fire would mean the occurrence gate is stuck.

---

### Task 8: Worktree janitor (gap G1)

**Files:**
- Create: `scripts/prune-harness-worktrees.sh`
- Create: `/Users/rem/harness-root/processes/worktree-janitor.json`

**Why:** `~/harness-root/worktrees` already holds 36 GB across 156 worktrees and nothing ever removes them (harness_v2 invariant #30). An Anela.Heblo worktree is 186 MB; twelve reviews a day adds ~2.2 GB/day. This task is not optional.

**Interfaces:**
- Consumes: `/Users/rem/harness-root/{done,archived}/*.json` (terminal tasks) and `/Users/rem/harness-root/worktrees/<task_id>/`.
- Produces: nothing on stdout in normal operation (so the `command` check yields no observation and no task) — it is a side-effect-only process, the same shape as `GithubConflictsCheck`'s branch update.

- [ ] **Step 1: Measure the starting point**

```bash
du -sh /Users/rem/harness-root/worktrees && ls /Users/rem/harness-root/worktrees | wc -l
```

Record both numbers — Step 6 compares against them.

- [ ] **Step 2: Write the pruner in dry-run-by-default form**

```bash
cat > /Users/rem/Anela.Heblo/scripts/prune-harness-worktrees.sh <<'EOF'
#!/usr/bin/env bash
#
# Remove worktrees belonging to TERMINAL harness tasks (done/ or archived/)
# older than AGE_DAYS. Never touches a worktree whose task is still live in any
# other queue - harness_v2 invariant #30 lets a live task's branch stay checked
# out in its own worktree, and removing it under a running task breaks reattach.
#
# Prints nothing on stdout in normal operation: it runs as a harness `command`
# check, and any stdout line would mint a task.
#
# Usage: prune-harness-worktrees.sh [--apply]   (default: dry run, to stderr)
set -euo pipefail

ROOT="${HARNESS_ROOT:-/Users/rem/harness-root}"
AGE_DAYS="${AGE_DAYS:-3}"
APPLY=0
[ "${1:-}" = "--apply" ] && APPLY=1

terminal_ids="$(
  find "$ROOT/done" "$ROOT/archived" -maxdepth 1 -name '*.json' -type f 2>/dev/null \
    | xargs -I{} basename {} .json 2>/dev/null | sort -u
)"

# Anything still live: every task json under the root EXCEPT the two terminal
# queues. Deliberately a denylist, not an allowlist of queue names - a queue
# added by a future harness version must read as "live", never as "prunable".
live_ids="$(
  find "$ROOT" -name '*.json' -type f \
       -not -path "$ROOT/done/*" -not -path "$ROOT/archived/*" \
       -not -path "$ROOT/agents/*" -not -path "$ROOT/workflows/*" \
       -not -path "$ROOT/processes/*" -not -path "$ROOT/triggers/*" \
       -not -path "$ROOT/worktrees/*" 2>/dev/null \
    | xargs -I{} basename {} .json 2>/dev/null | sort -u
)"

removed=0
freed=0
for dir in "$ROOT"/worktrees/*/; do
  [ -d "$dir" ] || continue
  id="$(basename "$dir")"

  printf '%s\n' "$terminal_ids" | grep -qx "$id" || continue
  printf '%s\n' "$live_ids" | grep -qx "$id" && continue
  [ -n "$(find "$dir" -maxdepth 0 -mtime "+$AGE_DAYS")" ] || continue

  size="$(du -sk "$dir" | cut -f1)"
  if [ "$APPLY" -eq 1 ]; then
    rm -rf "$dir"
    echo "removed $id (${size}K)" >&2
  else
    echo "would remove $id (${size}K)" >&2
  fi
  removed=$((removed + 1))
  freed=$((freed + size))
done

echo "worktree-janitor: ${removed} worktrees, $((freed / 1024))MB (apply=$APPLY)" >&2
EOF
chmod +x /Users/rem/Anela.Heblo/scripts/prune-harness-worktrees.sh
```

- [ ] **Step 3: Dry-run it and read every line before applying**

```bash
/Users/rem/Anela.Heblo/scripts/prune-harness-worktrees.sh
```

Expected: a list of `would remove tsk_… (…K)` lines and a summary, all on stderr, **nothing on stdout**. Verify by eye that at least one listed id really is in `done/` or `archived/`:

```bash
ls /Users/rem/harness-root/done/ /Users/rem/harness-root/archived/ | grep <one-listed-id>
```

Confirm stdout is genuinely empty (this is what keeps the check from minting tasks):

```bash
/Users/rem/Anela.Heblo/scripts/prune-harness-worktrees.sh 2>/dev/null | wc -c
```

Expected: `0`.

- [ ] **Step 4: Apply once by hand**

```bash
/Users/rem/Anela.Heblo/scripts/prune-harness-worktrees.sh --apply
```

Expected: `removed …` lines and a summary naming the space freed.

- [ ] **Step 5: Verify the harness is unharmed**

```bash
launchctl list | grep com.harness
tail -30 /Users/rem/harness-root/harness.log
```

Expected: the service is still running and the log shows no new errors. Pruning a terminal task's worktree is inert by invariant #30's own reasoning — the original worktree is permanently inert once its task reaches a terminal state.

- [ ] **Step 6: Schedule it daily**

```bash
cat > /Users/rem/harness-root/processes/worktree-janitor.json <<'EOF'
{
  "name": "worktree-janitor",
  "trigger": {
    "cron": "0 3 * * *"
  },
  "action": {
    "check": "command",
    "params": {
      "command": "/Users/rem/Anela.Heblo/scripts/prune-harness-worktrees.sh --apply",
      "timeout": 600
    }
  },
  "target": {
    "step": "arch-review"
  },
  "dedup": "per-interval",
  "sink": {
    "kind": "none"
  }
}
EOF
```

The `target` is required by the schema but never reached: the script prints nothing on stdout, so `CommandCheck` yields no observation and no task is ever minted. Naming an existing step keeps compile validation happy without inventing a queue.

Cron is UTC — `0 3 * * *` is 04:00/05:00 Prague depending on DST.

- [ ] **Step 7: Verify it compiles**

```bash
/Users/rem/.local/share/uv/tools/harness/bin/python -c "
import json
from harness.drivers.fs_processes import compile_process
from harness.drivers.system_clock import SystemClock
raw = json.load(open('/Users/rem/harness-root/processes/worktree-janitor.json'))
compile_process('worktree-janitor', raw, clock=SystemClock(),
                known_workflows={'arch-review'}, known_steps={'arch-review'},
                known_repositories={'Anela.Heblo','harness_v2','personal_assistant'})
print('OK')
"
```

Expected: `OK`.

- [ ] **Step 8: Commit the scripts**

```bash
cd /Users/rem/Anela.Heblo
git add scripts/prune-harness-worktrees.sh
git commit -m "feat(arch-review): worktree janitor for terminal harness tasks"
```

- [ ] **Step 9: Re-measure**

```bash
du -sh /Users/rem/harness-root/worktrees && ls /Users/rem/harness-root/worktrees | wc -l
```

Compare against Step 1 and record the delta.

---

## Post-implementation

Record in the spec's *Known gaps* section what the first week actually showed:
- how many parts were drawn, and how lumpy the distribution was (G4);
- whether any duplicate finding slipped past the persona's self-dedup (G2);
- whether the janitor holds the worktree total flat (G1);
- whether any filed finding was rejected on review (G5) — the first real signal on review quality.
