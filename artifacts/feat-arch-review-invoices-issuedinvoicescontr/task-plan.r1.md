Plan saved to `docs/superpowers/plans/2026-06-02-consolidate-issued-invoices-controller.md`.

**Summary:** 8 tasks, ~30 bite-sized steps, TDD-discipline preserved (test pin first, then deletion, then frontend repointing, then regeneration, then smoke + PR). Key correctness moves:

- Task 1 pins the empty-id `ValidationError` in `GetIssuedInvoiceDetailHandler` before the duplicate controller-side guard goes away.
- Task 3 step 2 enforces the arch-review correction the spec missed: the detail URL must append `?withDetails=true`, otherwise the modal silently loses its sync-history section.
- Task 4 catches the double-rename (`/sync-stats` → `/stats` **and** controller prefix change) in one short URL.
- Task 5 regenerates `api-client.ts` from a known-clean backend build, with a grep gate ensuring no `issuedInvoices_*` survivors.
- Task 7 is a required manual smoke test against a running dev stack — automated tests don't cover the modal/hook composition.
- Task 8 closes with the full validation gate (`dotnet build`/`format`/`test` + `npm run build`/`lint`), the four-commit history check, and a PR body that surfaces the two out-of-scope follow-ups (anonymous controllers, `(apiClient as any)` anti-pattern) flagged in the arch review.