### task: fix-claude-md-doc-path

**Files touched:** `CLAUDE.md` (repo root)

**Step 1 — Make the edit**

Open `CLAUDE.md` and locate line 15, inside the `## Documentation map` section under the `**Architecture**` subheading:

Old text (line 15):
```
- `docs/📘 Architecture Documentation – MVP Work.md` — modules, data flow, business logic
```

New text (line 15):
```
- `docs/architecture/📘 Architecture Documentation – MVP Work.md` — modules, data flow, business logic
```

The only change is inserting `architecture/` immediately after `docs/` and before the emoji. The emoji (📘), the en-dash characters (– in the filename, — before the description), the filename text, and the description (`modules, data flow, business logic`) must remain byte-identical. Do not touch any other line in the file.

If using a find/replace tool, match on the full old line text to guarantee uniqueness (the string `docs/📘 Architecture Documentation – MVP Work.md` appears only once in the file, on line 15).

**Step 2 — Verify the corrected path resolves**

Run:
```bash
grep -n 'Architecture Documentation' CLAUDE.md
```
Confirm the output shows line 15 with the new `docs/architecture/...` path.

Then confirm the referenced file actually exists at that path:
```bash
test -f "docs/architecture/📘 Architecture Documentation – MVP Work.md" && echo "OK: file exists"
```
This must print `OK: file exists`.

Also confirm no other line changed:
```bash
git diff CLAUDE.md
```
The diff must show exactly one changed line (line 15), with only the `architecture/` segment added — no other lines in `CLAUDE.md` and no other files in the repository should appear in the diff (`git status --porcelain` should list only `CLAUDE.md`).

**Step 3 — Commit**

Stage and commit only `CLAUDE.md`:
```bash
git add CLAUDE.md
git commit -m "docs: fix broken Architecture Documentation path in CLAUDE.md"
```

**Acceptance criteria for this task (from spec.r1.md FR-1):**
- Line 15 of `CLAUDE.md` reads exactly: `` - `docs/architecture/📘 Architecture Documentation – MVP Work.md` — modules, data flow, business logic ``
- The referenced path resolves to an existing file in the repository (verified via `test -f` in Step 2).
- No other line in `CLAUDE.md` is modified.
- No other file in the repository is modified.

---

## Self-review against spec

- FR-1 (correct the path on line 15, preserve emoji/dash/filename/description, no other lines or files touched) — covered by Step 1 (exact old/new text) and Step 2 (verification of both the corrected string and the byte-identical surrounding content via `git diff`).
- Non-functional requirements: N/A per spec — nothing further needed.
- Out of scope items (renaming/moving the target file, auditing other doc links, restructuring the map) — plan does not touch any of these.
