# Architecture Review: Document and remove the implicit MediatR registration-order dependency in ExpeditionListArchiveModule

## Skip Design: true

This is a backend-only DI composition fix with no UI, API contract, or user-visible behavior change. No design phase is required; the designer agent should produce a minimal or empty `design.r1.md` noting the skip.

## Architectural Fit Assessment

The finding is accurate and the risk is real but narrow. I traced the actual registration chain to confirm the mechanism:

- `Program.cs:109` — `builder.Services.AddApplicationServices(...)`, which internally calls `AddMediatR(...)` (`ApplicationModule.cs:74`) then, later in the same method, `AddExpeditionListArchiveModule(configuration)` (`ApplicationModule.cs:119`).
- `ExpeditionListArchiveModule.cs:21-29` registers a second, manual `AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(...)` factory *after* MediatR's assembly scan already registered `ReprintExpeditionListHandler` for the same service type. Because `IServiceCollection` resolves non-keyed single-instance lookups last-registration-wins, the manual factory is what actually gets constructed today — this is exactly the mechanism the issue describes, confirmed against the real files and line numbers.
- `Program.cs:142` — `builder.Services.AddPrintQueueSink(...)` runs *after* `AddApplicationServices`, and is what actually registers the keyed `"cups"` `IPrintQueueSink` (`ServiceCollectionExtensions.cs:445,451`, only in `"Cups"`/`"Combined"` print-sink modes) and the non-keyed `IPrintQueueSink` (`FileSystemPrintQueueSink`, `AzureBlobPrintQueueSink`, `CupsPrintQueueSink`, or `CombinedPrintQueueSink`, depending on configuration). This ordering does **not** matter for the bug at hand: the manual factory in `ExpeditionListArchiveModule.cs` is a *delegate*, evaluated lazily at request-resolution time (after `BuildServiceProvider()` has already seen every registration from both `AddApplicationServices` and `AddPrintQueueSink`), not at `AddTransient(...)` call time. The only ordering that matters for this bug is `AddMediatR` vs. `AddExpeditionListArchiveModule` inside `ApplicationModule.cs`, exactly as the issue states.
- **Important nuance not fully spelled out in the brief**: `ReprintExpeditionListHandler` intentionally does **not** want "whatever `IPrintQueueSink` the main expedition-list print flow is configured to" — it specifically wants the physical CUPS printer when one exists, falling back to the general non-keyed sink otherwise. This matters because the general non-keyed `IPrintQueueSink` can be `AzureBlobPrintQueueSink` or `CombinedPrintQueueSink` depending on `ExpeditionList:PrintSink` configuration (see `ServiceCollectionExtensions.cs:AddPrintQueueSink`), not just the CUPS/FileSystem case the brief's comment mentions. Any fix must preserve "prefer keyed `cups`, else fall back to the non-keyed sink" — it must **not** collapse to simply injecting the ambient non-keyed `IPrintQueueSink` into the handler, or reprints would silently start going to Azure Blob storage instead of the physical printer whenever `ExpeditionList:PrintSink` is `AzureBlob`.

This constraint rules out the naive reading of the issue's own Option B sketch (registering the resolver as *the* non-keyed `IPrintQueueSink`) — doing so would either conflict with `ExpeditionListService`'s own consumption of the ambient `IPrintQueueSink` for the main print flow, or (depending on registration order) silently break the very problem it's meant to fix by reintroducing an order-sensitive override of the general sink. The design below resolves this cleanly using a keyed-constructor-parameter approach.

## Proposed Architecture

### Component Overview

```
Program.cs
  AddApplicationServices(config, env)          [109]
    └─ AddMediatR(...)                          scans assembly, registers
                                                 IRequestHandler<ReprintExpeditionListRequest,
                                                 ReprintExpeditionListResponse> -> ReprintExpeditionListHandler
    └─ AddExpeditionListArchiveModule(config)    [no longer touches IRequestHandler<...> at all]
  AddPrintQueueSink(config)                     [142]
    └─ registers keyed "cups" IPrintQueueSink (Cups/Combined modes only)
    └─ registers non-keyed IPrintQueueSink (mode-dependent: FileSystem/Azure/Cups/Combined)

ReprintExpeditionListHandler (constructor)
    ├─ IExpeditionListArchiveBlobStore blobStore           (unchanged)
    ├─ [FromKeyedServices("cups")] IPrintQueueSink? cupsSink  (NEW: optional keyed param)
    ├─ IPrintQueueSink fallbackSink                        (NEW: replaces old "cupsSink" param; ambient non-keyed sink)
    ├─ ITemporaryFileAccessor temporaryFileAccessor        (unchanged)
    └─ IOptions<ExpeditionListArchiveOptions> options       (unchanged)
    constructor body: _cupsSink = cupsSink ?? fallbackSink;   (same selection logic as today, just relocated)
```

