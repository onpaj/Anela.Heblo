Plan saved to `docs/superpowers/plans/2026-06-02-relocate-picking-dtos-to-application.md`.

**Summary:**
- 10 tasks (Task 0 baseline + Tasks 1–9 execution + commit).
- 3 files created in `Application/Features/Logistics/Picking/`, 3 deleted from `Domain/Features/Logistics/Picking/`, empty Domain folder removed, 11 consumer `using` directives flipped (4 Application, 2 Adapter, 5 test files — the full count from the arch review, not the brief's incorrect "single reference" claim).
- `PrintPickingListRequest.cs` gets an added `using Anela.Heblo.Domain.Features.Logistics;` plus four `Logistics.Carriers.X` → `Carriers.X` rewrites to resolve the `Carriers` enum from its new namespace context — the only non-trivial detail in the move.
- Every code block is exact (verbatim file contents), every command has an expected output, and the final task includes a per-FR/NFR acceptance check matched to the spec.

Per the pipeline note, skipping the execution-choice prompt.