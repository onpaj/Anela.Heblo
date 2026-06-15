Plan saved to `docs/superpowers/plans/2026-06-13-frontend-typeerror-r-filter-on.md`.

**Summary of the plan:**

9 tasks taking the engineer from "telemetry blob" → "fix shipped":

1. **Resolve `Yq`** via local `GENERATE_SOURCEMAP=true` build + `source-map-cli` walk; record the resolved file/line.
2. **Write 3 failing regression tests** in `Dashboard.test.tsx` (`data: null`, `tiles: null`, non-array object with truthy `length` — the last one specifically exercises the gap the arch-review flagged).
3. **Apply `Array.isArray` guards** to `Dashboard.tsx`'s `visibleTileData` memo and `handleReorder` handler.
4. **Sibling fix in `DashboardSettings.tsx`** — same guard pattern across `availableTiles` and `userSettings?.tiles` chains, plus matching regression tests (file created if absent).
5. **JournalList audit** — read-only check; only patch if a finding surfaces.
6. **Broader FR-3 sweep** of PRs #2962/#2943/#2948 via `gh pr diff` grep for `.filter|.map|.reduce` additions; record findings in `audit-notes.md`.
7. **NFR-3 decision** — either file an upstream contract-drift issue or explicitly record "no drift identified."
8. **Validation gates** — `npm run build` + `npm run lint` + Jest (touched files + full suite).
9. **Push + open PR** with the FR-3 audit, NFR-3 decision, and symbol-resolution artifacts linked from the description.

Per the pipeline note, no execution handoff prompt — the plan file is the artifact.