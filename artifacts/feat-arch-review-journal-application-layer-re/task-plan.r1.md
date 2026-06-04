Now let me run the self-review checklist from the writing-plans skill.

**1. Spec coverage check:**
- FR-1 (add bindings to PersistenceModule) → Task 1 Steps 1–3 ✓
- FR-2 (remove bindings + persistence using from JournalModule) → Task 2 Steps 1–3 ✓ (also handles arch review amendment #1 about removing all unused usings)
- FR-3 (preserve AddJournalModule shell, ApplicationModule call site, no warnings) → Task 2 Step 1 (file content), Step 4 (warnings), Task 3 Step 4 (ApplicationModule not in diff) ✓
- FR-4 (no behavioral change, all tests pass) → Task 3 Steps 2–3 ✓
- NFR-4 (build + format clean) → Task 2 Steps 4–5, Task 3 Step 1 ✓
- Arch review amendment #1 (prune all unused usings) → Task 2 Step 1 + Step 4 IDE0005 check ✓
- Arch review amendment #2 (append at end, not alphabetize) → Task 1 Step 3 (insert after Packaging block, before `return services;`) ✓

**2. Placeholder scan:** No TBD/TODO/"implement later"; all commands are exact; all code blocks show the full content. ✓

**3. Type consistency:** `IJournalRepository`/`JournalRepository` and `IJournalTagRepository`/`JournalTagRepository` used consistently throughout. The two using directives match the namespaces referenced in the arch review and the actual source files I read. ✓

Plan saved to `artifacts/feat-arch-review-journal-application-layer-re/plan.r1.md`.

**Summary:** The implementation plan breaks the refactor into 4 tasks with pre-flight verification:
- **Pre-flight** — confirm branch, clean tree, baseline build/test green
- **Task 1** — add 2 `using` directives and a `// Journal repositories` block (3 lines) at the end of `PersistenceModule.AddPersistenceServices`, ordered after the Packaging block per the arch review's chronological-not-alphabetical convention
- **Task 2** — replace `JournalModule.cs` with a 14-line no-op shell, pruning all three now-unused `using` directives (Persistence + Contracts + Domain) to satisfy FR-3's zero-warnings requirement
- **Task 3** — full build, Journal-filtered tests, full backend test run, and a `git diff` sanity check
- **Task 4** — stage exactly the two changed files and commit with a `refactor:` conventional message

Per the pipeline note, skipping the execution choice prompt — the plan file content is the artifact.