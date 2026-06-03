# Decouple `InvoiceDqtComparer` from Invoices Module Interfaces Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `InvoiceDqtComparer`'s direct dependencies on `IIssuedInvoiceSource` and `IIssuedInvoiceClient` (Invoices-module behavior interfaces) with DataQuality-owned read contracts implemented by adapters in the Invoices module, then enforce the boundary via `ModuleBoundariesTests`.

**Architecture:** Consumer-owned contract pattern. DataQuality declares two narrow interfaces (`IInvoiceShoptetSource`, `IInvoiceErpClient`) under its `Contracts/` folder; Invoices supplies two `internal sealed` adapters that delegate to the existing services; DI bindings live in `InvoicesModule.cs` next to the existing `IInvoiceConsumptionSource` adapter; adapter lifetimes mirror the wrapped services (Singleton for Shoptet source, Scoped for Flexi client). Shared domain DTOs (`IssuedInvoiceDetail`, `IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`, `IssuedInvoiceDetailItem`, `InvoicePrice`) continue to live in `Anela.Heblo.Domain.Features.Invoices` and are referenced via an architecture-test allowlist — same precedent as `Catalog → Manufacture`.

**Tech Stack:** .NET 8, C#, Microsoft.Extensions.DependencyInjection, xUnit, Moq, FluentAssertions.

---

## File Structure

**New files (DataQuality consumer):**
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs` — single-method read contract for issued invoices from Shoptet.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs` — single-method read contract for issued invoices from the ERP.

**New files (Invoices provider):**
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs` — `internal sealed`; wraps `IIssuedInvoiceSource`.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceErpClientAdapter.cs` — `internal sealed`; wraps `IIssuedInvoiceClient`.

**Modified files:**
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — add two DI registrations after the existing `IInvoiceConsumptionSource` line. Lifetimes must mirror the wrapped services exactly: `Singleton` for Shoptet adapter, `Scoped` for Flexi adapter.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs` — swap constructor parameter types; add the `DataQuality.Contracts` using; keep the `Domain.Features.Invoices` using (still needed for DTOs).
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs` — switch `Mock<IIssuedInvoiceSource>` / `Mock<IIssuedInvoiceClient>` to the new contracts.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — add the new `DataQuality -> Invoices` rule plus an allowlist for the shared domain DTO leaks.

**Untouched (verify they still resolve at runtime):**
- `backend/src/Anela.Heblo.API/Program.cs:119` (`IIssuedInvoiceSource` Singleton registration).
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs:93` (`IIssuedInvoiceClient` Scoped registration).
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` (still consumes the original interfaces internally — unchanged).

---

## Task 1: Add `IInvoiceShoptetSource` contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs`

- [ ] **Step 1: Create the contract file**

```csharp
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// DataQuality-owned read contract over the Shoptet issued-invoice source.
/// Provider (Invoices) supplies an adapter — see InvoiceShoptetSourceAdapter.
/// </summary>
public interface IInvoiceShoptetSource
{
    Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(
        IssuedInvoiceSourceQuery query,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Verify the file compiles in isolation**

Run from `backend/`:

```bash
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds (no other references yet, so this is just an interface in a new file).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs
git commit -m "refactor: add IInvoiceShoptetSource contract for DataQuality"
```

---

## Task 2: Add `IInvoiceErpClient` contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs`

- [ ] **Step 1: Create the contract file**

```csharp
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// DataQuality-owned read contract over the ERP issued-invoice client.
/// Provider (Invoices) supplies an adapter — see InvoiceErpClientAdapter.
/// </summary>
public interface IInvoiceErpClient
{
    Task<List<IssuedInvoiceDetail>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
```

- [ ] **Step 2: Verify the file compiles in isolation**

```bash
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs
git commit -m "refactor: add IInvoiceErpClient contract for DataQuality"
```

---

## Task 3: Add `InvoiceShoptetSourceAdapter` (provider-side)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs`

- [ ] **Step 1: Create the adapter**

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

