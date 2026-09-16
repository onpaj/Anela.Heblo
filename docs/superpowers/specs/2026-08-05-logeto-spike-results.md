# Logeto API Verification Spike — Results

**Date**: 2026-08-05
**Account**: `anelacosmetics` (https://anelacosmetics.logeto.com)
**Related plan**: `docs/superpowers/plans/2026-08-05-logeto-break-insertion.md`

## Verdict: GO — merge=true splits correctly, with one caveat (see Finding 2)

## Setup

- Credentials (`AccountName`, `AccessKey`) stored in local user-secrets at
  `~/.microsoft/usersecrets/f4e6382a-aefd-47ef-9cd7-7e12daac7e45/secrets.json`
  under `Logeto`, per project convention (edit `secrets.json` directly, never
  `dotnet user-secrets set`).
- Integration worker found via `GET /api/v2/People`: **Andrea Pajgrt**,
  `Note: "integration"`, `Guid: c4d8857e-08c1-4027-b5d8-830570fbc22a`.
- Break-type activities found via `GET /api/v2/Activities`:
  - `"Přestávka"` (generic break, `Default: true`) — `Guid: a969483e-f36b-1410-80ad-00e813da89b0`
  - `"Oběd"` (lunch) — `Guid: ad69483e-f36b-1410-80ad-00e813da89b0` ← used for the spike
  - Work activity used: `"Práce"` — `Guid: 0233db1a-e04d-4cf2-a01b-9cec5d65c1e7`

**Decision needed from user**: `BreakActivityName` config should be `"Oběd"` or `"Přestávka"`?
Deferred to Task 5 (config wiring) — not blocking the rest of the plan.

## Finding 1: merge=true correctly splits the work record

Created a Work record `08:00–16:30` (no ExternalKey), then POSTed a Break
record `12:00–12:30` with `?merge=true`. Result, confirmed both via the API
response and visually in the Logeto web app by the user:

| Before | After |
|---|---|
| Práce 08:00–16:30 | Práce 08:00–12:00 (new record) |
| | Oběd 12:00–12:30 (new record) |
| | Práce 12:30–16:30 (**original record, `From` updated in place**) |

The original Work record's `Guid` is reused for the *last* segment (its
`From` is advanced past the break); the *first* segment is a brand-new
record. This is an implementation detail only — the plan's service reads
the resulting day fresh on each run rather than tracking which record was
whose, so it does not depend on this behavior.

Without the `merge` query parameter (or with `merge=false`), the POST
succeeds but does **not** split anything — it just adds a disconnected
overlapping record. This reproduces the original bug exactly and confirms
`merge=true` is required.

## Finding 2: merge=true throws ExternalKeyUniqueViolation if the record being split already has an ExternalKey

First attempt used a Work record created **with** `ExternalKey: "spike-test-work"`.
Every subsequent `POST ...?merge=true` against that day — regardless of the
break's own `ExternalKey` — failed:

```json
{"Error":{"Code":"ExternalKeyUniqueViolation","Message":"ExternalKey must be unique. Timetracking and Plan cannot have same external key."}}
```

Root cause (inferred, not confirmed by Systemart): the split appears to
propagate the original record's `ExternalKey` to *both* resulting Work
segments, which then collide with each other on insert.

**This does not block the plan**: production Work records in this account
(entered via the mobile/web app) never carry an `ExternalKey` — that field
is API-only. The break insertion service only ever *reads* existing Work
records and *writes* a new Break record (with its own `ExternalKey` for
idempotency) — it never sets `ExternalKey` on a Work record. Verified this
exact shape works: Work record with no key + Break record with
`ExternalKey: "autobreak-{personGuid}-{date}"` + `merge=true` → 201, correct
split, no error.

**Residual risk**: if any other integration ever writes a keyed Work record
into this account, a break-insertion attempt on that day will 400 with
`ExternalKeyUniqueViolation`. The plan's existing per-day error isolation
(Task 7, `ProcessDayAsync` try/catch in the day loop) already logs and
continues past this without crashing the run — no design change needed,
just noting it's a known, already-handled failure mode rather than an
unknown one.

## Finding 3: API times are Prague local wall-clock, not UTC

Pre-existing production records (fetched via `GET /TimeTracking`) return
`From`/`To` as bare ISO datetime strings with **no `Z` suffix and no offset**,
e.g. `"2026-07-27T05:26:00"` — unlike the documentation site's example
payloads, which show a `Z` suffix (`"2024-07-29T15:51:28.071Z"`). The real
account does not follow the documented example format.

