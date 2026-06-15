Verification complete: spec claims hold against the code. Producing the review.

# Architecture Review: Verify and Document ProductExportOptions Module Ownership

## Skip Design: true

Backend-only configuration audit, regression test, and documentation. No UI surface.

## Architectural Fit Assessment

The proposed work fits cleanly with the existing architecture and **requires no structural change to production code**. Verification:

- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:114` already binds `services.Configure<ProductExportOptions>(...)` — co-located with `DataSourceOptions` and `CatalogCacheOptions`, matching the established pattern.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:364` binds `HangfireOptions`, not `ProductExportOptions`. Repo-wide grep confirms `ProductExportOptions` lives only in `Features/Catalog/*` and its tests.
- `ProductExportOptions` is a POCO at `Features/Catalog/Infrastructure/ProductExportOptions.cs`; its only consumer is `Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs`. Owner = Catalog. The brief's suggested fix (move to FileStorage) would re-introduce the ADR-004 violation it claims to fix.

Integration points for the *new* artifacts:

1. **xUnit test** plugs into the existing `backend/test/Anela.Heblo.Tests/Features/Catalog/` slot, mirroring the proven `FileStorageModuleTests.cs` module-wiring pattern (in-memory `IConfiguration`, manually-constructed `ServiceCollection`, `AddXxxModule(...)`, resolve through `IOptions<T>`).
2. **Memory decision note** follows the existing `memory/decisions/*.md` format established by `repository-di-in-feature-module.md` (which already documents ADR-004 enforcement for repositories).
3. **Resolution artifact** uses the per-branch `artifacts/feat-arch-review-filestorage-productexportopt/` directory already populated by the arch-review routine (`brief.md`, `spec.r{N}.md`, `answers.r{N}.md`).

## Proposed Architecture

### Component Overview

```
backend/
├── src/Anela.Heblo.Application/Features/Catalog/
│   ├── CatalogModule.cs                       [unchanged — verified at :114]
│   └── Infrastructure/
│       ├── ProductExportOptions.cs            [unchanged]
│       └── Jobs/ProductExportDownloadJob.cs   [unchanged, sole consumer]
├── src/Anela.Heblo.API/Extensions/
│   └── ServiceCollectionExtensions.cs         [unchanged — no PEO reference]
└── test/Anela.Heblo.Tests/Features/Catalog/
    └── CatalogModuleProductExportOptionsTests.cs   [NEW — regression guard]

memory/decisions/
└── product-export-options-ownership.md        [NEW — decision record]

artifacts/feat-arch-review-filestorage-productexportopt/
└── resolution.md                              [NEW — closes the finding]
```

The flow is: **arch-review routine → brief.md (stale) → this spec/review → regression test + resolution.md + decision memory → daily routine sees finding closed on future runs.**

### Key Design Decisions

#### Decision 1: Scope the guard test to `CatalogModule`, not a cross-module convention test
**Options considered:**
- (A) Single-module `CatalogModule` test asserting `IOptions<ProductExportOptions>.Value` binds.
- (B) Cross-cutting `ArchitectureTests` rule that scans every `*Module.cs` and asserts every `Configure<T>` call is in the owning module's assembly (reflection/Roslyn).
- (C) No test; rely on code review.

**Chosen approach:** (A).

**Rationale:** Mirrors `FileStorageModuleTests.AddFileStorageModule_NonDevelopmentEnvironmentWithMissingKey_FailsValidation` and `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings` — both established, single-module, lightweight guards. (B) is a larger investment that should be tracked as its own task; (C) failed once already and is what produced the brief. Spec FR-2 explicitly rules out (B). Keep the test cheap, local, and direct.

#### Decision 2: Place the decision record under `memory/decisions/`, not `docs/architecture/decisions/`
**Options considered:**
- (A) `memory/decisions/product-export-options-ownership.md` (existing convention).
- (B) Create `docs/architecture/decisions/NNNN-...md` (numbered ADR ledger).

**Chosen approach:** (A).

**Rationale:** The repo has no numbered ADR directory. `CLAUDE.md` mandates `memory/decisions/` for "architectural and library choices with reasoning," and seven files already exist there — including `repository-di-in-feature-module.md` which records the analogous ADR-004 ruling for repositories. Establishing a new ADR ledger is out of scope per spec FR-3.