/// <summary>
/// Provider-side adapter binding the DataQuality contract IInvoiceShoptetSource
/// to the Invoices-module IIssuedInvoiceSource. Pure delegation, no business logic.
/// </summary>
internal sealed class InvoiceShoptetSourceAdapter : IInvoiceShoptetSource
{
    private readonly IIssuedInvoiceSource _inner;

    public InvoiceShoptetSourceAdapter(IIssuedInvoiceSource inner)
    {
        _inner = inner;
    }

    public Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(
        IssuedInvoiceSourceQuery query,
        CancellationToken ct = default)
        => _inner.GetAllAsync(query, ct);
}
```

- [ ] **Step 2: Verify the project compiles**

```bash
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs
git commit -m "refactor: add InvoiceShoptetSourceAdapter delegating to IIssuedInvoiceSource"
```

---

## Task 4: Add `InvoiceErpClientAdapter` (provider-side)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceErpClientAdapter.cs`

- [ ] **Step 1: Create the adapter**

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

/// <summary>
/// Provider-side adapter binding the DataQuality contract IInvoiceErpClient
/// to the Invoices-module IIssuedInvoiceClient. Pure delegation, no business logic.
/// </summary>
internal sealed class InvoiceErpClientAdapter : IInvoiceErpClient
{
    private readonly IIssuedInvoiceClient _inner;

    public InvoiceErpClientAdapter(IIssuedInvoiceClient inner)
    {
        _inner = inner;
    }

    public Task<List<IssuedInvoiceDetail>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
        => _inner.GetAllAsync(from, to, ct);
}
```

- [ ] **Step 2: Verify the project compiles**

```bash
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceErpClientAdapter.cs
git commit -m "refactor: add InvoiceErpClientAdapter delegating to IIssuedInvoiceClient"
```

---

## Task 5: Register adapters in `InvoicesModule.cs` with mirrored lifetimes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`

Current state (lines 1–24 relevant):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Bank;
...
        // Cross-module contract: Invoices implements PackingMaterials' IInvoiceConsumptionSource
        // via an adapter. DI registration owned by provider (Invoices), not consumer
        // (PackingMaterials) — keeps the dependency direction inverted properly.
        services.AddScoped<IInvoiceConsumptionSource, InvoiceConsumptionSourceAdapter>();
```

- [ ] **Step 1: Add the new `using` for DataQuality contracts**

In `InvoicesModule.cs`, add this `using` alongside the existing ones (alphabetically — after `Anela.Heblo.Application.Features.Invoices.Services;`):

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
```

- [ ] **Step 2: Register the two new adapters with mirrored lifetimes**

Immediately below the existing `services.AddScoped<IInvoiceConsumptionSource, InvoiceConsumptionSourceAdapter>();` line, add:

```csharp
        // Cross-module contracts: Invoices implements DataQuality's IInvoiceShoptetSource
        // and IInvoiceErpClient via adapters. Lifetimes mirror the wrapped services exactly:
        //   - IIssuedInvoiceSource is registered Singleton in Program.cs:119, so the adapter
        //     must also be Singleton (and DataQuality consumers must resolve it from a Scoped
        //     scope as usual — Singleton from Scoped is legal, the inverse is captive).
        //   - IIssuedInvoiceClient is registered Scoped in FlexiAdapterServiceCollectionExtensions.cs:93,
        //     so the adapter must also be Scoped.
        services.AddSingleton<IInvoiceShoptetSource, InvoiceShoptetSourceAdapter>();
        services.AddScoped<IInvoiceErpClient, InvoiceErpClientAdapter>();
```

- [ ] **Step 3: Build to confirm DI graph compiles**

```bash
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs
git commit -m "refactor: register InvoiceShoptetSource/ErpClient adapters in InvoicesModule"
```

---

## Task 6: Update existing `InvoiceDqtComparerTests` to mock new contracts (write the failing test first)

This task uses TDD: first switch the test's mock types so the tests *fail to compile* (`InvoiceDqtComparer` still asks for the old types), then in Task 7 we swap the comparer's constructor and the build goes green. Behavior of the tests is unchanged.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs`

- [ ] **Step 1: Replace the two `using` lines + the two mock field types**

In `InvoiceDqtComparerTests.cs`:

Replace:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Domain.Features.Invoices;
using Moq;
```

With:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Domain.Features.Invoices;
using Moq;
```

(The `Anela.Heblo.Domain.Features.Invoices` using stays because the tests still construct DTOs like `IssuedInvoiceDetail`, `IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`, `IssuedInvoiceDetailItem`, `InvoicePrice`.)

Then replace the two field declarations:

```csharp
    private readonly Mock<IIssuedInvoiceSource> _sourceMock = new();
    private readonly Mock<IIssuedInvoiceClient> _clientMock = new();
```

With:

```csharp
    private readonly Mock<IInvoiceShoptetSource> _sourceMock = new();
    private readonly Mock<IInvoiceErpClient> _clientMock = new();
```

- [ ] **Step 2: Build the test project to confirm the failing state**

```bash
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build **fails** with a constructor mismatch (`InvoiceDqtComparer(IIssuedInvoiceSource, IIssuedInvoiceClient)` cannot accept `Mock<IInvoiceShoptetSource>.Object`). This proves the change is real and not a no-op. Do **not** commit yet — the comparer must be updated next to make the build go green.

---

## Task 7: Refactor `InvoiceDqtComparer` constructor to consume the new contracts

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs`

- [ ] **Step 1: Add the `DataQuality.Contracts` using and swap constructor parameter types**

In `InvoiceDqtComparer.cs`, replace the top of the file:

```csharp
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Domain.Features.Invoices;
```

With:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Domain.Features.Invoices;
```

