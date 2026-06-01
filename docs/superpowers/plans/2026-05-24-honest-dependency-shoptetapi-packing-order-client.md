# Honest Dependency in ShoptetApiPackingOrderClient — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the dishonest `IEshopOrderClient` → `ShoptetOrderClient` runtime downcast in `ShoptetApiPackingOrderClient` by depending on the concrete `ShoptetOrderClient` directly, and adjust the typed-HttpClient registration so both `ShoptetOrderClient` and `IEshopOrderClient` resolve from a single typed-client configuration.

**Architecture:** Pure structural refactor inside the `Anela.Heblo.Adapters.ShoptetApi` adapter assembly. The constructor of `ShoptetApiPackingOrderClient` is changed from `IEshopOrderClient` to `ShoptetOrderClient` and the cast/throw is dropped. The DI registration is inverted: `AddHttpClient<ShoptetOrderClient>(...)` registers the typed client on the concrete class, and `IEshopOrderClient` is forwarded via a transient factory (`sp => sp.GetRequiredService<ShoptetOrderClient>()`). No new abstractions, no behavior changes, no new tests required — existing `ShoptetApiPackingOrderClientTests` already cover the happy paths and remain source-compatible.

**Tech Stack:** .NET 8, C#, xUnit, FluentAssertions, Moq, Microsoft.Extensions.Http (typed `HttpClient` via `HttpClientFactory`), Microsoft.Extensions.DependencyInjection.

---

## File Structure

Files touched (no new files):

- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` — change constructor parameter type from `IEshopOrderClient` to `ShoptetOrderClient`; remove the `as`/throw cast.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — replace `AddHttpClient<IEshopOrderClient, ShoptetOrderClient>(...)` with `AddHttpClient<ShoptetOrderClient>(...)` plus `AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>())`.

Files validated (must continue to pass / compile, but NOT edited):

- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs` — already passes a real `ShoptetOrderClient` (built around `FakeDelegatingHandler`); change is source-compatible.
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs` — consumer of `IEshopOrderClient`; must continue resolving to the same underlying `ShoptetOrderClient`.
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — same as above.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — has an **identical downcast pattern** at line 35. **Out of scope for this ticket** per arch-review; file a follow-up bug instead. Do NOT modify here.

---

## Pre-flight: Baseline before change

### Task 0: Establish a green baseline

**Files:** none modified.

- [ ] **Step 1: Build the backend solution to confirm a clean starting point**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded` with 0 errors. (Warnings are acceptable.)

- [ ] **Step 2: Run the affected unit test project to confirm a green baseline**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetApiPackingOrderClientTests" --no-build
```
Expected: 10 tests pass, 0 fail.

If either step fails, STOP. The repository is not in a clean state; do not begin the refactor.

---

## Task 1: Update the typed-HttpClient registration in the DI module

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs:37-42`

