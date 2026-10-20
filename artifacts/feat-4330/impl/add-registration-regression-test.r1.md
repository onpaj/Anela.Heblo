# Implementation: add-registration-regression-test

## What was implemented

Added `ReprintExpeditionListHandlerRegistrationTests`, a regression test that
resolves `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>`
through a real `ServiceCollection`/`ServiceProvider` (mirroring the production
composition path: `AddMediatR` assembly scan followed by
`AddExpeditionListArchiveModule`) instead of constructing the handler directly.
This mechanically proves the bug described in issue #4330: today there are two
competing `IRequestHandler<...>` registrations (MediatR's assembly scan plus
`ExpeditionListArchiveModule`'s manual factory), so
`GetServices<...>().Count()` is 2, not 1, and which one "wins" depends
entirely on registration order.

Two additional tests lock in the current (correct) keyed/fallback sink
selection behaviour of the manual factory, so the follow-up task
(`remove-manual-handler-factory-and-inject-keyed-sink`) has a safety net
proving it doesn't regress that behaviour while removing the extra
registration.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/API/ReprintExpeditionListHandlerRegistrationTests.cs` — new regression test file, exactly as specified in the task context (3 `[Fact]`s).
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — **out of scope for #4330**, see Notes below. One-line fix: `HasSeededFieldsChanged(existing, config)` → `HasSeededFieldsChanged(existingConfig, config)`.

## Tests

- `ReprintExpeditionListHandlerRegistrationTests.ReprintExpeditionListHandler_HasExactlyOneRequestHandlerRegistration` — **fails today** (`Assert.Single` sees the collection contains 2 items), which is the expected/required mechanical proof of the bug per the task's acceptance criteria.
- `ReprintExpeditionListHandlerRegistrationTests.KeyedCupsSinkRegistered_HandlerSendsThroughCupsSink` — passes today.
- `ReprintExpeditionListHandlerRegistrationTests.NoKeyedCupsSinkRegistered_HandlerFallsBackToAmbientSink` — passes today.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReprintExpeditionListHandlerRegistrationTests"
```

Actual result observed: `Failed: 1, Passed: 2, Total: 3` —
`ReprintExpeditionListHandler_HasExactlyOneRequestHandlerRegistration` fails
with `Assert.Single() Failure: The collection contained 2 items`; the other
two tests pass. This exactly matches Step 2's expected outcome in the task
context.

## Notes

**Deviation (blocker, fixed):** the whole backend solution failed to build
before any of this task's work, with a pre-existing compile error unrelated
to issue #4330:

```
RecurringJobSeeder.cs(51,45): error CS1503: Argument 1: cannot convert from
'System.Collections.Generic.List<RecurringJobConfiguration>' to
'RecurringJobConfiguration'
```

Confirmed via `git diff origin/main -- .../RecurringJobSeeder.cs` (no diff)
that this file is byte-identical to current `origin/main` — i.e. this is a
broken build already on `main` (introduced by merged PR #4325,
`HasSeededFieldsChanged(existing, config)` was passing the whole
`List<RecurringJobConfiguration>` instead of the single matched
`existingConfig`), not something introduced on this feature branch or by
this task. Since it blocked compiling anything — including running the new
regression test to verify Step 2's acceptance criteria — I applied the
minimal one-line fix (`existing` → `existingConfig` on that one call) in its
own separate commit, clearly scoped and described as unrelated to #4330. No
other change was made to that file.

No other deviations from the task context. Test file content matches the
task context's Step 1 verbatim.

## PR Summary
Added `ReprintExpeditionListHandlerRegistrationTests`, a DI-container regression test proving the bug from issue #4330: resolving `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` today yields 2 competing registrations (MediatR's assembly scan plus `ExpeditionListArchiveModule`'s manual factory) instead of 1, an order-dependent collision. The registration-count test fails today as expected; two companion tests confirm the manual factory's current keyed/fallback sink selection is otherwise correct, giving the next task (removing the manual factory) a safety net.

Also fixed a pre-existing, unrelated compile error in `RecurringJobSeeder.cs` (inherited from `main`, PR #4325) that was blocking the whole solution from building.

### Changes
- `backend/test/Anela.Heblo.Tests/API/ReprintExpeditionListHandlerRegistrationTests.cs` — new regression test (3 facts)
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — one-line pre-existing build fix, unrelated to #4330

## Status
DONE_WITH_CONCERNS
