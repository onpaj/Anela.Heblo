# Architecture Review: Fix phantom non-keyed `IPrintQueueSink` singleton in `AddAzurePrintQueueSink`

## Skip Design: true
This is a backend-only DI registration refactor with no UI/UX component.

## Architectural Fit Assessment

The finding is accurate and current. I re-read both files at their live line numbers (they've drifted slightly from the brief but the substance matches exactly):

- `AzureAdapterModule.AddAzurePrintQueueSink` (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs:14-27`) registers a `BlobContainerClient` singleton factory **and** `services.AddSingleton<IPrintQueueSink, AzureBlobPrintQueueSink>()` in one method.
- `ServiceCollectionExtensions.AddPrintQueueSink`'s `"Combined"` case (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:422-434`) still calls `AddAzurePrintQueueSink(configuration)` for its infrastructure side effect, carrying the exact workaround comment quoted in the brief ("it is unused here — the last non-keyed registration... wins").

This is a narrow, well-isolated DI-composition bug fix. It sits entirely in the composition root (`Anela.Heblo.API`) and one adapter module (`Anela.Heblo.Adapters.Azure`) — no Domain, Application, or Persistence layers are touched, no module boundary is crossed, and no contract/DTO is affected. It fits cleanly into the existing pattern already used elsewhere in this codebase: extension methods on `IServiceCollection`, config-driven switch in `AddPrintQueueSink`, keyed services for the `"Combined"` slot resolution (`AddKeyedScoped<IPrintQueueSink, ...>("azure"/"cups")`).

Notably, `docs/superpowers/plans/2026-06-08-decouple-combinedprintqueuesink-from-di-keying.md` — a prior, already-implemented plan — deliberately *preserved* the very comment and side-effect call this spec now wants removed (see its Task 2 "Self-Review Notes": *"Amendment 3 (carry the side-effect comment forward) applied"*). That plan solved a different problem (moving `CombinedPrintQueueSink` out of `Application` into `API`) and consciously left this phantom-registration issue as future work. This spec is the correct next increment — it is not undoing that decision, it is completing what that decision deferred.

**Verdict: architecturally sound, low risk, proceed as specified with the amendments below.**

## Proposed Architecture

### Component Overview

No new components. Two existing files change:

1. **`AzureAdapterModule.cs`** (`Anela.Heblo.Adapters.Azure`) — split one method into two:
   - `AddAzurePrintQueueSinkInfrastructure` — registers `BlobContainerClient` + makes `AzureBlobPrintQueueSink` resolvable. No non-keyed `IPrintQueueSink` binding.
   - `AddAzurePrintQueueSink` — calls the infrastructure method, then adds the non-keyed singleton `IPrintQueueSink` binding. Public signature unchanged (name, params, return type), per NFR-3.

2. **`ServiceCollectionExtensions.cs`** (`Anela.Heblo.API`) — `"Combined"` case calls `AddAzurePrintQueueSinkInfrastructure` instead of `AddAzurePrintQueueSink`; the workaround comment is deleted.

### Key Design Decisions

#### Decision 1: How `AzureBlobPrintQueueSink` is exposed by the infrastructure method

**Options considered:**
- (a) Register `AzureBlobPrintQueueSink` as a concrete singleton inside `AddAzurePrintQueueSinkInfrastructure`; have `AddAzurePrintQueueSink`'s `IPrintQueueSink` binding resolve to that same instance via a factory (`provider => provider.GetRequiredService<AzureBlobPrintQueueSink>()`), and have `"Combined"`'s keyed `"azure"` slot do the same.
- (b) Leave `AzureBlobPrintQueueSink` unregistered by the infrastructure method (only `BlobContainerClient` is registered there); have each of `AddAzurePrintQueueSink` and the `"Combined"` case register `AzureBlobPrintQueueSink` themselves, independently, as their own binding (`AddSingleton<IPrintQueueSink, AzureBlobPrintQueueSink>()` / `AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure")`).

**Chosen approach:** (a) — register the concrete `AzureBlobPrintQueueSink` type as a singleton in the infrastructure method, and have both consuming bindings resolve to that single instance.

**Rationale:** `AzureBlobPrintQueueSink`'s constructor (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs:15-23`) takes `BlobContainerClient`, `TimeProvider`, `ILogger<AzureBlobPrintQueueSink>` — all singleton-safe, no scoped dependencies. It also holds internal singleton-lifetime state (`SemaphoreSlim _ensureGate`, `bool _containerEnsured` used to memoize the one-time `CreateIfNotExistsAsync` call across the process). If `"AzureBlob"` mode and `"Combined"` mode each constructed their **own** `AzureBlobPrintQueueSink` instance (option b), the container-existence check (`EnsureContainerAsync`) would run twice under different code paths that are meant to talk to the same blob container, and it decouples "one physical sink" from "one C# instance" for no benefit — a wasteful, confusing duplication of a singleton that already exists. Option (a) also gives an honest single-instance guarantee: whether you resolve via `"AzureBlob"` mode's non-keyed slot or `"Combined"` mode's `"azure"` keyed slot, you get the *same* object talking to the *same* blob container, once.

This directly resolves one of the spec's flagged Open Questions ("should the `"azure"` keyed registration be promoted to `AddKeyedSingleton`, or kept scoped with a factory"): promote it to `AddKeyedSingleton` resolving the same shared instance — do not construct two separate instances at two different lifetimes.

#### Decision 2: Keep the two-method split inside `AzureAdapterModule`, not a new file

**Options considered:**
- (a) Add the new `AddAzurePrintQueueSinkInfrastructure` method directly beside `AddAzurePrintQueueSink` in the existing `AzureAdapterModule.cs`.
- (b) Extract a separate static class/file (e.g. `AzurePrintQueueSinkModule.cs`) to hold both methods.

**Chosen approach:** (a) — same file, same static class.

**Rationale:** `AzureAdapterModule` is a small, single-purpose module (currently 28 lines, one method). There's no other adapter-registration concern competing for space in this file, and no project convention (per `docs/architecture/development_guidelines.md`'s "Module Registration" pattern) requires splitting a module class once it exposes more than one method. Introducing a new file for two closely-related, always-called-together methods adds indirection without benefit — a violation of "surgical changes."

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Both changed files stay exactly where they are:
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

