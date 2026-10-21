# Implementation: invariant-parsing-fix

## What was implemented
`AmountClassificationRule` now parses via private `TryParseAmount` (InvariantCulture; AllowLeadingSign | AllowDecimalPoint | AllowLeadingWhite | AllowTrailingWhite). All six `decimal.TryParse` calls replaced; operator order and try/catch kept. `Description` mentions decimal dot (`>=1000.50`).

## Files modified
- backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/AmountClassificationRule.cs

## Tests
`dotnet test --filter AmountClassificationRule`: 44/44 pass. InvoiceClassification filter: 132 pass, 3 fail (ClassificationRuleRepositoryReorderIntegrationTests) because Testcontainers PostgreSQL (Docker) is unavailable in this environment; unrelated to the change. `dotnet build`: 0 errors. `dotnet format` (whole-solution run unavailable: no .sln in backend/); `dotnet format whitespace --verify-no-changes` on the file: clean.

## PR Summary
Culture-invariant decimal parsing in AmountClassificationRule.
