# Logeto — the `Note` is the only source of contract hours

**Date**: 2026-08-14
**Account**: `anelacosmetics` (https://anelacosmetics.logeto.com)
**Extends**: `docs/superpowers/specs/2026-08-10-logeto-absence-hours-design.md` (unchanged and still accurate)
**Related**: `docs/superpowers/specs/2026-08-10-overtime-ledger-design.md`

## Why this document exists

The 2026-08-10 absence-hours design already decided that `LogetoPerson.Note` carries
both the enrollment marker and the person's net daily hours (`integration 6,4`), and
justified it at length — the Logeto public API exposes no úvazek anywhere, proven three
ways. That decision stands. Nothing in it is re-opened here.

What it did not cover is the **overtime ledger**, which shipped the same day (#3911) with
its own, second source of the same number: `Overtime:ContractHours`, a person-GUID →
hours dictionary in `appsettings.json`, read by `ConfigurationContractHoursProvider`. Its
own XML doc calls itself "temporary source until the Logeto-backed
IContractHoursProvider lands."

Two sources for one fact is the problem to remove. This document covers only that
removal.

## Decision

`IntegrationNote` (introduced by Task 1 of the absence-hours plan) becomes the **single**
source of daily contract hours for every consumer in Heblo. The
`Overtime:ContractHours` configuration table is deleted, not deprecated — there is no
fallback, no override, no "config wins if present". A person's úvazek is edited in
Logeto's `Pracovníci → Note` field and nowhere else.

Consequence: a person whose note carries no parseable hours has *no* contract hours.
That is already a handled state — `OvertimeCalculationService` emits the `"Chybí úvazek"`
warning per row and `CloseMonthHandler` refuses to close the month with
`OvertimeContractHoursMissing` (3406). Deleting the config table does not create a new
failure mode; it removes the only way to paper over a missing note.

## Components

| File | Change |
|---|---|
| `Application/Features/Attendance/Overtime/Services/LogetoContractHoursProvider.cs` | **new** — `IContractHoursProvider` backed by `ILogetoClient` + `IntegrationNote` |
| `Application/Features/Attendance/Overtime/Services/ConfigurationContractHoursProvider.cs` | **deleted** |
| `Application/Features/Attendance/Overtime/OvertimeOptions.cs` | `- ContractHours` |
| `Application/Features/Attendance/Overtime/OvertimeModule.cs` | register `LogetoContractHoursProvider` |
| `API/appsettings.json` | `- Overtime:ContractHours` |
| `test/.../Overtime/ConfigurationContractHoursProviderTests.cs` | **replaced** by `LogetoContractHoursProviderTests.cs` |

`OvertimeModule` already depends on the Logeto adapter indirectly (the ledger reads
Logeto time tracking), so no new module wiring or adapter reference is introduced.

## The year/month parameter

`IContractHoursProvider.GetDailyHoursAsync(personId, year, month, ct)` is month-aware, but
a `Note` states only the person's **current** úvazek — it carries no history. The
provider therefore ignores `year`/`month` and returns today's value.

This is sound rather than a compromise, because the ledger already freezes history at the
right moment: `OvertimeMonthlyStatement` documents its hour fields as "a cache of the live
Logeto computation while Open; on close they freeze and become the audit record", and
`RequiredHours` is one of those frozen fields. A closed month keeps the úvazek that was in
force when it was closed. Only open months recompute, and an open month should follow the
current note — that is exactly what an úvazek change mid-flight means.

The interface keeps its parameters: if Systemart ever exposes `Úvazky pracovníků` with
validity dates, a history-aware implementation drops in without touching callers.

## Caching

`GetMonthlyStatementsHandler` calls the provider once per person per request — ~27 people,
so a naive implementation would issue 27 identical `GET /api/v2/People` calls to fetch one
list.

The provider is registered **scoped** and memoizes the people lookup for the lifetime of
the scope: one Logeto call per HTTP request (or per job run), regardless of how many
people are asked for. No `IMemoryCache`, no TTL to tune, no cross-request staleness — a
note edited in Logeto takes effect on the next page load.

Memoization stores the in-flight `Task`, not the awaited result, so concurrent callers
inside one scope share a single request rather than racing.

## Failure mode

If the Logeto call fails, the exception propagates. The overtime page shows an error
rather than rendering every person with `"Chybí úvazek"`, which would misreport a
transient outage as a data-entry problem across the whole company. Closing a month is
blocked in either case, so nothing incorrect can be persisted — this choice is about the
operator reading the right cause.

## Testing

`LogetoContractHoursProviderTests`, with a mocked `ILogetoClient`:

- returns `6.4m` for a person whose note is `integration 6,4`
- returns `6.4m` for `integration 6.4` (decimal separator does not matter)
- returns `null` for a person whose note is `integration` with no number
- returns `null` for a person absent from the Logeto people list
- returns `null` for an unenrolled person (note without the marker), even if the note
  contains a number
- ignores `year`/`month` — the same value comes back for any month
- calls `GetPeopleAsync` **once** when several people are queried in the same scope
- propagates a client exception rather than returning null

`TimeSpan → decimal` conversion is asserted exactly: `06:24:00` → `6.4m`, not `6.4000001m`.

Existing `OvertimeCalculationServiceTests.MissingContractHours_ProducesWarning_AndNullContract`
and `CloseMonthHandlerTests.Close_Fails_WhenContractHoursMissing` already cover the
null path and are unaffected — they mock `IContractHoursProvider` directly.

## Operational note

`Overtime:ContractHours` is empty in `appsettings.json` and has no override in
`kv-heblo-prod` or the `heblo` App Service settings (verified 2026-08-14), so **no
production value is lost by deleting it** — the ledger has never had contract hours from
config. The live notes already carry the numbers:

| Person | Note (live, 2026-08-14) |
|---|---|
| Andrea Pajgrt | `integration 8` |
| Lydie Fellnerová | `integration 6.4` |
| Petra Zilvarová | `integration 6.4` |
| Olga Petrová | `integration 6.4` |

This resolves the absence-hours design's open item for Lydie ("to confirm against the
Úvazky screen") — it is set to `6.4`.

## Out of scope

- Úvazek history / validity dates. Closed statements freeze their own value; see above.
- Validating the note format from Heblo's UI. The note is edited in Logeto; a malformed
  note surfaces as a missing-úvazek warning, which is the same signal as not setting it.
- Everything in the absence-hours design, which is implemented as written.
