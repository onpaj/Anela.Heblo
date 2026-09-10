# Architecture Review: ShippingMethodMapper Unit Test Coverage

## Skip Design: true

Confirmed — this is a pure backend unit-test addition to `backend/test/Anela.Heblo.Tests/`. No production code, no API surface, no UI component, and no new visual element is touched or introduced. There is no design work to scope here.

## Architectural Fit Assessment

This fits cleanly and trivially into the existing test architecture — it's the textbook "add a missing sibling test file" case. `ShippingMethodMapper` lives in the ShoptetApi adapter's `IssuedInvoices/Mapping` folder alongside `BillingMethodMapper`, and `BillingMethodMapperTests.cs` already establishes the exact pattern to mirror: a plain `xUnit` class in `Anela.Heblo.Tests.Adapters.ShoptetApi`, `[Theory]`/`[InlineData]` for enumerated mappings, `[Fact]` for edge cases, and `FluentAssertions` for assertions. No new abstractions, no new module boundaries, no DI wiring changes. The only element not present in the `BillingMethodMapper` sibling is logger verification (`BillingMethodMapper` takes no logger), but that pattern is well established elsewhere in this same test project — verified directly in `InvoiceImportServiceTests.cs` (same `Invoices` feature area) using `Mock<ILogger<T>>.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>(...), ..., It.IsAny<Func<...>>()), Times.Once)`. This is the idiom the new test should reuse verbatim rather than inventing a new one.

Integration point: none beyond the test project. `ShippingMethodMapper.cs`, `ShoptetApiSettings.cs`, and `ShippingMethod.cs` are all explicitly out of scope and confirmed unchanged after reading the source.

## Proposed Architecture

### Component Overview

No new components. One new leaf test file added to an existing test module:

```
backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/
├── BillingMethodMapperTests.cs      (existing — style template)
└── ShippingMethodMapperTests.cs     (new — this task)
```

Test targets the existing production class directly, no new seams required:

```
ShippingMethodMapperTests
        │  (constructs via Options.Create + Mock<ILogger<ShippingMethodMapper>>)
        ▼
ShippingMethodMapper.Map(ShoptetInvoiceShippingDto?)
        │
        ├─ null/empty Guid ───────────────► ShippingMethod.PickUp   (no log)
        ├─ Guid found in InvoiceShippingGuidMap ─► configured ShippingMethod (no log)
        └─ Guid not found ────────────────► ShippingMethod.PickUp   + LogWarning("...{Guid}...", guid)
```

### Key Design Decisions

#### Decision 1: Logger verification idiom
**Options considered:** (a) Custom `ITestLogger`/capturing sink; (b) `Mock<ILogger<T>>` with the standard Moq `x.Log(LogLevel, EventId, It.Is<It.IsAnyType>(...), exception, formatter)` verify shape.
**Chosen approach:** (b) — `Mock<ILogger<ShippingMethodMapper>>`, matching `InvoiceImportServiceTests.cs` and `TierBasedHydrationOrchestratorTests.cs`.
**Rationale:** This is already the codebase's established idiom for verifying structured-log calls including message content (via `v.ToString()!.Contains(...)` on the `It.Is<It.IsAnyType>` matcher). Introducing anything else would be inconsistent for zero benefit.

#### Decision 2: Options construction
**Options considered:** (a) `Mock<IOptions<ShoptetApiSettings>>` with `.Setup(x => x.Value)`; (b) `Options.Create(new ShoptetApiSettings { ... })`.
**Chosen approach:** (b), per spec and confirmed as the standing convention (e.g. referenced `MetaAdsTransactionSourceTests.cs` pattern).
**Rationale:** `Options.Create` is simpler, avoids an unnecessary mock, and is what sibling tests in this codebase already do for `IOptions<T>`.

#### Decision 3: Test grouping — Theory vs. separate Facts
**Options considered:** (a) One `[Theory]` covering all three branches with a parameter indicating expected log-call count; (b) Separate `[Fact]`/`[Theory]` per branch (FR-1 facts, FR-2 theory, FR-3 facts).
**Chosen approach:** (b), as specified in the spec's FR breakdown.
**Rationale:** Log-verification assertions differ qualitatively by branch (no-log vs. exactly-one-warning-with-content-check), which doesn't compress well into a single parameterized shape without losing clarity — matches `BillingMethodMapperTests`' existing mix of one `[Theory]` (pure value-mapping) plus separate `[Fact]`s (edge cases).

