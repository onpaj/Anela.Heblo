---
name: no-managed-transactions-polly-execution-strategy
description: BeginTransactionAsync/UseTransaction in backend/src is CI-blocked because PollyExecutionStrategy retries whole SaveChangesAsync calls, not partial transactions
metadata:
  type: gotcha
---

`backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` wires a custom `IExecutionStrategy`
(`PollyExecutionStrategy`, `RetriesOnFailure => true`) onto `ApplicationDbContext`'s Npgsql
connection for transient-fault retry. EF Core's execution-strategy contract requires that any
explicit, caller-owned transaction (`context.Database.BeginTransactionAsync()` /
`UseTransaction(...)`) be replayable as a whole by the strategy — a hand-rolled transaction
spanning multiple `SaveChangesAsync()` calls breaks that contract (stale `NpgsqlTransaction` on
retry). `scripts/check-no-managed-tx.sh` enforces this with a blunt textual grep for
`BeginTransaction|UseTransaction` across `backend/src/**/*.cs`, wired into
`.github/workflows/ci-feature-branch.yml` before the build step — it fails CI on *any* match,
regardless of whether the usage is otherwise EF-correct (e.g. wrapped in
`CreateExecutionStrategy().ExecuteAsync(...)`).

**Why:** added deliberately in the Npgsql resilience feature
(`docs/superpowers/impl/npgsql-resilience.r1.md`) specifically to protect the retry contract —
not an oversight, and not something to work around by wrapping the transaction "correctly" per
EF docs.

**How to apply:** any multi-statement write that needs to avoid a transient state (e.g.
redistributing values through a disjoint temp range to dodge a non-deferrable unique index) must
rely on **two or more independent `SaveChangesAsync()` calls**, each with EF's own implicit
per-call transaction — never an explicit `BeginTransactionAsync`. Accept the residual risk that a
crash between calls leaves partially-applied state, and document why that's tolerable (usually:
no invariant is violated, and the next successful write to the same rows self-heals it) rather
than reaching for a transaction to paper over it. Caught this reviewing a reorder-algorithm design
for `[[classificationrule-reorder-unique-index]]` that had proposed exactly this forbidden pattern.
