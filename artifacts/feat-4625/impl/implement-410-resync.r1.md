# Implementation: implement-410-resync

## What was implemented
`PhotobankIndexJob.GetDeltaWithResyncAsync` helper (per arch-review): on HttpRequestException 410 Gone with a stored DeltaLink it logs a warning (root id only), clears `root.DeltaLink`, and retries once with a null token. `IndexRootAsync` now calls it.

## Files modified
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`

## Verification
- `dotnet build` OK; `dotnet format` clean on the file.
- PhotobankIndexJobTests: 17/17 pass (incl. the 2 previously failing retry tests).
- `--filter Photobank`: 209 pass, 3 fail in PostgresSharedContainerFixture (no Docker in this environment; unrelated).

## Status
DONE
