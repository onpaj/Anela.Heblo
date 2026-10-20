# Remove ExpeditionListArchive's Implicit MediatR Registration-Order Dependency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the implicit "must be called after `AddMediatR`" ordering dependency between `AddExpeditionListArchiveModule` and `AddMediatR` in `ApplicationModule.cs`, so a future reordering of the module-registration chain can no longer silently make CUPS reprinting stop working in staging/production.

**Architecture:** `ExpeditionListArchiveModule` currently registers a manual `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` factory purely to inject the keyed `"cups"` `IPrintQueueSink` (falling back to the ambient non-keyed sink). That factory is deleted; `ReprintExpeditionListHandler` instead expresses the same keyed/fallback selection directly on its own constructor via `[FromKeyedServices("cups")] IPrintQueueSink? cupsSink` plus an ordinary `IPrintQueueSink fallbackSink` parameter, and is registered exclusively through MediatR's normal assembly scan like every other handler. A new regression test resolves the handler through a real `IServiceCollection`/`ServiceProvider` and mechanically proves both that exactly one `IRequestHandler<...>` registration exists (there were two before this fix) and that the keyed-vs-fallback sink selection behaves correctly, independent of registration order.

**Tech Stack:** .NET 8, ASP.NET Core, MediatR, Microsoft.Extensions.DependencyInjection (built-in keyed services), xUnit, Moq.

---

### task: add-registration-regression-test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/API/ReprintExpeditionListHandlerRegistrationTests.cs`

This task adds a regression test that resolves `ReprintExpeditionListHandler` through a real DI container instead of constructing it directly. Written and run **before** the fix, it mechanically proves the bug: today there are two competing `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` registrations (MediatR's assembly scan plus `ExpeditionListArchiveModule`'s manual factory), so `GetServices<...>().Count()` is `2`, not `1`. The `remove-manual-handler-factory-and-inject-keyed-sink` task below makes this test pass by leaving exactly one registration in place.

- [ ] **Step 1: Write the regression test file**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.API;

public class ReprintExpeditionListHandlerRegistrationTests
{
    private static ServiceProvider BuildProvider(
        bool registerKeyedCupsSink,
        out Mock<IPrintQueueSink> cupsSinkMock,
        out Mock<IPrintQueueSink> fallbackSinkMock)
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();

        // Mirrors the production composition path: MediatR's assembly scan registers every
        // handler in the Application assembly, then ExpeditionListArchiveModule is registered.
        // This test deliberately does NOT depend on which of these two calls comes first --
        // that is exactly the invariant this fix removes.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ReprintExpeditionListHandler>());
        services.AddExpeditionListArchiveModule(configuration);

        cupsSinkMock = new Mock<IPrintQueueSink>();
        cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fallbackSinkMock = new Mock<IPrintQueueSink>();
        fallbackSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Minimal, network-free stand-ins for the keyed "cups" slot and the ambient
        // (non-keyed) IPrintQueueSink -- the same two slots AddPrintQueueSink wires up in
        // production, without pulling in the real CUPS/FileSystem/Azure adapters.
        if (registerKeyedCupsSink)
        {
            services.AddKeyedSingleton<IPrintQueueSink>("cups", cupsSinkMock.Object);
        }
        services.AddSingleton<IPrintQueueSink>(fallbackSinkMock.Object);

        var blobStoreMock = new Mock<IExpeditionListArchiveBlobStore>();
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        blobStoreMock
            .Setup(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(pdfBytes));
        services.AddSingleton(blobStoreMock.Object);

