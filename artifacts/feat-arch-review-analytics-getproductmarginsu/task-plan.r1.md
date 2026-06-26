Plan saved.

Per the pipeline note, skipping the execution-handoff prompt.

**Plan:** `docs/superpowers/plans/2026-06-04-consolidate-margin-level-resolution-getproductmarginsummary.md`

Four tasks, all in the Analytics slice, no DI or contract changes:

1. **Add characterization test** — case-insensitive `marginLevel` resolution (locks existing semantics).
2. **Add characterization test** — unknown `marginLevel` falls back to `M2`.
3. **Refactor** `GetProductMarginSummaryHandler.CalculateTotalMarginForLevel` (lines 217–237) to delegate to `_marginCalculator.GetMarginAmountForLevel`. Includes the NFR-3 grep guard (`"M0" =>` should appear exactly once across `Features/Analytics/`, in `MarginCalculator.cs`).
4. **Final validation** — full build, full Analytics test suite, clean working tree.

Each task has explicit file paths, line numbers, complete code blocks, exact shell commands with expected output, and a discrete commit. The plan binds the refactor to FR-2's behavior-preservation guarantees by writing the tests first (against the current code) and re-running them after the refactor.