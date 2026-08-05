# Review — InvoiceClassification: rule reorder collides with the unique `Order` index

## Verdict: done

## What was checked

Read plan-01.md, design-01.md, architecture-01.md, development-01.md, then independently
re-derived and verified the diff (`git show 8afe9912`) against the original ticket, the
architecture step's corrected design, and the live codebase — not just against the prior
steps' self-reports.

### Conformance to spec / architecture

- The committed `ReorderRulesAsync` in
  `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleRepository.cs`
  matches architecture-01.md's corrected code block verbatim: no `BeginTransactionAsync`/
  `UseTransaction`, two independent `SaveChangesAsync()` calls (disjoint negative-offset phase,
  then redistributed-final-values phase). Confirmed `./scripts/check-no-managed-tx.sh` passes.
- Confirmed the CI guard's rationale directly against `PersistenceModule.cs` and
  `.github/workflows/ci-feature-branch.yml:75-76` — the architecture step's claim that an
  explicit transaction would fail the "Guard against managed transactions" CI step is accurate,
  and the fix correctly avoids it.
- Traced the algorithm by hand against all three new test scenarios (derangement, inactive-row
  exclusion, unknown-id) and the arithmetic checks out exactly as asserted (e.g. redistributing
  the active rows' own value set `{1,3,4,5}` rather than renumbering to `1..4`, so the untouched
  inactive row at `Order=2` is never touched).
- FR-6 (contract unchanged): confirmed via `git diff main --stat` / `git log main..HEAD` — no
  controller, handler, request/response DTO, or frontend file touched.
- FR-5 (other repository members untouched): confirmed by reading the full file; only
  `ReorderRulesAsync`'s body changed.

### Test coverage

- New test file `ClassificationRuleRepositoryReorderIntegrationTests.cs` mirrors the existing
  `GridLayoutRepositoryUpsertIntegrationTests.cs` pattern (shared Postgres testcontainer fixture,
  manual raw-SQL table bootstrap, `[Trait("Category", "Integration")]`) — consistent with
  established repo convention.
- Ran the tests myself rather than trusting the development step's report:
  - `dotnet build Anela.Heblo.sln` — 0 errors.
  - `dotnet test ... --filter "FullyQualifiedName~InvoiceClassification&Category!=Integration"` —
    **94/94 passed**, including the pre-existing `ClassificationRuleRepositoryTests` (`GetMaxOrderAsync`
    tests, confirming FR-5 with no regression).
  - `dotnet test ... --filter "FullyQualifiedName~ClassificationRuleRepositoryReorderIntegrationTests"`
    against a real Docker `postgres:16` container — **3/3 passed**
    (`ReorderRulesAsync_FullDerangement_PersistsRequestedSequenceWithoutThrowing`,
    `ReorderRulesAsync_WhenAnInactiveRowHoldsAnIntermediateOrderValue_NeverCollidesWithIt`,
    `ReorderRulesAsync_WithAnUnknownRuleId_SkipsItAndReordersTheRest`). This is exactly the class
    of coverage the ticket asked for — a real unique-index-enforcing provider exercising a genuine
    permutation, not the InMemory provider that missed the bug originally.
- This directly satisfies the ticket's explicit ask ("add a repository test that exercises a
  genuine permutation against a provider that enforces the unique index. Do not treat the
  InMemory green test as coverage").

### Correctness

- No logic errors found. The two-phase negative-offset approach is sound: `Order` is always ≥ 1
  for real rows (verified: `CreateClassificationRuleHandler` always assigns
  `GetMaxOrderAsync() + 1`, and there's no other write path to `Order` besides `SetOrder`), so the
  negative sentinel range can never collide, and redistributing the touched rows' own existing
  value set (rather than renumbering to `1..N`) preserves the table-wide uniqueness invariant
  against rows outside `ruleIds` (e.g. deactivated rows) — fixing both the ticket's literal defect
  and the additional latent defect found during planning.
- FR-4's relaxation (no atomicity guarantee across the two `SaveChangesAsync` calls, replaced with
  an accepted self-healing residual-risk note) is justified and consistent with a real,
  documented, CI-enforced repo constraint (`PollyExecutionStrategy` retry contract) — not an
  unexplained scope cut.

## Non-blocking observations (not requesting changes)

- Both the new integration test and the existing `GridLayoutRepositoryUpsertIntegrationTests` are
  tagged `Category=Integration`, which both `ci-feature-branch.yml` and `ci-main-branch.yml`
  explicitly exclude from the automated test filter (`Category!=Integration`). So this new
  coverage — like its precedent — only runs when a developer has Docker available locally, not in
  CI. This is an existing repository-wide pattern, not something this change introduces or should
  be expected to fix.
- `ClassificationRuleRepository.cs` ends without a trailing newline after this change (pre-existing
  in the original file, not introduced by the diff's content) — cosmetic only.

## Conclusion

Implementation meets every functional requirement from plan-01.md as corrected by
architecture-01.md, matches the proposed architecture exactly, includes the exact test coverage
the ticket demanded, and I independently verified build + full test suite (94 unit + 3 new
Postgres-backed integration tests) green. No correctness bugs found.

```json
{"outcome": "done", "summary": "Verified the implementation against plan/design/architecture and the live codebase: ReorderRulesAsync's two-phase negative-offset redistribution matches architecture-01.md exactly (no banned explicit transaction), check-no-managed-tx.sh passes, and all FRs (contract unchanged, other repo members untouched, unique-index-safe redistribution) hold up under manual trace. Independently ran the full verification myself rather than trusting the prior step's report: dotnet build succeeds with 0 errors, 94/94 existing InvoiceClassification tests pass, and the 3 new Postgres-testcontainer integration tests (derangement, inactive-row exclusion, unknown-id tolerance) pass against a real postgres:16 container, directly satisfying the ticket's explicit demand for unique-index-enforcing coverage. No correctness bugs found; only non-blocking observations noted (Integration-tagged tests are excluded from CI by existing repo-wide convention, and a pre-existing missing trailing newline)."}
```