Sent `"2026-08-06T08:00:00"` intending 08:00 Prague time; the API echoed
back the identical string, and **the user confirmed in the Logeto web app
that the record displays as 08:00–12:00 / 12:00–12:30 / 12:30–16:30** — i.e.
no timezone conversion happens anywhere in the pipeline. The API is a
pure pass-through of local wall-clock values.

**Conclusion: `ApiTimesAreUtc: false`.** This changes the plan's original
default (`appsettings.json` in Task 5 drafted `true`) — Task 5 must set
`"ApiTimesAreUtc": false` and `BreakInsertionOptions.ApiTimesAreUtc` must
default to `false`, not `true`. `LogetoTimeConverter`'s `false` branch
(`pragueLocal.ToString("yyyy-MM-ddTHH:mm:00")`, no `Z`) already matches this
exactly as written in Task 6 — no code change needed there, only the two
default values in Task 5's config and `BreakInsertionOptions.cs`.

### Concrete evidence: raw redacted response shape

For a redacted `GET /api/v2/TimeTracking` item as actually returned by the
real account (real `Guid` values replaced with fakes), so future readers
don't have to re-derive the shape from prose:

```json
{
  "ContinuationToken": null,
  "Items": [
    {
      "Guid": "11111111-1111-1111-1111-111111111111",
      "Person": "22222222-2222-2222-2222-222222222222",
      "Date": "2026-07-27T00:00:00",
      "From": "2026-07-27T07:08:00",
      "To": "2026-07-27T14:24:00",
      "Hours": null,
      "Activity": "0233db1a-e04d-4cf2-a01b-9cec5d65c1e7",
      "Description": null,
      "ExternalKey": null
    }
  ]
}
```

Note `Date` is also offset-less **and** carries a full datetime (with a
midnight time component) rather than a bare `yyyy-MM-dd` string. This was a
real, currently-shipped incompatibility: `LogetoTimeEntry.Date` is typed as
`DateOnly`, and `System.Text.Json`'s built-in `DateOnly` converter rejected
this full-datetime string outright — deserializing this exact real-world
payload used to throw `LogetoApiException` with message "Logeto returned an
unparseable response body for /api/v2/TimeTracking: The JSON value could not
be converted to System.DateOnly. Path: $.Items[0].Date | LineNumber: 3 |
BytePositionInLine: 30." That meant `GetTimeTrackingAsync` would have failed
against the live account whenever the response included any item — not just
an empty page.

**This has since been fixed** via a custom
`FlexibleDateOnlyJsonConverter`
(`backend/src/Adapters/Anela.Heblo.Adapters.Logeto/FlexibleDateOnlyJsonConverter.cs`),
registered on `LogetoClient`'s shared `JsonSerializerOptions`. It accepts both
a bare `"yyyy-MM-dd"` string and a full ISO datetime string with a time
component, taking just the date part, so both the full-datetime shape the
live account returns and a plain date string deserialize correctly. The test
that pinned the original failure,
`LogetoClientTests.GetTimeTrackingAsync_RealisticItem_DateFieldFailsToDeserialize`
(`backend/test/Anela.Heblo.Adapters.Logeto.Tests/LogetoClientTests.cs`), was
renamed to
`GetTimeTrackingAsync_RealisticItem_DateFieldWithTimeComponentDeserializesCorrectly`
and now asserts successful deserialization (`LogetoTimeEntry.Date` equals the
expected `DateOnly`) instead of the exception. A second test,
`GetTimeTrackingAsync_DateFieldWithoutTimeComponent_DeserializesCorrectly`,
was added to confirm the bare-date shape still works. See
`.superpowers/sdd/2026-08-05-logeto-break-insertion/final-review-fix-report.md`
for the full writeup of this fix.

## Cleanup

All six test records (three per test day, dates 2026-08-06 and 2026-08-07,
all tagged `"SPIKE TEST"` in their description) were deleted via
`DELETE /api/v2/TimeTracking/{guid}` and confirmed removed by a follow-up
`GET`. No test data remains in the account.

## Required plan amendments before continuing

1. Task 5, `appsettings.json` snippet: change `"ApiTimesAreUtc": true` → `"ApiTimesAreUtc": false`.
2. Task 7, `BreakInsertionOptions.cs`: change `ApiTimesAreUtc` default from `true` → `false`.
3. Task 5, `BreakActivityName` default: confirm `"Oběd"` (lunch) is the intended activity vs. the generic `"Přestávka"` — resolve with user before/during Task 5.

## Finding 4 (2026-09-16): merge=true does not bump the rewritten record's Revision — the record it rewrites must be touched

