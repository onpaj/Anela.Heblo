---
name: Transport box code uniqueness has a single owner
description: Code-occupancy was defined twice (allow-list vs deny-list) and drifted, letting two live boxes hold the same Code; TransportBoxStateRules is now the one definition.
type: project
---

# Transport Box Code Uniqueness — Single Definition

## Symptom
Box A sits in `Quarantine` holding `B001`. An operator assigns `B001` to a fresh box from the
box-detail screen (`frontend/src/components/pages/TransportBoxDetail.tsx`, `handleBoxNumberSubmit`);
the assignment succeeds and two live rows now hold `B001`. Every subsequent scan of the physical
`B001` label resolves to the wrong aggregate, and every scan-driven action — fill, receive,
stock-up — applies to it with no error raised.

## Root cause
The invariant had no single owner: an allow-list in `TransportBoxRepository.IsBoxCodeActiveAsync`
(missing `Quarantine` and `Error`) and a deny-list in `OpenOrResumeBoxByCodeHandler`, drifting apart
as states were added. There is no DB-level uniqueness constraint on `Code`, so the application
layer is the only defence.

## Fix
`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs` is the
**single** definition. It is a deny-list: only `Closed` and `Stocked` release a code; every other
state — present or future — occupies it, so forgetting about the type yields a false rejection,
never a silent duplicate. Its backing array is private; the public surface is
`OccupiesCode(TransportBoxState)` and `OccupiesCodePredicate`.

## Rules

1. **Transport-box code occupancy may only be defined in `TransportBoxStateRules`.** The three
   consuming call sites, by path, are:
   - `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` →
     `IsBoxCodeActiveAsync` and `GetByCodeAsync`
   - `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs`

   Comparing against `TransportBoxState.Closed`/`Stocked` directly for code-uniqueness purposes is
   a bug.

2. **`GetPagedListAsync`'s `isActiveFilter` (`State != Closed`, a UI list filter that deliberately
   shows `Stocked` boxes) and `GetTransportBoxByCodeHandler`'s `isReceivable`
   (`{InTransit, Reserve, Quarantine}`) are different concepts and must stay as they are.** Neither
   is a restatement of code occupancy — do not "fix" them to use `TransportBoxStateRules`.

3. **Adding a `TransportBoxState` member fails `TransportBoxStateRulesTests` by design** — classify
   the new state in `TransportBoxStateRules`, do not just append it to the test's expected map.

## Detection query (read-only)

Finds pre-existing duplicate `Code` values among rows the rule still considers active. No `UPDATE`,
no `DELETE`, no DDL:

```sql
SELECT "Code", COUNT(*), array_agg("Id"), array_agg("State")
FROM public."TransportBoxes"
WHERE "Code" IS NOT NULL AND "State" NOT IN ('Closed','Stocked')
GROUP BY "Code" HAVING COUNT(*) > 1;
```

**A9 — this SQL is a deliberate, unavoidable second copy.** The `'Closed'`/`'Stocked'` literals
above — and in any future partial-index predicate — restate the same partition as
`TransportBoxStateRules` because SQL cannot call into it. This is the one place the duplication is
unavoidable; naming it beats pretending otherwise. Whoever repartitions the states (adds, removes,
or reclassifies a `TransportBoxState`) must update this query **and** the follow-up index below in
the same change.

## Follow-up (A8, deferred): DB-level partial unique index

A partial unique index would close the TOCTOU window between the check and the save:

```sql
CREATE UNIQUE INDEX CONCURRENTLY "IX_TransportBoxes_Code_Active"
    ON public."TransportBoxes" ("Code")
    WHERE "Code" IS NOT NULL AND "State" NOT IN ('Closed','Stocked');
```

Deferred here — out of scope for issue #3887 — because migrations in this repo are applied
manually and out of band (see `memory/gotchas/ef-migration-codebase-drift.md`), and production may
already hold duplicate rows created by this bug, which would make index creation fail late.

Prerequisites before applying:
- Run the detection query above against **staging and production** and confirm zero rows.
- Use `migrationBuilder.Sql(sql, suppressTransaction: true)` — EF wraps migrations in a transaction
  by default and PostgreSQL rejects `CREATE INDEX CONCURRENTLY` inside one with `SQLSTATE 25001`
  (rule 1 of `memory/gotchas/postgres-partial-index-active-states.md`).
- Use **string** literals (`'Closed'`, `'Stocked'`), not integers — `TransportBox.State` is mapped
  with `HasConversion<string>()`, so the column holds the enum's string form, not its numeric value.

## Related files
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs`
- `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` (`HandleNewToOpened`, the guarded path)
- `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxStateRulesTests.cs`
- `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCodeOccupancySqlShapeTests.cs`
