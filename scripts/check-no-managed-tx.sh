#!/usr/bin/env bash
set -euo pipefail

# Fails if a managed-transaction API call appears in backend/src.
# The PollyExecutionStrategy retries an EF Core operation by replaying it; a
# caller-owned transaction would silently break that contract by reusing a
# stale NpgsqlTransaction. SaveChangesAsync's implicit transaction is safe.

hits=$(grep -rn -E "BeginTransaction|UseTransaction" \
  --include="*.cs" \
  backend/src || true)

if [[ -n "$hits" ]]; then
  echo "ERROR: managed-transaction API used in backend/src — incompatible with PollyExecutionStrategy" >&2
  echo "$hits" >&2
  exit 1
fi

echo "OK: no BeginTransaction / UseTransaction calls in backend/src"
