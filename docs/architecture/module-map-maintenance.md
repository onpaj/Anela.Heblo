# Keeping the Module Map Current

How to refresh `docs/architecture/module-map.md` so it stays an accurate partition of the codebase.

The map exists to be **iterated over**: pick part #N, analyse it, move on. That only works if the numbers stay
meaningful over time. This document defines the rules that keep them meaningful and the procedure for a refresh pass.

---

## The one rule that matters

**Part numbers are permanent identifiers. Never reuse, never renumber.**

Analysis output, issues, commits and notes will reference parts by number. If #14 means *Batch Planning* today and
*Photobank* after a refresh, every past reference silently becomes wrong — and nothing will fail loudly to tell you.

Consequences of that rule:

| Situation | What to do |
|---|---|
| New module appears | Append at the next free number (#53, #54, …). Do **not** insert it "where it belongs". |
| A part is split in two | Original number keeps the larger half; the new half gets a new number at the end. Note the split in both entries. |
| Two parts are merged | Keep the lower number. Mark the higher one **RETIRED → merged into #N** and leave the entry in place. |
| A part is deleted from the codebase | Mark it **RETIRED — code removed in `<commit>`**. Do not delete the entry. |
| A part is renamed | Change the title freely. The number does not move. |

Retired entries stay in the document forever, collapsed to a single line. They are cheap, and they make old
references resolvable.

The group letters (A–D) and the ordering of sections are **not** identifiers. Reorder or regroup those whenever it
improves readability.

---

## When to refresh

**Trigger a refresh when any of these happen:**

- A new folder appears under `Application/Features/`, `Domain/Features/`, or `backend/src/Adapters/`
- A new controller lands in `backend/src/Anela.Heblo.API/Controllers/`
- A new top-level route is added to `frontend/src/App.tsx`
- A new folder appears under `frontend/src/features/` or `frontend/src/components/`
- A part grows past ~6k hand-written LOC (too big for one analysis sitting → split it)
- A part shrinks below ~1k LOC and stops being independently interesting (→ merge it)

**Otherwise:** a scheduled pass every ~3 months, or before starting a new full analysis cycle.

A refresh is a documentation change. It touches `module-map.md` and nothing else.

---

## Refresh procedure

### Step 1 — Re-measure

Run from the repo root. These are the same commands used to build the original cut.

```bash
# Backend: LOC + file count per Application feature
for d in backend/src/Anela.Heblo.Application/Features/*/; do
  n=$(find "$d" -name '*.cs' | wc -l)
  l=$(find "$d" -name '*.cs' -exec cat {} + 2>/dev/null | wc -l)
  echo "$l	$n	$d"
done | sort -rn

# Backend: Domain, Persistence, Adapters
for d in backend/src/Anela.Heblo.Domain/Features/*/ \
         backend/src/Anela.Heblo.Persistence/*/ \
         backend/src/Adapters/*/; do
  l=$(find "$d" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -exec cat {} + 2>/dev/null | wc -l)
  echo "$l	$d"
done | sort -rn

# Frontend: LOC per component area (tests excluded)
for d in frontend/src/components/*/ frontend/src/features/*/ frontend/src/*/; do
  l=$(find "$d" -name '*.ts*' -not -path '*__tests__*' -exec cat {} + 2>/dev/null | wc -l)
  echo "$l	$d"
done | sort -rn
```

Anything over ~6k LOC is a **split candidate**. Anything under ~1k is a **merge candidate**.
Ignore `Persistence/Migrations/` and `frontend/src/api/generated/` — generated code, excluded by design.

### Step 2 — Find unassigned code

Every folder that exists should belong to exactly one part. Find the ones that don't:

```bash
# Folders and controllers that exist in the codebase
{ ls -d backend/src/Anela.Heblo.Application/Features/*/ \
        backend/src/Anela.Heblo.Domain/Features/*/ \
        backend/src/Adapters/*/ \
        frontend/src/components/*/ \
        frontend/src/features/*/ ;
  ls backend/src/Anela.Heblo.API/Controllers/*.cs ; } | sort > /tmp/actual.txt

# Check each against the map, matching on the leaf name
while read -r f; do
  grep -qF "$(basename "$f")" docs/architecture/module-map.md || echo "UNASSIGNED: $f"
done < /tmp/actual.txt
```

Match on the **leaf name**, not the full path. The map deliberately uses compact notation — `.../Features/Bank/`
shorthand and comma-separated lists like `` `ui/`, `common/`, `modals/` `` — so a full-path `grep` reports ~40 false
positives and is useless. Leaf matching currently returns zero hits.

The trade-off is false *negatives*: a short or generic leaf name (`Bank/`, `test/`) can match unrelated prose. When a
new part is added, confirm by eye that its folder is listed under a numbered entry, not merely mentioned somewhere.

Each hit is either a genuinely new part, an addition to an existing part, or something that belongs in the
"deliberately unassigned" list at the bottom of the map. Decide explicitly — don't leave it silent.

### Step 3 — Find dead references

The reverse check: paths the map claims exist but don't.

```bash
grep -oE '`[a-zA-Z_.][^`]*`' docs/architecture/module-map.md \
 | tr -d '`' \
 | grep -E '^(backend|frontend|docs|scripts|memory|artifacts|reportportal|\.github|\.claude)' \
 | sed 's/ .*//' | sed 's/,$//' | sort -u \
 | while read -r p; do [ -e "$p" ] || echo "MISSING: $p"; done
```

Expect three known false positives: the glob placeholders `backend/test/Anela.Heblo.Adapters.*.Tests/` and
`frontend/test/e2e/<module>/`, plus `docs/📘` (the architecture doc's filename contains a space, which the
`sed 's/ .*//'` truncates). Everything else is a stale path to fix.

### Step 4 — Check route coverage

Every routed page should map to a part:

```bash
grep -oE 'path="[^"]+"' frontend/src/App.tsx | sort -u
```

Cross-check against the "Primary route(s)" column in the summary table.

### Step 5 — Update the map

Apply the identifier rules from the top of this document. Update:

1. The affected part entries (owned folders, size, routes, dependencies)
2. The summary table rows
3. The part count in the opening line and the group headings
4. The "Coverage & known gaps" section — especially "overlaps to watch"

### Step 6 — Verify and commit

Re-run steps 2 and 3 until clean, then commit `module-map.md` on its own.

---

## What to look for while refreshing

Beyond mechanical accuracy, a refresh is the moment to notice structural drift. Watch for:

**Parts that no longer match their folder.** The biggest features (`Features/Catalog`, `Features/Manufacture`,
`Features/Logistics`) are deliberately split across several parts. New use cases land in those folders constantly and
default to the wrong part. Read the `UseCases/` listing, not the folder name, and assign each new subfolder explicitly.

**Adapters gaining a second consumer.** The map assigns single-consumer adapters to their consuming part
(`Adapters.Plaud` → #28). The moment a second part uses one, it should become a standalone part or move to
*Ancillary External Adapters* (#47). Check with:

```bash
grep -rl "Adapters.<Name>" backend/src --include=*.cs | grep -v "Adapters/Anela.Heblo.Adapters.<Name>"
```

**Adapters with no consumer at all.** Several are registered directly in `Program.cs` with nothing downstream. Worth
flagging rather than silently carrying.

**Cross-part leakage.** An interface declared in one part's namespace but only consumed by another is a sign the cut
is in the wrong place — or that the code is. (Existing example: `IPackingOrderClient` is declared in `ShoptetOrders`
but consumed by `Packaging` handlers.) Note it in the part's *Analysis notes* rather than moving the boundary
immediately.

**Dashboard tiles.** `Features/*/DashboardTiles/` folders stay with their owning feature; part #32 owns only the tile
framework. New tiles are easy to misfile.

**Generated code creeping into a part.** If a part's LOC jumps sharply, check it isn't counting generated output.

---

## Sizing reference

Targets used for the original cut, for consistency when splitting or merging:

| | Hand-written LOC |
|---|---|
| Too small — merge with a neighbour | < 1,000 |
| Target range | 1,500 – 6,000 |
| Too large — split along `UseCases/` | > 6,000 |

Split along existing seams (`UseCases/` subfolders, frontend route groups), not along arbitrary LOC boundaries. A
part should still be describable in one sentence after the split.
