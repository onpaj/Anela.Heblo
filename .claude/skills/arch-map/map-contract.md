# Module map contract

A **module map** partitions one repository into numbered *parts*, each small enough for a
single focused review session. It is the only repo-specific input the `/arch-review` skill
takes — the picking and the review rigor are generic, the map is not.

A map can live anywhere and be passed to the skill by path. `docs/architecture/module-map.md`
is only the default location.

There is a worked example in this repository at `docs/architecture/module-map.md`.

---

## Required — the skill breaks without these

### 1. Summary-table rows

Every part must appear as a row in a Markdown table:

```markdown
| # | Part | Approx. size |
|---|------|--------------|
| 1 | CLI & Project Scaffolding | PY ~302 |
| 2 | Checkpoint State | PY ~200 |
```

- The row must start with `|`, then the number, then `|`, then the name.
- `pick-module.sh` parses **only these rows** — never the `## N.` headings. That is
  deliberate: a retired part keeps its heading, and parsing headings would resurrect it.
- Extra columns after the name are free-form and ignored.
- A map may have several summary tables (grouped by area); all their rows are pooled.

### 2. A per-part section

Each live part needs a section giving the reviewer its boundary:

```markdown
## 7. Monitoring TUI

**Purpose:** one or two sentences on what this part is for.

**Owns:**
- `path/to/module.py`
- `path/to/tests/`

**Depends on:** #2, #5.

**Analysis notes:** where the soft spots are believed to be.
```

- **`Owns:` is the scope boundary and is mandatory.** Every finding a review produces must
  live under one of these paths. Without it the reviewer has nothing to bound itself with
  and will wander into neighbouring parts, refiling the same findings every cycle.
- `Purpose` and `Depends on` are strongly recommended — they are what let the reviewer
  judge whether a defect belongs to this part or to one it merely calls.
- `Analysis notes` is optional and high-value: it points at a known soft spot, and the
  reviewer is told to start there. It is a *lead*, not a finding — the reviewer verifies
  it against code.

### 3. Stable part numbers

Part numbers are the identity and are **never reused or reassigned**.

- A part that disappears keeps its row, with the name replaced by `RETIRED — <reason>`.
  The picker skips those rows; old references stay resolvable.
- Adding a part appends the next free number.
- Splitting a part retires the original and adds two new numbers, so no existing reference
  silently changes meaning.

---

## Recommended — this is what makes reviews good rather than generic

### `## Normative documents`

The reviewer needs to know what *this* repository has decided, or it falls back on generic
best-practice opinion. Declare that corpus in the map:

```markdown
## Normative documents

Read these before judging; they are what a finding must be grounded in.

- `docs/architecture/adr/` — the ADRs. There is no other ADR location.
- `CONTRIBUTING.md` — the conventions this project actually enforces.
- `CLAUDE.md` — project concepts and state machine.
```

Be specific about **where** the rules live. A reviewer told to "check the ADRs" in a repo
with no `docs/adr/` will find none and fall back to opinion — naming the real location is
the difference between a grounded finding and a plausible one.

### Warn about stale documents

If part of the corpus is known to be out of date, **say so here**, and say what is wrong
with it. The reviewer is instructed to verify documented claims against code, but it can
only be sceptical about staleness it has been told to expect:

```markdown
> `README.md` still describes the pre-2.0 queue architecture. Verify anything it claims
> against the code; a gap between it and correct code is a documentation finding, not an
> architecture one.
```

### Sizing

Aim for parts a single review can hold in context — roughly 1.5k–6k LOC for a large
codebase, less for a small one. Split a part that is too big along its internal seams;
merge parts too thin to say anything about.

State the cut rules you used at the top of the map. It tells a future maintainer — and the
reviewer — whether a given file *should* belong to the part it is in.

### Maintenance

Say how the map is refreshed when the codebase moves. A map that drifts from the code makes
every review drift with it, silently.

---

## Checklist

Before pointing `/arch-review` at a new map:

- [ ] Every part has a summary-table row `| n | Name | … |`
- [ ] Every live part has a `## n. Name` section with a non-empty `Owns:`
- [ ] Part numbers are unique and never reused
- [ ] Retired parts are marked `RETIRED` in the **row**, not just deleted
- [ ] `## Normative documents` names where the rules actually live
- [ ] Known-stale documents are called out as such
- [ ] `pick-module.sh <map>` prints a well-formed line, ~20 runs in a row
