Plan saved to `docs/superpowers/plans/2026-06-02-invoices-frontend-hooks-types-refactor.md`.

The plan decomposes the type-only refactor into 9 tasks (0–8), each with file paths, the exact code changes, the verification commands, and the expected output:

- **Task 0** — baseline build/lint/test verification on a clean tree.
- **Tasks 1–2** — refactor `useAsyncInvoiceImport.ts` first (simplest, all four local types deletable), then immediately repair `useAsyncInvoiceImport.test.ts` (the only file whose imports break in isolation: the deleted `BackgroundJobInfo` name and the string `createdAt` mock).
- **Task 3** — refactor `useIssuedInvoices.ts`, keeping only `IssuedInvoicesFilters` and importing only the two generated response types actually used.
- **Task 4** — the bulk of the consumer work in `IssuedInvoiceDetailModal.tsx`: removes the contract-orphan UI sections (customer email/phone/address, items, sync.error.code/field), repairs both `errorType` numeric-vs-string-enum branches, and trims the now-unused lucide-react icons.
- **Task 5** — single-site narrowing fix in `IssuedInvoicesPage.tsx` for `runningJobs.map(j => j.id).filter(...)` → `string[]`.
- **Task 6** — explicit verify-only checkpoint for the two consumers that should not need edits (`InvoiceImportJobTracker.tsx`, `InvoiceImportRunningIndicator.tsx`).
- **Task 7** — full validation: build, lint, full Jest suite, and grep-based spec-acceptance checks (one remaining `export interface`, no `IssuedInvoiceItemDto`/`ImportResultDto`/`IssuedInvoiceSyncHistoryDto` references anywhere, no `as any` field laundering).
- **Task 8** — PR write-up template that flags every user-visible UI removal.

The self-review section maps each task back to the spec FRs, the arch-review's five spec amendments, and the four design decisions.