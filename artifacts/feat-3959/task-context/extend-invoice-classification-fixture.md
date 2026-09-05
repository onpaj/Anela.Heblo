### task: extend-invoice-classification-fixture

Add the `companyVat` parameter to the shared `InvoiceClassificationFixtures.CreateInvoice` helper so later tests can build invoices with a `CompanyVat` value. This is additive test infrastructure with a default that preserves all existing behavior — verified by building the test project, not by a dedicated unit test (the fixture itself has no test class of its own, matching existing convention: `CreateRule` above it isn't unit-tested either).

**Step 1 — Open the fixture file.**

File: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/TestHelpers/InvoiceClassificationFixtures.cs`

**Step 2 — Add the `companyVat` parameter and set it on the constructed invoice.**

Replace the `CreateInvoice` method:

```csharp
    internal static ReceivedInvoice CreateInvoice(
        decimal totalAmount = 0m,
        string companyName = "",
        string description = "",
        params string[] itemNames)
    {
        return new ReceivedInvoice
        {
            CompanyName = companyName,
            Description = description,
            TotalAmount = totalAmount,
            Items = itemNames
                .Select(name => new ReceivedInvoiceItem { Name = name })
                .ToList()
        };
    }
```

with:

```csharp
    internal static ReceivedInvoice CreateInvoice(
        decimal totalAmount = 0m,
        string companyName = "",
        string description = "",
        string companyVat = "",
        params string[] itemNames)
    {
        return new ReceivedInvoice
        {
            CompanyName = companyName,
            Description = description,
            CompanyVat = companyVat,
            TotalAmount = totalAmount,
            Items = itemNames
                .Select(name => new ReceivedInvoiceItem { Name = name })
                .ToList()
        };
    }
```

Note: `companyVat` is inserted immediately before the trailing `params string[] itemNames` — this is the only legal position in C#, since a `params` parameter must be last in the parameter list.

Do not touch `CreateRule` or the file's `using`/namespace lines — only the `CreateInvoice` signature and body change.

**Step 3 — Build the test project to confirm every existing call site still compiles.**

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: `Build succeeded.` with 0 errors. This confirms the four existing call sites (`AmountClassificationRuleTests`, `CompanyNameClassificationRuleTests`, `DescriptionClassificationRuleTests`, `ItemDescriptionClassificationRuleTests`, `RuleEvaluationEngineTests` — all of which use named arguments) are unaffected by the new optional parameter.

**Step 4 — Run the full backend test suite to confirm no existing test's behavior changed.**

```bash
cd backend && dotnet test
```

Expected: all tests pass (same pass count as before this change — the new parameter defaults to `""`, which is what `ReceivedInvoice.CompanyVat` already defaults to, so no existing assertion changes).

**Step 5 — Commit.**

```bash
cd backend && git add test/Anela.Heblo.Tests/Features/InvoiceClassification/TestHelpers/InvoiceClassificationFixtures.cs
git commit -m "Add companyVat parameter to InvoiceClassificationFixtures.CreateInvoice"
```

---

