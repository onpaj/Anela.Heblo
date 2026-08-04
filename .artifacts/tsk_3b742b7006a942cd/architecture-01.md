# Architecture Assessment: Remove hidden `DateTime.UtcNow` from `RecurringJobConfiguration`

## Verdict

Approved. This is a low-risk, single-module refactor with a design that is already fully verified against the actual source. No changes to plan-01.md / design-01.md are required; this document confirms alignment and adds implementation-order guidance, contract-safety notes, and risk mitigations for the dev step.

## Alignment with existing patterns

Verified directly against source, not inferred:

- `RecurringJobConfiguration.cs` currently calls `DateTime.UtcNow` at lines 76 (constructor), 102 (`UpdateConfiguration`), 112 (`Enable`), 122 (`Disable`), 134 (`UpdateCronExpression`) — five call sites, matching the finding save for a few lines of drift from the file having shifted slightly since the finding was filed. All five are in scope.
- The `TimeProvider` abstraction is already the dominant pattern for testable time in this codebase — it's injected in `GetRecurringJobHandler` and `GetRecurringJobsListHandler` (the module's own read-side handlers) and in dozens of other handlers/services across Manufacture, Catalog, Logistics, Dashboard, Analytics, Transport. This is not a new pattern being introduced; it's closing the one remaining gap in an already-established convention.
- `TimeProvider.System` is registered as a DI singleton in four places (`ServiceCollectionExtensions.cs:130`, plus three adapter modules) — safe to depend on via plain constructor injection, no new registration needed.
- The test double convention is `Mock<TimeProvider>` (Moq), used in 40+ test files including both existing BackgroundJobs handler tests (`GetRecurringJobHandlerTests.cs`, `GetRecurringJobsListHandlerTests.cs`). Design-01's choice to reuse this instead of pulling in `Microsoft.Extensions.Time.Testing.FakeTimeProvider` is correct — it avoids introducing a second, redundant test-double mechanism for the same abstraction in the same module.
- Call-site inventory is exhaustive: grepping the full `backend/` tree for `new RecurringJobConfiguration(`, `.Enable(`, `.Disable(`, `.UpdateCronExpression(`, `.UpdateConfiguration(` returns exactly the 12 files plan-01.md scoped (9 test files + `UpdateRecurringJobStatusHandler.cs`, `UpdateRecurringJobCronHandler.cs`, `RecurringJobSeeder.cs`). Nothing outside BackgroundJobs touches this entity's constructor or mutators, and nothing in scope was missed.
- `RecurringJobsControllerTests.cs` is correctly excluded — it builds DTOs directly, never the domain entity.

This is a pure mechanical signature change plus two constructor-injection additions. No new abstractions, no schema change, no API contract change — consistent with the project's "surgical changes" rule and with `development_guidelines.md`'s domain/application layering (entity stays framework-free; handlers own I/O and environment concerns like the clock).

## Proposed architecture

No new architecture is introduced. The shape is: **entity becomes a pure function of its explicit inputs; two write-side handlers become the clock's owners, exactly mirroring the two read-side handlers that already own it.**

Decision points and rationale (already resolved correctly in design-01.md, restated here as the binding direction):

1. **Timestamp parameter placement**: append `DateTime modifiedAt`/`lastModifiedAt` as the trailing argument on every mutator and the constructor. Rejected alternative: overload or optional-parameter approach — rejected because it would let `DateTime.UtcNow` sneak back in as a default value, defeating the purpose of the change.
2. **`RecurringJobSeeder` gets no `TimeProvider` injection.** It's startup-only seed code, not under test for timestamp precision (confirmed: `RecurringJobSeederTests.cs` exists but per plan-01.md doesn't assert exact timestamps). Injecting `TimeProvider` there would be scope creep with no test benefit — passing `DateTime.UtcNow` inline at the two call sites is correct and matches the finding's own explicit guidance.
3. **Test double: `Mock<TimeProvider>`, not `FakeTimeProvider`.** Confirmed via grep — this codebase already has a working, repeated convention for mocking `TimeProvider` with Moq, including inside this exact module's own tests. Introducing `Microsoft.Extensions.Time.Testing` would add a second, inconsistent way to fake the same dependency for no gain.
4. **`Enable`/`Disable` keep `modifiedBy` as the first parameter**, with `modifiedAt` appended. This minimizes diff noise at call sites that use positional arguments and matches the finding's own example signature.

## Implementation guidance