**Why first:** If the production constructor is changed before the DI is updated, the next `dotnet build` is still clean (it's only a parameter type change), but the application would fail at boot when resolving `ShoptetApiPackingOrderClient` because `ShoptetOrderClient` is not yet registered as a concrete type. Doing the DI fix first keeps every intermediate commit boot-safe.

- [ ] **Step 1: Replace the typed-client registration**

Open `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`.

Find this block (lines 37-42):

```csharp
services.AddHttpClient<IEshopOrderClient, ShoptetOrderClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
});
```

Replace it with:

```csharp
services.AddHttpClient<ShoptetOrderClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
});
services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());
```

Leave every other `AddHttpClient<...>` block in this file untouched.

- [ ] **Step 2: Verify the file still compiles**

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 3: Apply formatter**

Run:
```bash
dotnet format backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: command completes without errors. No prompt; the file is reformatted in place if needed.

- [ ] **Step 4: Re-run the existing unit tests to confirm nothing regressed**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetApiPackingOrderClientTests"
```
Expected: 10 tests pass. (The constructor of `ShoptetApiPackingOrderClient` is still typed `IEshopOrderClient` at this point; the tests pass a `ShoptetOrderClient` which implicitly converts. The cast in the constructor body still runs and still succeeds.)

- [ ] **Step 5: Commit the DI change**

Run:
```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs
git commit -m "refactor(shoptet-api): register ShoptetOrderClient typed client as concrete, forward IEshopOrderClient"
```

---

## Task 2: Remove the dishonest cast in `ShoptetApiPackingOrderClient`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs:24-39`

The private field `_orderClient` is already typed `ShoptetOrderClient` (line 18) — no change there. Only the constructor parameter and the body of the assignment change.

- [ ] **Step 1: Change the constructor signature and remove the cast**

Open `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`.

Find this block (lines 24-39):

```csharp
    public ShoptetApiPackingOrderClient(
        IEshopOrderClient orderClient,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling,
        ILogger<ShoptetApiPackingOrderClient> logger,
        IOptions<ShoptetApiSettings> settings)
    {
        _orderClient = orderClient as ShoptetOrderClient
            ?? throw new InvalidOperationException(
                $"{nameof(IEshopOrderClient)} must be {nameof(ShoptetOrderClient)} " +
                $"but got {orderClient.GetType().Name}.");
        _catalog = catalog;
        _carrierCooling = carrierCooling;
        _logger = logger;
        _defaultItemWeightGrams = settings.Value.DefaultItemWeightGrams;
    }
```

Replace it with:

```csharp
    public ShoptetApiPackingOrderClient(
        ShoptetOrderClient orderClient,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling,
        ILogger<ShoptetApiPackingOrderClient> logger,
        IOptions<ShoptetApiSettings> settings)
    {
        _orderClient = orderClient;
        _catalog = catalog;
        _carrierCooling = carrierCooling;
        _logger = logger;
        _defaultItemWeightGrams = settings.Value.DefaultItemWeightGrams;
    }
```

- [ ] **Step 2: Remove the now-unused `using` for the `IEshopOrderClient` namespace if it is no longer referenced**

Check whether `Anela.Heblo.Application.Features.ShoptetOrders` (the namespace `IEshopOrderClient` lives in) is still needed by other symbols in this file (e.g., `ExpeditionOrderDetail`, `IPackingOrderClient`, settings types).

Run:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: `Build succeeded` with 0 errors.

If the build is clean and `dotnet format` (next step) does not remove the using directive, leave the using list alone. **Do not perform surgical cleanups of unrelated usings** — the project rule is "surgical changes".

- [ ] **Step 3: Apply formatter to the adapter project**

Run:
```bash
dotnet format backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: command completes without errors. `dotnet format` will remove any using that is genuinely unused as part of analyzer fixups.

- [ ] **Step 4: Build the whole backend solution to confirm no consumer broke**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded` with 0 errors. (Compile-time check that no other production code was passing something other than `ShoptetOrderClient` to this constructor.)

- [ ] **Step 5: Run the targeted unit tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetApiPackingOrderClientTests"
```
Expected: 10 tests pass. The test helper `BuildSut` already constructs a `ShoptetOrderClient` and passes it in (see `ShoptetApiPackingOrderClientTests.cs:78-87`), so no test edit is needed.

If any test fails because the test was relying on the cast/throw behavior (it should not — none of the 10 tests exercise that path), do NOT delete the test. Report the failure and stop; the spec premise needs revisiting.

- [ ] **Step 6: Commit the production code change**

Run:
```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs
git commit -m "refactor(shoptet-api): take ShoptetOrderClient directly in ShoptetApiPackingOrderClient (drop downcast)"
```

---

## Task 3: Full-solution validation

**Files:** none modified.

- [ ] **Step 1: Run the full backend test suite**

Run:
```bash
dotnet test backend/Anela.Heblo.sln --no-build
```
Expected: All tests pass. In particular, the following test files must remain green because they depend (directly or transitively) on the changed types:
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiPackingOrderClientTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/*` (these mock or wire `IEshopOrderClient`; the interface is unchanged, so they should continue passing)

If `dotnet test` reports `--no-build` cannot find binaries, fall back to:
```bash
dotnet test backend/Anela.Heblo.sln
```

- [ ] **Step 2: Sanity-check DI resolution doesn't throw at boot**

This refactor changes the DI registration shape. The cheapest way to confirm the composition root still composes is to run the API project's startup path. If a quick boot test is available, use it; otherwise run the API in build-only mode:

Run:
```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```
Expected: `Build succeeded`. A real boot test is not required because no controller/handler depends on the *concrete* `ShoptetOrderClient` at construction time other than `ShoptetApiPackingOrderClient` itself (verified at compile time by Step 1 of Task 2).

If there is a startup integration test (search for `WebApplicationFactory<Program>` in `backend/test/`), run that test specifically:
```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~WebApplicationFactory" --no-build
```
Expected: pass.

- [ ] **Step 3: No-op commit guard**

If Steps 1-2 added any modifications (they should not — those are read-only commands), run:
```bash
git status
```
Expected: `nothing to commit, working tree clean`.

If `dotnet format` from Task 2 Step 3 left untracked formatting changes that didn't get included in the Task 2 commit, decide whether they belong to Task 1 or Task 2 by file, and amend the appropriate commit OR create a `chore: dotnet format` commit. Do not push a half-formatted tree.

---

## Out of Scope (do NOT do in this plan)

1. **Do NOT fix the identical downcast in `ShoptetApiExpeditionListSource.cs:35`.** It's the same smell and worth a follow-up ticket, but the spec explicitly excludes it. Per the project's "surgical changes" rule, do not touch adjacent code.
2. **Do NOT add an `IExpeditionOrderDetailClient` narrow interface.** Spec rejects it as YAGNI (single consumer until/unless a second appears).
3. **Do NOT add `GetExpeditionOrderDetailAsync` to `IEshopOrderClient`.** Out of scope; would pollute the interface for the single consumer.
4. **Do NOT add new unit tests.** FR-4 of the original spec was based on a false premise (the arch review corrected it). The 10 existing `ShoptetApiPackingOrderClientTests` already cover the happy paths. Adding a redundant test would violate YAGNI.
5. **Do NOT refactor `ShoptetOrderClient` for "easier mockability".** Out of scope; existing `FakeDelegatingHandler` pattern works.

---

## Done When

- `dotnet build backend/Anela.Heblo.sln` is green.
- `dotnet test backend/Anela.Heblo.sln` is green.
- `dotnet format backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj` produces no further changes.
- `ShoptetApiPackingOrderClient` constructor declares `ShoptetOrderClient orderClient` (not `IEshopOrderClient`); no `as` cast, no `InvalidOperationException` for the order-client parameter.
- DI registration uses `AddHttpClient<ShoptetOrderClient>(...)` and forwards `IEshopOrderClient` via `AddTransient`.
- Two commits exist on the branch — one for the DI change, one for the production code change. (A third `chore: dotnet format` commit is acceptable if formatting needed to be split off.)
- `ShoptetApiExpeditionListSource.cs` is **untouched** (the identical pattern there is a separate follow-up).
