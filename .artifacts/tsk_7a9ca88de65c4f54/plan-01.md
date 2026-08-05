# Plan — InvoiceClassification: rule reorder collides with the unique `Order` index

## Summary

`ClassificationRuleRepository.ReorderRulesAsync` renumbers the reordered rows to `1..N` in place and calls `SaveChangesAsync()` once. Because `Order` carries a non-deferrable PostgreSQL unique index and EF Core emits one `UPDATE` per row (checked immediately, not at commit), any permutation that isn't the identity ordering throws `23505 unique_violation` — drag-and-drop reorder in the UI fails for essentially every real reorder. Verified during this planning step: the fix must do more than avoid the *transient* self-collision the ticket describes — it must also avoid a **second, permanent** collision that the same "renumber to `1..N`" strategy causes whenever any rule has ever been deactivated (see Context). The fix is a repository-only algorithm change (no schema/migration change) plus a new Postgres-testcontainer-backed integration test.

## Context

Verified directly in code during this planning step (no prior pipeline artifacts existed for this task — this is the first step).

**The bug as filed:**
- `ClassificationRuleConfiguration.cs:59-60` — `builder.HasIndex(x => x.Order).IsUnique()`, applied via migration `20251031101657_InvoiceClassificationFeature.cs:79-83` (`unique: true`, non-deferrable in PostgreSQL — no EF fluent API or migration sets it deferrable).
- `ClassificationRuleRepository.cs:65-81` (`ReorderRulesAsync`) fetches the rows for the given `ruleIds`, then loops `rule.SetOrder(i + 1)` for `i` in `0..ruleIds.Count-1`, then one `SaveChangesAsync()`. EF Core issues N sequential `UPDATE` statements inside that call; PostgreSQL checks the non-deferrable unique index after each statement. Any non-identity permutation (e.g. `[A=1,B=2,C=3]` → `[C,A,B]`) hits a transient duplicate on the first statement and throws.
- Reachable path: `RulesList.tsx:175-184` (`dnd-kit` drag end) → `onReorder` → `useReorderClassificationRules` → `PUT api/InvoiceClassification/rules/reorder` → `InvoiceClassificationController.cs:46-47` → `ReorderClassificationRulesHandler.cs:17` → `ReorderRulesAsync`. Rule order directly drives which rule matches an invoice first, so a broken reorder is a functional regression, not cosmetic.
- `ClassificationRuleRepositoryTests.cs` uses the EF Core InMemory provider, which does not enforce unique indexes, and has no reorder test — it gives false confidence and must not be treated as coverage (explicit in the ticket).