#### Decision 3: Test builds `ServiceCollection` directly; does not boot the API host
**Options considered:**
- (A) Pure DI test: `new ServiceCollection() → AddCatalogModule(config, env) → BuildServiceProvider() → resolve IOptions<ProductExportOptions>`.
- (B) `WebApplicationFactory<Program>` integration test.

**Chosen approach:** (A).

**Rationale:** The contract under test is "the binding lives in `CatalogModule`." A `WebApplicationFactory` test would also pass if the binding moved back to `ServiceCollectionExtensions` — defeating the regression guard. Pure DI test fails closed if the `services.Configure<ProductExportOptions>(...)` line is deleted from `CatalogModule`. This matches the `FileStorageModuleTests` precedent.

#### Decision 4: Test asserts value round-trip, not just registration descriptor presence
**Options considered:**
- (A) `services.Single(d => d.ServiceType == typeof(IConfigureOptions<ProductExportOptions>))` — descriptor-level check.
- (B) Build provider, set in-memory `IConfiguration` keys, resolve `IOptions<ProductExportOptions>.Value`, assert `Url`/`ContainerName` round-trip.

**Chosen approach:** (B).

**Rationale:** (B) catches both regressions of interest — binding deleted *and* binding pointed at the wrong configuration section — with one assertion. The spec's FR-2 acceptance criterion explicitly requires both values round-trip.

## Implementation Guidance

### Directory / Module Structure

Three new files, zero changes to production code:

| File | Purpose |
|------|---------|
| `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs` | xUnit regression guard (FR-2) |
| `memory/decisions/product-export-options-ownership.md` | Decision record (FR-3) |
| `artifacts/feat-arch-review-filestorage-productexportopt/resolution.md` | Routine close-out note (FR-4) |

PR description must include one-line summary linking to `resolution.md` and the decision memory (FR-4 second acceptance criterion).

### Interfaces and Contracts

**Test fixture pattern (mirrors `FileStorageModuleTests`):**

```csharp
namespace Anela.Heblo.Tests.Features.Catalog;

public class CatalogModuleProductExportOptionsTests
{
    private const string ExpectedUrl       = "https://example.invalid/export";
    private const string ExpectedContainer = "product-exports";

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductExportOptions:Url"]           = ExpectedUrl,
                ["ProductExportOptions:ContainerName"] = ExpectedContainer,
            })
            .Build();

    [Fact]
    public void AddCatalogModule_BindsProductExportOptions_FromConfigurationSection()
    {
        // Arrange — only CatalogModule wires DI; API layer is NOT involved.
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddCatalogModule(BuildConfiguration(), /* env */ ...);

        // Act
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ProductExportOptions>>().Value;

        // Assert — round-trip both fields. Fails closed if the Configure<T> line is
        // deleted from CatalogModule or repointed at the wrong section name.
        Assert.Equal(ExpectedUrl,       options.Url);
        Assert.Equal(ExpectedContainer, options.ContainerName);
    }
}
```

**`AddCatalogModule` signature:** verify the exact parameter list at `CatalogModule.cs` before writing the test — pass whatever the method requires (likely `IConfiguration` + `IHostEnvironment`, mirroring `FileStorageModuleTests.BuildEnvironment`). Use `Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development)` to avoid any production-environment validation paths the module may have.

**Decision memory contract (FR-3):**

Follow the format of `memory/decisions/repository-di-in-feature-module.md`:
- `# Decision: ...` heading
- `**Decision:**` paragraph stating Catalog owns `ProductExportOptions`
- `**Why:**` paragraph citing the 2026-06-02 and 2026-06-12 plans and the consumer co-location rationale
- `**How to apply:**` bullet list naming `CatalogModule.cs:114`, `ServiceCollectionExtensions.cs` (negative — must NOT contain the binding), and the new guard test
- Cross-reference ADR-004 in `docs/architecture/development_guidelines.md`

**Resolution artifact contract (FR-4):**

