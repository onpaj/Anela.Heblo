Plan saved to `docs/superpowers/plans/2026-06-03-decouple-invoicedqtcomparer-from-invoices.md`.

**Summary:**
- 9 sequenced tasks, each with TDD-style steps, exact file paths, complete code, and explicit commit boundaries.
- Tasks 1–4 add the two consumer contracts and two provider adapters (additive, low-risk).
- Task 5 wires DI in `InvoicesModule.cs` with **mirrored lifetimes** (Singleton for Shoptet, Scoped for Flexi) per arch-review Amendment 2.
- Task 6 deliberately breaks the test build by retargeting mocks before swapping the production type — TDD red.
- Task 7 swaps `InvoiceDqtComparer`'s constructor, turning the build green; tests rerun unchanged.
- Task 8 adds the `DataQuality → Invoices` `ModuleBoundariesTests` rule plus a pre-populated allowlist for the five expected shared-DTO leaks.
- Task 9 is the full validation gate: build, format, all tests, plus an API startup smoke check to catch captive-dependency regressions.

Self-review confirms every spec/arch-review requirement maps to a task, no placeholders, and type/name/lifetime references are consistent across tasks.