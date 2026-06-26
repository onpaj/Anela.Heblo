Plan saved to `docs/superpowers/plans/2026-06-03-remove-notimplemented-manufacture-protocol-renderer.md`.

**Summary:**
- 5 tasks, all TDD-ordered around the existing regression gate (`CompositionRootTests`):
  1. **Baseline verification** — build green, `CompositionRootTests` green, handler tests green, grep confirms only 2 references exist.
  2. **Edit `ManufactureModule.cs`** — exact before/after blocks, drop the now-unused `using` only if no other symbol references it, recompile Application project.
  3. **Delete `NotImplementedManufactureProtocolRenderer.cs`** — `rm`, regrep for zero remaining matches, full solution build.
  4. **Validate via existing gates** — `CompositionRootTests` (primary FR-4 gate per Amendment 1), Manufacture suite, module-boundaries, full suite, `dotnet format --verify-no-changes`.
  5. **Commit** — staged-diff sanity check enforces NFR-4's two-file blast radius, conventional commit message, final grep.

Per the pipeline note, skipping the execution handoff prompt — the plan file is the artifact.