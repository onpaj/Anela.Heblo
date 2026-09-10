# Specification: ShippingMethodMapper Unit Test Coverage

## Summary
`ShippingMethodMapper.Map` — the method that translates a Shoptet invoice's shipping GUID into the domain `ShippingMethod` enum — has 42.1% line coverage against a 60% threshold, and none of its three logical branches are exercised by tests. This spec defines a pure unit-test suite covering all three branches, including the silent-defaulting behavior for unmapped GUIDs, which is the branch most likely to hide a real data-quality bug in production. No production code change is proposed; the mapper's existing behavior is treated as correct and is being specified for test purposes only.

## Background
`ShippingMethodMapper` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Mapping/ShippingMethodMapper.cs`) converts the `guid` field of a Shoptet invoice's shipping block (`ShoptetInvoiceShippingDto`) into the internal `ShippingMethod` enum (`Anela.Heblo.Domain.Features.Invoices.ShippingMethod`: `PickUp`, `PPL`, `PPLParcelShop`, `ZasilkovnaDoRuky`, `Zasilkovna`, `GLS`). The GUID-to-method mapping is externally configured via `ShoptetApiSettings.InvoiceShippingGuidMap` (a `Dictionary<string, ShippingMethod>`, injected as `IOptions<ShoptetApiSettings>`), documented as configurable per environment via `Shoptet:InvoiceShippingGuidMap:{guid}`.

Because `Map` returns `ShippingMethod.PickUp` both when there genuinely is no shipping GUID (self-pickup order) and when the GUID is present but unrecognized (misconfiguration), the two cases are indistinguishable downstream except via the log warning emitted only in the second case. A sibling mapper, `BillingMethodMapper`, already has an established, comparable test file (`BillingMethodMapperTests.cs`) that this suite should mirror in style and location.

This is a test-only task: add coverage for existing, presumed-correct behavior. It does not change `ShippingMethodMapper.cs`, `ShoptetApiSettings.cs`, or the `ShippingMethod` enum.

## Functional Requirements

### FR-1: Cover the "no shipping GUID" path (defaults to PickUp)
When `Map` is called with `shipping == null`, or with a non-null `ShoptetInvoiceShippingDto` whose `Guid` is `null` or `string.Empty`, it must return `ShippingMethod.PickUp` and must **not** log a warning (this is the expected self-pickup case, not an error condition).

**Acceptance criteria:**
- A test calling `Map(null)` asserts the result equals `ShippingMethod.PickUp`.
- A test calling `Map(new ShoptetInvoiceShippingDto { Guid = null })` asserts the result equals `ShippingMethod.PickUp`.
- A test calling `Map(new ShoptetInvoiceShippingDto { Guid = "" })` asserts the result equals `ShippingMethod.PickUp`.
- For all three cases above, the mocked `ILogger<ShippingMethodMapper>` receives **no** call to `LogWarning` (verify via `Mock.Verify(..., Times.Never)` or equivalent, scoped to `LogLevel.Warning`).

### FR-2: Cover the "known GUID" path (configured mapping is honored)
When `shipping.Guid` is non-empty and present as a key in `ShoptetApiSettings.InvoiceShippingGuidMap`, `Map` must return exactly the `ShippingMethod` value configured for that key, and must not log a warning.

**Acceptance criteria:**
- Construct `ShoptetApiSettings` with `InvoiceShippingGuidMap` containing at least two distinct GUID→method entries (e.g. one mapping to `PPL`, one to `Zasilkovna`) to demonstrate the lookup is not coincidentally correct for a single value.
- For each configured entry, `Map(new ShoptetInvoiceShippingDto { Guid = "<that guid>" })` returns the exact configured `ShippingMethod`.
- Recommended: implement as an `xUnit` `[Theory]`/`[InlineData]` (or `[MemberData]`) test, following the pattern already used in `BillingMethodMapperTests.Map_ResolvesByDocumentedNumericId`.
- No `LogWarning` call occurs for these cases.

### FR-3: Cover the "unknown GUID" path (silent default + warning log)
When `shipping.Guid` is non-empty but **not** present in `InvoiceShippingGuidMap`, `Map` must return `ShippingMethod.PickUp` **and** must log exactly one warning via `ILogger<ShippingMethodMapper>.LogWarning`, with the unknown GUID value present in the logged message/state. This is the branch called out in the brief as the most dangerous (silent misclassification), so the test must assert both the return value and the logging side effect — asserting only the return value would not actually close the coverage gap that matters.

**Acceptance criteria:**
- `Map(new ShoptetInvoiceShippingDto { Guid = "unknown-guid-not-in-map" })` returns `ShippingMethod.PickUp`.
- The mocked logger's `Log` method is verified to have been invoked exactly once at `LogLevel.Warning` for this call (via Moq's `Mock<ILogger<T>>.Verify(x => x.Log(LogLevel.Warning, ...), Times.Once())`, matching the codebase's existing pattern for logger verification, e.g. `TierBasedHydrationOrchestratorTests.cs`).
- The verification should assert the log call's formatted state/message contains the GUID value that was passed in (matching the `{Guid}` structured-logging placeholder in the source), so the test would fail if the GUID were dropped from the log message.
- Use an `InvoiceShippingGuidMap` that is non-empty (contains unrelated entries) to prove the lookup genuinely misses rather than trivially operating on an empty dictionary — include at least one variant test with an empty `InvoiceShippingGuidMap` as well, since that is also a realistic configuration state.

### FR-4: Exercise both constructors
`ShippingMethodMapper` has two public constructors: `ShippingMethodMapper(IOptions<ShoptetApiSettings>)` (delegates to `NullLogger<ShippingMethodMapper>.Instance`) and `ShippingMethodMapper(IOptions<ShoptetApiSettings>, ILogger<ShippingMethodMapper>)`. The test suite should primarily use the two-argument constructor with a mocked logger (required for FR-3's log verification), but at least one test should confirm the single-argument constructor works end-to-end without throwing and produces correct `Map` results (covering the delegating constructor line, which contributes to the 60% line-coverage threshold).

**Acceptance criteria:**
- At least one test instantiates `ShippingMethodMapper` via the single-`IOptions`-argument constructor and calls `Map` with a case that does not require log verification (e.g. the null-GUID case from FR-1), asserting the correct result.
- All FR-3 tests use the two-argument constructor since log-warning verification requires the mocked `ILogger`.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — these are synchronous, in-memory unit tests with no I/O. Full suite should execute in well under 1 second.

### NFR-2: Security
Not applicable — no secrets, auth, or external data involved. Test GUID values should be clearly fake/synthetic (e.g. `"11111111-1111-1111-1111-111111111111"` or descriptive placeholder strings like `"known-guid-ppl"`), not copied from any real Shoptet production configuration.

### NFR-3: Isolation
Tests must be pure unit tests with no dependency on a running database, HTTP server, or Shoptet API — consistent with the brief's "no infrastructure" requirement. All dependencies (`IOptions<ShoptetApiSettings>`, `ILogger<ShippingMethodMapper>`) are constructed in-test via `Options.Create(...)` and `Mock<ILogger<...>>`.

### NFR-4: Coverage target
The new tests, combined with existing coverage, must bring `ShippingMethodMapper.cs` line coverage to at least 60% (the stated filter threshold). Given the file is 40 lines with a single method containing 3 branches plus 2 constructors, full coverage of all branches described in FR-1–FR-4 should reach at or near 100% line coverage for this file.

## Data Model

No persistent data model changes. Relevant existing types (all read-only for this task):

- `ShoptetInvoiceShippingDto` (`IssuedInvoices/Model/ShoptetInvoiceShippingDto.cs`): `{ string? Guid; string? Name; }`, JSON-deserialized from Shoptet's `guid`/`name` fields.
- `ShoptetApiSettings` (`Orders/ShoptetApiSettings.cs`): relevant member is `Dictionary<string, ShippingMethod> InvoiceShippingGuidMap`. Note this class also has an unrelated `ShippingGuidMap` (`Dictionary<string,string>`) property — do not confuse the two in test setup; `ShippingMethodMapper` only reads `InvoiceShippingGuidMap`.
- `ShippingMethod` enum (`Anela.Heblo.Domain.Features.Invoices.ShippingMethod`): `PickUp = 0, PPL, PPLParcelShop, ZasilkovnaDoRuky, Zasilkovna, GLS`.

## API / Interface Design

Not applicable — internal mapper class, no public HTTP/API surface. Method under test:

```csharp
public class ShippingMethodMapper
{
    public ShippingMethodMapper(IOptions<ShoptetApiSettings> settings);
    public ShippingMethodMapper(IOptions<ShoptetApiSettings> settings, ILogger<ShippingMethodMapper> logger);
    public ShippingMethod Map(ShoptetInvoiceShippingDto? shipping);
}
```

## Dependencies

- **Test framework**: xUnit (`[Fact]`, `[Theory]`/`[InlineData]`) — matches existing suite conventions.
- **Assertion library**: FluentAssertions (`result.Should().Be(...)`) — matches `BillingMethodMapperTests.cs`.
- **Mocking library**: Moq (`Mock<ILogger<ShippingMethodMapper>>`) — matches `TierBasedHydrationOrchestratorTests.cs` and other suite files.
- **Options construction**: `Microsoft.Extensions.Options.Options.Create(new ShoptetApiSettings { ... })` — the codebase's standard pattern for supplying `IOptions<T>` in tests (see `MetaAdsTransactionSourceTests.cs`, `AnthropicChatClientVisionTests.cs`).
- **File location**: New test file `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs`, placed alongside the existing `BillingMethodMapperTests.cs` in the same directory/namespace (`Anela.Heblo.Tests.Adapters.ShoptetApi`), following that file's structural pattern (no shared constructor/fixture needed given the class's simplicity, though a private helper to build a mapper with a given `InvoiceShippingGuidMap` is reasonable to reduce duplication).
- No new NuGet packages required — all three (xUnit, FluentAssertions, Moq) are already referenced by `Anela.Heblo.Tests`.

## Out of Scope

- Any change to `ShippingMethodMapper.cs`, `ShoptetApiSettings.cs`, or `ShippingMethod.cs` production code.
- Testing the caller(s) of `ShippingMethodMapper` (e.g. the invoice import pipeline that constructs `ShoptetInvoiceShippingDto` from the raw Shoptet API response) — this spec covers only the mapper's own unit tests.
- Adding new `ShippingMethod` enum values or changing what shipping methods Shoptet supports.
- Alerting/monitoring improvements for the "unknown GUID" warning (e.g. promoting it to an error, adding metrics, or reconciliation tooling) — the brief's "why it matters" section identifies this as a real operational risk, but fixing that risk (as opposed to testing the current behavior) is a separate, larger decision addressed below in Open Questions.
- Integration or E2E tests against a live Shoptet store — explicitly excluded per the brief ("pure unit tests, no infrastructure") and per this repo's rule that Shoptet API calls must not be made casually (`docs/integrations/shoptet-api.md`).

## Open Questions

None.

## Status: COMPLETE