### Interfaces and Contracts

`AzureAdapterModule.cs` — final shape:

```csharp
public static class AzureAdapterModule
{
    /// <summary>
    /// Registers Azure Blob print-queue infrastructure (BlobContainerClient, AzureBlobPrintQueueSink)
    /// without binding a non-keyed IPrintQueueSink. Use this when the caller will register its own
    /// (e.g. keyed) IPrintQueueSink binding, such as the "Combined" print-sink mode.
    /// </summary>
    public static IServiceCollection AddAzurePrintQueueSinkInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PrintPickingListOptions>>().Value;
            return new BlobContainerClient(options.BlobConnectionString, options.BlobContainerName);
        });

        services.AddSingleton<AzureBlobPrintQueueSink>();

        return services;
    }

    /// <summary>
    /// Registers Azure Blob print-queue infrastructure and binds the non-keyed IPrintQueueSink
    /// singleton to AzureBlobPrintQueueSink. Use this for the "AzureBlob" print-sink mode, where
    /// AzureBlobPrintQueueSink is the sole, directly-resolvable IPrintQueueSink implementation.
    /// </summary>
    public static IServiceCollection AddAzurePrintQueueSink(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAzurePrintQueueSinkInfrastructure(configuration);
        services.AddSingleton<IPrintQueueSink>(provider => provider.GetRequiredService<AzureBlobPrintQueueSink>());
        return services;
    }
}
```

`ServiceCollectionExtensions.cs` — `"Combined"` case, final shape (replacing lines 422-434):

```csharp
case "Combined":
    services.AddAzurePrintQueueSinkInfrastructure(configuration);
    services.AddKeyedSingleton<IPrintQueueSink>("azure",
        (provider, _) => provider.GetRequiredService<AzureBlobPrintQueueSink>());
    services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
    services.AddScoped<IPrintQueueSink>(provider =>
    {
        var azure = provider.GetRequiredKeyedService<IPrintQueueSink>("azure");
        var cups = provider.GetRequiredKeyedService<IPrintQueueSink>("cups");
        return new Anela.Heblo.API.Features.ExpeditionList.CombinedPrintQueueSink(azure, cups);
    });
    break;
```

Note the `"azure"` keyed registration changes from `AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure")` to a factory-based `AddKeyedSingleton` that resolves the shared `AzureBlobPrintQueueSink` instance registered by the infrastructure method (Decision 1). This is a deliberate deviation from the spec's API/Interface Design sketch, which left the exact keyed-registration lifetime as an open question — this review closes that question.

The `"AzureBlob"` case (line 415-417) and `"Cups"`/default cases are untouched.

### Data Flow

No runtime data flow changes. This is purely a DI graph correction:

