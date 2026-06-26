I have enough context. The `Invoices` module already uses the exact `IInterface, Implementation` registration pattern this refactor proposes — the change brings `MarketingInvoices` into alignment with an existing project convention. A dedicated `MarketingInvoiceImportServiceTests` class already exists, so FR-6 is trivially satisfied.

# Architecture Review: Decouple ImportMarketingInvoicesHandler via IMarketingInvoiceImportService

## Skip Design: true

## Architectural Fit Assessment

This refactor is a near-perfect fit for the codebase. The sibling `Invoices` feature slice already follows the exact pattern proposed here:

- `IInvoiceImportService` lives in `Features/Invoices/Services/` alongside its implementation `InvoiceImportService`.
- `InvoicesModule.cs:28` registers it as `services.AddScoped<IInvoiceImportService, InvoiceImportService>();` — byte-for-byte the form the spec prescribes for `MarketingInvoicesModule.cs:13`.

The `MarketingInvoices` slice is the outlier: it registers the concrete service and its handler binds to the concrete type, while the same slice already binds `IImportedMarketingTransactionRepository` and `IMarketingTransactionSource` to interfaces. The proposal restores internal symmetry rather than introducing a new abstraction.

Integration points:
- **MediatR pipeline** — handler resolution is unchanged; `IRequestHandler<ImportMarketingInvoicesRequest, ImportMarketingInvoicesResponse>` continues to be the sole external contract.
- **DI container** — single registration line change. No assembly scanning, no convention-based binding to disturb.
- **Test project** — Moq is already in use against `IImportedMarketingTransactionRepository` and `IMarketingTransactionSource` in the same test file; reusing it for the new interface introduces no new dependencies.
- **Hangfire job (caller of the handler)** — untouched; the catch-log-rethrow contract documented in `ImportMarketingInvoicesHandler.cs:50–51` is preserved.

A dedicated `MarketingInvoiceImportServiceTests` class already exists at `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — service-level coverage is independent of the handler test today, so FR-6 imposes no extra work.

## Proposed Architecture

### Component Overview

```
        ┌─────────────────────────────────────────────┐
        │ Hangfire job / MediatR caller               │
        └─────────────────────────────────────────────┘
                          │ Send(ImportMarketingInvoicesRequest)
                          ▼
        ┌─────────────────────────────────────────────┐
        │ ImportMarketingInvoicesHandler              │   <-- high-level policy
        │  - IEnumerable<IMarketingTransactionSource> │
        │  - IMarketingInvoiceImportService  ◄── NEW  │   <-- depends on abstraction
        │  - ILogger                                  │
        └─────────────────────────────────────────────┘
                          │ ImportAsync(source, from, to, ct)
                          ▼
        ┌─────────────────────────────────────────────┐
        │ IMarketingInvoiceImportService   ◄── NEW    │   <-- abstraction (port)
        └─────────────────────────────────────────────┘
                          ▲
                          │ implements
        ┌─────────────────────────────────────────────┐
        │ MarketingInvoiceImportService               │   <-- low-level detail
        │  - IImportedMarketingTransactionRepository  │
        │  - ILogger                                  │
        └─────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Place the interface alongside the implementation (`Features/MarketingInvoices/Services/`)

**Options considered:**
- (A) Co-locate `IMarketingInvoiceImportService.cs` next to `MarketingInvoiceImportService.cs` in `Services/`.
- (B) Place the interface in `Domain/Features/MarketingInvoices/` as a port consumed by the application layer.
- (C) Place the interface in a `Contracts/` sub-folder, mirroring the `Purchase`/`Logistics` slices.

**Chosen approach:** (A). Co-locate in `Features/MarketingInvoices/Services/IMarketingInvoiceImportService.cs`.

**Rationale:** This is the exact location and naming used by `IInvoiceImportService` in the sibling `Invoices` slice — the most direct precedent in the codebase. The interface's parameter types include `IMarketingTransactionSource` (already in `Domain`) and `MarketingImportResult` (in `Application/Features/MarketingInvoices`), so placing the interface in `Domain` (option B) would create a downward namespace reference. Option C is reserved in this codebase for cross-slice contracts, which this is not.

