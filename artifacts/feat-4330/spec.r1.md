# Specification: Document and remove the implicit MediatR registration-order dependency in ExpeditionListArchiveModule

## Summary
`ExpeditionListArchiveModule.AddExpeditionListArchiveModule` manually registers an `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` factory to inject the keyed `"cups"` `IPrintQueueSink` (falling back to the non-keyed sink). This override only wins over MediatR's auto-scanned handler binding because `AddExpeditionListArchiveModule(...)` happens to be called after `AddMediatR(...)` in `ApplicationModule.cs` — an ordering contract that exists nowhere in writing. This specification defines the work to eliminate that implicit dependency so a future, innocuous-looking reordering of `ApplicationModule.cs` cannot silently break CUPS reprinting in staging/production without any compiler error or test failure.

## Background
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` line 74 calls `services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))`, which auto-scans the assembly and registers `ReprintExpeditionListHandler` as the `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` implementation.
- Line 119 calls `services.AddExpeditionListArchiveModule(configuration)`, which registers a second, *manual* factory for the same `IRequestHandler<...>` service type (`ExpeditionListArchiveModule.cs` lines 21–29). This factory resolves the keyed `"cups"` `IPrintQueueSink` with `GetKeyedService`, falling back to the non-keyed `IPrintQueueSink` via `GetRequiredService` when no keyed registration exists (e.g. FileSystem sink in Development/Test).
- .NET's `IServiceCollection` resolves non-keyed, single-instance-per-request services last-registration-wins. Because MediatR's scan (line 74) runs before the module's manual registration (line 119), the manual factory "wins" and is what actually gets constructed at runtime today.
- Nothing in either file states that this ordering is required. A future edit to `ApplicationModule.cs` — e.g. alphabetizing the module calls, extracting a "core infrastructure" block, or moving `AddExpeditionListArchiveModule` earlier for an unrelated reason — would silently flip which registration wins:
  - MediatR's auto-scanned binding would then win instead.
  - The keyed `"cups"` sink would never be requested.
  - `ReprintExpeditionListHandler` would receive whatever plain `IPrintQueueSink` is registered (the non-keyed fallback), in **every** environment including production/staging.
  - There is no compile error (both registrations satisfy the same interface) and the existing unit test(s) construct `ReprintExpeditionListHandler` directly, bypassing the DI container entirely — so no test would fail either.
  - The failure mode is silent: CUPS printing (the physical reprint path used in production/staging) stops being invoked; reprint requests would go to the fallback sink instead, and diagnosing this would require noticing that expedition list reprints are no longer reaching the label printer.
- This is a correctness/maintainability issue filed by the automated architecture-review routine (`arch-review`) against a real but narrow blast radius: only the `ExpeditionListArchive` reprint path, only when the registration order in `ApplicationModule.cs` changes.

## Functional Requirements

### FR-1: Eliminate the implicit registration-order dependency
The DI wiring for `ReprintExpeditionListHandler`'s `IPrintQueueSink` (keyed `"cups"` with fallback to the non-keyed sink) must no longer depend on `AddExpeditionListArchiveModule` being called after `AddMediatR` in `ApplicationModule.cs`. Reordering the module registration calls in `ApplicationModule.cs` (in any order, including moving `AddExpeditionListArchiveModule` before `AddMediatR`) must not change which `IPrintQueueSink` `ReprintExpeditionListHandler` receives at runtime.

**Acceptance criteria:**
- Moving `services.AddExpeditionListArchiveModule(configuration);` to any other position in `AddApplicationServices` (including before `services.AddMediatR(...)`) does not change the sink resolved for `ReprintExpeditionListHandler` — the keyed `"cups"` sink must still be preferred when registered, with fallback to the non-keyed sink only when no keyed registration exists.
- `ReprintExpeditionListHandler` continues to be resolved as the `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` implementation via DI, exactly as today.
- No behavior change to `ReprintExpeditionListHandler` itself, `ReprintExpeditionListRequest`/`Response`, or any other MediatR handler in the assembly.
- The implementation approach is **Option B** from the issue (see Dependencies / Design note below): extract the keyed/fallback `IPrintQueueSink` selection into a small resolver so MediatR's normal auto-scan can register `ReprintExpeditionListHandler` without any manual `IRequestHandler<...>` factory override, removing the ordering invariant entirely rather than merely documenting it. This is preferred over Option A (a comment-only fix) because Option A leaves the fragile invariant in place — a future editor could still remove or ignore the comment and reintroduce the silent failure mode. The architect phase should confirm this choice and flag if Option B has an implementation obstacle not visible from the brief (e.g. constraints on keyed-service resolution timing, or DI container limitations), in which case falling back to Option A plus a regression test is the documented alternative.

### FR-2: Add a regression test that fails when the ordering assumption is (re)introduced
Today's tests construct `ReprintExpeditionListHandler` directly, which is why a registration-order regression wouldn't be caught. A test must exist that resolves the handler's `IPrintQueueSink` dependency **through the actual DI container** (`AddApplicationServices` or an equivalent minimal registration of the relevant modules), in a configuration where both a keyed `"cups"` sink and a non-keyed sink are registered, and asserts the keyed sink is the one actually used.

**Acceptance criteria:**
- The new/updated test builds a service provider via the real module registration path (not a hand-constructed handler), registers both a keyed `"cups"` `IPrintQueueSink` and a non-keyed `IPrintQueueSink` (test doubles/mocks are acceptable), resolves `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` from the container, and asserts (directly or via an observable side effect) that the keyed `"cups"` sink is the one invoked/injected.
- The test fails if the resolution logic regresses to always using the non-keyed fallback sink (i.e. it must genuinely exercise the keyed-vs-fallback selection, not just confirm *a* handler resolves).
- The test does not depend on the position of `AddExpeditionListArchiveModule` relative to `AddMediatR` in `ApplicationModule.cs` — it must pass regardless of the order in which the modules under test are registered, since after FR-1 that order is no longer meaningful. (If FR-1 is delivered as Option A only, this test should instead assert against the actual `AddApplicationServices` composition to catch a real reordering.)

### FR-3: Preserve existing behavior for the no-keyed-sink case
In environments where no keyed `"cups"` `IPrintQueueSink` is registered (e.g. Development/Test, where only the FileSystem-backed non-keyed sink exists), `ReprintExpeditionListHandler` must continue to receive the non-keyed `IPrintQueueSink` exactly as it does today.

**Acceptance criteria:**
- Existing tests covering the Development/Test (non-keyed-only) configuration continue to pass unmodified in behavior (test code may need to change to reflect the new resolution path per FR-2, but the observed sink selection outcome must not change).
- No new required configuration or registration is introduced for environments that currently work correctly (i.e. this is not a breaking change for FileSystem-sink environments).

## Non-Functional Requirements

### NFR-1: No behavioral or performance change
This is a maintainability/robustness fix, not a feature change. Request handling latency, DI container startup cost, and printed output must be unaffected. Any new type introduced (e.g. a resolver class) must be a thin, allocation-light wrapper with no additional I/O or async work beyond what `ReprintExpeditionListHandler` already does.

### NFR-2: Scope discipline
The fix must be scoped to the `ExpeditionListArchive` feature's DI wiring and its test coverage. No other feature module's registration order, MediatR configuration, or `ApplicationModule.cs` structure beyond what FR-1 requires should be touched. Per this project's coding standards, only lines that trace directly to this fix should change.

## Data Model
No data model changes. No new entities, no persistence changes. This is a pure dependency-injection / composition-root fix scoped to `ExpeditionListArchive`.

## API / Interface Design

Current wiring (`ExpeditionListArchiveModule.cs`):
```csharp
services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
{
    var blobStore = provider.GetRequiredService<IExpeditionListArchiveBlobStore>();
    var cupsSink = provider.GetKeyedService<IPrintQueueSink>("cups")
        ?? provider.GetRequiredService<IPrintQueueSink>();
    var temporaryFileAccessor = provider.GetRequiredService<ITemporaryFileAccessor>();
    var options = provider.GetRequiredService<IOptions<ExpeditionListArchiveOptions>>();
    return new ReprintExpeditionListHandler(blobStore, cupsSink, temporaryFileAccessor, options);
});
```

Target wiring (Option B, per the issue's suggested fix — subject to architect confirmation):
- Introduce a small internal type (e.g. `CupsPrintQueueSinkResolver : IPrintQueueSink`) under `Application/Features/ExpeditionListArchive/Infrastructure/` (exact namespace/location to be confirmed by the architect against `docs/architecture/filesystem.md` and `docs/architecture/development_guidelines.md`) that:
  - Depends on `[FromKeyedServices("cups")] IPrintQueueSink?` (optional) and the non-keyed `IPrintQueueSink` (required fallback).
  - Implements `IPrintQueueSink`, delegating to whichever sink was selected at construction (keyed if present, else fallback).
- Register this resolver in DI (scoped or transient, matching the lifetime conventions used elsewhere for `IPrintQueueSink` implementations — to be confirmed by the architect) as the concrete `IPrintQueueSink` **used specifically by `ReprintExpeditionListHandler`**, without changing the sink registrations consumed by any other handler.
- `ReprintExpeditionListHandler`'s constructor dependency on `IPrintQueueSink` is satisfied by the resolver, and the explicit `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` factory override in `ExpeditionListArchiveModule.cs` is removed — MediatR's normal assembly scan then registers `ReprintExpeditionListHandler` like any other handler, and registration order in `ApplicationModule.cs` becomes irrelevant to this handler.
- **Open design question for the architect** (see Open Questions below): `ReprintExpeditionListHandler`'s constructor currently takes a plain `IPrintQueueSink`. If the resolver is registered as the *only* `IPrintQueueSink` implementation the handler's constructor parameter binds to, care is needed that no other consumer of the plain (non-keyed) `IPrintQueueSink` is inadvertently redirected to the resolver, and that the resolver itself does not get selected by MediatR-unrelated keyed-lookup call sites. The architect phase should confirm the exact registration shape (e.g. registering the resolver keyed for just this handler's constructor parameter via `[FromKeyedServices]` on the handler itself, vs. as the sole non-keyed `IPrintQueueSink` — the brief's Option B sketch implies the latter, which needs verification that nothing else resolves the plain `IPrintQueueSink` and expects the raw fallback/keyed sinks unmodified).

## Dependencies
- Existing types: `IPrintQueueSink` (`Application/Shared/Printing`), `IExpeditionListArchiveBlobStore`, `ITemporaryFileAccessor`, `ExpeditionListArchiveOptions`, `ReprintExpeditionListHandler`, `ReprintExpeditionListRequest`/`Response` — all under `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/` and `backend/src/Anela.Heblo.Application/Shared/Printing/`.
- MediatR's `AddMediatR` assembly-scan behavior (`ApplicationModule.cs` line 74) — this fix must remain compatible with it and must not require MediatR to scan in a particular order relative to other module registrations.
- .NET's keyed DI services feature (`GetKeyedService`, `[FromKeyedServices]`), already in use in the current (pre-fix) implementation.
- This is filed from an automated architecture-review finding (`docs/architecture` review routine); implementation should re-confirm current behavior against `docs/architecture/development_guidelines.md` (module boundary / DI conventions) before making the change, per this repo's CLAUDE.md instruction to consult architecture docs before implementation work touches that area.

## Out of Scope
- Changing the CUPS printing integration itself, `IPrintQueueSink` implementations, or any other `ExpeditionListArchive` behavior.
- Auditing or fixing other modules in `ApplicationModule.cs` for similar manual-`IRequestHandler`-factory-override patterns (this spec is scoped to `ExpeditionListArchive` only; if the architect phase identifies the same pattern elsewhere, that should be filed as a separate follow-up issue, not folded into this change).
- Reordering or restructuring `ApplicationModule.cs` beyond what FR-1 strictly requires (and if Option B fully removes the ordering dependency, no reordering of `ApplicationModule.cs` should be necessary at all).
- Any change to how MediatR handlers are generally registered/discovered across the codebase.

## Open Questions
None. The one open design question about the exact keyed-vs-non-keyed registration shape for the resolver (see API / Interface Design) is a routine implementation detail to be resolved by the architect and designer phases using the codebase's existing DI conventions — it does not require a decision from the requester and does not block progressing this specification.

## Status: COMPLETE
