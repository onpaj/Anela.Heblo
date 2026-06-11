Plan saved to `docs/superpowers/plans/2026-06-05-remove-dead-errormessage-runexpeditionlistprintfix.md`.

Summary of the plan:
- **7 tasks**, one commit per task (lock-in test → handler cleanup → DTO cleanup → TS client regen → hand-coded mirror prune → hook test mock → final validation).
- Touches the exact 4 files from arch-review (DTO, handler, hand-coded mirror, hook test) plus 1 new lock-in unit test file.
- Preserves the `errorData?.errorMessage` fallback in `useExpeditionList.ts` (consumes ASP.NET middleware payloads, not the typed DTO — arch-review Decision 3).
- Each step includes exact code blocks, exact commands, and expected outcomes — no placeholders.