# Specification: ShoptetApiInvoiceSource Unit Test Coverage

## Summary
`ShoptetApiInvoiceSource.GetAllAsync` currently has 18.4% line coverage against a 60% threshold, driven entirely by an integration test that requires live Shoptet API credentials and rarely exercises edge cases. This spec adds a unit test suite that mocks `IShoptetInvoiceClient` to deterministically cover the single-invoice fetch branch (including its null sub-case), the in-memory case-insensitive currency filter, and the null-detail guard in the per-code detail-fetch loop. This is a test-only change — no production code is modified.

## Background
`ShoptetApiInvoiceSource` implements `IIssuedInvoiceSource.GetAllAsync`, the entry point Flexi invoice import uses to pull invoices from Shoptet. It has two operating modes selected by `IssuedInvoiceSourceQuery.QueryByInvoice` (true when `InvoiceId` is set):

1. **Single-invoice mode**: fetches one invoice by ID via `IShoptetInvoiceClient.GetInvoiceAsync`, maps it (or returns an empty list if the client returned null), and wraps it in a single-batch result.
2. **Date-range/list mode**: lists invoice summaries via `ListInvoicesAsync`, filters them in-memory by `query.Currency` (case-insensitive `StringComparison.OrdinalIgnoreCase` on `Price.CurrencyCode`), then fetches full detail for each matching code via `GetInvoiceAsync`, silently dropping any code for which the detail call returns null.

An existing integration test (`ShoptetApiInvoiceSourceIntegrationTests`) exercises the class against the live Shoptet API but is skipped/inert whenever `Shoptet:ApiToken` is not configured (the normal case in CI), which is why real behavioral coverage of this class is near zero. The three gaps the coverage-gap routine flagged are also the class's most safety-critical branches: an unguarded currency filter would silently mix EUR and CZK invoices into one Flexi import batch (duplicate/mis-attributed accounting records), and removing the null-detail guard would throw a `NullReferenceException` mid-batch and abort the entire import run.

## Functional Requirements

### FR-1: Single-invoice fetch returns the mapped invoice when the client finds it
When `query.QueryByInvoice` is `true` (i.e. `query.InvoiceId` is non-null) and `IShoptetInvoiceClient.GetInvoiceAsync` returns a non-null `ShoptetInvoiceDto` for that ID, `GetAllAsync` must return a list containing exactly one `IssuedInvoiceDetailBatch` whose `BatchId` equals `query.RequestId` and whose `Invoices` list contains exactly one mapped invoice corresponding to the returned DTO. `ListInvoicesAsync` must not be called in this mode.

**Acceptance criteria:**
- Mock `IShoptetInvoiceClient.GetInvoiceAsync(query.InvoiceId!, ...)` to return a populated `ShoptetInvoiceDto`; assert the result has exactly 1 batch, `BatchId == query.RequestId`, and `Invoices.Count == 1`.
- Assert the mapped invoice's identifying field (e.g. `Code`/`OrderCode`, matching the mapper's field mapping) reflects the DTO passed in, confirming the real `ShoptetInvoiceMapper` was invoked (not a stub).
- Verify `IShoptetInvoiceClient.ListInvoicesAsync` was never called (`Times.Never`) — single-fetch mode must not also hit the list endpoint.
- Verify `GetInvoiceAsync` was called exactly once with the exact `InvoiceId` from the query.

### FR-2: Single-invoice fetch returns an empty invoice list when the client returns null
When `query.QueryByInvoice` is `true` and `IShoptetInvoiceClient.GetInvoiceAsync` returns `null`, `GetAllAsync` must not throw and must return a list containing exactly one `IssuedInvoiceDetailBatch` with `BatchId == query.RequestId` and an empty (`Count == 0`) `Invoices` list — not `null`.

**Acceptance criteria:**
- Mock `GetInvoiceAsync` to return `(ShoptetInvoiceDto?)null`; call `GetAllAsync` and assert it completes without throwing.
- Assert exactly one batch is returned, `Invoices` is non-null and empty.

### FR-3: List-mode currency filter excludes non-matching currencies (case-insensitive)
In list mode (`QueryByInvoice == false`), after `ListInvoicesAsync` returns a set of invoice summaries with varying `Price.CurrencyCode` values, only summaries whose `CurrencyCode` matches `query.Currency` under `StringComparison.OrdinalIgnoreCase` must have their detail fetched via `GetInvoiceAsync` and appear in the final `Invoices` list. Summaries with a different currency must be excluded from both the detail-fetch calls and the result.

