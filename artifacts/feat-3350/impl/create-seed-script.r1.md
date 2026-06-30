# Implementation: create-seed-script (r1)

## What was done

Created `scripts/seed-manufacture-orders-for-e2e.sh` — a bash script that seeds the staging environment with the two manufacture orders required by `protocol.spec.ts`.

## Key implementation details

- Pre-flight validation: checks for required env vars (`E2E_CLIENT_ID`, `E2E_CLIENT_SECRET`, `AZURE_TENANT_ID`) and for `jq`/`curl` availability
- Azure AD token: acquired via `client_credentials` flow, same scope as E2E tests
- Creates Draft order: `POST /api/ManufactureOrder` with `manufactureType: 1` (SinglePhase) — stays in Draft
- Creates Completed order: same POST, then two PATCH calls `Draft → Planned (newState=2) → Completed (newState=5)`
- Error handling: each step checks the response and exits with a descriptive message on failure
- Script is executable (`chmod +x`)

## File changed

- `scripts/seed-manufacture-orders-for-e2e.sh` — new file, 113 lines
