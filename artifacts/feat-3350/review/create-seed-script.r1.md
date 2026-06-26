# Review: create-seed-script (r1)

## Correctness check

- Pre-flight env var check: correct. All three required vars validated.
- `jq`/`curl` availability check: correct.
- Azure AD token acquisition: correct pattern (`client_credentials`, correct scope).
- Token null check: correct (`jq -r '.access_token // empty'`).
- Draft order creation: `manufactureType: 1` (SinglePhase) is correct for a simple order with no products.
- Completed order transition: Draft(1) → Planned(2) → Completed(5). Valid state transitions per the domain model.
- Error handling: each `patch_status` and `create_order` checks for success. Good.
- `--fail-with-body` on curl ensures HTTP 4xx/5xx cause script to exit via `set -e`.

## Issues

- None blocking.

## Advisory

- The `--fail-with-body` flag requires curl ≥ 7.76. On older systems, use `--fail` instead (loses error body on failure). Acceptable trade-off for a dev/CI script.
- The script produces permanent data on staging. Re-running is safe but creates extra rows. A future enhancement could check if sufficient orders already exist before creating new ones (out of scope for this fix).
- `plannedDate: 2027-01-01` is a future date. This is intentional to avoid "past date" validation errors and mirrors the existing `draftHedvabnyPan` fixture pattern (`productionDate: '24. 1. 2029'`).

**Status:** PASS
