# Architecture Review: Seed Manufacture Orders for E2E

## Summary

The solution is a standalone shell script in `scripts/`. This is the correct placement per the existing project pattern (see `scripts/run-playwright-tests.sh`, `scripts/start-backend-dev.sh`, etc.). No backend or frontend code changes are needed.

## Design decisions

- **Shell script (not TypeScript):** The seeding is a one-time operational task, not part of the test runner. A bash script with curl calls is simpler and has no Node.js dependency.
- **Uses same Azure AD creds as E2E tests:** Reuses `E2E_CLIENT_ID`, `E2E_CLIENT_SECRET`, `AZURE_TENANT_ID` — no new secrets needed.
- **Creates new orders, not modifying existing:** New orders appear at the top of the `CreatedDate DESC` sorted list, satisfying the "near the top" requirement without touching existing data.
- **SinglePhase orders:** Simplest state-transition path. Draft → Planned → Completed requires only two PATCH calls without `confirm-semi-product` or `confirm-products` intermediate steps. The PATCH /status endpoint allows direct transitions when required.
- **No test code changes:** The protocol.spec.ts tests are correctly written and should not be modified.

## Risk

- Low. Script only creates data, does not delete or modify existing records.
- Idempotent usage: running twice creates extra orders, but they're harmless (more Completed/Draft rows at the top).
- The staging API must be accessible from where the script is run (developer machine or CI with VPN/network access).

## Approved approach

Single script: `scripts/seed-manufacture-orders-for-e2e.sh`
