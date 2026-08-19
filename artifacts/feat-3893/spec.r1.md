# Specification: Fix broken Architecture Documentation path in CLAUDE.md

## Summary
`CLAUDE.md`'s documentation map lists the top Architecture reference at `docs/📘 Architecture Documentation – MVP Work.md`, but the file actually lives at `docs/architecture/📘 Architecture Documentation – MVP Work.md`. This is a one-line path correction so the reference resolves correctly.

## Background
`CLAUDE.md` instructs readers (human and AI agent alike) to "Read the relevant doc **before** implementation work touches that area. No architectural changes without consulting these first." The Architecture Documentation entry is the first item under the Architecture heading in that map — anyone following the instruction literally on an architecture question hits a file-not-found on the very entry meant to prevent that outcome. Every other entry in the documentation map (`docs/architecture/filesystem.md`, `docs/development/setup.md`, `docs/design/ui_design_document.md`, `docs/testing/playwright-e2e-testing.md`, `docs/integrations/mcp-server.md`, etc.) resolves correctly; this single entry is missing the `architecture/` path segment. Confirmed via `find . -iname "*Architecture Doc*"`, which locates the file at `./docs/architecture/📘 Architecture Documentation – MVP Work.md`.

## Functional Requirements

### FR-1: Correct the Architecture Documentation path in CLAUDE.md
On line 15 of `CLAUDE.md`, change the path from `docs/📘 Architecture Documentation – MVP Work.md` to `docs/architecture/📘 Architecture Documentation – MVP Work.md`, keeping the existing filename, emoji, dash character, and description (`— modules, data flow, business logic`) unchanged.

**Acceptance criteria:**
- Line 15 of `CLAUDE.md` reads exactly: `` - `docs/architecture/📘 Architecture Documentation – MVP Work.md` — modules, data flow, business logic ``
- The referenced path resolves to an existing file in the repository (verifiable via `find . -iname "*Architecture Doc*"` or `test -f`).
- No other line in `CLAUDE.md` is modified.
- No other file in the repository is modified.

## Non-Functional Requirements

N/A — this is a single-line documentation path correction with no runtime, performance, or security surface.

## Data Model

N/A.

## API / Interface Design

N/A.

## Dependencies

None. Self-contained change to `CLAUDE.md`; no code, build, or external service dependencies.

## Out of Scope

- Renaming, moving, or reformatting the target file (`docs/architecture/📘 Architecture Documentation – MVP Work.md`) itself.
- Auditing or fixing any other documentation links in `CLAUDE.md` or elsewhere in `docs/` — the brief and evidence confirm this is the only broken path in the list.
- Any change to the structure, wording, or ordering of the documentation map beyond the one path correction.

## Open Questions

None.

## Status: COMPLETE