**Additional defect discovered while planning (same root cause, larger blast radius than the ticket's example):**
- The rule list the UI reorders is **not** the full table. `InvoiceClassificationPage.tsx:31` calls `useClassificationRules(false)` → `GetClassificationRulesRequest.IncludeInactive = false` → `GetClassificationRulesHandler.cs:20` calls `GetActiveRulesOrderedAsync()`, i.e. **only active rules**. There is no toggle to include inactive rules in this view. So `ruleIds` sent to `ReorderRulesAsync` is routinely a strict subset of all rows in `ClassificationRules`.
- `RuleForm.tsx:235-241` exposes an "Pravidlo je aktivní" (rule is active) checkbox — `UpdateClassificationRuleRequest.IsActive` — so a rule can be deactivated without being deleted. `ClassificationRuleRepository.DeleteAsync` (hard delete) is the only path that frees an `Order` slot; deactivation does not.
- `CreateClassificationRuleHandler.cs:33,40` assigns every new rule `Order = GetMaxOrderAsync() + 1`, where `GetMaxOrderAsync` (`ClassificationRuleRepository.cs:30-33`) is unfiltered by `IsActive`. So `Order` is a dense-ish, globally unique sequence across **all** rows (active and inactive), with gaps only where rows were hard-deleted.
- Consequence: once any rule earlier in the sequence has ever been deactivated, `count(active rules)` no longer matches the highest `Order` among active rows, so renumbering the active subset to `1..N` will, at some point, try to write an `Order` value permanently owned by an untouched inactive row — a **persistent** unique violation, not merely a transient one from statement ordering. A fix that only reorders the two-phase-offset way but still targets `1..N` will still be broken in this (common, one-checkbox-click-away) scenario.

**Working example:** rules with `Order` 1,2,3,4,5; the rule at `Order=2` gets deactivated via the form checkbox. Active rules now hold `{1,3,4,5}`. Any subsequent drag-reorder of the 4 active rules renumbers them to `1,2,3,4` — writing `Order=2` onto one of them, which collides permanently with the still-present inactive row. This never succeeds until that inactive row is deleted or given a different value, regardless of how the transient collision is fixed.

## Decision: permute the rows' own existing `Order` values, don't renumber to `1..N`

- Instead of assigning `1..N`, take the current `Order` values already held by exactly the rows named in `ruleIds` (a set of N real, currently-unique values — e.g. `{1,3,4,5}`), sort them ascending, and redistribute that same value set across the rows in the caller's new sequence.
- This preserves the table-wide invariant ("`Order` is unique across all rows, active or inactive") by construction: every value written was already legally owned by one of the rows being touched, so it can never collide with an untouched row (fixes the discovered defect) — and redistributing a fixed value set among the same rows is the classic derangement problem, solved with a two-phase update through a disjoint temporary range (fixes the ticket's literal defect).
- Rejected alternative: making the unique index a deferrable unique **constraint** (Postgres indexes can't be deferrable; only constraints can) — requires a schema migration. Per this repo's project facts, DB migrations are applied manually to a live Postgres instance, so a schema change carries strictly more deployment risk than a pure application-code fix for the same outcome. Not pursued unless the architecture step overrides this.
- Rejected alternative: a single set-based `UPDATE ... FROM (VALUES ...)` — technically viable in raw SQL, but this repository otherwise uses plain EF Core LINQ/`SaveChanges` throughout (`ClassificationRuleRepository.cs`), and the two-phase in-place approach reaches the same correctness guarantee without introducing raw SQL / a different persistence idiom into this class.

## Functional requirements

- **FR-1**: `ReorderRulesAsync` succeeds for any permutation of the given `ruleIds`, including a full derangement (no row ends at its original position).
  - Acceptance: new Postgres-backed integration test seeds 3 rows with `Order` 1,2,3 and calls `ReorderRulesAsync` with ids in the sequence `[C,A,B]`; the call does not throw, and the persisted rows end up in `Order` 1,2,3 matching `[C,A,B]`.
- **FR-2**: `ReorderRulesAsync` never writes an `Order` value owned by a row not included in `ruleIds`.
  - Acceptance: new integration test seeds 5 rows, deactivates (via `IsActive=false`, not delete) the row at `Order=2`, then reorders the remaining 4 active rows (ids excluding the inactive one) into a new sequence. The call succeeds; the inactive row's `Order` is unchanged at 2; the 4 reordered rows end up holding exactly `{1,3,4,5}` (redistributed, not renumbered to `1..4`) in the caller's requested sequence.
- **FR-3**: Rows named in `ruleIds` but not found in the database are silently skipped, matching current behavior (`rules.FirstOrDefault(...)` returning `null`).
  - Acceptance: existing/adjusted test calls `ReorderRulesAsync` with one id that doesn't exist mixed into an otherwise valid list; call succeeds, found rows get reordered among themselves, no exception.
- **FR-4**: If the process fails between the temporary-offset phase and the final phase, no row is left holding a temporary/offset `Order` value once the operation as a whole has completed or failed — the two phases run inside one explicit database transaction.
  - Acceptance: covered by code review / the transaction wrapping being present in the diff; not independently testable via the Postgres testcontainer without fault injection, so this is a design requirement rather than a test requirement.
- **FR-5**: All other `IClassificationRuleRepository` members (`GetAllAsync`, `GetActiveRulesOrderedAsync`, `GetMaxOrderAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`) are unchanged.
  - Acceptance: no diff outside `ReorderRulesAsync`'s body; existing `ClassificationRuleRepositoryTests.cs` (`GetMaxOrderAsync` tests) pass unmodified.
- **FR-6**: The public contract is unchanged — `IClassificationRuleRepository.ReorderRulesAsync(List<Guid>)`, `ReorderClassificationRulesRequest`/`Response`, and the `PUT api/InvoiceClassification/rules/reorder` endpoint keep their existing shapes.
  - Acceptance: no changes to `ReorderClassificationRulesHandler.cs`, `ReorderClassificationRulesRequest.cs`/`Response.cs`, `InvoiceClassificationController.cs`, or any frontend file; `dotnet build` succeeds with no generated-client diff.

## Non-functional requirements

- No schema/migration change (see Decision) — keeps this a low-risk, application-only fix given manual production migrations.
- No behavior change visible to a user beyond "reorder now works" — the final visual order after a drag-drop always matches what the user dragged to, exactly as the currently-broken code intended.
- Rule counts in this module are small (dozens, not thousands), so O(N) round-trip statements per reorder (two phases × N rows) is not a performance concern; no need to optimize to a single set-based statement.
- The fix must not weaken the existing unique index/constraint in any environment (no temporary "drop and recreate the index" trick).

## Data model

No entity, table, or migration changes. Documenting the invariant the fix must uphold (already true today, just not currently respected by `ReorderRulesAsync`):
- `ClassificationRule.Order` is unique across **every** row in `ClassificationRules`, active and inactive alike (enforced by the DB unique index; never scoped to `IsActive`).
- New rows get `Order = GetMaxOrderAsync() + 1` (table-wide max, `CreateClassificationRuleHandler.cs:40`).
- Hard delete (`DeleteAsync`) permanently frees an `Order` value; it is never reclaimed by later inserts (which always append at `max+1`).
- Deactivation (`IsActive=false` via `UpdateClassificationRuleHandler`) does **not** free the `Order` value — the row keeps its slot in the global sequence indefinitely.

## Interfaces

- `PUT api/InvoiceClassification/rules/reorder` — unchanged request/response shape (`{ ruleIds: string[] }` → `{ success: bool }`).
- `IClassificationRuleRepository.ReorderRulesAsync(List<Guid> ruleIds)` — unchanged signature; only its internal implementation in `ClassificationRuleRepository.cs` changes.
- No frontend interface changes (`RulesList.tsx`, `useReorderClassificationRules` in `useInvoiceClassification.ts` are untouched).

## Dependencies and scope

**In scope:**
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleRepository.cs` — rewrite `ReorderRulesAsync` to redistribute the touched rows' own existing `Order` values via a two-phase (disjoint temporary offset, then final) update, wrapped in one explicit DB transaction.
- New integration test file under `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/` (or `Persistence/`, matching existing conventions such as `GridLayoutRepositoryUpsertIntegrationTests.cs`), using the shared `PostgresSharedContainerFixture` (`[Collection("PostgresIntegration")]`) and manually creating just the `ClassificationRules` table + its unique `Order` index via raw SQL (mirroring the GridLayouts test — avoids the `vector`-extension issue that blocks a full `EnsureCreatedAsync`/`MigrateAsync` against a plain `postgres:16` image). Covers FR-1, FR-2, FR-3.

**Explicitly out of scope:**
- Any schema/migration change (deferrable constraint) — rejected direction, see Decision.
- Concurrent reorder from two simultaneous sessions (last-writer-wins is the existing implicit behavior for every write path in this repository; no optimistic concurrency token exists on `ClassificationRule` today, and introducing one is a larger, unrequested change).
- The pre-existing "gaps if an id in `ruleIds` isn't found" indexing quirk beyond preserving current tolerant behavior (FR-3) — not a new regression, not the subject of this ticket.
- Any frontend change — the contract and behavior from the UI's perspective is exactly "drag reorder now works," with no new props/handlers needed.
- `ClassificationRuleRepositoryTests.cs` (existing InMemory test file) — left as-is; per the ticket, InMemory coverage must not be treated as evidence for this fix, so new coverage goes into the Postgres integration test instead rather than adding a misleading InMemory "reorder" test there.

## Rough plan

1. Rewrite `ReorderRulesAsync` in `ClassificationRuleRepository.cs`:
   - Fetch rows via `Where(r => ruleIds.Contains(r.Id))` (unchanged).
   - Build the list of found rows in the sequence given by `ruleIds`, skipping ids not found (preserves FR-3).
   - Collect those rows' *current* `Order` values, sorted ascending — this is the fixed value set to redistribute.
   - Open an explicit transaction (`_context.Database.BeginTransactionAsync()`).
   - Phase 1: assign each found row a temporary `Order` outside the valid range (e.g. `-(index + 1)`; `Order` is always ≥ 1 in this codebase, so negatives are safe temporary values) and `SaveChangesAsync()`.
   - Phase 2: assign each row, in the caller's requested sequence, the i-th smallest value from the sorted value set collected earlier, and `SaveChangesAsync()`.
   - Commit the transaction.
2. Add the new Postgres-testcontainer integration test class covering FR-1, FR-2, FR-3 (derangement succeeds; inactive/untouched row never collides; unmatched id is tolerated).
3. Run `dotnet build` and `dotnet format` on the backend.
4. Run the full `Anela.Heblo.Tests` suite (at minimum the `InvoiceClassification` and new integration test, plus the existing `ClassificationRuleRepositoryTests.cs` to confirm FR-5) and confirm green.
5. No frontend build/lint/E2E changes required (no frontend files touched) — skip those validation steps for this change.

## Open questions

- **Explicit transaction wrapping (FR-4):** no other repository in this codebase currently wraps multiple `SaveChangesAsync()` calls in an explicit transaction (`BeginTransactionAsync` doesn't appear anywhere in `backend/src`). This plan introduces that pattern here because it's the correct fix for this specific two-phase write, not a stylistic change — flagging in case the architecture step wants a shared helper instead of an inline transaction, or has a house convention for this that a repo-wide grep didn't surface.
- **Scope of FR-2 (inactive-row collision):** this is a real, currently-reachable defect discovered during planning, adjacent to but distinct from the ticket's literal example. Including it keeps the fix from being reopened the first time someone deactivates a rule, but it does widen the ticket's stated scope. If the reviewer wants this split into a separate follow-up ticket instead of being folded into this fix, that's a smaller change (drop FR-2/the second test case) — but note the two defects share the exact same code path and root cause, so fixing FR-1 without FR-2 would ship a still-partially-broken reorder feature under a "fixed" label.
- **Tolerance for mismatched `ruleIds` (FR-3):** preserved as-is (silently skip unmatched ids) to stay surgical; not verified whether this silent-skip is actually desired product behavior (vs. rejecting the request) — out of scope to change without a separate product decision.
