Plan saved to `docs/superpowers/plans/2026-06-11-remove-dead-issuedinvoicerepository-find-methods.md` and mirrored to `artifacts/feat-arch-review-invoices-iissuedinvoicerepos/plan.r1.md`.

Three tasks, all bite-sized:
1. **Delete obsolete tests + orphan `SetLastSyncTime` helper** — 12 steps, intermediate compile state stays green because production code still satisfies the interface.
2. **Delete interface declarations + EF Core implementations atomically** — 6 steps, must land together to avoid `CS0535`; gates with build, `dotnet format --verify-no-changes`, full test suite.
3. **Final verification + single commit** — 5 steps, enforces the surgical-diff requirement (exactly three files modified) and asserts test count drops by exactly 7.