# Architecture Review: Remove direct filesystem I/O from ExpeditionListService

## Skip Design: true

This is a pure backend refactor (interface extraction + DI rewiring) with zero UI surface. `IExpeditionListService`'s public contract, the generated PDFs, email content, and print-queue behavior are all explicitly required to stay unchanged (spec NFR-4, "Out of Scope"). No new or changed screens, components, or visual decisions are involved. Confirmed by reading the spec end-to-end — nothing in FR-1 through FR-6 touches `frontend/`.

## Architectural Fit Assessment

This fits the codebase's established Clean Architecture layering exactly, and the spec correctly identifies the precedent to mirror: `IPrintQueueSink`.

Verified in the actual code:
- `Anela.Heblo.Application.Shared.Printing.IPrintQueueSink` (`backend/src/Anela.Heblo.Application/Shared/Printing/IPrintQueueSink.cs`) is a one-method Application-owned contract taking only primitives (`IEnumerable<string>`, no `System.IO` types in the signature).
- `FileSystemPrintQueueSink` (`backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemPrintQueueSink.cs`) implements it with raw `File.Copy`/`Directory.CreateDirectory` calls, living under `Adapters/`, not `Application/Features/*/Services/`.
- `FileSystemAdapterServiceCollectionExtensions.AddFileSystemPrintQueueSink()` registers it as `AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>()`.
- The composition root, `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (`AddPrintQueueSink`, ~line 404), switches on `ExpeditionList:PrintSink` config and calls `AddFileSystemPrintQueueSink()` in the default branch (line 436).
- `docs/architecture/filesystem.md` codifies this explicitly under "Application Layer" component placement rules: *"I/O placement rule: Concrete `IPrintQueueSink` implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`."*

`ExpeditionListService.cs` (`backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs`, lines 70-109) violates exactly this rule via inline `File.Exists`/`File.Delete`/`File.ReadAllBytesAsync`. The fix — a new `ITemporaryFileAccessor` contract implemented by a sibling class next to `FileSystemPrintQueueSink` — is not a new pattern; it is the same pattern applied a second time in the same feature, in the same adapter project. This is low architectural risk and high consistency payoff.

One correction to the spec's framing: the "consumer owns the contract" pattern documented in `docs/architecture/development_guidelines.md` ("Cross-Module Communication Example: ILeafletKnowledgeSource") is designed for **cross-module** dependency inversion (module A depends on module B's data without touching B's internals). `ITemporaryFileAccessor` is not cross-module — both the contract and its only consumer live inside `Features/ExpeditionList`. The correct precedent is the **intra-module Application/Adapter split** already used for `IPrintQueueSink`, not the cross-module contract-ownership pattern. The spec already says this in a parenthetical ("applied here intra-module for I/O rather than cross-module data access") — this review makes it the primary framing, since citing the cross-module pattern as the main precedent could mislead an implementer into over-engineering (e.g., adding an allowlist entry in `ModuleBoundariesTests.cs`, which is unnecessary here — see Specification Amendments).

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application (Application layer)
  Features/ExpeditionList/
    Contracts/
      ITemporaryFileAccessor.cs          <- NEW: consumer-facing I/O contract
    Services/
      ExpeditionListService.cs           <- MODIFIED: injects ITemporaryFileAccessor,
                                             no more File.* calls
                    |
                    | depends on (interface only)
                    v
Anela.Heblo.Adapters.FileSystem (Adapter layer)
    Features/ExpeditionList/
      FileSystemPrintQueueSink.cs         (existing, unchanged)
      FileSystemTemporaryFileAccessor.cs  <- NEW: System.IO-backed implementation
    FileSystemAdapterServiceCollectionExtensions.cs  <- MODIFIED: new registration method
                    ^
                    | composed by
                    |
Anela.Heblo.API (Composition root)
    Extensions/ServiceCollectionExtensions.cs  <- MODIFIED: calls new registration
                                                   unconditionally (not behind the
                                                   PrintSink switch)