(Keep `Anela.Heblo.Domain.Features.Invoices` — it's still needed for DTOs used in the method bodies: `IssuedInvoiceSourceQuery`, `IssuedInvoiceDetailItem`, `InvoicePrice`, etc.)

Then replace the field declarations and constructor:

```csharp
    private readonly IIssuedInvoiceSource _shoptetSource;
    private readonly IIssuedInvoiceClient _flexiClient;

    public InvoiceDqtComparer(IIssuedInvoiceSource shoptetSource, IIssuedInvoiceClient flexiClient)
    {
        _shoptetSource = shoptetSource;
        _flexiClient = flexiClient;
    }
```

With:

```csharp
    private readonly IInvoiceShoptetSource _shoptetSource;
    private readonly IInvoiceErpClient _flexiClient;

    public InvoiceDqtComparer(IInvoiceShoptetSource shoptetSource, IInvoiceErpClient flexiClient)
    {
        _shoptetSource = shoptetSource;
        _flexiClient = flexiClient;
    }
```

Do **not** change anything in the method bodies — `_shoptetSource.GetAllAsync(...)` and `_flexiClient.GetAllAsync(...)` resolve correctly against the new narrower interfaces.

- [ ] **Step 2: Run the test project and confirm the comparer's tests are green**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceDqtComparerTests"
```

Expected: all tests in `InvoiceDqtComparerTests` pass — same counts and assertions as before the refactor.

- [ ] **Step 3: Run a full backend build and format check**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: solution builds with no errors and no new warnings; `dotnet format` reports no diffs.

- [ ] **Step 4: Commit the comparer + test changes together**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs
git commit -m "refactor: switch InvoiceDqtComparer to consumer-owned contracts"
```

---

## Task 8: Add the `DataQuality -> Invoices` module-boundary rule

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

The new rule must enforce that no type under `Anela.Heblo.Application.Features.DataQuality` may reference any namespace under `Anela.Heblo.Domain.Features.Invoices`, `Anela.Heblo.Application.Features.Invoices`, or `Anela.Heblo.Persistence.Invoices` — except for the shared domain DTOs listed in the allowlist.

- [ ] **Step 1: Add the allowlist declaration**

Inside the `ModuleBoundariesTests` class, immediately below the existing `CatalogManufactureAllowlist` declaration (the last `HashSet` before the `Rules()` method, ends ~line 159), add:

```csharp
    // Allowlist for DataQuality -> Invoices. The DataQuality module owns IInvoiceShoptetSource
    // and IInvoiceErpClient (in Application/Features/DataQuality/Contracts/) and consumes
    // them via InvoiceDqtComparer. Shared invoice domain DTOs are referenced on the contracts
    // and inside the comparer; lifting these to a shared kernel is a separate follow-up.
    // Follow-up: extract a DataQuality-owned snapshot DTO and map in the adapters.
    private static readonly HashSet<string> DataQualityInvoicesAllowlist = new(StringComparer.Ordinal)
    {
        // IInvoiceShoptetSource exposes IssuedInvoiceDetailBatch and IssuedInvoiceSourceQuery.
        "Anela.Heblo.Application.Features.DataQuality.Contracts.IInvoiceShoptetSource -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailBatch",
        "Anela.Heblo.Application.Features.DataQuality.Contracts.IInvoiceShoptetSource -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery",

        // IInvoiceErpClient exposes IssuedInvoiceDetail.
        "Anela.Heblo.Application.Features.DataQuality.Contracts.IInvoiceErpClient -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetail",

        // InvoiceDqtComparer consumes shared invoice DTOs internally.
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetail",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailBatch",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailItem",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.InvoicePrice",
    };
```

- [ ] **Step 2: Add the rule entry to `Rules()`**

Inside the `Rules()` method, append a new entry immediately after the existing `"Catalog -> Manufacture"` rule (the last one before the closing `};`), as a final item:

```csharp
        new ModuleBoundaryRule(
            Name: "DataQuality -> Invoices",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.DataQuality",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Invoices",
                "Anela.Heblo.Application.Features.Invoices",
                "Anela.Heblo.Persistence.Invoices",
            },
            Allowlist: DataQualityInvoicesAllowlist),
```

(Watch the trailing comma: the previous last entry ended without one; add one to it as well so the new entry is appended cleanly.)

- [ ] **Step 3: Run the rule and confirm pass**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces"
```

Expected: all theory cases pass — including the new `DataQuality -> Invoices` row.

- [ ] **Step 4: If there are leak entries beyond the allowlist, copy them in and re-run**

If the test fails with violations not in the allowlist (for instance, a DTO this plan did not anticipate is referenced from `InvoiceDqtJobRunner.cs` or some other DataQuality type that's currently consuming Invoices types), the failure message lists every offender in `Consumer.FullName -> Provider.FullName` form. Copy each missing line into `DataQualityInvoicesAllowlist`, add a one-line comment per entry per the existing file's convention, and re-run Step 3 until green. **Do not** add any allowlist entry for `IIssuedInvoiceSource` or `IIssuedInvoiceClient` themselves — those are the behavior interfaces the refactor removed, and any reference to them must be fixed in code, not allowed.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: enforce DataQuality -> Invoices module boundary"
```

---

## Task 9: Full validation — build, format, all tests, DI smoke

These are gates from `CLAUDE.md`'s "Validation before completion" plus a DI resolution smoke check to catch a captive-dependency regression.

**Files:**
- No code changes. Validation only.

- [ ] **Step 1: Full backend build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with no errors and no new warnings.

- [ ] **Step 2: Format check**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: no diffs.

- [ ] **Step 3: Full test run (focus on touched areas)**

```bash
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: green. Pay particular attention to:
- `InvoiceDqtComparerTests` — all 12 test methods still pass.
- `ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces` — the new `DataQuality -> Invoices` row passes.
- All other existing rules (Leaflet, Article, Logistics, PackingMaterials, Purchase, etc.) still pass — the change must not have introduced cross-module leaks elsewhere.

If `InvoiceImportServiceTests` or other test files that still mock `IIssuedInvoiceSource` / `IIssuedInvoiceClient` are affected, that is expected only if behavior changed. Inspect the diff: those tests live in the Invoices module and should not have been touched by this refactor.

- [ ] **Step 4: DI smoke test — confirm `InvoiceDqtComparer` resolves**

This guards against captive-dependency bugs (e.g., a Singleton adapter wrapping a Scoped service would resolve but break at runtime). Because `InvoiceDqtComparer` is registered Scoped and depends on a Singleton (`IInvoiceShoptetSource`) plus a Scoped (`IInvoiceErpClient`), both lifetime combinations are legal — but verify the host can build the DI graph.

```bash
dotnet run --project src/Anela.Heblo.API/Anela.Heblo.API.csproj -- --urls "http://localhost:5099" &
APP_PID=$!
sleep 8
curl -sf http://localhost:5099/health || echo "HEALTH CHECK FAILED"
kill $APP_PID
```

Expected: the app starts (printed `Now listening on...` line), the health check returns 200, and the process is killed cleanly. If the app fails to start with a DI exception mentioning `InvoiceShoptetSourceAdapter`, `InvoiceErpClientAdapter`, or `InvoiceDqtComparer`, fix the adapter lifetimes in `InvoicesModule.cs` before proceeding.

(If a port is in use or the API needs auth to hit `/health`, an alternative smoke check is to run the integration test suite — but the test project as configured should still surface the resolution failure.)

- [ ] **Step 5: Final commit (if validation produced no changes)**

If steps 1–4 succeeded without further edits, there is nothing to commit. If any step revealed a missed import / allowlist row / lifetime mismatch and required a follow-up edit, commit it:

```bash
git add -A
git commit -m "refactor: finalize InvoiceDqtComparer decoupling (validation fixups)"
```

---

## Self-Review

### Spec coverage

| Spec/Arch-review requirement | Task |
|---|---|
| FR-1 — `IInvoiceShoptetSource` (single `GetAllAsync`) under DataQuality | Task 1 |
| FR-1 — `IInvoiceErpClient` (single `GetAllAsync`) under DataQuality | Task 2 |
| FR-2 — `InvoiceShoptetSourceAdapter` delegates only | Task 3 |
| FR-2 — `InvoiceErpClientAdapter` delegates only | Task 4 |
| FR-3 — DI bindings in `InvoicesModule.cs`, existing registrations untouched | Task 5 |
| FR-4 — `InvoiceDqtComparer` ctor takes new contracts, behavior unchanged | Task 7 |
| FR-5 — Existing `InvoiceDqtComparerTests` retargeted at new contracts | Tasks 6 + 7 |
| NFR-1/2 — Performance/security non-changes | Implicit (pure delegation, same call paths) |
| NFR-3 — `dotnet build` + `dotnet format` clean | Task 9 steps 1–2 |
| NFR-4 — Backwards compatibility (existing Invoices consumers untouched) | Untouched files explicit in File Structure; Task 9 step 3 verifies |
| Arch-review Amendment 1 — DI binding in `InvoicesModule.cs` (not `Program.cs`) | Task 5 |
| Arch-review Amendment 2 — Lifetimes: Singleton + Scoped | Task 5 step 2 |
| Arch-review Amendment 3 — `DataQuality -> Invoices` rule with shared-DTO allowlist | Task 8 |
| Arch-review Amendment 4 — Test mock renames at known line numbers | Task 6 |

### Placeholder scan

No "TBD" / "implement later" / "similar to" / "add appropriate error handling" entries. Every code step shows complete code; every command step shows the exact command and expected outcome.

### Type/name consistency

- Contract names: `IInvoiceShoptetSource` and `IInvoiceErpClient` — used identically in Tasks 1, 2, 3, 4, 5, 6, 7, 8.
- Adapter names: `InvoiceShoptetSourceAdapter` and `InvoiceErpClientAdapter` — used identically in Tasks 3, 4, 5, 8.
- Namespaces: `Anela.Heblo.Application.Features.DataQuality.Contracts` and `Anela.Heblo.Application.Features.Invoices.Infrastructure` used consistently.
- Method signatures match across contract, adapter, and consumer (`GetAllAsync(IssuedInvoiceSourceQuery, CancellationToken)` and `GetAllAsync(DateOnly, DateOnly, CancellationToken)`).
- Lifetimes: Shoptet adapter Singleton (mirrors `Program.cs:119`), Erp adapter Scoped (mirrors `FlexiAdapterServiceCollectionExtensions.cs:93`).

Plan is internally consistent. Ready to execute.
