### task: module-boundary-guard

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Add the new allowlist field**

Find this line near the top of the class (right after the `LeafletAllowlist` declaration, alphabetically/logically grouped with the other per-pair allowlist fields — insert near the other `ExpeditionList*` allowlists, e.g. right after `ExpeditionListShoptetOrdersAllowlist`):

```csharp
    // Allowlist for ExpeditionListArchive -> FileStorage. Empty — the four ExpeditionListArchive
    // handlers now consume the module-owned IExpeditionListArchiveBlobStore contract; the
    // FileStorage adapter (ExpeditionListArchiveBlobStoreAdapter) lives in FileStorage.Infrastructure
    // and implements it there, so no ExpeditionListArchive type needs to reference FileStorage directly.
    private static readonly HashSet<string> ExpeditionListArchiveFileStorageAllowlist = new(StringComparer.Ordinal);
```

- [ ] **Step 2: Add the new rule to `Rules()`**

Find the existing `"ExpeditionListArchive -> ExpeditionList"` rule entry inside the `Rules()` `TheoryData` initializer and add a new entry immediately after it:

```csharp
        new ModuleBoundaryRule(
            Name: "ExpeditionListArchive -> ExpeditionList",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionListArchive",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.ExpeditionList",
                "Anela.Heblo.Application.Features.ExpeditionList",
                "Anela.Heblo.Persistence.ExpeditionList",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "ExpeditionListArchive -> FileStorage",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionListArchive",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.FileStorage",
                "Anela.Heblo.Application.Features.FileStorage",
                "Anela.Heblo.Persistence.FileStorage",
            },
            Allowlist: ExpeditionListArchiveFileStorageAllowlist),
```

(The first block, `"ExpeditionListArchive -> ExpeditionList"`, is unchanged and shown only for exact insertion-point context — do not duplicate it.)

- [ ] **Step 3: Run the architecture test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: PASS, all rules in the `Rules()` theory pass with zero violations, including the new `"ExpeditionListArchive -> FileStorage"` row. If this fails, the failure message lists every remaining `ExpeditionListArchive → FileStorage` reference by name — cross-check that all four tasks above (`migrate-download-handler`, `migrate-reprint-handler`, `migrate-get-lists-by-date-handler`, `migrate-get-dates-handler`) were completed and committed first.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(architecture): guard ExpeditionListArchive -> FileStorage module boundary"
```

---

