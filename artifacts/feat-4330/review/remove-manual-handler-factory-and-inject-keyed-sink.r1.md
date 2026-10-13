# Code Review: remove-manual-handler-factory-and-inject-keyed-sink

## Summary

The implementation removes `ExpeditionListArchiveModule`'s manual `IRequestHandler` factory and
moves the keyed/fallback `IPrintQueueSink` selection onto `ReprintExpeditionListHandler`'s own
constructor via `[FromKeyedServices("cups")]`, exactly matching FR-1's intent. The task's own
Step 4 acceptance test (`ReprintExpeditionListHandlerRegistrationTests`) was actually run against
the implementation rather than assumed to pass, which surfaced a real problem with the
task-context's literal code snippet (a missing `= null` default on the keyed parameter causing a
DI resolution failure, plus the resulting C# CS1737 compile error once fixed naively) and the
developer corrected it with a minimal, well-justified reordering. All acceptance tests pass.

## Review Result: PASS

### task: remove-manual-handler-factory-and-inject-keyed-sink
**Status:** PASS

**Verification performed:**
- `ReprintExpeditionListHandlerTests` (5 tests) and `ReprintExpeditionListHandlerRegistrationTests`
  (3 tests) all pass: `Passed! - Failed: 0, Passed: 39, Skipped: 0, Total: 39`, including the
  previously-red `ReprintExpeditionListHandler_HasExactlyOneRequestHandlerRegistration`.
- `dotnet build` on the full solution succeeds with 0 errors (93 pre-existing warnings, none new).
- `dotnet format --verify-no-changes` reports no violations in any file this task touched (the 7
  pre-existing WHITESPACE violations found are in unrelated `MarketingPerformance` test files).
- Full `dotnet test` on the solution: 198 failures, all independently verified as pre-existing
  environmental issues unrelated to this change (Docker/Testcontainers unavailable in this
  sandbox, and Shoptet/Flexi live-integration tests requiring real credentials/a live
  environment) -- none in `ExpeditionListArchive`, `ReprintExpeditionList`, or any file this task
  touched.
- Grep confirms `ReprintExpeditionListHandler`'s constructor is referenced by exactly the two
  files this task and the prior task modified -- no other call site depends on parameter order.

**On the parameter-order deviation from the task-context's Step 1 snippet:** the task-context
specified `cupsSink` as the constructor's second parameter with only a nullable annotation
(`IPrintQueueSink?`, no `= null`). Taken literally, this both fails to compile (C# requires
optional parameters trailing) and -- more importantly -- fails at runtime exactly in the
scenario FR-3 requires (`NoKeyedCupsSinkRegistered_HandlerFallsBackToAmbientSink`), because the
built-in DI container's optional-constructor-parameter check is `ParameterInfo.HasDefaultValue`,
which nullable reference annotations do not set. The developer verified this empirically (ran
the test, got the exact `InvalidOperationException` predicted, root-caused it) rather than
guessing, then applied the smallest possible fix: add `= null` and move `cupsSink` to the end of
the parameter list so the required parameters stay in front. The constructor body's
`cupsSink ?? fallbackSink` selection logic is untouched, and the change is fully covered by the
existing and regression test suites. This is exactly the kind of implementation-detail
correction a developer should make to satisfy the task's own explicit acceptance criterion
("all three ReprintExpeditionListHandlerRegistrationTests tests now pass") -- not scope creep,
since it touches no additional files beyond the three the task already named and preserves every
test's assertions and every consumer's behavior.

**Spec/architecture compliance:**
- FR-1 (eliminate the registration-order dependency): satisfied -- `ExpeditionListArchiveModule`
  no longer registers `IRequestHandler<...>` at all; MediatR's scan is the sole registrant,
  independent of module registration order.
- FR-3 (fallback behavior when no keyed sink registered) and NFR-1 (no behavior change): both
  verified directly by the passing regression tests, and the selection expression is unchanged
  from the deleted factory's logic.
- NFR-2 (scope discipline): only the three files named in the task context (plus this
  correction, contained entirely within them) were touched.

**Completeness:** all 7 steps in the task context were executed -- constructor update, module
update, test update, filtered test run, full build, format check, full test suite -- and their
outcomes are documented in the impl artifact with actual command output, not assumed results.

## Docs to Update

None. This is an internal DI-wiring fix with no public API, configuration, or operational
change; nothing in `docs/` describes `ExpeditionListArchiveModule`'s internal registration
mechanism at this level of detail.

## Overall Notes

No cross-cutting concerns. The developer's decision to actually run the acceptance test against
the literal task-context code before accepting it at face value, and to document the resulting
deviation in detail rather than silently changing it, is good practice worth noting positively --
it caught a genuine DI-framework gotcha (nullable annotations vs. `HasDefaultValue` for optional
keyed-service constructor parameters) that would otherwise have shipped a still-broken fix for
issue #4330's FR-3.
