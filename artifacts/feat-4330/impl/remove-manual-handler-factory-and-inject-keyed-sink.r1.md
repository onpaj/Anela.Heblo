# Implementation: remove-manual-handler-factory-and-inject-keyed-sink

## What was implemented

`ReprintExpeditionListHandler` no longer depends on `ExpeditionListArchiveModule` manually
registering an `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>`
factory to override MediatR's auto-scanned binding. The keyed `"cups"` `IPrintQueueSink`
preference (falling back to the ambient, non-keyed sink) now lives directly on the handler's
own constructor via `[FromKeyedServices("cups")]`, so MediatR's normal assembly scan is the
sole registrant and registration order relative to `AddMediatR` in `ApplicationModule.cs` no
longer matters.

`ExpeditionListArchiveModule.AddExpeditionListArchiveModule` now only configures
`ExpeditionListArchiveOptions`; the manual `services.AddTransient<IRequestHandler<...>>(...)`
factory delegate is gone entirely.

### Deviation from the task-context's literal code snippet

The task-context's Step 1 code sample declared the constructor as:

```csharp
public ReprintExpeditionListHandler(
    IExpeditionListArchiveBlobStore blobStore,
    [FromKeyedServices("cups")] IPrintQueueSink? cupsSink,
    IPrintQueueSink fallbackSink,
    ITemporaryFileAccessor temporaryFileAccessor,
    IOptions<ExpeditionListArchiveOptions> options)
```

Running the task's own Step 4 acceptance test (`ReprintExpeditionListHandlerRegistrationTests`)
against this exact code surfaced two problems that required a small, targeted deviation:

1. **Compile error (CS1737).** `cupsSink` needs an explicit `= null` default value (see point 2
   below), and C# requires optional parameters to appear after every required one. Declaring
   `cupsSink` with a default while `fallbackSink`, `temporaryFileAccessor`, and `options` remain
   required (no defaults) does not compile in that position.
2. **Runtime DI failure (verified empirically, not just theorized).** `NoKeyedCupsSinkRegistered_HandlerFallsBackToAmbientSink`
   failed with `System.InvalidOperationException: Unable to resolve service for type
   'Anela.Heblo.Application.Shared.Printing.IPrintQueueSink' while attempting to activate
   'ReprintExpeditionListHandler'` when built exactly as specified (nullable annotation only,
   no `= null` default). The built-in DI container's optional-parameter check for constructor
   injection is based on `ParameterInfo.HasDefaultValue`, which is unrelated to C#'s
   compile-time-only nullable reference annotations (erased at the IL/reflection level for
   reference types). Without an explicit default value, the container does not treat the
   keyed parameter as optional and throws when no keyed `"cups"` registration exists at all,
   instead of resolving it to `null` so the constructor body's `cupsSink ?? fallbackSink` can
   fall back.

**Fix:** `cupsSink` now has an explicit `= null` default, and it was moved to be the constructor's
*last* parameter (after `fallbackSink`, `temporaryFileAccessor`, and `options`) so the C#
optional-parameters-must-be-trailing rule is satisfied. The selection logic in the constructor
body (`_cupsSink = cupsSink ?? fallbackSink;`) is unchanged. This is a purely mechanical
reordering plus one default-value addition; it does not change any registration, any test's
assertions, or the class's public contract beyond parameter order (which nothing outside this
task's two files constructs positionally except the handler's own unit test, updated to match).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` — constructor now takes `[FromKeyedServices("cups")] IPrintQueueSink? cupsSink = null` as its trailing parameter (moved from position 2, given an explicit default) instead of receiving an already-resolved `cupsSink` from a manual factory.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` — removed the manual `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` factory registration; the module now only configures `ExpeditionListArchiveOptions`.
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` — added `_fallbackSinkMock`, updated the constructor call to the new parameter order (`blobStore, fallbackSink, temporaryFileAccessor, options, cupsSink`). Every `[Fact]` body is unchanged; they still assert against `_cupsSinkMock`.

## Tests

- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` — 5 existing unit tests, updated only for the new constructor signature; assertions unchanged.
- `backend/test/Anela.Heblo.Tests/API/ReprintExpeditionListHandlerRegistrationTests.cs` — the prior task's 3 regression tests (`ReprintExpeditionListHandler_HasExactlyOneRequestHandlerRegistration`, `KeyedCupsSinkRegistered_HandlerSendsThroughCupsSink`, `NoKeyedCupsSinkRegistered_HandlerFallsBackToAmbientSink`) now all pass against a real `AddMediatR` + `AddExpeditionListArchiveModule` composition, including the previously-red "exactly one registration" assertion.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListArchive|FullyQualifiedName~ReprintExpeditionListHandlerRegistrationTests"
# Passed! - Failed: 0, Passed: 39, Skipped: 0, Total: 39

dotnet build ../Anela.Heblo.sln
# Build succeeded. 0 Error(s)

dotnet format ../Anela.Heblo.sln --verify-no-changes
# No formatting violations in any file this task touched (the 7 pre-existing WHITESPACE
# violations reported are in unrelated MarketingPerformance test files, not introduced here)

dotnet test ../Anela.Heblo.sln
# 198 failures, all pre-existing and environmental: Testcontainers/Docker-dependent tests
# (Docker daemon not available in this sandbox) and Shoptet/Flexi live-integration tests
# (require real API credentials / a live, non-test environment). None are in
# ExpeditionListArchive, ReprintExpeditionList, or any test this task touched.
```

Note: `dotnet build`/`dotnet test` needed `-p:UseSharedCompilation=false -maxcpucount:1` in
this sandbox to avoid an apparent MSBuild/VBCSCompiler named-pipe compiler-server deadlock
(unrelated to this change) — a pure environment workaround, not a code change.

## Notes

- No behavioral or performance change versus the deleted factory: the constructor's
  `cupsSink ?? fallbackSink` expression is the same selection logic as the old factory's
  `provider.GetKeyedService<IPrintQueueSink>("cups") ?? provider.GetRequiredService<IPrintQueueSink>()`.
- Scope discipline preserved: only the three files listed above changed. No other module's
  registration order, `ApplicationModule.cs`, or unrelated `ExpeditionListArchive` behavior
  was touched.
- The parameter-order deviation from the task-context's literal snippet is called out above
  in detail since it wasn't literally what Step 1 specified; the reviewer should confirm this
  is an acceptable, necessary correction rather than scope creep. It was driven entirely by
  making the task's own Step 4 acceptance test actually pass, which is the task's stated goal.

## PR Summary
Removed `ExpeditionListArchiveModule`'s manual `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` factory, which silently depended on being registered after MediatR's `AddMediatR` assembly scan to override it. `ReprintExpeditionListHandler` now expresses its "prefer the keyed CUPS sink over the ambient one" logic directly on its own constructor via `[FromKeyedServices("cups")]` with an explicit `= null` default (required for the DI container to treat the missing-keyed-registration case as optional rather than throwing), making MediatR's normal scan the sole registrant regardless of module registration order.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` — keyed/fallback sink selection moved into the constructor
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` — manual handler factory removed
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` — updated for the new constructor signature

## Status
DONE