```

Dependency direction is unchanged from today: `Application` defines the interface, `Adapters.FileSystem` implements it, `API` wires the binding. `Application` never references `Adapters.FileSystem` directly — this already holds for `IPrintQueueSink` and the spec's "Dependencies" section correctly restates it.

### Key Design Decisions

#### Decision 1: New standalone contract vs. folding into an existing one

**Options considered:**
- (a) New `ITemporaryFileAccessor` interface, as the spec proposes.
- (b) Extend `IPrintQueueSink` with read/delete methods.
- (c) Change `IExpeditionPickingSource`/`ExpeditionPickingResult` to return bytes/streams instead of paths, eliminating the need for any file-path abstraction (the brief's "even cleaner" alternative).

**Chosen approach:** (a), matching the spec.

**Rationale:** `IPrintQueueSink` models "hand a batch of files to a print destination" — a fundamentally different responsibility from "read/delete a temp file I already own." Conflating them (option b) would force every `IPrintQueueSink` implementation (`AzureBlobPrintQueueSink`, `CupsPrintQueueSink`, `CombinedPrintQueueSink`) to also implement file-read/delete semantics they don't need, since those are keyed/swapped independently via the `ExpeditionList:PrintSink` config switch — verified in `ServiceCollectionExtensions.AddPrintQueueSink`. Option (c) is architecturally cleaner long-term (it was called out as such in the original brief) but is a materially larger change: it touches `IExpeditionPickingSource`, `LogisticsExpeditionPickingAdapter`, `IPickingListSource`, `ShoptetApiExpeditionListSource`, and `IPrintQueueSink.SendAsync` (which also takes file paths, per `IPrintQueueSink.cs`) — a cross-cutting redesign of the whole picking/printing pipeline, not a scoped fix for one Application-layer boundary violation. Correctly deferred; do not bundle it into this change.

#### Decision 2: Method shape — `DeleteIfExists` vs. separate `Exists`/`Delete`

**Options considered:**
- (a) Single `DeleteIfExists(string path)` method (spec's choice).
- (b) Mirror `File.Exists`/`File.Delete` as two interface members, letting the caller compose them (as the original brief's `Delete` name implied, though the brief also showed a single `Delete`).

**Chosen approach:** (a).

**Rationale:** Exposing `Exists` as a standalone member invites a check-then-act race and needlessly widens the interface surface for a caller (`ExpeditionListService.Cleanup`) that only ever wants "make sure this is gone." A single idempotent `DeleteIfExists` is the minimal contract the consumer needs and is trivially mockable (`Verify(x => x.DeleteIfExists(path), Times.Once)`), which is the whole point of this refactor (NFR-3). This is a case where deviating slightly from the brief's literal proposal is correct — noted as a spec amendment below since it's already what the spec chose.

#### Decision 3: DI registration — unconditional vs. inside the `PrintSink` switch

**Options considered:**
- (a) Register `ITemporaryFileAccessor` unconditionally in `AddPrintQueueSink` (or a sibling method called from the same place), regardless of which `PrintSink` value is configured.
- (b) Register it only in the `default` branch alongside `AddFileSystemPrintQueueSink()`, mirroring where the spec's example diff is anchored (line ~436).

**Chosen approach:** (a) — register unconditionally, not gated by the `PrintSink` switch.

**Rationale:** `ExpeditionListService.Cleanup`/`SendEmailCopy` operate on temp files regardless of which print sink is active — even when `PrintSink=AzureBlob` or `Cups`, the exported PDFs still land on local disk first (per `IExpeditionPickingSource.CreatePickingListAsync`'s `ExportedFiles`) and still need local read/delete. If registration is only added to the `default` case, resolving `IExpeditionListService` under `PrintSink=AzureBlob` or `Cups` throws a DI resolution error — a regression the spec's own FR-3 acceptance criteria explicitly rules out ("resolves with no DI errors in all existing environments"). The spec's prose gets this right ("unconditionally... since... temporary-file read/delete is not swapped per environment today") but its FR-3 code anchor ("alongside... at line ~436") sits inside the `default:` case of the switch — that's the wrong anchor point. See Specification Amendments.

## Implementation Guidance

### Directory / Module Structure

New files (paths verified against actual project structure):
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs` — new interface, alongside existing `IExpeditionPickingSource.cs` in the same folder.
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs` — new class, alongside existing `FileSystemPrintQueueSink.cs`.

Modified files:
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` — constructor + `Cleanup`/`SendEmailCopy` bodies.
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs` — add `AddFileSystemTemporaryFileAccessor()`.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — call the new registration method **outside/before** the `switch (printSink)` block in `AddPrintQueueSink`, so it runs for every branch. Do not place it inside the `default:` case.

Test files (verified locations):
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs` and `ExpeditionListServiceOrderStateTests.cs` — both already construct `ExpeditionListService` via a private `CreateService()` helper with `Mock<T>` fields; add a `Mock<ITemporaryFileAccessor>` field there and pass `.Object` into the constructor call.
- No dedicated `Anela.Heblo.Adapters.FileSystem.Tests` project exists — `FileSystemPrintQueueSinkTests.cs` (the direct precedent for testing this adapter) already lives in `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/`. Put the new `FileSystemTemporaryFileAccessorTests.cs` in that same folder, not in a new adapter-specific test project (spec FR-5's "adapter's own test project, if one exists" — it does not; use the existing convention).

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ITemporaryFileAccessor.cs
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface ITemporaryFileAccessor
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    void DeleteIfExists(string path);
}
```

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemTemporaryFileAccessor.cs
namespace Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;

public class FileSystemTemporaryFileAccessor : ITemporaryFileAccessor
{
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllBytesAsync(path, cancellationToken);

    public void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
```

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs
public static IServiceCollection AddFileSystemTemporaryFileAccessor(this IServiceCollection services)
{
    services.AddScoped<ITemporaryFileAccessor, FileSystemTemporaryFileAccessor>();
    return services;
}
```

Lifetime: `Scoped`, matching `IPrintQueueSink`'s existing registration — consistency over the (valid but marginal) observation that the type is stateless and could be `Singleton`. Don't introduce a lifetime inconsistency between two adapters in the same feature for no functional benefit.

`ExpeditionListService` constructor gains one parameter; append it after `IPrintQueueSink printQueueSink` and before `ILogger` to keep the logger last, consistent with the current parameter ordering convention (options/dependencies first, logger last):

```csharp
public ExpeditionListService(
    IExpeditionPickingSource pickingSource,
    IEmailSender emailSender,
    TimeProvider clock,
    IOptions<PrintPickingListOptions> options,
    IPrintQueueSink printQueueSink,
    ITemporaryFileAccessor temporaryFileAccessor,
    ILogger<ExpeditionListService> logger)
