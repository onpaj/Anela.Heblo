Plan complete and saved to `docs/superpowers/plans/2026-06-02-relocate-issuedinvoice-filters-and-paginatedresult.md`.

The plan covers all 17 tasks across four phases:

1. **Additive creation (Tasks 1–5):** new `PaginatedResult<T>`, `IssuedInvoiceFilters`, extracted `IssuedInvoiceSyncStats`, new `IIssuedInvoiceRepository` and `IssuedInvoiceRepository` in Application — coexisting with the Domain/Persistence originals so the build stays green at every step.
2. **Consumer migration (Tasks 6–11):** swap `using` directives one file at a time across handlers, services, the consumption-source adapter, `InvoicesModule`, and tests — with explicit `CS0104` fallback to fully-qualified names where ambiguity could arise.
3. **DI flip + cleanup (Tasks 12–14):** rewire `InvoicesModule.cs` then delete the Domain and Persistence originals (flagged that these two deletions land in the same commit because the Persistence file briefly fails to compile after the Domain file is gone).
4. **Verification + guard (Tasks 15–17):** full build/test/format gates, NFR-3 Domain-grep gate, and a new `[Fact]` in `ModuleBoundariesTests.cs` enforcing NFR-6 (no Domain→Application reference + no relocated type names anywhere under Domain).

The self-review section maps every FR/NFR/risk from `spec.r1.md` and `arch-review.r1.md` to a specific task. Per the pipeline instruction, the execution-choice prompt is skipped — the plan file is the artifact.