#### Decision 2: Single-method interface that mirrors the current public surface — no additional members

**Options considered:**
- (A) Add only `ImportAsync` (the sole method the handler currently calls).
- (B) Expose all public members of `MarketingInvoiceImportService`.

**Chosen approach:** (A). One method only.

**Rationale:** `MarketingInvoiceImportService.cs` exposes exactly one public method today (`ImportAsync`, lines 19–23). YAGNI: do not pre-expose surface that no caller needs. If a future caller needs more, the interface can grow.

#### Decision 3: Keep the implementation class `public` and unsealed

**Options considered:**
- (A) Leave `MarketingInvoiceImportService` as-is (`public class`).
- (B) Mark `sealed` to discourage inheritance.
- (C) Change to `internal` now that callers depend on the interface.

**Chosen approach:** (A). Status quo.

**Rationale:** The spec is explicit that internals and visibility are out of scope (Out of Scope, line 4). Sealing or changing access is scope creep and risks breaking `MarketingInvoiceImportServiceTests`, which constructs the class directly.

#### Decision 4: Do not touch `MarketingInvoiceImportServiceTests`

**Options considered:**
- (A) Leave the existing test class untouched; it already constructs the concrete service and asserts service behavior.
- (B) Re-point it through the interface for consistency.

**Chosen approach:** (A).

**Rationale:** Service tests should exercise the concrete service — that is their purpose. Routing through the interface adds no value and is out of scope per the spec.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/MarketingInvoices/
├── MarketingInvoicesModule.cs            # MODIFY line 13
├── MarketingImportResult.cs              # untouched
├── Services/
│   ├── IMarketingInvoiceImportService.cs # NEW
│   └── MarketingInvoiceImportService.cs  # MODIFY class declaration only
└── UseCases/ImportMarketingInvoices/
    ├── ImportMarketingInvoicesHandler.cs # MODIFY field type (l.12) + ctor param (l.17)
    ├── ImportMarketingInvoicesRequest.cs # untouched
    └── ImportMarketingInvoicesResponse.cs# untouched

backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/
├── ImportMarketingInvoicesHandlerTests.cs       # MODIFY (mock the interface)
└── MarketingInvoiceImportServiceTests.cs        # untouched — preserves FR-6 coverage
```

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/IMarketingInvoiceImportService.cs
using Anela.Heblo.Domain.Features.MarketingInvoices;

namespace Anela.Heblo.Application.Features.MarketingInvoices.Services;

public interface IMarketingInvoiceImportService
{
    Task<MarketingImportResult> ImportAsync(
        IMarketingTransactionSource source,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}
```

Note on namespaces: `MarketingImportResult` lives in `Anela.Heblo.Application.Features.MarketingInvoices` (root of the slice), and the interface namespace is `Anela.Heblo.Application.Features.MarketingInvoices.Services` — so `MarketingImportResult` is reachable without an additional `using`. `IMarketingTransactionSource` lives in `Anela.Heblo.Domain.Features.MarketingInvoices` and **does** require a `using`.

Handler edit (surgical):

```csharp
// ImportMarketingInvoicesHandler.cs
private readonly IMarketingInvoiceImportService _importService;   // line 12

public ImportMarketingInvoicesHandler(
    IEnumerable<IMarketingTransactionSource> sources,
    IMarketingInvoiceImportService importService,                  // line 17
    ILogger<ImportMarketingInvoicesHandler> logger)
```

Module edit:

```csharp
// MarketingInvoicesModule.cs:13
services.AddScoped<IMarketingInvoiceImportService, MarketingInvoiceImportService>();
```

### Data Flow

Unchanged at runtime. The container now resolves `IMarketingInvoiceImportService` → `MarketingInvoiceImportService` instead of resolving the concrete type directly. One additional vtable indirection per request — negligible.

Test-time data flow changes:

| Before | After |
|---|---|
| `CreateHandler` → `CreateService()` → `new MarketingInvoiceImportService(mockRepo, ...)` → handler calls real service which calls `mockRepo` | `CreateHandler` → handler calls `Mock<IMarketingInvoiceImportService>` which returns a stub `MarketingImportResult` |

