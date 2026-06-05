Plan saved to `docs/superpowers/plans/2026-06-04-relocate-journal-persistence.md`.

**Self-review against spec — all clean:**

- **Spec coverage:** All five FRs and four NFRs map to specific steps; the arch-review amendments (integration test consumer, six-file inventory, no style change, dead "Catalog becomes empty" check) are all reflected.
- **Placeholder scan:** No TBDs, no "implement later," every code/grep step has the exact command and the exact expected output, the commit message is provided in full.
- **Type consistency:** Only namespace and folder paths change; consistent across all 16 steps in Task 2.

**Plan structure:**
- **Task 1 — Baseline** (5 steps): verify file inventory, target absent, grep matches 8 known references, build green, Journal tests pass.
- **Task 2 — Atomic refactor** (16 steps, single commit): `git mv` × 6 → namespace edit × 6 → consumer `using` swap × 2 → folder cleanup → repo-wide grep returns zero → build → Journal tests → staged-diff inspection → conventional commit. Intermediate states don't compile, so the entire refactor is one commit by design.
- **Task 3 — Final sweep** (8 steps): wider grep, folder state checks, `git log --follow` rename detection, `dotnet format --verify-no-changes`, full test suite, full build, and a tick-off checklist mapping each spec acceptance criterion to its verification step.

Critical guardrails surfaced in the plan: keep block-scoped namespaces (don't let `dotnet format` rewrite them), don't touch `Persistence/Catalog/`, don't run a separate format pass on the moved files in Task 2, and an explicit note for coordinating with issue #2513 if it has already merged the DI registration to `PersistenceModule.cs`.