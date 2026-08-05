---
name: arch-map
description: Create or refresh a repository's module map — the numbered partition of a codebase that /arch-review samples from. Run it inside the repo you want mapped and it needs no arguments; with no existing map it crawls the repo and cuts a first map, with one present it refreshes it in place under the never-renumber rules. Use when the user says "arch map", "create a module map", "refresh the module map", "map this repo", "update the map", "arch-map", or when /arch-review reports no map. Writes only the map file — never touches source code.
---

You produce and maintain the **module map**: the stable, numbered partition of a repository
into parts small enough that a single review can hold one in context.

The map is the substrate `/arch-review` samples from. Everything that skill knows about a
repository — where a part's boundary lies, which documents are normative, which soft spots
are already suspected — it learns from this file. A map that drifts from the code makes
every review drift with it, silently.

**Read `.claude/skills/arch-map/map-contract.md` before you write anything.** It defines
what a map must contain. This skill is the procedure; the contract is the format.

**You write exactly one file: the map.** Never modify source code, never commit anything
else, never open a PR.

## Arguments

```
/arch-map [repo-path]
```

- **`repo-path`** — the repository to map. Optional; defaults to the repo you are standing
  in. The map is written to `docs/architecture/module-map.md` inside it, unless a map
  already exists elsewhere in that repo, in which case that one is refreshed in place.

## Which mode you are in

```bash
.claude/skills/arch-map/survey.sh <repo-path>
```

The survey ends with an **Existing module map** section.

- **No map → CREATE.** Cut a first map from scratch. Follow *Create* below.
- **Map found → REFRESH.** The existing numbering is now permanent history. Follow
  *Refresh* below. **Do not regenerate it from scratch** — that would renumber parts and
  silently invalidate every issue, artifact and commit that references them.

If the user explicitly asks for a fresh cut of a repo that already has a map, tell them
what renumbering costs and make them confirm before doing it.

---

# Create

## C1. Survey

Read the survey output in full. It gives you, without knowing anything about frameworks:

- **ecosystems** — which build markers are present, so you know which conventional seams to
  look for (feature folders, controllers, packages, route groups);
- **LOC and file counts per directory** at depth 1 and 2 — the raw material for the cut;
- **largest individual files** — often a part boundary, or a split candidate on their own;
- **candidate normative documents** — the shortlist for the map's `## Normative documents`
  section.

## C2. Learn the real seams before cutting

The survey shows sizes, not meaning. Before you cut, read enough of the code to know what
the natural boundaries actually are:

- What is the organising principle — layers, feature slices, packages, domains?
- Where do the entry points live (CLI commands, routes, controllers, jobs, handlers)?
- Which directories are tests, and do they sit beside their subject or in a parallel tree?
- What is generated, vendored, or otherwise not hand-written?

**Cut along seams that already exist.** A part that follows a real boundary can be described
in one sentence; a part cut at an arbitrary LOC threshold cannot.

## C3. Cut

Apply, in priority order:

1. **Follow the dominant organising principle.** If the codebase is organised by feature
   slice, a part is a slice; if by layer, a part is a layer's coherent chunk.
2. **Split anything too big for one sitting** along its internal seams.
3. **Merge anything too thin to say something useful about** with the neighbour it actually
   collaborates with.
4. **Tests travel with their subject** unless the repo keeps a large independent test tree,
   which is then its own part.
5. **Non-code concerns get real parts** — packaging and CI, documentation, agent/skill
   definitions, infrastructure. That is where a lot of drift lives, and a map that omits
   them is not exhaustive.
6. **Behaviour-bearing Markdown is code.** Where prompts, skills or agent definitions drive
   runtime behaviour, they get parts cut by role — not lumped into a docs bucket.

Sizing targets are in the survey output and in the contract. Scale them to the repo: a
1k-LOC project's part is one module plus its tests; a 100k-LOC project's is a whole feature.

## C4. Write the map

Follow `map-contract.md` exactly. Required: summary-table rows, a `## N.` section per part
with a non-empty `Owns:`, stable numbers.

Then the two sections that decide whether reviews are any good:

- **`## Normative documents`** — where this project's rules actually live. Be specific:
  name the file the ADRs are in, not "the ADRs". **If part of the corpus is stale, say so
  and say what is wrong with it** — a reviewer can only be sceptical about staleness it was
  told to expect.