The handler test will no longer need the repository mock and will assert handler behavior only (source selection, error paths, response mapping). The service's repository interactions remain covered by `MarketingInvoiceImportServiceTests`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing handler test's `Handle_SelectsSourceMatchingPlatform_AndMapsResult` asserts `Imported == 1`, which today depends on real service behavior (counting one staged transaction). After mocking the interface, the test must instead stub `ImportAsync` to return `new MarketingImportResult { Imported = 1 }`. | Low | Explicitly stub the interface mock's `ImportAsync` to return the expected `MarketingImportResult`; verify the handler maps `Imported`/`Skipped`/`Failed` and selects the correct source via `meta.Verify(...)` / `google.Verify(...)`. |
| `ImportMarketingInvoicesResponse.Success` is asserted in the existing test (line 57) but is inherited from `BaseResponse` and not set by the handler explicitly. The default value must continue to flow through. | Low | No code change needed; verify the test still passes. If `BaseResponse.Success` defaults to `true`, the assertion is unaffected. |
| DI consumer outside this module accidentally resolves the concrete `MarketingInvoiceImportService` (would fail after the registration changes to interface-only). | Low | Grep for `MarketingInvoiceImportService` references outside `Services/`, `MarketingInvoicesModule.cs`, the handler, and the service's own test file before merging. Today only the handler and the service test reference it. |
| Scope creep: temptation to introduce `IMarketingTransactionSource`-style abstractions for other concrete dependencies in the slice. | Low | Spec explicitly lists this as Out of Scope; reviewer should reject any unrelated abstractions in this PR. |
| Test verification of `_mockRepository` interactions in `Handle_SelectsSourceMatchingPlatform_AndMapsResult` becomes meaningless once the service is mocked. | Low | Remove the now-irrelevant `_mockRepository.Setup(...)` lines for `ExistsAsync`/`AddAsync`/`SaveChangesAsync` in that test, and consider removing the `_mockRepository` field from the test class if no remaining test references it. |

## Specification Amendments

1. **Clarify FR-5 — handler test must re-stub return value.** Add an explicit acceptance criterion: "The mocked `IMarketingInvoiceImportService.ImportAsync` returns a `MarketingImportResult` whose `Imported`, `Skipped`, `Failed` fields match the assertions in each test scenario." Without this, the existing `Imported == 1` assertion in `Handle_SelectsSourceMatchingPlatform_AndMapsResult` (line 59 of the test file) will fail.

2. **Clarify FR-5 — remove now-unused repository mock setups.** Add: "The `_mockRepository.Setup(...)` calls for `ExistsAsync`, `AddAsync`, and `SaveChangesAsync` in `Handle_SelectsSourceMatchingPlatform_AndMapsResult` are removed since the service is no longer invoked. If no test scenario still uses `_mockRepository`, the field is removed from the test class entirely."

3. **FR-6 is already satisfied** — note in the spec that `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` already exists and owns service-level coverage; the spec's "(b) note in a comment" branch is not needed.

4. **Resolve `Handle_SourceThrows_ExceptionPropagates`.** This test today asserts that an exception from `source.GetTransactionsAsync` propagates through the handler. With the service mocked, this no longer goes through `source` at all — the mocked `IMarketingInvoiceImportService.ImportAsync` must be set up to throw `HttpRequestException` instead. Update the spec to call out this rewrite explicitly so the test continues to assert "handler does not swallow import-time exceptions" (the contract documented in `ImportMarketingInvoicesHandler.cs:50–51`).

5. **Specify file path.** FR-1 says "`Services/IMarketingInvoiceImportService.cs` exists in the `MarketingInvoices` feature folder" — make the path fully qualified to match the convention used elsewhere in the spec: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/IMarketingInvoiceImportService.cs`.

## Prerequisites

None. No migrations, no configuration, no infrastructure changes. The implementation can begin immediately:

- No new NuGet packages.
- No environment variables.
- No database schema work.
- No coordination with any other feature slice.
- Existing test infrastructure (Moq, xUnit, `NullLogger`) is sufficient.