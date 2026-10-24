### task: invariant-parsing-fix
Edit `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/AmountClassificationRule.cs`.
- Add `using System.Globalization;`.
- Add private static `TryParseAmount` using `NumberStyles.AllowLeadingSign | AllowDecimalPoint | AllowLeadingWhite | AllowTrailingWhite` and `CultureInfo.InvariantCulture`.
- Replace all six `decimal.TryParse` calls with the helper; keep operator order and try/catch.
- Update `Description` to mention the decimal dot (e.g. `>=1000.50`).
- Run `dotnet build`, `dotnet format`, and the full InvoiceClassification tests; all must pass.
- Depends on: invariant-parsing-tests.