- **`Analysis notes`** per part — where you suspect the soft spots are. Write these while
  the code is fresh in your mind; they are the single highest-value field in the map,
  because the reviewer is told to start there. Note what you *noticed but did not chase*.

Also record, at the top, the cut rules you actually applied. A future refresh needs to know
whether a new file belongs to the part it landed in.

## C5. Verify

```bash
.claude/skills/arch-map/check-map.sh <map-path>
```

Fix every **ERROR** — a dead `Owns:` path is a broken review boundary. Read every
**WARNING** and decide explicitly: an unassigned directory is either a missing part, an
addition to an existing part, or deliberately unmapped. Do not leave it silent.

Then confirm the picker can actually draw from it:

```bash
for i in 1 2 3 4 5; do
  ARCH_NO_PULL=1 .claude/skills/arch-review/pick-module.sh <map-path>
done
```

---

# Refresh

## R1. The one rule that matters

**Part numbers are permanent identifiers. Never reuse, never renumber.**

Reviews, issues, artifacts and commits reference parts by number. If #14 means *Checkpoint
State* today and something else after a refresh, every past reference silently becomes
wrong — and nothing fails loudly to tell you.

| Situation | What to do |
|---|---|
| New module appears | Append at the next free number. Do **not** insert it "where it belongs". |
| A part is split | The original number keeps the larger half; the new half gets a new number at the end. Note the split in both entries. |
| Two parts merge | Keep the lower number. Mark the higher one `RETIRED — merged into #N` and leave the row in place. |
| A part is deleted | Mark it `RETIRED — code removed in <commit>`. Do not delete the row. |
| A part is renamed | Change the title freely. The number does not move. |

Retired rows stay forever, collapsed to one line. They are cheap, and they keep old
references resolvable. Group letters and section order are **not** identifiers — reorder
those whenever it improves readability.

## R2. Re-measure

```bash
.claude/skills/arch-map/survey.sh <repo-path>
```

Compare against the sizes recorded in the map. Anything that has grown past a sitting is a
**split candidate**; anything that has shrunk below independent interest is a **merge
candidate**.

## R3. Run both coverage passes

```bash
.claude/skills/arch-map/check-map.sh <map-path>
```

- **Dead `Owns:` paths (ERROR)** — the code moved or was deleted. Update the part, or
  retire it.
- **Stale paths in prose (WARNING)** — fix the prose, or confirm the reference is a
  deliberate statement about something that does not exist.
- **Unassigned directories (WARNING)** — new code that belongs to no part. Each one is a
  new part, an addition to an existing part, or deliberately unmapped. **Decide explicitly.**

## R4. Look for structural drift

A refresh is the moment to notice what mechanical checks cannot:

- **Parts that no longer match their folder.** The biggest parts attract new code that
  defaults to the wrong one. Read the subfolder listing, not the folder name.
- **A shared dependency gaining a second consumer.** A helper assigned to its single
  consumer should become its own part the moment a second part uses it.
- **Cross-part leakage.** An interface declared in one part but consumed only by another is
  a sign the cut is in the wrong place — or that the code is. Note it in *Analysis notes*
  rather than moving the boundary immediately.
- **Generated code creeping into a part.** If a part's LOC jumped sharply, check it is not
  counting generated output; the survey excludes the usual directories but not unusual ones.
- **The normative corpus going stale.** Re-check that the documents the map names still
  describe the code. Update the staleness warnings — they are what stop a reviewer treating
  fiction as a rule.

## R5. Update, verify, report

Update the affected part entries, the summary rows, and the part count in the opening line.
Re-run `check-map.sh` until it is clean, then tell the user what changed: parts added,
retired, split, merged, and which warnings you consciously accepted.

---

## Notes

- **The map is a documentation change.** It touches the map file and nothing else. Commit it
  on its own.
- **Refresh triggers** — a new top-level source directory, a new entry point or route group,
  a part outgrowing a sitting, or a part shrinking below interest. Otherwise a scheduled
  pass every few months, or before starting a new review cycle.
- **`check-map.sh` exits non-zero on ERRORs**, so it can gate a refresh in CI or a loop.
  Warnings never fail it — a repo legitimately contains deliberately unmapped code.
- The forward pass matches on **leaf names, not full paths**, and a directory is covered if
  any ancestor is mentioned. Full-path matching produces dozens of false hits against the
  compact notation maps use; the trade is occasional false negatives, so confirm by eye that
  a genuinely new part is listed under a numbered entry rather than merely mentioned.
