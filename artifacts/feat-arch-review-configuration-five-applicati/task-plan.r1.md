Plan saved to `artifacts/feat-arch-review-configuration-five-applicati/plan.r1.md`.

**Summary of the plan:**

- **9 tasks** covering pre-flight verification, five per-file edits, whole-solution verification, test run, and a single commit at the end.
- **Per-file edit pattern (Tasks 2–6):** insert `using Anela.Heblo.Domain.Features.Configuration;` in alphabetical position + substitute the literal `"BypassJwtValidation"` for `ConfigurationConstants.BYPASS_JWT_VALIDATION`, preserving the positional `, false` argument style (no normalization to `defaultValue: false`).
- **Verification (Task 7):** scoped grep matching the exact `GetValue<bool>("BypassJwtValidation"` pattern over `backend/src/**/*.cs` (per arch-review Amendment 1), plus `dotnet build`, format check, and per-file diff stat sanity.
- **Single commit (Task 9):** per the arch review's risk-mitigation guidance — all five edits land together.
- **Explicit scope boundaries** at the top capture every "do not" from the spec and arch review, including the `UseMockAuth` parallel issue staying out of scope, no `using` reordering, no `dotnet format` solution-wide rewrite, and no test changes.