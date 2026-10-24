### task: invariant-parsing-tests
Add failing tests first in `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/AmountClassificationRuleTests.cs`.
- Add a helper that sets `CultureInfo.CurrentCulture` (and `CurrentUICulture`) to `cs-CZ` and restores it in `finally`.
- Theory under cs-CZ with decimal patterns for every operator (`>=1000.50`, `<=1000.50`, `>1000.50`, `<1000.50`, `=1000.50`, bare `1000.50`) using invoice totals on both sides of the boundary (use decimal totals via `InvoiceClassificationFixtures.CreateInvoice(totalAmount: ...)`).
- Fact: `>=1000,50` returns false; fact: `>= 100` (whitespace) still matches 100.
- Run `dotnet test --filter AmountClassificationRuleTests`; the cs-CZ tests must fail before the fix.

