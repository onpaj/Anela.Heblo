# Implementation: code-review-fixes (round 2)

## What was reviewed
`code-review.r2.md` reported one Blocking finding: the r1 detach-on-failure fix only guards the `isNew == true` path. For an *existing* (re-imported) invoice, `GetByIdAsync` returns a tracked entity, `_mapper.Map(invoiceDetail, invoice)` mutates it directly, and if a later transformation (or another step) throws before this invoice's own `SaveChangesAsync`, the mutated tracked entity is never reverted — EF's automatic change detection can pick it up and silently persist it on a *later* invoice's `SaveChangesAsync` call in the same `AddScoped` batch.

## Verification
Independently confirmed via `git diff origin/main...HEAD -- backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`: the `_mapper.Map(invoiceDetail, invoice)` refresh call and the transformation loop are **completely unchanged** by this PR's diff (no `+`/`-` on those lines, pure unchanged context). This confirms the finding describes a bug in the *existing-invoice* code path that is byte-for-byte identical before and after this fix — it is not a regression introduced or worsened by this change. It is pre-existing latent behavior in code this PR does not touch.

Also confirmed: `IssuedInvoiceRepository.GetByIdAsync` (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:22-26`) indeed returns a tracked entity (no `.AsNoTracking()`), supporting the review's technical claim.

## Decision: out of scope for this PR
This feature's spec (`spec.r1.md`) explicitly scopes this fix to the new-invoice double-save problem:
- **FR-4**: "No behavior change to existing (re-import) invoices" — the existing-invoice code path must remain unchanged.
- **Out of Scope**: "Any change to the per-invoice transactional/error-isolation boundary in `ImportInvoicesAsync`."

Fixing the existing-invoice tracked-entity leak would require changing that exact code path and its error-isolation semantics — directly conflicting with FR-4 and the spec's Out of Scope section. It is also architecturally distinct from this fix's scope (would need e.g. `EntityEntry.State` manipulation or a reload, a broader look at the repository abstraction boundary, and its own dedicated test coverage) — expanding this narrow, surgical bugfix PR to also rewrite the existing-invoice error-handling model would be a materially larger and riskier change than what issue #3566 asked for.

**Action taken:** filed a separate follow-up issue, **#3575**, documenting the finding in full (including the suggested fix and suggested test from the reviewer) so it is tracked and not lost, without folding it into this PR.

## Status
DONE_WITH_CONCERNS — one Blocking finding from round 2 is confirmed real but deliberately not fixed in this PR (out of scope, pre-existing, not a regression); tracked as follow-up issue #3575 instead.
