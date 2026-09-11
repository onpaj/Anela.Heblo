# Design: Unit test coverage for `VatClassificationRule.Evaluate`

## Component Design

**`VatClassificationRuleTests`** (new)
`backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/VatClassificationRuleTests.cs`
Namespace: `Anela.Heblo.Tests.Features.InvoiceClassification.Rules`

- xUnit test class, structured like its four siblings in the same folder.
- SUT held as `private readonly VatClassificationRule _sut = new();`.
- Responsibility: exercise `VatClassificationRule.Evaluate(ReceivedInvoice, string)` to 100% line coverage — no other component under test.
- Test layout:
  - One `[Theory]`/`[InlineData]` block over `(companyVat, pattern, expected)` tuples for the fixture-buildable cases: exact match, case-insensitive match, non-match, leading/trailing whitespace (either/both sides), internal-whitespace non-match, empty/whitespace-only values. Invoices built via `InvoiceClassificationFixtures.CreateInvoice(companyVat: ...)`.
  - Separate `[Fact]`s for the null-`CompanyVat` cases, since `ReceivedInvoice.CompanyVat` is non-nullable and the fixture stays non-nullable (Decision 3 of the arch review) — these construct `new ReceivedInvoice { CompanyVat = null! }` directly, matching the `CompanyNameClassificationRuleTests` precedent.
  - `[Fact]`s (or `null!` inline args on existing fixture-built invoices) for the null-`pattern` cases, since `pattern` is passed straight through without going via the fixture.
- No mocking, no DI, no async — pure in-memory AAA tests.

**`InvoiceClassificationFixtures.CreateInvoice`** (modified)
`backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/TestHelpers/InvoiceClassificationFixtures.cs`

- Responsibility unchanged: shared builder for `ReceivedInvoice` test instances.
- Contract gains one optional parameter, inserted immediately before the trailing `params string[] itemNames` (the only legal position):

  ```csharp
  internal static ReceivedInvoice CreateInvoice(
      decimal totalAmount = 0m,
      string companyName = "",
      string description = "",
      string companyVat = "",
      params string[] itemNames)
  ```

- Sets `CompanyVat = companyVat` on the constructed `ReceivedInvoice`.
- Default `""` preserves every existing call site's behavior unchanged; all current callers use named arguments, so the insertion is non-breaking.

No other component changes. `VatClassificationRule` itself, `ReceivedInvoice`, and `IClassificationRule` are exercised as-is and not modified.

## Data Schemas

No database, API, or event schema changes — this is test code only.

The only shape change is the `InvoiceClassificationFixtures.CreateInvoice` parameter list above (an additional optional `string companyVat = ""` argument that maps 1:1 to the existing `ReceivedInvoice.CompanyVat` property). No new types are introduced.
