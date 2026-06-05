Self-review against the spec and architecture review:

**Spec coverage:** FR-1 → Task 2; FR-2 → Task 3; FR-3 → Task 4; FR-4 → Task 5; FR-5 → Task 6; FR-6 → Tasks 1, 7. NFR-2/4 → covered by Task 1 regression tests and Task 7 full suite. NFR-3 → encoded in PR description requirements.

**Architecture review amendments:** Amendment 1 (five targeted tests) → Task 1. Amendment 2 (MarketingActionRepository note in PR description) → "PR description requirements" section.

**Placeholder scan:** No TBD/TODO/handle-edge-cases stubs. Every code change has the exact before/after; every command has expected output.

**Type consistency:** Method signatures and property names (`GetEntriesAsync(int, int, string, string)`, `SearchEntriesAsync(...)`, `JournalIndicatorSnapshot` fields, `PagedResult<T>` shape, `JournalEntry.IsDeleted` setter, `AssociateWithProduct(string)`) all match the source files I read.

Plan saved to `docs/superpowers/plans/2026-06-04-journal-redundant-soft-delete-predicates.md`. Seven tasks, twelve final tests (7 existing + 5 new), six commits expected.