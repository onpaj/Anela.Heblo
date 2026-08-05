# Architecture review — InvoiceClassification: rule reorder collides with the unique `Order` index

## Verdict

The plan's diagnosis and value-redistribution algorithm (two-phase update through a disjoint
negative offset range, done in code review during this step) are correct and should proceed
unchanged. **One load-bearing part of the design must change before implementation**: the
explicit `_context.Database.BeginTransactionAsync()` / `CommitAsync()` wrapper proposed in
design-01.md is forbidden by this codebase's own CI guard and will fail the build. This was
flagged as an open question in plan-01.md ("no other repository wraps multiple `SaveChangesAsync`
calls in an explicit transaction... flagging in case the architecture step has a house
convention") — there is in fact a hard, automated convention, just not one a repo-wide grep for
usage would surface (it's enforced by a *different* grep, over the code that would introduce it).

## Alignment check against codebase invariants

Verified directly against current source in this worktree (not from the artifacts alone):

1. **Entity / config / migration match the plan's description exactly.**
   `ClassificationRuleConfiguration.cs:59-60` — `HasIndex(x => x.Order).IsUnique()`, no
   `.HasFilter(...)` (i.e. not a partial index scoped to `IsActive`, unlike the precedent in
   `memory/gotchas/postgres-partial-index-active-states.md` for a different table) — confirms
   the plan's central claim that `Order` uniqueness spans active *and* inactive rows table-wide.
   `ClassificationRule.cs` has `SetOrder(int)`, `Order` is a plain `int` with no domain-level
   floor — negative temporary values are safe both at the DB (`integer NOT NULL`, no check
   constraint on sign) and domain level.
   `ClassificationRuleRepository.cs:65-81` (current) matches the ticket's quoted code verbatim.

2. **The proposed algorithm (redistribute the touched rows' own existing values via a disjoint
   negative-offset phase, then final values, two `SaveChangesAsync` calls) is sound and is the
   right fix.** A single set-based `UPDATE ... FROM (VALUES ...)` would *not* have worked either,
   for a stronger reason than the plan gave (style/idiom): PostgreSQL checks a non-deferrable
   unique index per row as it is written into the index during statement execution, not once at
   the end of the statement — a multi-row swap inside one `UPDATE` still hits the same transient
   collision. Two physically separate statement batches (i.e. two `SaveChangesAsync` round trips)
   are necessary; this part of the design should not change.

3. **The explicit-transaction wrapper conflicts with a deliberate, CI-enforced repo invariant.**
   `PersistenceModule.cs:99-119` wires `ApplicationDbContext` (for real Postgres, i.e. everywhere
   except tests using `useInMemory`) with a custom `PollyExecutionStrategy`
   (`Infrastructure/Resilience/PollyExecutionStrategy.cs`, `RetriesOnFailure => true`) that retries
   transient faults by replaying the operation via a Polly pipeline. EF Core's execution-strategy
   contract disallows caller-owned (`BeginTransaction`/`UseTransaction`) transactions under a
   retrying strategy — a partial retry inside a stale user transaction is unsafe. This codebase
   enforces that with `scripts/check-no-managed-tx.sh`, wired into
   `.github/workflows/ci-feature-branch.yml:75-76` ("Guard against managed transactions") *before*
   the build step, which greps `backend/src/**/*.cs` for `BeginTransaction|UseTransaction` and
   fails the pipeline on any match — the script's own comment states the rationale explicitly:
   *"The PollyExecutionStrategy retries an EF Core operation by replaying it; a caller-owned
   transaction would silently break that contract... SaveChangesAsync's implicit transaction is
   safe."* This was introduced deliberately in the Npgsql resilience feature
   (`docs/superpowers/impl/npgsql-resilience.r1.md`), not an oversight to route around.
   design-01.md's code block (lines 52, 66 — `_context.Database.BeginTransactionAsync()` /
   `transaction.CommitAsync()`) will trip this guard verbatim. The guard is a textual grep, not a
   semantic check — wrapping the same call in `CreateExecutionStrategy().ExecuteAsync(...)` (the
   textbook EF-correct way to use a caller-owned transaction under a retrying strategy) would
   *still* fail it, and more importantly is still against this codebase's stated policy: every
   retryable write unit must be a single `SaveChangesAsync()` call, full stop.

4. **The new integration test as designed would not have caught #3.** The test's own
   `ApplicationDbContext` (mirroring `GridLayoutRepositoryUpsertIntegrationTests.cs:53-56`) is
   built via plain `new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connectionString)`
   with no `PollyExecutionStrategy` wired in — so it would not throw
   `InvalidOperationException` even with the explicit transaction left in. This isn't a flaw to
   fix in the test (wiring the resilience pipeline into a repository test is out of scope and not
   what this test is for); it just means the CI guard, not the new test, is what would have caught
   this — worth knowing so the fix isn't declared "verified by the new integration test" when
   actually `./scripts/check-no-managed-tx.sh` is the control that matters here.

## Required design change

Drop the explicit transaction. Keep everything else in design-01.md's algorithm as-is:

```csharp
public async Task ReorderRulesAsync(List<Guid> ruleIds)
{
    var rules = await _context.ClassificationRules
        .Where(r => ruleIds.Contains(r.Id))
        .ToListAsync();

    var orderedRules = ruleIds
        .Select(id => rules.FirstOrDefault(r => r.Id == id))
        .Where(r => r != null)
        .Select(r => r!)
        .ToList();

    if (orderedRules.Count == 0)
    {
        return;
    }

    var valuesToRedistribute = orderedRules
        .Select(r => r.Order)
        .OrderBy(o => o)
        .ToList();

    // Phase 1: disjoint negative range — never collides with any existing row,
    // active or inactive (Order is always >= 1 for real rows).
    for (int i = 0; i < orderedRules.Count; i++)
    {
        orderedRules[i].SetOrder(-(i + 1));
    }
    await _context.SaveChangesAsync();

    // Phase 2: final values, redistributed across the caller's requested sequence.
    for (int i = 0; i < orderedRules.Count; i++)
    {
        orderedRules[i].SetOrder(valuesToRedistribute[i]);
    }
    await _context.SaveChangesAsync();
}
```

No `BeginTransactionAsync`, no `IDbContextTransaction`, no `await using var transaction`. Each
`SaveChangesAsync()` call keeps EF Core's own implicit per-call transaction, which is exactly what
the guard's comment calls out as safe and what the resilience strategy is built to retry as one
unit.

### FR-4 must be rewritten, not just re-implemented

Plan's FR-4 ("If the process fails between the temporary-offset phase and the final phase, no row
is left holding a temporary/offset value... the two phases run inside one explicit transaction")
is no longer achievable and must be replaced with an explicitly accepted residual risk, not
silently dropped:

- **New FR-4**: If the process crashes/restarts between phase 1's `SaveChangesAsync()` and phase
  2's, the rows touched by that call are left holding negative sentinel `Order` values. This is
  bounded and self-healing:
  - It never violates the unique index (negatives are always free — no real row ever holds one).
  - It's a cosmetic ordering artifact only (`GetAllAsync`/`GetActiveRulesOrderedAsync` still sort
    correctly by `Order`; negative rows just sort first), not data loss or corruption.
  - The next successful `ReorderRulesAsync` call that includes any of those rows moves them
    through the same two-phase dance and lands them on real values again.
  - The window is a single in-process gap between two round trips with no I/O or branching in
    between — already an accepted class of risk in this codebase, which has no optimistic
    concurrency tokens and no other cross-call atomicity anywhere in this repository (plan's own
    "Explicitly out of scope" section already accepts equivalent last-writer-wins gaps elsewhere).
  - Acceptance: code review confirms no `BeginTransaction`/`UseTransaction` call was introduced
    (`./scripts/check-no-managed-tx.sh` passes); this replaces the prior "transaction wrapping
    present in the diff" acceptance criterion.

