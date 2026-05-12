Implementation complete. Here's what was done:

**New file:** `Application/Features/Journal/Mapping/JournalEntryMapper.cs` — `internal static` mapper with a single pure `ToDto(JournalEntry)` method. Applies `.Where(ta => ta.Tag != null)` uniformly, fixing the latent `NullReferenceException` that existed on the list and search endpoints.

**3 handlers updated:** Each now calls `JournalEntryMapper.ToDto` instead of repeating the 22-line projection. `SearchJournalEntriesHandler` retains its content-preview `foreach` and private helpers unchanged.

**8 new tests:** `JournalEntryMapperTests` covers scalar fields, distinct products, empty collections, tag mapping, null-tag skip, and search fields staying null/empty after mapping. All pre-existing handler tests continue to pass.

Build: zero errors. All 32 Journal tests pass. Output artifact written to `artifacts/feat-arch-review-journal-journalentry-journal/impl/main.r1.md`.