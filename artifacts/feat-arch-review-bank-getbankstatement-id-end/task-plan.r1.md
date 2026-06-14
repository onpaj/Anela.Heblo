Self-review pass complete. Plan saved.

**Plan saved to:** `docs/superpowers/plans/2026-06-03-bank-statement-by-id-handler.md`

Summary:
- 7 tasks, TDD ordering (failing tests in Task 2 → handler in Task 3 → controller in Task 4 → more tests + validation in 5–7).
- All locked decisions from the arch review baked into specific tasks: reuse `BankStatementImportDto`, no new mapper, preserve 404 `{message}` body, no `:int` route constraint, no CT to repo, drop the controller's `try/catch`.
- Every step has exact file paths, complete code blocks (no "TBD" / "similar to above"), exact bash commands, and expected outputs.
- Spec-coverage table at the end maps every FR/NFR (with arch-review amendments applied) to the task(s) that implement it.

Pipeline note honored — skipping the execution-choice prompt.