- **Before:** `"Combined"` mode's `IServiceCollection` ends up with two non-keyed `IPrintQueueSink` registrations (phantom `Singleton AzureBlobPrintQueueSink` + real `Scoped CombinedPrintQueueSink` factory). `GetService<IPrintQueueSink>()` returns the last one (correct by luck); `GetServices<IPrintQueueSink>()` returns both (bug).
- **After:** `"Combined"` mode's `IServiceCollection` has exactly one non-keyed `IPrintQueueSink` registration (the `CombinedPrintQueueSink` factory). `GetServices<IPrintQueueSink>()` returns exactly one item.
- **`"AzureBlob"` mode:** unchanged behavior — one non-keyed singleton, now reached by resolving through the shared `AzureBlobPrintQueueSink` singleton via a factory delegate instead of a direct `AddSingleton<IPrintQueueSink, AzureBlobPrintQueueSink>()` — but this is an implementation detail invisible to any consumer resolving `IPrintQueueSink`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Promoting the `"Combined"` `"azure"` keyed slot from `AddKeyedScoped` to a singleton-backed factory silently changes its lifetime for any future consumer that assumes scoped semantics | Low | No current consumer resolves the `"azure"` keyed slot directly except `CombinedPrintQueueSink`'s factory, which is stateless per call. Document the lifetime change in the PR description; the existing test `Combined_KeyedAzureSlot_ResolvesAzureBlobPrintQueueSink` only asserts type, not lifetime — add an assertion (see Prerequisites) that two resolutions within the same scope return the same instance, proving the singleton sharing works as intended. |
| `dotnet format`/build catches nothing if the `"azure"` factory registration syntax (`AddKeyedSingleton<IPrintQueueSink>("azure", factory)` overload) is wrong for .NET 8's keyed DI API | Low | Verify the exact `AddKeyedSingleton` factory overload compiles (`(IServiceCollection, object? key, Func<IServiceProvider, object?, TService> factory)`) before finalizing — confirmed available in .NET 8's `Microsoft.Extensions.DependencyInjection` keyed APIs, already in use elsewhere in this file for `AddKeyedScoped`. |
| Regression test (FR-4) miscounts because `GetServices<IPrintQueueSink>()` only enumerates *non-keyed* registrations, not keyed ones — an implementer might mistakenly expect it to also exclude/include keyed slots | Low | State explicitly in the test: keyed registrations (`"azure"`, `"cups"`) are invisible to `GetServices<IPrintQueueSink>()` by design (ASP.NET Core DI does not merge keyed and non-keyed registrations for enumeration) — only the non-keyed `CombinedPrintQueueSink` factory should appear, count = 1. |

## Specification Amendments

1. **Resolve the spec's Open Question decisively (Decision 1 above):** register `AzureBlobPrintQueueSink` as a concrete singleton inside `AddAzurePrintQueueSinkInfrastructure`, and have both the `"AzureBlob"` non-keyed binding and the `"Combined"` `"azure"` keyed binding resolve to that *same* instance via factory delegates (`provider.GetRequiredService<AzureBlobPrintQueueSink>()`). Do not construct two separate `AzureBlobPrintQueueSink` instances at two different lifetimes — this is wasteful and reintroduces the exact "two things pretending to be one" smell this spec is fixing. Promote the `"Combined"` `"azure"` keyed slot from `AddKeyedScoped` to `AddKeyedSingleton` (via factory) to reflect that it now shares the process-lifetime instance.
2. **FR-4 regression test — add an instance-identity assertion, not just a count assertion.** In addition to asserting `GetServices<IPrintQueueSink>().Count() == 1` and its type is `CombinedPrintQueueSink`, add an assertion that resolving `"AzureBlob"` mode's non-keyed `IPrintQueueSink` and `"Combined"` mode's keyed `"azure"` slot (in separate `BuildProvider` calls, since they're different configs) each still correctly type as `AzureBlobPrintQueueSink` — the existing four tests in `CombinedPrintQueueSinkRegistrationTests` already do this per-mode, so no new test class is needed, just extend the same file per FR-4's instruction.
3. **Doc comment wording:** the spec's sketch describes `AddAzurePrintQueueSinkInfrastructure` as registering `AzureBlobPrintQueueSink` "as itself (a concrete singleton) ... resolvable for keyed/direct construction." Confirm in the XML doc comment that it registers `AzureBlobPrintQueueSink` as a concrete singleton (not just "constructible via DI" as FR-1's acceptance criteria loosely states) — this is now a firm decision, not an option.

## Prerequisites

1. Read `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs` in full before implementing — confirm no scoped dependency was added since this review (constructor takes `BlobContainerClient`, `TimeProvider`, `ILogger<AzureBlobPrintQueueSink>`, all singleton-safe as of this review).
2. Confirm the .NET 8 `AddKeyedSingleton<TService>(this IServiceCollection, object? serviceKey, Func<IServiceProvider, object?, TService> implementationFactory)` overload signature before writing the `"Combined"` case's `"azure"` registration (Decision 1) — match the existing `GetRequiredKeyedService` usage pattern already in the file.
3. Grep for `AddAzurePrintQueueSink` across the whole repo before starting (already done for this review — only `ServiceCollectionExtensions.cs`'s `"AzureBlob"` case is a call site beyond the method's own definition) to satisfy NFR-3; re-run this grep as the final gate before merging, since the spec explicitly asks the implementer to re-verify.
4. No `AzureAdapterModuleTests` file exists today — do not create one speculatively; the existing `CombinedPrintQueueSinkRegistrationTests.cs` (`backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`) is the correct and sufficient home for FR-4's new assertions, per its own `BuildProvider(string printSink)` harness.
5. **Final grep gate before declaring done:** `grep -rn "AddAzurePrintQueueSink\b" backend/` must show exactly two call sites of the *method itself* (its own definition, and the `"AzureBlob"` case in `ServiceCollectionExtensions.cs`), and the `"Combined"` case must show `AddAzurePrintQueueSinkInfrastructure`, not `AddAzurePrintQueueSink`.