        var temporaryFileAccessorMock = new Mock<ITemporaryFileAccessor>();
        temporaryFileAccessorMock
            .Setup(a => a.CreateFromStreamAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/registration-test.pdf");
        services.AddSingleton(temporaryFileAccessorMock.Object);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ReprintExpeditionListHandler_HasExactlyOneRequestHandlerRegistration()
    {
        // Arrange -- regression guard for the manual IRequestHandler factory this fix removes.
        // Before the fix, both MediatR's assembly scan and ExpeditionListArchiveModule's manual
        // factory register IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>,
        // so this count is 2 and the *last* registration silently wins -- entirely dependent on
        // the order AddMediatR and AddExpeditionListArchiveModule are called in.
        using var provider = BuildProvider(registerKeyedCupsSink: true, out _, out _);

        // Act
        var handlers = provider
            .GetServices<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>()
            .ToList();

        // Assert
        Assert.Single(handlers);
    }

    [Fact]
    public async Task KeyedCupsSinkRegistered_HandlerSendsThroughCupsSink()
    {
        // Arrange
        using var provider = BuildProvider(registerKeyedCupsSink: true, out var cupsSinkMock, out var fallbackSinkMock);
        var handler = provider
            .GetRequiredService<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>();

        // Act
        var result = await handler.Handle(
            new ReprintExpeditionListRequest { BlobPath = "2026-03-25/picking-list-001.pdf" }, default);

        // Assert
        Assert.True(result.Success);
        cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        fallbackSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoKeyedCupsSinkRegistered_HandlerFallsBackToAmbientSink()
    {
        // Arrange
        using var provider = BuildProvider(registerKeyedCupsSink: false, out var cupsSinkMock, out var fallbackSinkMock);
        var handler = provider
            .GetRequiredService<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>();

        // Act
        var result = await handler.Handle(
            new ReprintExpeditionListRequest { BlobPath = "2026-03-25/picking-list-002.pdf" }, default);

        // Assert
        Assert.True(result.Success);
        fallbackSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run the new tests and confirm the registration-count test fails against today's code**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReprintExpeditionListHandlerRegistrationTests"`

Expected: `ReprintExpeditionListHandler_HasExactlyOneRequestHandlerRegistration` **FAILS** (`Assert.Single` sees 2 items) -- this is the mechanical proof of the bug described in the issue. `KeyedCupsSinkRegistered_HandlerSendsThroughCupsSink` and `NoKeyedCupsSinkRegistered_HandlerFallsBackToAmbientSink` are expected to **PASS** already (today's manual factory implements the same keyed/fallback selection correctly; only the *extra, order-dependent* registration is the bug).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/API/ReprintExpeditionListHandlerRegistrationTests.cs
git commit -m "test(expedition-list-archive): add DI registration regression test for issue #4330

Proves the bug mechanically: resolving IRequestHandler<ReprintExpeditionListRequest,
ReprintExpeditionListResponse> through the real container currently returns 2
registrations (MediatR's assembly scan + ExpeditionListArchiveModule's manual
factory), not 1 -- an order-dependent last-wins collision. This test goes green
once the manual factory is removed in the next commit."
```

---

### task: remove-manual-handler-factory-and-inject-keyed-sink

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs`

This task performs the actual fix: `ReprintExpeditionListHandler` gets a `[FromKeyedServices("cups")]`-annotated optional constructor parameter plus an ordinary fallback `IPrintQueueSink` parameter (identical `cupsSink ?? fallbackSink` selection logic, just relocated from the factory delegate into the constructor body), and `ExpeditionListArchiveModule` drops the manual `IRequestHandler<...>` registration entirely so MediatR's normal assembly scan is the sole registrant.

- [ ] **Step 1: Update `ReprintExpeditionListHandler`'s constructor**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Shared.Printing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private readonly IExpeditionListArchiveBlobStore _blobStore;
    private readonly IPrintQueueSink _cupsSink;
    private readonly ITemporaryFileAccessor _temporaryFileAccessor;
    private readonly string _containerName;

    // Prefers the keyed "cups" IPrintQueueSink (the physical CUPS printer, registered in
    // Cups/Combined print-sink modes) over whatever the ambient, non-keyed IPrintQueueSink
    // is configured to (which can be FileSystem, AzureBlob, Cups, or Combined depending on
    // ExpeditionList:PrintSink) -- reprints must always target the physical printer when one
    // is available, never the general expedition-list print flow's sink. Falls back to the
    // ambient sink only when no keyed "cups" registration exists (e.g. FileSystem in
    // development/test). This selection is expressed here via [FromKeyedServices] instead of
    // a manual IRequestHandler factory so it no longer depends on ExpeditionListArchiveModule
    // being registered after AddMediatR -- see issue #4330.
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

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail();
        }

        string? tempFile = null;
        try
        {
            await using var blobStream = await _blobStore.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
            tempFile = await _temporaryFileAccessor.CreateFromStreamAsync(blobStream, ".pdf", cancellationToken);

            await _cupsSink.SendAsync(new[] { tempFile }, cancellationToken);
            return new ReprintExpeditionListResponse { Success = true };
        }
        finally
        {
            if (tempFile != null)
            {
                _temporaryFileAccessor.DeleteIfExists(tempFile);
            }
        }
    }
}
```

- [ ] **Step 2: Remove the manual factory from `ExpeditionListArchiveModule`**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` with:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

public static class ExpeditionListArchiveModule
{
    public static IServiceCollection AddExpeditionListArchiveModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExpeditionListArchiveOptions>(configuration.GetSection(ExpeditionListArchiveOptions.ConfigurationKey));

        // ReprintExpeditionListHandler is registered by MediatR's assembly scan (see
        // ApplicationModule.AddApplicationServices -> AddMediatR), like every other handler.
        // Its preference for the keyed "cups" IPrintQueueSink, with fallback to the ambient
        // sink, is expressed directly on its own constructor via [FromKeyedServices("cups")]
        // -- no manual IRequestHandler registration here, and no dependency on this module
        // being registered after AddMediatR. See issue #4330.

        return services;
    }
}
```

- [ ] **Step 3: Update the existing handler unit tests for the new constructor signature**

In `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs`, update the field declarations and constructor call in the test fixture's setup:

```csharp
    private readonly Mock<IExpeditionListArchiveBlobStore> _blobStoreMock;
    private readonly Mock<IPrintQueueSink> _cupsSinkMock;
    private readonly Mock<IPrintQueueSink> _fallbackSinkMock;
    private readonly Mock<ITemporaryFileAccessor> _temporaryFileAccessorMock;
    private readonly ReprintExpeditionListHandler _handler;
    private const string ContainerName = "expedition-lists";