**Acceptance criteria:**
- Arrange `ListInvoicesAsync` to return a mix of summaries, e.g. codes `"A"` (currency `"CZK"`) and `"B"` (currency `"EUR"`), with `query.Currency = "CZK"`.
- Assert `GetInvoiceAsync` is called for code `"A"` only (`Times.Once` for `"A"`, `Times.Never` for `"B"`).
- Assert the final batch's `Invoices` contains exactly the mapped result for `"A"` and does not contain anything derived from `"B"`.

### FR-4: List-mode currency filter comparison is case-insensitive
When a list-mode summary's `Price.CurrencyCode` differs from `query.Currency` only in letter casing (e.g. summary currency `"czk"` vs `query.Currency = "CZK"`, or vice versa), the invoice must still be treated as matching and included in the detail-fetch and result set.

**Acceptance criteria:**
- Arrange one summary with `CurrencyCode = "czk"` (lowercase) and `query.Currency = "CZK"` (uppercase); assert its code is included in the `GetInvoiceAsync` calls and its mapped invoice appears in the result.
- (Optional/combinable with FR-3 as a `[Theory]`) also cover the reverse casing pair to remove ambiguity about which side is case-normalized.

### FR-5: Null individual-detail guard excludes the affected code without aborting the batch
In list mode, when `GetInvoiceAsync` returns `null` for one matched code but returns a non-null DTO for other matched codes, `GetAllAsync` must not throw, must exclude the null-result code from the final `Invoices` list, and must still include the successfully-fetched invoices for the other codes in the same batch.

**Acceptance criteria:**
- Arrange `ListInvoicesAsync` to return two (or more) summaries that both match `query.Currency`, e.g. codes `"A"` and `"B"`.
- Mock `GetInvoiceAsync("A", ...)` to return `null` and `GetInvoiceAsync("B", ...)` to return a populated DTO.
- Assert `GetAllAsync` completes without throwing, returns one batch, and `Invoices.Count == 1` containing only the mapped result for `"B"`.
- Assert `GetInvoiceAsync` was still called for both `"A"` and `"B"` (the loop must not short-circuit/abort on the null result).

## Non-Functional Requirements

### NFR-1: Performance
N/A — this is a test-only change adding unit tests against mocked collaborators; no production code path or runtime performance characteristic changes.

### NFR-2: Security
N/A — no new external calls, secrets, or data handling are introduced. Tests use only in-memory mocks; no live Shoptet API credentials are required or used (this is a unit test suite, distinct from the existing credential-gated integration test).

## Data Model
No new data model. Tests will construct/consume the following existing types:

- `Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery` — `RequestId`, `InvoiceId` (drives `QueryByInvoice`), `DateFrom`, `DateTo`, `Currency` (defaults to `"CZK"`).
- `Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailBatch` — `BatchId`, `Invoices` (`List<IssuedInvoiceDetail>`).
- `Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model.ShoptetInvoiceDto` — the client's return type for `GetInvoiceAsync`/entries of `ListInvoicesAsync`; relevant fields: `Code`, `OrderCode`, `Items` (non-null `List<ShoptetInvoiceItemDto>`, required by the real mapper — must be initialized, even if empty, in every test DTO to avoid a `NullReferenceException` inside `ShoptetInvoiceMapper.Map`), `Price` (`ShoptetInvoicePriceDto?` — `CurrencyCode` is the field the in-memory filter reads).
- `Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model.ShoptetInvoicePriceDto` — carries `CurrencyCode` (string) used both for filtering and for populating the mapped invoice's price.

No changes to any of these types are required or in scope.

## API / Interface Design
Class under test: `Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.ShoptetApiInvoiceSource`, implementing `IIssuedInvoiceSource`.

Constructor dependencies to mock/instantiate in tests:
- `IShoptetInvoiceClient _client` — mock with Moq (`Mock<IShoptetInvoiceClient>`). Relevant members:
  - `Task<IReadOnlyList<ShoptetInvoiceDto>> ListInvoicesAsync(DateTime? dateFrom, DateTime? dateTo, CancellationToken ct = default)`
  - `Task<ShoptetInvoiceDto?> GetInvoiceAsync(string code, CancellationToken ct = default)`
  - (`GetInvoiceRawJsonAsync` is unused by `ShoptetApiInvoiceSource` and needs no setup.)
