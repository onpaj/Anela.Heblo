# Spec: Seed Staging Manufacture Orders for Protocol Tests

## Problem Statement

Two E2E tests in `frontend/test/e2e/manufacturing/protocol.spec.ts` are failing on the nightly staging run because the manufacture orders list does not have any Completed or Draft/Planned orders in the first 5 rows.

- **Test 1 (line 34):** Searches for a "Completed|Dokončeno" order. Throws with "No completed manufacture order found in the first 5 rows."
- **Test 2 (line 57):** Searches for a "Draft|Návrh|Planned|Plánováno" order. Throws with "No non-completed manufacture order found in the first 5 rows."

## Root Cause

The `findAndClickRowByState` helper scans `maxRows = 5` rows (rows 0–4) of the manufacture orders table. The list is ordered `CreatedDate DESC`, so only the 5 most recently created orders are checked. On staging, these top rows apparently do not include orders in the required states.

## Solution

Create a shell seeding script that uses the staging REST API to create the required manufacture orders. Because orders are sorted by `CreatedDate DESC`, newly created orders appear first, guaranteeing they land in the first 5 rows.

**Script creates:**
1. One **Draft** manufacture order (created, left in Draft state)
2. One **Completed** manufacture order (created, then transitioned Draft → Planned → Completed)

Both orders are created with the known staging semi-product `MAS001001M` (Hedvábný pan Jasmín).

## Key Findings

- API: `POST /api/ManufactureOrder` — creates in Draft state
- API: `PATCH /api/ManufactureOrder/{id}/status` — transitions state (body: `{"Id": id, "NewState": <int>}`)
  - Draft = 1, Planned = 2, Completed = 5
  - Valid transition: Draft → Planned → Completed
- Auth: Azure AD service principal, same creds as E2E tests (`E2E_CLIENT_ID`, `E2E_CLIENT_SECRET`, `AZURE_TENANT_ID`)
- Scope: `api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default`
- Staging base URL: `https://heblo.stg.anela.cz`

## No Code Changes Required in Tests

The protocol tests are correctly written — they expect data to exist. The fix is operational (seeding the data), not a test logic change.

## Deliverable

`scripts/seed-manufacture-orders-for-e2e.sh` — a shell script that can be run on-demand to create the required staging data.
