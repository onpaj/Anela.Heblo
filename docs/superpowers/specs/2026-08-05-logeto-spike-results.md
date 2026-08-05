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
midnight time component) rather than a bare `yyyy-MM-dd` string. This is
pinned by a characterization test,
`LogetoClientTests.GetTimeTrackingAsync_RealisticItem_DateFieldFailsToDeserialize`
(`backend/test/Anela.Heblo.Adapters.Logeto.Tests/LogetoClientTests.cs`), which
documents a real, currently-shipped incompatibility: `LogetoTimeEntry.Date` is
typed as `DateOnly`, and `System.Text.Json`'s built-in `DateOnly` converter
rejects this full-datetime string outright — deserializing this exact
real-world payload throws `LogetoApiException` with message "Logeto returned
an unparseable response body for /api/v2/TimeTracking: The JSON value could
not be converted to System.DateOnly. Path: $.Items[0].Date | LineNumber: 3 |
BytePositionInLine: 30." This means `GetTimeTrackingAsync` would fail against
the live account today whenever the response includes any item — not just an
empty page. Fixing the DTO/JSON options is out of scope for this note; see
`.superpowers/sdd/2026-08-05-logeto-break-insertion/final-review-fix-report.md`
for the full writeup.

## Cleanup

All six test records (three per test day, dates 2026-08-06 and 2026-08-07,
all tagged `"SPIKE TEST"` in their description) were deleted via
`DELETE /api/v2/TimeTracking/{guid}` and confirmed removed by a follow-up
`GET`. No test data remains in the account.

## Required plan amendments before continuing

1. Task 5, `appsettings.json` snippet: change `"ApiTimesAreUtc": true` → `"ApiTimesAreUtc": false`.
2. Task 7, `BreakInsertionOptions.cs`: change `ApiTimesAreUtc` default from `true` → `false`.
3. Task 5, `BreakActivityName` default: confirm `"Oběd"` (lunch) is the intended activity vs. the generic `"Přestávka"` — resolve with user before/during Task 5.