- `ShoptetInvoiceMapper _mapper` — use the **real** mapper (not mocked), constructed the same way `ShoptetInvoiceMapperTests` does: `new ShoptetInvoiceMapper(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())))`. Using the real mapper keeps the tests focused on `ShoptetApiInvoiceSource`'s own branching logic while still proving the mapper is actually invoked (per FR-1's acceptance criteria) — mirrors the brief's suggested approach and the sibling `ShoptetApiExpeditionListSourceTests` pattern of using real collaborator objects where practical.
- `ILogger<ShoptetApiInvoiceSource> _logger` — `Mock<ILogger<ShoptetApiInvoiceSource>>().Object` (or `NullLogger<ShoptetApiInvoiceSource>.Instance`); no assertions on log output are required by the brief, so a no-op logger is sufficient.

Method under test: `Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query, CancellationToken cancellationToken = default)`.

`CommitAsync`/`FailAsync` are trivial no-ops already at 100% coverage by inspection (`Task.CompletedTask`); no additional tests are needed for them (the existing integration test already asserts they don't throw, which is out of scope to duplicate here).

## Dependencies
- Test project: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests` (`Anela.Heblo.Adapters.Shoptet.Tests.csproj`), which already references the `Anela.Heblo.Adapters.ShoptetApi` project and contains the existing `Integration/ShoptetApiInvoiceSourceIntegrationTests.cs` for this same class. New file: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`, namespace `Anela.Heblo.Adapters.Shoptet.Tests.Unit` (matching sibling files in that folder, e.g. `ShoptetPriceClientTests.cs`).
- Mocking library already in use: **Moq** (`Mock<T>`), via the `Moq` NuGet package already referenced by the test project.
- Assertion library already in use: **FluentAssertions** (`.Should()...`).
- Test framework: **xunit** (`[Fact]` / `[Theory]` + `[InlineData]`), already referenced.
- No new NuGet packages are required — all needed packages (`Moq`, `FluentAssertions`, `xunit`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging`) are already in `Anela.Heblo.Adapters.Shoptet.Tests.csproj`.
- No test fixture/builder class currently exists specifically for `ShoptetInvoiceDto`/`ShoptetInvoicePriceDto` in this project; tests should construct these DTOs inline (as `ShoptetInvoiceMapperTests` and `ShoptetApiExpeditionListSourceTests` both do for their respective DTOs) rather than introducing a new shared builder, to keep the change minimal and self-contained. A small private `static` helper method (e.g. `BuildDto(string code, string currency)`) local to the new test class is acceptable and consistent with the `BuildClient`/`DetailFor` helper pattern in `ShoptetApiExpeditionListSourceTests`.

## Out of Scope
- Any change to `ShoptetApiInvoiceSource.cs`, `ShoptetInvoiceMapper.cs`, `IShoptetInvoiceClient.cs`, or any other production code. Source code was reviewed and contains no correctness bug that blocks testing — the null guard and currency filter behave as intended and are simply untested.
- Testing `ListInvoicesAsync`'s own implementation, pagination, or HTTP behavior (that lives in the concrete `IShoptetInvoiceClient` implementation and its own tests, e.g. `ShoptetOrderClient`-style tests elsewhere) — here it is mocked as a collaborator.
- Testing `CommitAsync` / `FailAsync` beyond what the existing integration test already covers (they are no-ops).
- Testing `ShoptetInvoiceMapper`'s internal mapping correctness (VAT rates, address mapping, rounding, etc.) beyond confirming it is invoked and produces a non-trivial result — that is already covered by `ShoptetInvoiceMapperTests.cs`.
- Modifying or extending the existing `ShoptetApiInvoiceSourceIntegrationTests.cs` (it remains the live-API smoke test; this spec adds a separate, deterministic unit test file alongside it).
- Achieving 100% coverage of the file; the goal is to close the three specifically flagged gaps (and, as a natural byproduct of exercising both `GetAllAsync` branches end-to-end, the currently-uncovered list-mode logging call and batch construction).

## Open Questions
None.

## Status: COMPLETE
