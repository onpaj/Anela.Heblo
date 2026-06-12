Plan complete and saved to `docs/superpowers/plans/2026-06-11-journal-presentation-logic-to-frontend.md`.

**Summary:** 12 tasks, ordered backend-first (DTO → mapper → handler → tests, committed as one unit), then frontend (new `journalPreview.ts` module behind failing tests → implementation → client regeneration → `JournalList` + `JournalTab` migration → test updates, committed as a second unit), then end-to-end validation. Key architectural decisions baked in per the arch review:

- **Highlighting dropped** — `HighlightedTerms` was populated, transported, and never rendered. The plan removes the field entirely rather than introducing net-new `<mark>` rendering.
- **Helper lives at `frontend/src/components/pages/Journal/journalPreview.ts`** — co-located with the journal feature, but its own file so the cross-module `JournalTab` consumer can import without coupling to React.
- **Both consumers migrated in the same change** — `JournalList.tsx` (browse + search rows) and `JournalTab.tsx` (catalog product detail). Task 7 explicitly verifies the regenerated TypeScript client surfaces compile errors only in those two files; anything else means a missed consumer.
- **Mapper + mapper tests included** — `JournalEntryMapper.ToSearchDto` now sets `Content = entry.Content`, and the two mapper tests that asserted on the removed fields are rewritten in Task 4.
- **Browse preview length intentionally grows 150 → 200 chars** — called out in the Task 11 commit message.