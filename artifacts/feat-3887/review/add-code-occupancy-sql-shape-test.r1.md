# Code Review: add-code-occupancy-sql-shape-test

## Summary

The new `TransportBoxRepositoryCodeOccupancySqlShapeTests` class satisfies every acceptance criterion in
the task context: correct `[Collection]`/`[Trait]` attributes, three facts covering the `WHERE` translation,
the `ORDER BY` translation, and end-to-end resolution order, using the interceptor pattern and DDL copied
from the specified reference files. No production code was touched. Docker is genuinely unavailable in
this sandbox (confirmed to be an environment-wide limitation, not specific to this file, by reproducing
the identical failure against the pre-existing `ChangeTransportBoxStateReceiveAtomicityIntegrationTests`),
so the three new integration tests are correctly reported as unverified rather than silently skipped,
deleted, or weakened, exactly per the task's explicit escape-hatch instruction.

## Review Result: PASS

### task: add-code-occupancy-sql-shape-test
**Status:** PASS

Acceptance criteria checked against the task context:
- `[Collection("PostgresIntegration")]` + `[Trait("Category", "Integration")]` present — yes.
- `IsBoxCodeActiveAsync("B001")` assertion checks `true`, single statement, `"State"` column reference,
  and negated set membership (`NOT` + `IN`/`= ANY`) without pinning the exact literal — yes, and matches
  the task's own suggestion to prefer readable `Should().Contain` assertions over a brittle regex.
- `GetByCodeAsync` assertion checks no `InvalidOperationException` and an `ORDER BY` referencing
  `"State"` — yes.
- Resolution-order fact seeds Quarantine (lower Id) then Stocked (higher Id) sharing `B001` and asserts
  the Quarantine box wins — yes, matches `DESC` + `false < true` semantics from `TransportBoxRepository`.
- No production file modified — confirmed (`git status` shows only the new test file).
- `dotnet build` and `dotnet format --verify-no-changes` both clean, no new warnings.

Verified independently:
- Traced `TransportBoxRepository.IsBoxCodeActiveAsync`/`GetByCodeAsync` and
  `TransportBoxStateRules.OccupiesCodePredicate` — the seeded scenarios and assertions match the actual
  query shape.
- Traced the `TransportBox` aggregate's state machine (`Open`/`ToQuarantine`/`AddItem`/`ToTransit`/
  `Receive`/`ToPick`) — the seeding helpers reach Quarantine and Stocked correctly without needing any
  new aggregate behavior.
- Reproduced the Docker-unavailable failure directly and against the existing integration suite to
  confirm it is an environment limitation, not a defect in the new test.
- Ran the full non-Integration TransportBox filter (222/222 pass) to confirm no regression.

No blocking issues. The one style nit found in first pass (a redundant `Contains("NOT") ||
ToUpperInvariant().Contains("NOT")` check) was already simplified during implementation before this
review — nothing further to flag.

## Docs to Update

None. This is a test-only addition; it doesn't change public behavior, add new concepts, or change how
the system is operated. `docs/testing/*` conventions (Postgres integration test pattern) are already
documented and this test follows them without introducing anything new to document.

## Overall Notes

The task explicitly anticipated the Docker-unavailable case and instructed the developer to report it
rather than block; that is exactly what happened here. Recommend the human owner (or CI running with
Docker available) run the `--filter "FullyQualifiedName~TransportBoxRepositoryCodeOccupancySqlShapeTests"`
command once against a real Docker daemon before merging, since these are the load-bearing assertions
per Amendment A2 — but that is outside what a REVISION_NEEDED in this pipeline can accomplish, since no
Docker is available anywhere in this sandboxed round either.