```
# Resolution: ProductExportOptions ownership (FileStorage arch-review finding)

**Source:** daily arch-review routine, 2026-06-05, FileStorage module
**Current state:** ProductExportOptions bound in
  backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:114.
  ServiceCollectionExtensions.cs contains no reference to ProductExportOptions.
**Conclusion:** Not applicable — already resolved by the 2026-06-12 plan that
  moved both ProductExportOptions and ProductExportDownloadJob to Catalog.
**Guard:** backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleProductExportOptionsTests.cs
**Decision:** memory/decisions/product-export-options-ownership.md
```

### Data Flow

Test execution path:

```
xUnit runner
  → new ServiceCollection
  → in-memory IConfiguration { ProductExportOptions:Url, ProductExportOptions:ContainerName }
  → CatalogModule.AddCatalogModule(services, configuration, env)
        └─→ services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"))   [line 114]
  → BuildServiceProvider
  → GetRequiredService<IOptions<ProductExportOptions>>().Value
  → assert Url + ContainerName equal seeded values
```

Note: `CatalogModule` registers many other services (validators, MediatR behaviors, AutoMapper, repositories, refresh tasks). For an isolated options test, only the dependencies of the **options binding itself** matter — `ILogger<>` (defensive) and the in-memory `IConfiguration`. If `AddCatalogModule` throws because some unrelated downstream registration needs a missing dependency at *registration* time, narrow the test scope by extracting just the relevant `Configure<>` call into a smaller test seam — but expect this to be unnecessary; module registration is typically side-effect-free until `BuildServiceProvider` + resolution.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Future arch-review routine refiles the same stale finding because it scans static state, not git history | Medium | Resolution artifact + decision memory + regression test give the finding three durable markers a reviewer/agent can find via grep on `ProductExportOptions`. |
| Someone "fixes" the finding by moving the binding to `FileStorageModule` after reading only the brief | Medium | Decision memory explicitly states FileStorage is **not** the owner and explains why. Guard test would catch the move (binding disappears from `CatalogModule` → test fails). |
| `AddCatalogModule` has unrelated registration-time side effects (e.g., requires a connection string, throws if a dependency is missing) that break the isolated test | Low | Pattern-match `FileStorageModuleTests` which boots the same kind of provider successfully. If `AddCatalogModule` does throw, seed minimum config keys to satisfy it — do not weaken the assertion. |
| Test asserts descriptor presence instead of round-trip and silently passes when the section name is wrong | Low | Decision 4 forces value round-trip, which fails on either deletion or wrong section name. |
| Spec FR-4's "PR description includes a one-line summary" gets forgotten | Low | Implementation checklist must include "update PR description" as an explicit step, not a tail-end thought. |
| `CatalogModule` accumulates more options bindings later and one ends up in `ServiceCollectionExtensions.cs` again (a different option type) | Low | Out of scope. A cross-module convention test (Decision 1 option B) remains the right future investment; capture as a follow-up note in the decision memory's "How to apply" section. |

## Specification Amendments

The spec is solid. Three small clarifications worth folding in:

1. **FR-2 — name the exact pattern to mirror.** Add a sentence: "The test follows the same shape as `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` — in-memory `IConfiguration` + `Mock.Of<IHostEnvironment>` + direct `ServiceCollection` + `IOptions<T>` resolution." This eliminates ambiguity about test framework and idiom choice (xUnit, no FluentAssertions required — neither the FileStorage nor existing Catalog module tests use FluentAssertions consistently).

2. **FR-2 — clarify environment parameter.** Specify the test uses `Environments.Development` as the environment name to avoid tripping any production-only validators the module may add later.

3. **FR-3 — explicit cross-reference to ADR-004 and the existing `repository-di-in-feature-module.md`.** The decision memory should sit alongside the repository ADR-004 enforcement note as a sister record, not as a standalone. State in the file body: "Companion to `repository-di-in-feature-module.md`; same ADR-004 principle applied to options bindings."

No changes to scope, acceptance criteria, or out-of-scope list.

## Prerequisites

None. All of the following already exist in the worktree:

- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:114` — correct binding.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ProductExportOptions.cs` — option class.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/` — test project location.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` — pattern to copy.
- `memory/decisions/` directory with prior format examples.
- `artifacts/feat-arch-review-filestorage-productexportopt/` directory.

No migrations, config changes, infrastructure, or package additions are required. Implementation is purely additive (3 new files) and risk-free with respect to runtime behavior.