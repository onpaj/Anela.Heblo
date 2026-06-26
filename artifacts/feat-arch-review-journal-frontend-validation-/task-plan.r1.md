Plan saved to `artifacts/feat-arch-review-journal-frontend-validation-/plan.r1.md`.

## Summary

**13 tasks** decomposed bottom-up: domain entity → contracts → handlers (with TDD) → integration test → persistence config → EF migration → frontend wiring → final integration pass. Every step shows the exact code to write, exact paths to touch, and exact commands to run with expected output.

**Key architectural alignment captured in the plan:**
- Validation is done **inside handlers** using `string.IsNullOrWhiteSpace(request.Title)` returning `BaseResponse(ErrorCodes.InvalidJournalTitle)` — *not* via `[Required]` + ASP.NET ModelState — to keep one error envelope across the whole Journal slice (Task 1 verifies the existing `[HttpStatusCode(HttpStatusCode.BadRequest)]` mapping at line 158).
- Backfill placeholder is **`"Bez názvu"`** (no parentheses) to match `JournalList.tsx:57`'s existing render fallback — migrated rows stay visually unchanged.
- Migration is **hand-edited** so the `UPDATE` runs before `AlterColumn nullable: false` (otherwise the alter would fail on NULL rows).
- Existing test `Handle_WhenValidRequestWithoutOptionalFields_…` (the one currently asserting `Title = null` is fine) is **replaced** by Theory-based whitespace/null rejection tests in Task 5.
- Read DTOs (`JournalEntryDto`, `SearchJournalEntryDto`) tightened so the regenerated TS client surfaces `title: string` everywhere (Task 4 + FR-8).

Self-review confirms every spec FR (and the arch-review amendments FR-7, FR-8) maps to a concrete task; no placeholders; signatures are consistent across handler/test/integration-test occurrences.