# Implementation Plan: Culture-invariant AmountClassificationRule

### task: invariant-parsing-tests
Add failing tests first in `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/AmountClassificationRuleTests.cs`.
- Add a helper that sets `CultureInfo.CurrentCulture` (and `CurrentUICulture`) to `cs-CZ` and restores it in `finally`.
- Theory under cs-CZ with decimal patterns for every operator (`>=1000.50`, `<=1000.50`, `>1000.50`, `<1000.50`, `=1000.50`, bare `1000.50`) using invoice totals on both sides of the boundary (use decimal totals via `InvoiceClassificationFixtures.CreateInvoice(totalAmount: ...)`).
- Fact: `>=1000,50` returns false; fact: `>= 100` (whitespace) still matches 100.
- Run `dotnet test --filter AmountClassificationRuleTests`; the cs-CZ tests must fail before the fix.

### task: invariant-parsing-fix
Edit `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/AmountClassificationRule.cs`.
- Add `using System.Globalization;`.
- Add private static `TryParseAmount` using `NumberStyles.AllowLeadingSign | AllowDecimalPoint | AllowLeadingWhite | AllowTrailingWhite` and `CultureInfo.InvariantCulture`.
- Replace all six `decimal.TryParse` calls with the helper; keep operator order and try/catch.
- Update `Description` to mention the decimal dot (e.g. `>=1000.50`).
- Run `dotnet build`, `dotnet format`, and the full InvoiceClassification tests; all must pass.
- Depends on: invariant-parsing-tests.

### task: process-doc-update
Update `docs/processes/feed-invoice-classification.md` and `docs/processes/module-invoice-classification.md` (whichever describes AMOUNT rules): note that AMOUNT patterns use invariant culture with a dot decimal separator (comma not accepted), bump `verified_at`, then run `python3 scripts/process-docs/check.py index`.
- Depends on: invariant-parsing-fix.