    public ReprintExpeditionListHandlerTests()
    {
        _blobStoreMock = new Mock<IExpeditionListArchiveBlobStore>();
        _cupsSinkMock = new Mock<IPrintQueueSink>();
        _fallbackSinkMock = new Mock<IPrintQueueSink>();
        _temporaryFileAccessorMock = new Mock<ITemporaryFileAccessor>();
        _handler = new ReprintExpeditionListHandler(
            _blobStoreMock.Object,
            _cupsSinkMock.Object,
            _fallbackSinkMock.Object,
            _temporaryFileAccessorMock.Object,
            Options.Create(new ExpeditionListArchiveOptions()));
    }
```

Leave every `[Fact]` method body in that file unchanged -- they already assert against `_cupsSinkMock`, which still occupies the "keyed cups sink is present" constructor slot, so their intent (does the handler send through the CUPS sink) is preserved unmodified. `_fallbackSinkMock` is unused by any assertion in this file and exists purely so the constructor call compiles with a non-null fallback, matching the new signature.

- [ ] **Step 4: Run the full ExpeditionListArchive and registration test suites**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListArchive|FullyQualifiedName~ReprintExpeditionListHandlerRegistrationTests"`

Expected: **PASS** -- all `ReprintExpeditionListHandlerTests` tests pass with the new signature, and all three `ReprintExpeditionListHandlerRegistrationTests` tests now pass, including `ReprintExpeditionListHandler_HasExactlyOneRequestHandlerRegistration` (count is now `1`), which failed before this task.

- [ ] **Step 5: Full backend build and format check**

Run: `cd backend && dotnet build`
Expected: Build succeeds with no new warnings or errors.

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports violations introduced by this change, run `dotnet format` (without `--verify-no-changes`) to apply them, then re-run the verify command and include any resulting formatting-only diff in this task's commit.

- [ ] **Step 6: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: **PASS** -- no regressions anywhere else in the solution. This change touches only `ExpeditionListArchiveModule.cs` and `ReprintExpeditionListHandler.cs`, both scoped to the `ExpeditionListArchive` feature, so no other feature's tests should be affected.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs \
        backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs
git commit -m "fix(expedition-list-archive): remove order-dependent manual IRequestHandler factory

ExpeditionListArchiveModule no longer manually registers
IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse> to override
MediatR's auto-scanned binding. The keyed \"cups\" IPrintQueueSink preference (with
fallback to the ambient sink) now lives directly on ReprintExpeditionListHandler's
constructor via [FromKeyedServices(\"cups\")], so MediatR's normal assembly scan is the
sole registrant and registration order relative to AddMediatR in ApplicationModule.cs
no longer matters. Fixes #4330."
```

---

## Self-Review

**Spec coverage:**
- FR-1 (eliminate the implicit registration-order dependency) -> `remove-manual-handler-factory-and-inject-keyed-sink`, Steps 1-2 (manual factory deleted; keyed/fallback selection moved to the handler's own constructor, order-independent by construction since MediatR's scan is the only registrant left).
- FR-2 (regression test that fails when the ordering assumption is reintroduced) -> `add-registration-regression-test` (written first, proven red against current code in Step 2 of that task) plus `remove-manual-handler-factory-and-inject-keyed-sink` Step 4 (proven green after the fix). The "exactly one registration" assertion is a direct, mechanical, order-independent proxy for "no competing registration can silently win" -- stronger than re-testing a specific call order, and it does not require actually reordering `ApplicationModule.cs` to exercise the failure mode.
- FR-3 (preserve behavior when no keyed sink is registered) -> `add-registration-regression-test`'s `NoKeyedCupsSinkRegistered_HandlerFallsBackToAmbientSink`, and `remove-manual-handler-factory-and-inject-keyed-sink` Step 3 preserves the existing `ReprintExpeditionListHandlerTests` behavior-level coverage of the same case.
- NFR-1 (no behavioral/performance change) -> the constructor's `cupsSink ?? fallbackSink` expression is bit-for-bit the same selection logic as the deleted factory's `provider.GetKeyedService<IPrintQueueSink>("cups") ?? provider.GetRequiredService<IPrintQueueSink>()`; no new I/O, no new async work, no new allocations beyond one extra constructor parameter.
- NFR-2 (scope discipline) -> only the three files listed across both tasks change; no other module's registration order, `ApplicationModule.cs` structure, or `ExpeditionListArchive` behavior is touched.
- Out of Scope items (CUPS integration behavior, other modules' factory patterns, `ApplicationModule.cs` reordering, MediatR registration conventions elsewhere) -> none of this plan's steps touch any of them.

**Placeholder scan:** No "TBD"/"TODO"/"add appropriate handling" language; every step shows complete, runnable code and exact commands with expected output.

**Type consistency:** `ReprintExpeditionListHandler`'s constructor parameter names (`blobStore`, `cupsSink`, `fallbackSink`, `temporaryFileAccessor`, `options`) and the private field `_cupsSink` are used identically across both tasks' code blocks and the self-review. `ReprintExpeditionListHandlerRegistrationTests.BuildProvider`'s `out` parameter names (`cupsSinkMock`, `fallbackSinkMock`) match their use in all three `[Fact]` methods in that same task. No signature drift between tasks.
