Plan saved to `docs/superpowers/plans/2026-06-03-nswag-409-typed-branch-articles-feedback.md`.

**Summary of the plan (12 tasks):**

1. **Task 1** — Add `[ProducesResponseType(200)]` + `[ProducesResponseType(409)]` to `SubmitFeedback`.
2. **Task 2** — Create `nswag-templates/README.md` documenting the override contract, predicate, and verification.
3. **Task 3** — Identify and override the NSwag 14.1.0 Liquid template that emits `processX(response)` bodies; predicate is "4xx body schema equals 2xx body schema".
4. **Task 4** — Wire `templateDirectory: "nswag-templates"` in `nswag.frontend.json`.
5. **Task 5** — Regenerate `api-client.ts`, verify the diff is scoped to `articles_SubmitFeedback` only, verify idempotency.
6. **Task 6** — Suppress global toast on 409 + structured `BaseResponse` (arch-review Spec Amendment #1, option A), with TDD tests in a new `client.test.ts`.
7. **Task 7** — TDD rewrite of `useSubmitArticleFeedbackMutation` to call the generated client; rewrite the 3 mutation tests.
8. **Task 8** — Update JSDoc on `getAuthenticatedFetch`.
9. **Task 9** — Rewrite the status-branching section in `docs/development/api-client-generation.md`.
10. **Task 10** — Produce the FR-6 audit table (substantially larger than the brief expected — ~30 hook files have `apiClient as any`); classify each and prep umbrella follow-up.
11. **Task 11** — Full validation pass (build, format, lint, tests, grep checks).
12. **Task 12** — Push, open PR with audit table.

**Pre-Flight decisions baked in** (so the implementer doesn't block on questions): toast-mitigation = option A (global), discriminator = inline `BaseResponse` check, template predicate = strict schema-equality, escape hatch = revert to hook-level `try/catch SwaggerException` if Task 5's byte-equality check fails.

Pipeline note acknowledged — skipping the execution-choice prompt.