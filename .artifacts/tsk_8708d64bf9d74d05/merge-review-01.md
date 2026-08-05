# Merge review — PR #3851

**Decision: REJECT** (unattended merge)

## What the PR claims to be

Title and body describe a single backend correctness fix: `PurchaseOrderNumberGenerator`
was reading two different clocks (`orderDate` for the date part, `DateTime.Now` for the
time part) at minute resolution, colliding with the `UNIQUE` index on
`PurchaseOrder.OrderNumber`. "Closes #3848." The prior-step artifacts
(`architecture-01.md`, `review-01.md`) review only that fix.

## The core fix itself is good

The intended change is small, correct, and well-executed (Option A from the architecture
review):

- `PurchaseOrderNumberGenerator` (Domain) is now a pure, dependency-free formatter
  `GenerateCandidate(orderDate, now, attempt)` — no clock read, no repository, no `async`.
- `CreatePurchaseOrderHandler` (Application) reads one `TimeProvider.GetUtcNow()`, runs a
  bounded 5-attempt retry against the previously-dead `OrderNumberExistsAsync`, and returns
  a typed `ErrorCodes.PurchaseOrderNumberGenerationFailed` (1109 / Conflict) on exhaustion
  instead of letting a `DbUpdateException` surface as a 500.
- `ErrorCodes` 1109 is the correct next free slot after 1108; i18n entry added.
- Format `PO{yyyyMMdd}-{HHmmss}[-{attempt}]` stays within `OrderNumberMaxLength = 50`; no
  schema change; explicit-`OrderNumber` path untouched. Test coverage matches the FRs.

If this were the whole diff, it would clear the bar.

## Why it is rejected anyway — scope contamination

`origin/main` is at `0a5ebe8b`, which is **exactly the merge-base** with this branch. The
PR therefore does not merge ~130 lines of PO-fix code; it merges all 49 commits on the
branch — **5127 additions across 121 files**. Only ~6 commits are the PO fix. The other
~43 commits introduce an entirely unrelated `test-health` routine:

- executable shell scripts: `test-health-digest.sh` (627 lines), `gh-api.sh`,
  `rp-query.sh` and their `.test.sh` companions;
- a harness installer and wiring: `docs/routines/test-health/harness/install.sh`,
  `test-health.agent.json`, `test-health.process.json`;
- 100+ ReportPortal JSON fixtures under `docs/routines/test-health/fixtures/`;
- a 325-line README, a 1594-line plan, and a 323-line design spec.

This content is:
1. **Unmentioned** — the PR title/body describe only the PO fix; nothing about test-health.
2. **Unrelated to the issue** — #3848 is purely the PO number-generator bug.
3. **Unreviewed by this pipeline** — neither `architecture-01.md` nor `review-01.md`
   examines any of it; the review chain only validated the PO fix.
4. **Not inert** — it adds executable tooling and harness install configs, which carry
   their own blast radius that no reviewer in this chain assessed.

The cause is a branch-hygiene failure: the fix branch was cut from a test-health branch
rather than from `main`, so the test-health work rides along in the diff.

## Verdict rationale

The review mandate is explicit: the change must do what its PR and issue say "no more, no
less — unrelated scope is a reason to withhold, even when the code is good," and "an
unreviewable diff is a rejection." ~4900 lines of unrelated, in-this-context-unreviewed
infrastructure would land on the default branch under a bugfix title. That is a wrong
merge waiting to happen; a human needs to split the PO fix out (or rebase the branch onto
`main`) so the PR contains only its intended change.

```json
{"outcome": "reject", "summary": "The PurchaseOrderNumberGenerator fix itself is clean, correctly scoped to #3848, and verified — but origin/main sits at the merge-base, so this PR would also merge ~4900 lines of an unrelated, unmentioned, and (in this review chain) unreviewed test-health routine (shell scripts, harness install configs, 100+ fixtures, plan/spec docs) into main. Unrelated scope plus unreviewed executable tooling is a reject; a human must split the fix from the test-health work or rebase onto main."}
```
