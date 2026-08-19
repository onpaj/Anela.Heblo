### task: document-code-uniqueness-invariant

**Files:**
- Create: `memory/gotchas/transport-box-code-uniqueness-single-definition.md`
- No source or test changes.

**Depends on:** nothing. Can run at any point; do it last so the paths it names are final.

#### Goal

FR-7 plus amendments A8 and A9: record the invariant, its single owner, its consuming call sites, the read-only detection query for pre-existing duplicates, and the DB-constraint follow-up — so the next person to add a `TransportBoxState` finds the rule before they find the bug.

#### Context

- The repo's memory convention lives in `CLAUDE.md` § Memory. Follow the front-matter + **Symptom / Root cause / Fix / Rules / Related files** shape of `memory/gotchas/postgres-partial-index-active-states.md` (read it first — it established the "a single source of truth keeps the handler and the schema in lockstep" rule for exactly this class of problem, and is the closest sibling).
- Front-matter shape used by that file:

```
---
name: <short title>
description: <one-line summary>
type: project
---
```

- **Amendment A9 is binding:** the `'Closed'` / `'Stocked'` literals in the detection SQL — and in any future partial-index predicate — are a **deliberate second copy** of the rule that cannot reference `TransportBoxStateRules` from SQL. Say so explicitly, and say that whoever repartitions the states must update the query and the follow-up index alongside the type. That is the one place the duplication is unavoidable, and naming it beats pretending otherwise.
- **Amendment A8 is binding:** the DB-level partial unique index is out of scope here but must be a written, findable follow-up rather than a prose aside. It must record that rule 1 of `memory/gotchas/postgres-partial-index-active-states.md` applies (`CREATE INDEX CONCURRENTLY` needs `migrationBuilder.Sql(sql, suppressTransaction: true)` or PostgreSQL rejects it with SQLSTATE 25001), that the predicate must use **string** literals (`State` is `HasConversion<string>()`, not int), and that the index cannot be applied while duplicate rows may still exist — run the detection query first.

#### Implementation steps

- [ ] **Step 1: Write `memory/gotchas/transport-box-code-uniqueness-single-definition.md`**

It must contain:

- **Symptom.** Box A sits in `Quarantine` holding `B001`. An operator assigns `B001` to a fresh box from the box-detail screen (`frontend/src/components/pages/TransportBoxDetail.tsx`, `handleBoxNumberSubmit`); the assignment succeeds and two live rows now hold `B001`. Every subsequent scan of the physical `B001` label resolves to the wrong aggregate, and every scan-driven action — fill, receive, stock-up — applies to it with no error raised.
- **Root cause.** The invariant had no single owner: an allow-list in `TransportBoxRepository.IsBoxCodeActiveAsync` (missing `Quarantine` and `Error`) and a deny-list in `OpenOrResumeBoxByCodeHandler`, drifting apart as states were added. There is no DB-level uniqueness constraint on `Code`, so the application layer is the only defence.
- **Fix.** `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs` is the **single** definition. It is a deny-list: only `Closed` and `Stocked` release a code; every other state — present or future — occupies it, so forgetting about the type yields a false rejection, never a silent duplicate. Its backing array is private; the public surface is `OccupiesCode(TransportBoxState)` and `OccupiesCodePredicate`.
- **Rules.** (1) Transport-box code occupancy may only be defined in `TransportBoxStateRules`. The three consuming call sites, by path, are `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` → `IsBoxCodeActiveAsync` and `GetByCodeAsync`, and `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs`. Comparing against `TransportBoxState.Closed`/`Stocked` directly for code-uniqueness purposes is a bug. (2) `GetPagedListAsync`'s `isActiveFilter` (`State != Closed`, a UI list filter that deliberately shows `Stocked` boxes) and `GetTransportBoxByCodeHandler`'s `isReceivable` (`{InTransit, Reserve, Quarantine}`) are **different concepts** and must stay as they are. (3) Adding a `TransportBoxState` member fails `TransportBoxStateRulesTests` by design — classify the new state in `TransportBoxStateRules`, do not just append it to the test's expected map.
- **Detection query**, verbatim, marked read-only (no `UPDATE`, no `DELETE`, no DDL):

```sql
SELECT "Code", COUNT(*), array_agg("Id"), array_agg("State")
FROM public."TransportBoxes"
WHERE "Code" IS NOT NULL AND "State" NOT IN ('Closed','Stocked')
GROUP BY "Code" HAVING COUNT(*) > 1;
```

- **The A9 note**, immediately after the query: these `'Closed'`/`'Stocked'` literals are a deliberate second copy of the partition that SQL cannot take from `TransportBoxStateRules`. Repartitioning the states means updating this query and the follow-up index in the same change.
- **Follow-up (A8).** A partial unique index — `CREATE UNIQUE INDEX CONCURRENTLY ... ON public."TransportBoxes" ("Code") WHERE "Code" IS NOT NULL AND "State" NOT IN ('Closed','Stocked')` — would close the TOCTOU window between the check and the save. Deferred because migrations here are applied manually and out of band (`memory/gotchas/ef-migration-codebase-drift.md`) and production may already hold duplicate rows created by this bug, which would make the index creation fail late. Prerequisites: run the detection query against staging **and** production and confirm zero rows; use `migrationBuilder.Sql(sql, suppressTransaction: true)` (SQLSTATE 25001 otherwise, per rule 1 of `memory/gotchas/postgres-partial-index-active-states.md`); use **string** literals because `State` is `HasConversion<string>()`.
- **Related files.** `TransportBoxStateRules.cs`, `TransportBoxRepository.cs`, `OpenOrResumeBoxByCodeHandler.cs`, `ChangeTransportBoxStateHandler.cs` (`HandleNewToOpened`, the guarded path), `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateRulesTests.cs`, `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs`.

- [ ] **Step 2: Verify every path named in the file exists**

```bash
cd /home/user/worktrees/feature-3887-Arch-Review-Transportboxes-Box-Code-Uniqueness-Is && \
  grep -o 'backend/[a-zA-Z0-9_./-]*' memory/gotchas/transport-box-code-uniqueness-single-definition.md | sort -u | xargs -I{} test -e {} && echo "all paths OK"
```

- [ ] **Step 3: Final full-solution validation gate**

With every other task complete:

```bash
cd backend && dotnet build
cd backend && dotnet format
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Category!=Integration"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox"
```

The third command is the broad regression sweep; the fourth runs the transport-box surface **including** the new `Category=Integration` SQL-shape test. Pre-existing, change-unrelated failures elsewhere in the suite (there are known timing-flaky tests) should be identified as such by re-running them on a clean checkout rather than papered over. No frontend files change, so `npm run build` / `npm run lint` and the E2E suite are not required.

#### Acceptance criteria

- `memory/gotchas/transport-box-code-uniqueness-single-definition.md` exists, carries the `name` / `description` / `type: project` front matter, and follows the Symptom / Root cause / Fix / Rules / Related files shape of its sibling.
- It names all three consuming call sites by full path.
- It contains the detection query verbatim, and the query is read-only — no `UPDATE`, no `DELETE`, no DDL.
- It carries the A9 note that the SQL's `'Closed'`/`'Stocked'` literals are a deliberate, unavoidable second copy of the partition.
- It carries the A8 follow-up for the partial unique index, including the `suppressTransaction: true` requirement, the string-literal requirement, and the "run the detection query first" precondition.
- Every file path it references resolves.
- No source or test file is modified by this task.

#### Tests to run

No test targets this file. Run the final validation gate from Step 3 as the feature-level completion check.