```

### Data Flow

Unchanged at the level that matters (external behavior): `PrintPickingListAsync` still calls `_pickingSource.CreatePickingListAsync`, gets back `ExpeditionPickingResult.ExportedFiles` (still `IList<string>` — paths, not bytes, per the "Out of Scope" decision to defer Decision 1's option (c)), then `Cleanup` and `SendEmailCopy` still operate on those paths. The only change is that `Cleanup`/`SendEmailCopy` now go through `_temporaryFileAccessor` instead of calling `System.IO.File` inline — an indirection with no new round-trips, no new I/O, no behavior change (per spec NFR-1).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| New registration placed inside the `default:` branch of `AddPrintQueueSink`'s switch (as the spec's FR-3 line-number anchor suggests) breaks `IExpeditionListService` resolution when `PrintSink=AzureBlob`/`Cups`/`Combined` | High | Register `ITemporaryFileAccessor` unconditionally, before/outside the `switch` statement in `AddPrintQueueSink` — not conditionally on the `PrintSink` value. Covered by spec FR-3's acceptance criterion "no DI errors in all existing environments," which implementers must actually exercise for non-default `PrintSink` values, not just the default. |
| `CancellationToken` now flows into `ReadAllBytesAsync` inside `SendEmailCopy`, which previously ignored it — a real (if minor) behavior change during a "pure refactor" | Low | Accept it; it only causes earlier/more-precise cancellation on an already-cancelled token, which is strictly more correct. Call it out explicitly in the PR description per NFR-4's "pure refactor" framing so it isn't mistaken for scope creep. |
| Someone treats this as precedent for the cross-module "consumer owns the contract" pattern and adds an unneeded `ModuleBoundariesTests.cs` allowlist/rule entry for `ExpeditionList` | Low | This review's framing (Architectural Fit Assessment) should be read by whoever implements FR-6's investigation step — no new module-boundary rule is needed since both sides of `ITemporaryFileAccessor` live in the same `Features/ExpeditionList` namespace tree. |
| Test rewrite (FR-5) misses a real-filesystem dependency because `ExpeditionListServiceOrderStateTests.PrintPickingListAsync_CleanupRunsAfterSuccess` (line 119-139) currently uses `Path.GetTempFileName()` + `Assert.False(File.Exists(tmpFile))`, coupling the assertion to the real filesystem | Low | Verified this test exists and is exactly the one FR-5 must rewrite: replace with `_temporaryFileAccessor.Setup(...)`/`Verify(x => x.DeleteIfExists(path), Times.Once)`, dropping `Path.GetTempFileName()` and the `File.Exists` assertion entirely. |

## Specification Amendments

1. **FR-3 anchor point is wrong.** The spec says to call the new registration "alongside... `services.AddFileSystemPrintQueueSink();` call at line ~436" — but line 436 is inside the `default:` case of the `switch (printSink)` block in `AddPrintQueueSink` (`ServiceCollectionExtensions.cs`). Registering there only wires `ITemporaryFileAccessor` when `PrintSink` is unset/`"FileSystem"`, breaking DI for `AzureBlob`/`Cups`/`Combined`. Amend FR-3 to say: call `services.AddFileSystemTemporaryFileAccessor()` **once, unconditionally, before the `switch` statement** inside `AddPrintQueueSink` (or immediately after `services.AddCupsPrinting(configuration)` at the top of that method) — not inside any branch.

2. **FR-6's premise should be corrected, not just "investigated."** This review already did the investigation FR-6 asks for: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` exists, but it is a **namespace-reference boundary checker** (asserts module A's types don't reference module B's forbidden namespaces), not an I/O-call detector. There is no existing reflection-based check for `File.*`/`System.IO` usage inside `Features/*/Services/` anywhere in the test suite. FR-6's acceptance criterion ("investigation is done and documented in the PR description") is satisfied by citing this review; implementers do not need to re-derive it. Adding an automated I/O-call guard remains optional/out-of-scope, as the spec already states.

3. **Test project location for FR-5's new adapter test.** FR-5 hedges between "the adapter's own test project, if one exists, or a new lightweight test." Confirmed: no adapter-specific test project exists for `Anela.Heblo.Adapters.FileSystem` — its existing test (`FileSystemPrintQueueSinkTests.cs`) lives in `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/`. Amend FR-5 to specify that path directly for `FileSystemTemporaryFileAccessorTests.cs`, removing the ambiguity.

4. **Constructor parameter placement.** FR-4 says "existing parameters and their order are otherwise preserved as closely as practical." This review specifies the exact position: after `printQueueSink`, before `logger` (see Implementation Guidance) — matching the codebase convention of logger-last observed in this same constructor today.

## Prerequisites

None beyond what already exists in the repository. No new project references are needed (`Anela.Heblo.API` already references `Anela.Heblo.Adapters.FileSystem` for `AddFileSystemPrintQueueSink`), no migrations, no config changes, no infrastructure provisioning. This can start immediately.
