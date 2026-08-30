### task: close-dataquality-invoices-boundary-allowlist

**Goal**

Empty the `DataQuality -> Invoices` allowlist in `ModuleBoundariesTests.cs` so the module-boundary
architecture test becomes a hard, zero-tolerance CI gate — mirroring the already-closed
`LeafletAllowlist`, `ArticleAllowlist`, and `SmartsuppKnowledgeBaseAllowlist`. Depends on the
previous task having already removed every actual reference from
`Anela.Heblo.Application.Features.DataQuality.*` to `Anela.Heblo.Domain.Features.Invoices` — this
task only removes the now-unnecessary escape hatch and verifies the reflection scan finds nothing.

**Context** (self-contained — the engineer only reads this section)

`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforces module boundaries by
reflecting over `Anela.Heblo.Application`'s types (see `ModuleBoundaryRule` record and the
`Consumer_types_should_not_reference_provider_owned_namespaces` theory, which iterates every rule in
the `Rules` `MemberData` and fails if any type under `rule.InspectedNamespacePrefix` references a
type under one of `rule.ForbiddenNamespacePrefixes`, unless the exact
`"{consumerType} -> {referencedType}"` string is present in `rule.Allowlist`).

Three sibling allowlists in the same file are already empty, each with a similar one-line comment
explaining the violation was closed:
```csharp
// Allowlist for Leaflet → KnowledgeBase. Empty — IDocumentTextExtractor and IOneDriveService
// were relocated to Anela.Heblo.Application.Shared.Rag, closing the compile-time dependency.
private static readonly HashSet<string> LeafletAllowlist = new(StringComparer.Ordinal);

// Allowlist for Article → KnowledgeBase. Empty — all violations fixed.
private static readonly HashSet<string> ArticleAllowlist = new(StringComparer.Ordinal);

// Allowlist for Smartsupp -> KnowledgeBase. Empty — GenerateDraftReplyHandler now consumes
// the Smartsupp-owned ISmartsuppKnowledgeSource contract; the KnowledgeBase adapter
// (KnowledgeBaseSmartsuppKnowledgeSource) lives in KnowledgeBase.Infrastructure.
private static readonly HashSet<string> SmartsuppKnowledgeBaseAllowlist = new(StringComparer.Ordinal);
```

The `DataQualityInvoicesAllowlist` this task empties currently has 7 entries and sits right after
`DataQualityCatalogAllowlist` (a **separate**, out-of-scope violation covering
`ProductPairingDqtComparer → Catalog` — do not touch `DataQualityCatalogAllowlist`, it is untouched
by this spec):

```csharp
    // Allowlist for DataQuality -> Invoices. The DataQuality module owns IInvoiceShoptetSource
    // and IInvoiceErpClient (in Application/Features/DataQuality/Contracts/) and consumes
    // them via InvoiceDqtComparer. Shared invoice domain DTOs are referenced on the contracts
    // and inside the comparer; lifting these to a shared kernel is a separate follow-up.
    // Follow-up: extract a DataQuality-owned snapshot DTO and map in the adapters.
    private static readonly HashSet<string> DataQualityInvoicesAllowlist = new(StringComparer.Ordinal)
    {
        // IInvoiceShoptetSource exposes IssuedInvoiceDetailBatch and IssuedInvoiceSourceQuery.
        "Anela.Heblo.Application.Features.DataQuality.Contracts.IInvoiceShoptetSource -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailBatch",
        "Anela.Heblo.Application.Features.DataQuality.Contracts.IInvoiceShoptetSource -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery",

        // IInvoiceErpClient exposes IssuedInvoiceDetail.
        "Anela.Heblo.Application.Features.DataQuality.Contracts.IInvoiceErpClient -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetail",

        // InvoiceDqtComparer consumes shared invoice DTOs internally.
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetail",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailBatch",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailItem",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery",
        "Anela.Heblo.Application.Features.DataQuality.Services.InvoiceDqtComparer -> Anela.Heblo.Domain.Features.Invoices.InvoicePrice",
    };
```

Its `ModuleBoundaryRule` registration (further down the same file, in the `Rules` `MemberData`, not
modified by this task) already correctly scopes the check:
```csharp
new ModuleBoundaryRule(
    Name: "DataQuality -> Invoices",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.DataQuality",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Invoices",
        "Anela.Heblo.Application.Features.Invoices",
        "Anela.Heblo.Persistence.Invoices",
    },
    Allowlist: DataQualityInvoicesAllowlist),
```

**Files to create/modify/delete**

- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

**Implementation steps**

1. **Empty the allowlist and update its comment.** Replace the 21-line block quoted above (comment
   + `DataQualityInvoicesAllowlist` declaration with its 7 entries) with:

   ```csharp
    // Allowlist for DataQuality -> Invoices. Empty — IInvoiceShoptetSource/IInvoiceErpClient now
    // expose DataQuality-owned DqtInvoiceSnapshot/DqtInvoiceItem/DqtInvoiceSourceQuery types;
    // InvoiceShoptetSourceAdapter/InvoiceErpClientAdapter (Invoices.Infrastructure) map from
    // Invoices domain types to the DataQuality shape via InvoiceDqtSnapshotMapper.
    private static readonly HashSet<string> DataQualityInvoicesAllowlist = new(StringComparer.Ordinal);
   ```

   Leave `DataQualityCatalogAllowlist` immediately above it, and the `ModuleBoundaryRule` entry for
   `"DataQuality -> Invoices"` further down the file, completely untouched.

2. **Run the module-boundary theory test and confirm every case passes**, including the
   `DataQuality -> Invoices` case now running with a fully empty allowlist:

   ```bash
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces"
   ```

   Expected: all rule cases pass, `0` failed. If the `DataQuality -> Invoices` case fails, the
   assertion message lists each offending `"{consumerType} -> {referencedType}" (via {member})`
   entry — that means task 2 left a reference behind; go back and fix it there rather than
   re-populating this allowlist.

3. **Run the full backend test suite and build/format checks** (per this repo's validation
   convention — BE: `dotnet build` + `dotnet format`, all touched tests must pass):

   ```bash
   dotnet build
   dotnet format --verify-no-changes
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   ```

   Expected: build succeeds, `dotnet format --verify-no-changes` reports no changes needed, and the
   full test run passes with `0` failed.

4. **Commit.**

   ```bash
   git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
   git commit -m "Close DataQuality -> Invoices module-boundary allowlist"
   ```
