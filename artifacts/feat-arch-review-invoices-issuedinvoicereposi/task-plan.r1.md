Plan saved to `artifacts/feat-arch-review-invoices-issuedinvoicereposi/plan.r1.md`.

**Summary of the plan and key decisions:**

The plan diverges from the arch review on one critical point. The arch review proposed moving `IIssuedInvoiceRepository` and `IssuedInvoiceFilters` into `Anela.Heblo.Domain`, but that placement is **blocked by an existing architecture test** (`ModuleBoundariesTests.cs:692-746`, added by PR #2329 "Relocate IssuedInvoiceFilters and PaginatedResult<T> out of Domain"). The codebase team explicitly removed these types from Domain in the past and added a guard test to keep them out.

**Adjusted placement:**
- `PaginatedResult<T>` → `Anela.Heblo.Xcc.Persistance` (matches arch review)
- `IIssuedInvoiceRepository` → `Anela.Heblo.Persistence.Invoices` (co-located with implementation, NOT Domain)
- `IssuedInvoiceFilters` → `Anela.Heblo.Persistence.Invoices` (travels with interface)
- `IssuedInvoiceRepository` (impl) → `Anela.Heblo.Persistence.Invoices`

**Also dropped:** spec FR-3 (removing `Application → Persistence` reference) — that direction is load-bearing for the `InvoicesModule.cs` DI wiring per ADR-004.

**Plan structure:** 6 tasks (Task 1: add PaginatedResult to Xcc; Task 2: switch consumers and delete old PaginatedResult; Task 3: move interface; Task 4: move filters; Task 5: move implementation; Task 6: final verification + PR). Each task uses exact file paths, complete code blocks, exact commands with expected output, and frequent commits. Tasks 3 and 4 are committed together because the build is temporarily broken between them (the relocated interface references the filter, which only resolves after Task 4).