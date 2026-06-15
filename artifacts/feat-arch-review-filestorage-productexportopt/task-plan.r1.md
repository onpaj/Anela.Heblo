Plan saved to `docs/superpowers/plans/2026-06-14-verify-and-document-productexportoptions-ownership.md`.

The plan implements all four FRs as five tasks:
- **Task 1** — verification greps confirming the baseline holds (no commit)
- **Task 2** — write the xUnit guard test, prove it passes against current code AND fails closed when the binding is removed (commit)
- **Task 3** — write the decision memo cross-referencing both prior plans and the sister `repository-di-in-feature-module.md` record (commit)
- **Task 4** — write the per-branch resolution artifact (commit)
- **Task 5** — full validation gate + PR-description one-liner (no commit)

Key corrections folded in during planning:
- The arch-review's code sketch called `AddCatalogModule(config, env)` — the actual signature is `AddCatalogModule(services, configuration)` only. The test code uses the real two-parameter form.
- Test uses pure xUnit assertions (no FluentAssertions) to match `FileStorageModuleTests` exactly, per arch-review Decision 4.
- Includes a one-shot "delete the line, re-run, restore" step so the engineer empirically verifies the test fails closed — not just that it passes today.