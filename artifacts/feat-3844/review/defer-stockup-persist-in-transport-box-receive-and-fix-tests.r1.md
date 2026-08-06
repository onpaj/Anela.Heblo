# Code Review: Defer StockUpOperation persistence in TransportBox Receive

## Summary
This is the actual bug fix for #3844. Verified against the live source: the single call site inside
`HandleReceived` now passes `persistImmediately: false`, with no other change to `Handle`'s control
flow — meaning the box's own `SaveChangesAsync` (unchanged, still the sole flush point at the end of
`Handle`) now atomically commits both the box state transition and the staged `StockUpOperation`
inserts together (FR-1). Combined with task 1's idempotency pre-check, a retried Receive after a
partial failure now completes instead of hitting the unique-constraint violation described in the
issue (FR-2). All 47 tests across the 5 directly relevant test classes pass; full-solution build and
`dotnet format --verify-no-changes` are clean.

## Review Result: PASS

### task: defer-stockup-persist-in-transport-box-receive-and-fix-tests
**Status:** PASS

## Docs to Update
(None — internal persistence/transaction-boundary fix; no public API, DTO, or operational behavior
change beyond "a previously-wedged Receive retry now succeeds", which is the bug being fixed, not a
new feature requiring documentation.)

## Overall Notes
- Confirmed the only production change in `ChangeTransportBoxStateHandler.cs` is the one call site
  inside `HandleReceived` gaining `persistImmediately: false` as a 7th, named argument — `Handle`'s
  transition/update/`SaveChangesAsync` sequence (lines ~126-135) is byte-for-byte unchanged, satisfying
  the task's "no control-flow restructuring" constraint and the architecture review's Decision 1
  rationale (this fix rides the existing flush point rather than introducing a new one).
- Confirmed the new regression test `Handle_InTransitToReceived_PassesPersistImmediatelyFalse` asserts
  a literal `false` for `persistImmediately` on the mock call — this directly pins the fix, not just
  an incidental side effect.
- Confirmed all 12 pre-existing `Verify`/`Setup` call sites in `ChangeTransportBoxStateHandlerTests.cs`
  targeting `CreateOperationAsync` were updated with a trailing 7th argument (`It.IsAny<bool>()` or, for
  the new test, a literal `false`) — necessary because these are Moq expression-tree lambdas, and C#
  disallows an implicitly-filled optional-parameter default inside an expression tree (CS0854) once the
  mocked interface gained a new parameter.
- FR-3 verified: `GiftPackageManufactureService.cs`'s 4 real call sites are untouched and still omit
  `persistImmediately` entirely, so they keep resolving to `persistImmediately: true` (today's
  immediate-commit behavior) — no regression to that consumer. Its test file needed only the same
  compile-fix (Setup call updated), not a behavior-asserting change.
- The two test files outside this task's originally-listed scope
  (`TransportBoxUniquenessTests.cs`, `GiftPackageManufactureServiceTests.cs`) were fixed for the same
  CS0854 reason — this is accepted as required to keep the branch buildable, not a scope violation; see
  the equivalent note on task 2's review for the same reasoning.
- Verified via direct execution: `dotnet test` filtered to the 5 relevant test classes → 47/47 pass.
  `dotnet build Anela.Heblo.sln` → 0 errors. `dotnet format Anela.Heblo.sln --verify-no-changes` → no
  diff. Full-solution `dotnet test Anela.Heblo.sln` was also run: 186 failures observed, but every one
  traces to a pre-existing environment/infrastructure gap unrelated to this change (Docker not
  available for Testcontainers-based integration tests — 102 failures; missing Flexi/Shoptet
  integration-test fixtures and live-API secrets — 84 failures). None of the 186 failing tests belong
  to Logistics/TransportBox/StockUp/Catalog/GiftPackage namespaces touched by this fix; these are
  sandboxed-environment limitations that would fail identically on `master` with no code changes at
  all, not regressions introduced by this branch.
