# Specification: Remove Debug `console.log` Statements from `JournalEntryForm.tsx`

## Summary
Remove three leftover debug `console.log` statements from `frontend/src/components/JournalEntryForm.tsx` that print full journal entry payloads (title, content, tags, associated products) to the browser console on every modal open and edit. This is a small, surgical cleanup that eliminates inadvertent data exposure and console noise.

## Background
A daily architecture review on 2026-05-27 flagged three `console.log` calls in `JournalEntryForm.tsx` with `🐛` emoji prefixes — a clear signal they are leftover debugging artifacts. The logs run inside a `useEffect` that fires on every modal open and edit, dumping the full entry payload (including potentially sensitive business notes in `content`) into the browser console of all environments, including production and staging. There is no intentional telemetry, observability, or business value attached to these calls; they are purely development scaffolding that escaped cleanup.

## Functional Requirements

### FR-1: Remove debug `console.log` for `useEffect` entry trace
Delete the `console.log("🐛 JournalEntryForm useEffect - entry:", entry, "isEdit:", isEdit);` statement at approximately line 85 of `frontend/src/components/JournalEntryForm.tsx`.

**Acceptance criteria:**
- The line is removed entirely (no replacement, no commented-out version).
- The surrounding `useEffect` hook logic is unchanged.
- No new references to `console.log` are introduced.

### FR-2: Remove debug `console.log` for entry-data update trace
Delete the multi-line `console.log("🐛 Updating form with entry data:", { ... })` statement (approximately lines 88–94) that logs `title`, `content`, `entryDate`, `tags`, and `associatedProducts`.

**Acceptance criteria:**
- The full multi-line statement is removed (no orphaned object literal, no replacement).
- The form-population logic that follows (e.g., `setValue` / `reset` calls) is preserved verbatim.
- No new references to `console.log` are introduced.

### FR-3: Remove debug `console.log` for form-reset trace
Delete the `console.log("🐛 Resetting form for new entry");` statement at approximately line 107.

**Acceptance criteria:**
- The line is removed entirely.
- The form-reset logic in the same code path is preserved verbatim.
- No new references to `console.log` are introduced.

### FR-4: Preserve existing component behavior
The component's runtime behavior — modal open, edit-mode population, new-entry reset, form submission, validation — must remain functionally identical after the removals.

**Acceptance criteria:**
- Opening the modal for a new entry produces an empty form (same as before).
- Opening the modal in edit mode populates the form with the entry's `title`, `content`, `entryDate`, `tags`, and `associatedProducts` (same as before).
- Saving, cancelling, and re-opening the modal behave identically to current production behavior.
- No new lint warnings, type errors, or build errors are introduced.
- The browser console shows no `🐛`-prefixed messages from this component after the change.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change expected. Removing three `console.log` calls inside a non-hot path (modal-open `useEffect`) has negligible performance impact, though it slightly reduces work on each modal open in environments where console buffering is non-trivial.

### NFR-2: Security
Eliminates client-side disclosure of journal entry content (`title`, `content`, `tags`, `associatedProducts`) to the browser console. Journal entries may contain sensitive business notes; logging them to `console` exposes them to anyone with DevTools open (including shoulder-surfing, screen recordings, browser extensions that capture console output, and shared screen-share sessions).

### NFR-3: Maintainability
Removes debug scaffolding that adds noise to the production console, making genuine error messages easier to spot during triage.

### NFR-4: Compatibility
Change is purely a deletion of three log statements. No API contracts, props, types, or downstream consumers are affected. No migrations or feature-flag gating required.

## Data Model
No changes. The component continues to operate on the existing journal entry shape (`title`, `content`, `entryDate`, `tags`, `associatedProducts`).

## API / Interface Design
No changes. No public API, component props, hook signatures, or UI affordances are modified. The only observable difference is the absence of `🐛`-prefixed messages in the browser console.

## Dependencies
- File: `frontend/src/components/JournalEntryForm.tsx` (only file modified).
- No package, library, or service dependencies added, removed, or upgraded.
- No backend changes required.

## Out of Scope
- Introducing a structured logging utility or replacing the deleted logs with leveled logging (e.g., `debug`, `info`).
- Auditing other components or modules for similar leftover `console.log` statements.
- Adding a lint rule (e.g., `no-console`) to prevent future regressions — can be considered in a follow-up.
- Refactoring the `useEffect` logic, form-reset flow, or any other behavior in `JournalEntryForm.tsx`.
- Backend journal module changes.
- Adding unit tests specifically for "absence of `console.log`" — covered by manual verification and existing component tests.

## Open Questions
None.

## Status: COMPLETE