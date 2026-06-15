Implementation plan saved to `artifacts/feat-arch-review-orgchart-controller-error-pa/task-plan.r2.md`.

The plan decomposes the fix into 6 TDD-driven tasks:

1. **Task 1 (baseline GREEN)** — Create `OrgChartControllerTests.cs` with the success-path test to establish the harness against unchanged behavior.
2. **Task 2 (RED)** — Add the typed-error envelope test; FAILS against current anonymous-object code.
3. **Task 3 (RED + log pin)** — Add the no-leak test (with a `SECRET-MARKER` sentinel in the exception message) and log-preservation test.
4. **Task 4 (GREEN)** — Swap the two lines in `OrgChartController.cs:50-51` to return `new OrgChartResponse(ErrorCodes.InternalServerError)`, with explicit guard against the `BaseResponse(Exception)` leak path. Format and commit prod + tests together.
5. **Task 5** — Rebuild frontend to regenerate the OpenAPI/TS client; commit only if the generated artifact drifted.
6. **Task 6** — Final BE + FE sweep and `git status --stat` audit to confirm only `OrgChartController.cs` was touched.

Every step includes exact file paths, exact code blocks, exact commands, and expected outcomes. The self-review table maps each FR/NFR plus all three arch-review decisions to a specific task. No placeholders.