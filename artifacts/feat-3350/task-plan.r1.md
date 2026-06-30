# Task Plan: Seed Manufacture Orders for E2E Protocol Tests

## Overview

Single task: create the seeding script. No backend or frontend code changes.

---

### task: create-seed-script

**Goal:** Create `scripts/seed-manufacture-orders-for-e2e.sh` that seeds staging with the required manufacture orders for the protocol E2E tests.

**Files to create:**
- `scripts/seed-manufacture-orders-for-e2e.sh`

**Implementation steps:**

1. Write a bash script with:
   - Shebang: `#!/usr/bin/env bash`
   - `set -euo pipefail`
   - Required env var validation (`E2E_CLIENT_ID`, `E2E_CLIENT_SECRET`, `AZURE_TENANT_ID`)
   - Optional `PLAYWRIGHT_BASE_URL` env var (defaults to `https://heblo.stg.anela.cz`)
   - `jq` availability check
   - Azure AD token acquisition via client-credentials flow
   - POST to `/api/ManufactureOrder` to create a **Draft** order
   - POST to `/api/ManufactureOrder` to create a second order, then two PATCH /status calls to bring it to **Completed**
   - Print created order numbers on success

2. Make the script executable (`chmod +x scripts/seed-manufacture-orders-for-e2e.sh`)

**Acceptance criteria:**
- Script exits with code 0 when successful
- Script exits with non-zero and clear error message when env vars missing or API call fails
- After running against staging, `protocol.spec.ts` tests pass