Finding 1 above is correct about *what* `merge=true` produces, but incomplete about
*how*. When the split rewrites the surviving original work record (advancing its
`From` past the break), Logeto does **not** bump that record's `Revision` or
`TimestampChanged`. Only the newly created records get fresh ones.

Verified against the live account over a 45-day window — on **128 of 128**
auto-break days:

- the work segment starting at the break's end had a `Revision` **lower** than the
  break's, and
- its `TimestampChanged` **predated** the break's `TimestampCreated` (it was the
  worker's own clock-out time, hours before the 03:00 job run).

The stored data is correct — 0 overlapping records across 666 entries. The damage
is to clients that sync incrementally: the **Logeto mobile app** picks up the new
break (fresh revision) but never refetches the rewritten work record, so the
employee sees the pre-split full-day record *next to* the new break — a collision
that exists only on their device. The web app and any device that had not cached
the day beforehand render it correctly, which makes it look like a data bug when
it is not.

**Consequence for this codebase:** `BreakInsertionService` still uses `merge=true`
— the split stays a single atomic server-side operation — and then *touches* the
records it produced: it re-reads the day and PUTs each work record adjacent to the
break back unchanged, which bumps its `Revision`. The same touch runs against days
that already carry **a break this job created** — matched on the
`autobreak-{person}-{date}` `ExternalKey` — whose neighbouring work records still
have a `Revision` below the break's, so days split before this behaviour existed
are healed on the next run that sees them.

Breaks a worker entered themselves are never touched. Their own work record is
always written before the break they add afterwards, so the account-wide counter
leaves it "below" the break and it would look stale on every run — but we never
split that day, so there is nothing to make visible.

Two things that made this safe, both confirmed against the live API:

- `GET /swagger/v2/swagger.json` → `TimeTrackingRequest` is
  `TimesheetRequestBase` + `Date` + `Billable` with `additionalProperties: false`.
  The writable set is exactly `CustomFields, Person, From, To, Hours, Activity,
  Contract, Subcontract, Description, ExternalKey, Date, Billable`.
  `Location`, `EndLocation`, `CostRate`, `BillingRate`, `CostPrice` and
  `BillingPrice` are **response-only**, so a full-replacement PUT cannot wipe the
  GPS locations that mobile-entered work records carry.
- `GET /TimeTracking/CustomFields` and `/CustomFieldsList` → 0 items, so there are
  no custom fields to preserve either.

## Finding 5 (2026-09-16): every PUT bumps Revision, including a no-op; no write ever moves TimestampChanged

Measured on a throwaway record (created on a future date, then deleted):

| operation | Revision | TimestampChanged |
|---|---|---|
| baseline, once settled | 46219 | 13:18:23 |
| PUT with identical values | **46220** | unchanged |
| PUT with a real change (`To` −5 min) | **46221** | unchanged |

Two things follow. A **no-op PUT is enough** to refresh a record for syncing
clients — nothing needs to be altered to make one look changed. And
`TimestampChanged` is useless as a sync signal from our side: no API write moves
it, so any fix can only ever change `Revision`.

A freshly created record briefly reports `Revision: -1` before its real revision
is assigned, so code must not compare against the revision of a record it just
created — the break's own revision is not yet meaningful at that moment.

Confirmed on real data (Olga Petrová, 2026-09-07 and 2026-09-08, with the user's
authorisation): touching the stale records moved **only** `Revision`
(45609 → 46223, 45686 → 46224, 45690 → 46225). Every other field — `From`, `To`,
`Hours`, `Description`, `Billable`, `PersonChanged`, `TimestampCreated`,
`TimestampChanged` — came back byte-identical. `Hours` survived even though the
request omits it. The employee's app then showed the day correctly without
logging out, which is what confirmed the whole mechanism.

**Still not verified live:** that a PUT against a work record which actually
carries `Location`/`EndLocation` leaves them intact. The request contract says it
must, and the records touched so far all had `Location: null`. 88 of the 127
records still awaiting a backfill touch do carry GPS data, so this needs one
single-record check before any bulk run.

The `ExternalKey` guard above limits the exposure: only days this job split are
ever touched, so a worker's own mobile-entered records are out of reach of the
sweep entirely. The check is still worth doing before the backfill, because the
records our own splits produced can carry GPS too.

**Also unverified:** `LogetoTimeConverter.ToApiTime` always formats seconds as
`:00`. Every record measured so far stored `:00` seconds, so a touch was a true
no-op — but a record stored with non-zero seconds would have its time shifted by
up to 59 seconds by the "unchanged" resend. Worth one spot-check on such a record
if one is ever found.
