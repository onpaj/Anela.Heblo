**Source:** Nightly E2E triage — run 28147951139 (2026-06-25). Clears **2** manufacturing failures (protocol).

## Problem
`frontend/test/e2e/manufacturing/protocol.spec.ts:34/57` throw by design — no **Completed** and no **Draft/Planned** manufacture order in the first 5 rows on staging.

## Scope
Seed staging (`Heblo_TST`) with at least one **Completed** and one **Draft/Planned** manufacture order so both appear near the top of the list. Per project rule these tests throw (not skip) when data is missing — the fixture data must exist.

## Acceptance criteria
- Both protocol tests find the required orders and pass against staging.
