# Specification: Map folderKey to path for Marketing folder links

## Summary
Editing a marketing action that has folder links silently deletes them, because the page passes API `folderLinks` (`{folderKey, folderType}`) through without renaming `folderKey` to `path`. Fix the mapping in the frontend so existing links survive an edit.

## Background
`MarketingCalendarPage.tsx` lines 135 and 207 assign `folderLinks: a.folderLinks`. `MarketingActionModal.tsx` (line 124) reads `fl.path`, which is undefined, so paths initialize to "" and the submit filter (`fl.path.trim()`) drops all links. Related: `calendar/MobileAgendaView.tsx:72` has the same raw pass-through (display only).

## Functional Requirements

### FR-1: Map folderKey to path in list mapping
In `listActions` (MarketingCalendarPage.tsx ~135), map `a.folderLinks ?? []` to `{ path: fl.folderKey ?? "", folderType: fl.folderType }`.
**Acceptance criteria:**
- List-derived actions expose `path` populated from `folderKey`.

### FR-2: Map folderKey to path in detail mapping
Same mapping in the detailQuery effect (~207), including any `label` the API provides.
**Acceptance criteria:**
- Opening an action with folder links shows the paths in the edit form.
- Saving without touching the folder section preserves all links.

### FR-3: Regression test
Add a test in `pages/__tests__/MarketingCalendarPage.test.tsx` covering edit-and-save preserving folder links.

## Non-Functional Requirements
### NFR-1: Performance
No impact.
### NFR-2: Security
No change.

## Data Model
No change. API DTO fields: `folderKey`, `folderType`. Local type field: `path`.

## API / Interface Design
No backend or OpenAPI changes.

## Dependencies
None.

## Out of Scope
Backend changes; refactoring the local `MarketingActionDto` type; typing cleanup of `any` usages.

## Open Questions
None.

## Status: COMPLETE