Everything else in plan-01.md/design-01.md — FR-1, FR-2, FR-3, FR-5, FR-6, the data model
section, the three integration test cases, the manual-table-bootstrap approach mirroring
`GridLayoutRepositoryUpsertIntegrationTests.cs` — is unaffected by this change and should proceed
as designed. The test file's structure, fixture usage, and three test cases (derangement,
inactive-row non-collision, unknown-id tolerance) are all still exactly what's needed to prove FR-1/FR-2/FR-3 against the real Postgres unique index; nothing there depends on the transaction mechanics being removed.

## Risks and mitigations

- **Risk**: implementer copies design-01.md's code block verbatim (with the transaction) and CI
  fails on the managed-transaction guard. *Mitigation*: this document supersedes that one code
  block; the corrected snippet above is what should be committed. Run
  `./scripts/check-no-managed-tx.sh` locally before considering the change complete, per this
  repo's existing CI step.
- **Risk**: reviewer sees FR-4 relaxed and reads it as a regression. *Mitigation*: the residual
  risk is narrower than it sounds (unique-index-safe, self-healing, cosmetic-only, and strictly
  smaller in scope than the *current* production bug, which fails on essentially every reorder,
  not just ones that race a process crash) — call this out explicitly in the PR description so
  it isn't mistaken for an oversight.
- **Risk** (pre-existing, not introduced by this fix): two concurrent reorder requests touching
  overlapping rule sets could interleave their phase-1/phase-2 writes and produce a duplicate or
  incorrect final ordering (no unique-index violation, since each request's phase 1 still uses its
  own disjoint negative range, but final values could still end up misassigned if two requests
  race on the same rows). This is the same last-writer-wins characteristic already accepted
  everywhere else in this repository (plan's own scope notes this) and does not need a fix here —
  noting only so it isn't rediscovered as a new defect later.

## Prerequisites before implementation begins

None outside this document — no schema change, no new package, no DI change. The only action item
is applying the transaction removal above to the code the implementation step writes; the rest of
plan-01.md/design-01.md stands.

```json
{"outcome": "done", "summary": "Verified design against codebase invariants: the two-phase negative-offset redistribution algorithm is correct, but design-01.md's explicit context.Database.BeginTransactionAsync()/CommitAsync() wrapper is banned by this repo's CI guard (scripts/check-no-managed-tx.sh, wired into ci-feature-branch.yml) because ApplicationDbContext uses a custom retrying PollyExecutionStrategy that is incompatible with caller-owned transactions. Wrote architecture-01.md instructing implementation to drop the explicit transaction, keep the two independent SaveChangesAsync calls (each safe under the retry strategy via its own implicit transaction), and rewrite FR-4 as an accepted, self-healing residual risk instead of a full-atomicity guarantee. All other FRs/tests from plan-01.md and design-01.md are unaffected. Also saved a repo memory gotcha documenting this invariant for future work."}
```