Registration order between `AddMediatR` and `AddExpeditionListArchiveModule` becomes irrelevant to this handler, because there is no longer a second, competing `IRequestHandler<...>` registration — MediatR's normal assembly scan is the only one, ever.

### Key Design Decisions

#### Decision 1: Remove the manual `IRequestHandler<...>` factory; select keyed-vs-fallback `IPrintQueueSink` via constructor-level `[FromKeyedServices]` instead of a wrapper class

**Options considered:**
1. **Option A from the issue (comment-only)** — add a warning comment above `AddExpeditionListArchiveModule(configuration)` in `ApplicationModule.cs`. Rejected as insufficient: it leaves the ordering invariant in place; a future edit can still delete or ignore the comment, and nothing enforces it mechanically. The spec (FR-1) already calls this out as the weaker option.
2. **Option B as literally sketched in the issue** — a new `CupsPrintQueueSinkResolver : IPrintQueueSink` class registered as *the* `IPrintQueueSink`. Rejected: as shown in the Architectural Fit Assessment above, `IPrintQueueSink` is also consumed by the main expedition-list print flow (`ExpeditionListService`, via `AddPrintQueueSink`'s mode-dependent registration) for a *different* purpose (send to whatever sink `ExpeditionList:PrintSink` selects — Azure/Cups/Combined/FileSystem). Making the resolver *the* non-keyed `IPrintQueueSink` would either collide with that registration (reintroducing a last-registration-wins ordering hazard, just moved to a different pair of files) or require excluding `ExpeditionListArchiveModule`'s registration from that resolution path, which is fragile and non-obvious.
3. **Chosen: give `ReprintExpeditionListHandler` two distinct constructor parameters** — `[FromKeyedServices("cups")] IPrintQueueSink? cupsSink` (optional, resolves to `null` when no keyed `"cups"` registration exists) and `IPrintQueueSink fallbackSink` (the ambient non-keyed sink, resolved normally). The handler's constructor keeps the exact same `cupsSink ?? fallbackSink` selection logic it has today (currently inside the removed factory delegate; the target state moves it, unchanged, into the constructor body). Then delete the manual `AddTransient<IRequestHandler<...>>(...)` factory from `ExpeditionListArchiveModule.cs` entirely and let MediatR's assembly scan register `ReprintExpeditionListHandler` normally, like every other handler in the codebase.

**Rationale:**
- `[FromKeyedServices]` constructor-parameter injection is a built-in feature of `Microsoft.Extensions.DependencyInjection`'s default `ServiceProvider` since .NET 8 — this codebase uses the built-in container (confirmed: no `UseServiceProviderFactory`/Autofac in `Program.cs`), and MediatR resolves handlers via that same `IServiceProvider`, so this works with zero MediatR-specific configuration.
- It requires no new class, no new file, and no new registration statement anywhere — it is the smallest change that fully removes the ordering dependency. `ExpeditionListArchiveModule.cs` shrinks to just the options binding (line 15); it no longer touches `IRequestHandler<...>` or `IPrintQueueSink` resolution at all.
- It keeps the keyed/fallback selection logic scoped exactly to the one handler that needs it, with no risk of an unrelated consumer of the ambient `IPrintQueueSink` picking up CUPS-specific behavior by accident (the failure mode that ruled out Option B as literally sketched).
- The selection logic itself (`cupsSink ?? fallbackSink`) is unchanged — same semantics, same fallback rule, same behavior in every environment (FileSystem/dev-test, Cups, AzureBlob, Combined). Only *where* the selection happens moves, from an opaque factory delegate to an ordinary, DI-container-driven constructor — which is also easier to unit-test through the real container (FR-2).

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Two existing files change:

- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` — delete lines 17-29 (the comment and the manual `AddTransient<IRequestHandler<...>>` factory). The method keeps only the `services.Configure<ExpeditionListArchiveOptions>(...)` call (line 15) and the `using MediatR;` import can be dropped if nothing else in the file needs it (verify at implementation time — `IRequestHandler<,>` was the only MediatR usage).
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` — change the constructor signature (see Interfaces and Contracts below) and add `using Microsoft.Extensions.DependencyInjection;` for the `[FromKeyedServices]` attribute.

Test changes (existing file, no new directory):
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` — update the constructor call in the test fixture's setup (currently `new ReprintExpeditionListHandler(_blobStoreMock.Object, _cupsSinkMock.Object, _temporaryFileAccessorMock.Object, Options.Create(...))`) to match the new two-sink-parameter constructor. These tests construct the handler directly (not through DI) and should keep doing so for the handler's own business-logic tests — direct construction is fine for *this* file's existing tests, which verify `Handle()` behavior, not DI wiring.

New test (FR-2), following the established pattern in `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`:
- Add a new test file, e.g. `backend/test/Anela.Heblo.Tests/API/ReprintExpeditionListHandlerRegistrationTests.cs`, that builds a real `ServiceCollection`/`ServiceProvider` via `AddApplicationServices(...)` + `AddPrintQueueSink(...)` (mirroring `CombinedPrintQueueSinkRegistrationTests.BuildProvider`, reusing the same in-memory configuration approach), resolves `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` from the container in `"Cups"` (or `"Combined"`) mode, and asserts — via a probe on the resolved handler or via `SendAsync` interaction — that the keyed CUPS sink is the one actually used, not the ambient/general one. In `"FileSystem"` mode (no keyed `"cups"` registration), assert the handler falls back to the ambient non-keyed sink, preserving FR-3.
- Building a full `AddApplicationServices` provider pulls in every other feature module's registrations too (config, connection strings, etc.), which `CombinedPrintQueueSinkRegistrationTests` avoids by calling `AddPrintQueueSink` directly rather than the full application module. The developer should evaluate at implementation time whether resolving just `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` needs the *full* `AddApplicationServices` graph, or whether a narrower composition — `services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ReprintExpeditionListHandler>())` plus `AddExpeditionListArchiveModule(configuration)` plus `AddPrintQueueSink(configuration)` plus the minimal mocks for `IExpeditionListArchiveBlobStore`/`ITemporaryFileAccessor` — is sufficient and faster/more isolated. Either satisfies FR-2's acceptance criteria (resolve through the real container, assert keyed sink wins); prefer the narrower composition if it resolves cleanly, since it keeps the test's intent legible and avoids coupling this regression test to unrelated modules' configuration requirements.

### Interfaces and Contracts

`ReprintExpeditionListHandler` constructor, before → after:

```csharp
// Before
public ReprintExpeditionListHandler(
    IExpeditionListArchiveBlobStore blobStore,
    IPrintQueueSink cupsSink,
    ITemporaryFileAccessor temporaryFileAccessor,
    IOptions<ExpeditionListArchiveOptions> options)
{
    _blobStore = blobStore;
    _cupsSink = cupsSink;
    _temporaryFileAccessor = temporaryFileAccessor;
    _containerName = options.Value.BlobContainerName;
}
```

```csharp
// After
public ReprintExpeditionListHandler(
    IExpeditionListArchiveBlobStore blobStore,
    [FromKeyedServices("cups")] IPrintQueueSink? cupsSink,
    IPrintQueueSink fallbackSink,
    ITemporaryFileAccessor temporaryFileAccessor,
    IOptions<ExpeditionListArchiveOptions> options)
{
    _blobStore = blobStore;
    _cupsSink = cupsSink ?? fallbackSink;
    _temporaryFileAccessor = temporaryFileAccessor;
    _containerName = options.Value.BlobContainerName;
}
```

The `_cupsSink` field, its type (`IPrintQueueSink`), and every other member of the class are unchanged — only the constructor's parameter list and body change. `ReprintExpeditionListRequest`/`Response`, `Handle(...)`, and all other behavior are untouched, satisfying spec NFR-1.

`ExpeditionListArchiveModule.AddExpeditionListArchiveModule`, before → after:

```csharp
// Before
public static IServiceCollection AddExpeditionListArchiveModule(this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<ExpeditionListArchiveOptions>(configuration.GetSection(ExpeditionListArchiveOptions.ConfigurationKey));

    // ReprintExpeditionListHandler needs the keyed "cups" IPrintQueueSink when available
    // (production/staging). In environments where only the non-keyed sink is registered
    // (e.g. FileSystem in development/test), we fall back to the non-keyed registration.
    // This explicit factory overrides MediatR's auto-registration so the correct sink is injected.
    services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
    {
        var blobStore = provider.GetRequiredService<IExpeditionListArchiveBlobStore>();
        var cupsSink = provider.GetKeyedService<IPrintQueueSink>("cups")
            ?? provider.GetRequiredService<IPrintQueueSink>();
        var temporaryFileAccessor = provider.GetRequiredService<ITemporaryFileAccessor>();
        var options = provider.GetRequiredService<IOptions<ExpeditionListArchiveOptions>>();
        return new ReprintExpeditionListHandler(blobStore, cupsSink, temporaryFileAccessor, options);
    });

    return services;
}
```

```csharp
// After
public static IServiceCollection AddExpeditionListArchiveModule(this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<ExpeditionListArchiveOptions>(configuration.GetSection(ExpeditionListArchiveOptions.ConfigurationKey));

    // ReprintExpeditionListHandler is auto-registered by MediatR's assembly scan (see
    // ApplicationModule.AddApplicationServices -> AddMediatR). Its keyed "cups" IPrintQueueSink
    // preference, with fallback to the ambient non-keyed sink, is expressed directly on its
    // constructor via [FromKeyedServices("cups")] -- no manual IRequestHandler registration
    // needed here, and no dependency on registration order relative to AddMediatR.
    return services;
}
```

### Data Flow

Unchanged at runtime for every existing environment/configuration:
- `"Cups"` or `"Combined"` print-sink mode: keyed `"cups"` registration exists → `cupsSink` resolves to `CupsPrintQueueSink` → handler sends reprints to CUPS. Identical to today.
- `"FileSystem"` (dev/test) or `"AzureBlob"` mode: no keyed `"cups"` registration → `cupsSink` resolves to `null` → handler falls back to `fallbackSink` (the ambient non-keyed `IPrintQueueSink`, i.e. `FileSystemPrintQueueSink` or `AzureBlobPrintQueueSink` respectively). Identical to today.
- The only thing that changes is *when/where* this selection is expressed (constructor parameter default resolution by the container, instead of a hand-written factory delegate) and that it now happens via MediatR's normal handler construction path rather than a competing manual registration.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `[FromKeyedServices]` resolution behaves unexpectedly if MediatR ever swaps out the underlying `IServiceProvider` (e.g. a future migration to a third-party DI container) | Low | Confirmed today's `Program.cs` uses the built-in Microsoft DI container with no custom `IServiceProviderFactory`; FR-2's new registration test will fail immediately if this assumption is ever broken by a container swap, since it resolves through the real container. |
| Existing `ReprintExpeditionListHandlerTests.cs` direct-construction tests silently pass the wrong sink into the `fallbackSink` slot after the signature change, masking a regression | Low | The signature change from `(blobStore, cupsSink, ...)` to `(blobStore, cupsSink, fallbackSink, ...)` is a compile-time break in the existing test file — the developer must touch every call site, which the build will force; no risk of a silent miscompile. Ensure the updated test still asserts the mock passed as `cupsSink` position is the one whose `SendAsync` is verified, to keep the existing tests' intent (which sink receives the reprint) meaningful. |
| A future developer adds a new consumer of the ambient (non-keyed) `IPrintQueueSink` inside `ExpeditionListArchive` and assumes it gets the CUPS-preferring behavior for free | Low-Medium | Not mitigated by this change (out of scope per spec) — this is a pre-existing sharp edge in how `IPrintQueueSink` is consumed project-wide, not something this fix introduces or worsens. Worth a one-line code comment on `ReprintExpeditionListHandler`'s constructor explaining the `[FromKeyedServices("cups")] ?? fallbackSink` pattern exists specifically because this handler needs the physical printer over whatever `ExpeditionList:PrintSink` selects, so a future reader doesn't assume it's arbitrary. |
| Removing `using MediatR;` from `ExpeditionListArchiveModule.cs` if no longer needed, but missing another usage | Low | Purely a build-time check; if `dotnet build` fails or warns about an unused/missing using after the edit, the developer resolves it. Not worth flagging as a real risk beyond "run the build." |

## Specification Amendments

- **FR-1** in `spec.r1.md` says the implementation approach is "Option B from the issue" via a separate resolver class. Amend: the chosen approach is a variant of Option B that achieves the same goal (no manual `IRequestHandler<...>` factory, no ordering dependency) via `[FromKeyedServices("cups")]` directly on `ReprintExpeditionListHandler`'s constructor, with **no new class or file**. This is architecturally preferable to a wrapper resolver class for the reasons in Decision 1 above (a wrapper registered as the ambient `IPrintQueueSink` would conflict with the main expedition-list print flow's own consumption of that same interface). The spec's "Open design question for the architect" is resolved by this decision — no separate `CupsPrintQueueSinkResolver` type should be created.
- **FR-2**'s acceptance criteria are unchanged in substance; the Implementation Guidance above gives the concrete test-construction approach (build via `AddPrintQueueSink` + either full or narrowed MediatR/module registration, resolve `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>`, assert keyed-sink selection).
- No other amendments. FR-3, NFR-1, NFR-2, Data Model, Dependencies, and Out of Scope all hold as written.

## Prerequisites

None. No migrations, no configuration changes, no new infrastructure. This is a self-contained code change to two existing files plus test updates, buildable and testable in isolation.
