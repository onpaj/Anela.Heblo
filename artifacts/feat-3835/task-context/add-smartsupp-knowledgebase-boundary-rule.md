### task: add-smartsupp-knowledgebase-boundary-rule

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:27-28` (new allowlist)
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:401-403` (new `TheoryData` entry)

This task adds a CI-enforced rule that Smartsupp code must never reference KnowledgeBase-owned namespaces directly. It depends on the previous two tasks already being applied (the handler is already migrated), so the new theory case will pass immediately once added — this is what proves the fix is complete and locks it in against future regressions. Before the previous two tasks were applied, this exact rule (with `GenerateDraftReplyHandler.cs` still importing `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments`) would have failed, which is what makes it a meaningful regression guard rather than a no-op.

- [ ] **Step 1: Add the `SmartsuppKnowledgeBaseAllowlist` allowlist**

In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, the current lines 26-28 read:

```csharp
    // Allowlist for Article → KnowledgeBase. Empty — all violations fixed.
    private static readonly HashSet<string> ArticleAllowlist = new(StringComparer.Ordinal);

```

Replace with:

```csharp
    // Allowlist for Article → KnowledgeBase. Empty — all violations fixed.
    private static readonly HashSet<string> ArticleAllowlist = new(StringComparer.Ordinal);

    // Allowlist for Smartsupp -> KnowledgeBase. Empty — GenerateDraftReplyHandler now consumes
    // the Smartsupp-owned ISmartsuppKnowledgeSource contract; the KnowledgeBase adapter
    // (KnowledgeBaseSmartsuppKnowledgeSource) lives in KnowledgeBase.Infrastructure.
    private static readonly HashSet<string> SmartsuppKnowledgeBaseAllowlist = new(StringComparer.Ordinal);

```

- [ ] **Step 2: Add the `Smartsupp -> KnowledgeBase` rule to `Rules()`**

In the same file, the current lines 392-403 read:

```csharp
        new ModuleBoundaryRule(
            Name: "Article -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: ArticleAllowlist),

        new ModuleBoundaryRule(
            Name: "Logistics -> Manufacture",
```

Replace with:

```csharp
        new ModuleBoundaryRule(
            Name: "Article -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: ArticleAllowlist),

        new ModuleBoundaryRule(
            Name: "Smartsupp -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Smartsupp",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: SmartsuppKnowledgeBaseAllowlist),

        new ModuleBoundaryRule(
            Name: "Logistics -> Manufacture",
```

- [ ] **Step 3: Run the architecture test suite to verify the new rule passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: PASS for every `Consumer_types_should_not_reference_provider_owned_namespaces` theory case, including the new `Smartsupp -> KnowledgeBase` case (zero violations found, since `GenerateDraftReplyHandler.cs` no longer references any `Anela.Heblo.Domain.Features.KnowledgeBase`, `Anela.Heblo.Application.Features.KnowledgeBase`, or `Anela.Heblo.Persistence.KnowledgeBase` type after the previous task).

- [ ] **Step 4: Run the full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: PASS — no regressions in any other module's tests.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: enforce Smartsupp -> KnowledgeBase module boundary"
```
