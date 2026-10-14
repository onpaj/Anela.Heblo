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