## Implementation Guidance

### Directory / Module Structure

New file only:
`backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs`

Namespace: `Anela.Heblo.Tests.Adapters.ShoptetApi` (matches the sibling file and directory).

No `.csproj` changes — `Anela.Heblo.Tests` already references xUnit, Moq, and FluentAssertions (confirmed via existing usages in `BillingMethodMapperTests.cs` and `InvoiceImportServiceTests.cs`).

### Interfaces and Contracts

No new interfaces. The test exercises the existing public surface only:

```csharp
public ShippingMethodMapper(IOptions<ShoptetApiSettings> settings);
public ShippingMethodMapper(IOptions<ShoptetApiSettings> settings, ILogger<ShippingMethodMapper> logger);
public ShippingMethod Map(ShoptetInvoiceShippingDto? shipping);
```

Recommended private test helper (keeps FR-2/FR-3 setup DRY, consistent with the spec's suggestion):

```csharp
private static ShippingMethodMapper CreateMapper(
    Dictionary<string, ShippingMethod>? guidMap,
    out Mock<ILogger<ShippingMethodMapper>> loggerMock)
{
    loggerMock = new Mock<ILogger<ShippingMethodMapper>>();
    var settings = Options.Create(new ShoptetApiSettings
    {
        InvoiceShippingGuidMap = guidMap ?? new()
    });
    return new ShippingMethodMapper(settings, loggerMock.Object);
}
```

Log-warning verification, mirroring `InvoiceImportServiceTests.cs` exactly:

```csharp
loggerMock.Verify(
    x => x.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(unknownGuid)),
        null,
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Once);
```

And for the "no warning" assertions (FR-1, FR-2):

```csharp
loggerMock.Verify(
    x => x.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Never);
```

### Data Flow

Not applicable beyond the diagram above — this is a single pure-function unit under test with no external I/O, matching NFR-1/NFR-3 in the spec. Every test constructs its own `IOptions<ShoptetApiSettings>` and `Mock<ILogger<...>>` inline or via the helper; no shared fixture, no `IClassFixture`, no test server.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Moq's `It.Is<It.IsAnyType>` matcher signature is version-sensitive and easy to get subtly wrong (fails silently as "0 invocations matched" rather than a compile error) | Low | Copy the exact lambda shape from `InvoiceImportServiceTests.cs` (already confirmed working in this codebase) rather than writing it from scratch |
| Confusing `InvoiceShippingGuidMap` (`Dictionary<string, ShippingMethod>`) with the unrelated `ShippingGuidMap` (`Dictionary<string, string>`) also on `ShoptetApiSettings` | Low | Spec already flags this explicitly (Data Model section); the `CreateMapper` helper above only ever touches `InvoiceShippingGuidMap`, making the mistake structurally hard to make |
| New test file nudges the file's line coverage past 60% but a stray untested line (e.g. an added future branch) could still leave the CI gate red | Low | FR-4's explicit coverage of both constructors plus all three `Map` branches gets this 40-line file to ~100%; no action needed beyond following the spec's FRs literally |

No medium/high risks identified — this is a self-contained, zero-blast-radius test addition.

## Specification Amendments

None required. The spec is implementation-ready as written: it correctly identifies the sibling test file to mirror, correctly separates the "no log" vs. "exactly one warning log with GUID content" assertions per branch, and correctly flags the `InvoiceShippingGuidMap`/`ShippingGuidMap` naming trap. The one addition worth calling out explicitly to the implementer (not a spec defect, just a pointer they'll want): use `InvoiceImportServiceTests.cs`'s `Log(...)` verify lambda as the copy-paste source for FR-3's logger-content assertion — it's a closer, already-confirmed-working match than `TierBasedHydrationOrchestratorTests.cs`, which the spec cites but which (on inspection) doesn't itself contain a `.Log(...)` content-matching verify to copy from.

## Prerequisites

None. No migrations, no config, no infrastructure. All required test dependencies (xUnit, Moq, FluentAssertions) are already referenced by the `Anela.Heblo.Tests` project. Implementation can start immediately.