Recommended order (tightened from plan-01.md's rough plan into a build-clean-at-each-step sequence, since this is a signature change that will not compile until every call site is updated):

1. **Entity first** (`RecurringJobConfiguration.cs`): add the trailing `DateTime` parameter to the constructor and all four mutators; replace the five `DateTime.UtcNow` reads with the parameter. This alone breaks the build everywhere else — expected, drives the rest of the sequence.
2. **Production call sites**, in this order:
   - `RecurringJobSeeder.cs` — pass `DateTime.UtcNow` inline at both call sites (construction + `UpdateConfiguration`). No constructor change.
   - `UpdateRecurringJobStatusHandler.cs` — add `TimeProvider timeProvider` as a fourth constructor parameter (after `currentUserService`, matching `GetRecurringJobHandler`'s parameter ordering convention where present), add the `ArgumentNullException` guard consistent with the other three parameters in this constructor, compute `var now = _timeProvider.GetUtcNow().UtcDateTime;` once in `Handle`, pass it to both `Enable`/`Disable` call sites.
   - `UpdateRecurringJobCronHandler.cs` — same pattern, `TimeProvider` as a fifth constructor parameter (after `scheduler`), same null guard, same `now` computed once, passed to `UpdateCronExpression`.
3. **Test call sites**, in dependency order (entity-level tests unblock first, they don't need a `TimeProvider` mock at all):
   - `RecurringJobConfigurationTests.cs`, `RecurringJobStatusCheckerTests.cs`, `RecurringJobDiscoveryServiceTests.cs`, `RecurringJobConfigurationRepositoryTests.cs`, `GetRecurringJobHandlerTests.cs`, `GetRecurringJobsListHandlerTests.cs`, `RecurringJobSeederTests.cs` — pass a literal/local `DateTime` constant at each call site. No `TimeProvider` mock needed for entity construction (existing `Mock<TimeProvider>` in `GetRecurringJobHandlerTests`/`GetRecurringJobsListHandlerTests` stays as-is; it's already there for `NextRunAt` calculation, unrelated to this change).
   - `UpdateRecurringJobStatusHandlerTests.cs` and `UpdateRecurringJobCronHandlerTests.cs` — add `_timeProviderMock` (Moq, `Setup(tp => tp.GetUtcNow()).Returns(fixedDateTimeOffset)`), pass `.Object` into the handler constructor, and **tighten** the assertion at `UpdateRecurringJobStatusHandlerTests.cs:76` from `BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5))` to exact equality against the fixed value. This tightened assertion is the concrete, verifiable proof the refactor achieved its goal — treat it as a required acceptance check, not optional polish.
4. `dotnet build` after step 1 to confirm the expected compile break surface matches the 12-file inventory exactly (no missed thirteenth call site); iterate through steps 2–3 until clean.
5. `dotnet format`, then full BackgroundJobs test suite run.

**Data flow**: unchanged at the API/DB boundary. `LastModifiedAt` is still a plain `DateTime` written to the same column via the same `IRecurringJobConfigurationRepository.UpdateAsync`; only its origin moves from `DateTime.UtcNow` inside the entity to `_timeProvider.GetUtcNow().UtcDateTime` computed once per `Handle` call in the two write handlers, then threaded through as a method argument. No new interfaces, no new DI registrations, no HTTP contract change.

## Risks and mitigations

- **Silent behavior drift if `now` is computed more than once per `Handle` call.** Both handlers only call one mutator per invocation today (`Enable` XOR `Disable`, or a single `UpdateCronExpression`), so a single `var now = ...` per `Handle` is correct and sufficient — flag if the dev step accidentally calls `_timeProvider.GetUtcNow()` multiple times, which would reintroduce non-determinism-adjacent inconsistency (two slightly different timestamps for what should be one atomic update).
- **Missed call site breaking the build silently via a default-parameter fallback.** Mitigated by design: no default value is given to `modifiedAt`, so any missed call site is a compile error, not a silent revert to old behavior. Confirms the "no optional parameter" decision above is load-bearing, not stylistic.
- **Test assertion left loose after the refactor** (i.e., dev step changes the signature but leaves `BeCloseTo(..., TimeSpan.FromSeconds(5))` in place). This would mean the refactor shipped without delivering its actual benefit. Treat tightening `UpdateRecurringJobStatusHandlerTests.cs:76` (and the equivalent in `UpdateRecurringJobCronHandlerTests.cs` if a similar tolerance exists there — verify during implementation) as a required, not optional, part of this change.
- **Constructor parameter ordering inconsistency** between `Enable`/`Disable`/`UpdateCronExpression` (modifiedBy, then modifiedAt) and the class constructor (lastModifiedBy, then lastModifiedAt) versus property declaration order (`LastModifiedAt` declared before `LastModifiedBy`). This is cosmetic and already flagged as a non-architectural judgment call in plan-01.md — no action needed beyond what's already decided.

## Prerequisites before implementation begins

None outstanding. `TimeProvider` DI registration exists, the Moq-based test-double pattern exists and is proven in this exact module, and the full call-site inventory has been independently confirmed against the current source tree. The dev step can proceed directly from plan-01.md + design-01.md + this assessment.
