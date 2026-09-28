# Architecture Review: Coverage gap — FlexiBankStatementImportService failure/exception paths

## Skip Design: true

Backend-only test addition. No UI, no visual components, no user-facing surface at all — pure unit-test coverage work.

## Architectural Fit Assessment

This is a narrow, well-bounded addition: one new xUnit test class in the existing `Anela.Heblo.Adapters.Flexi.Tests` project, mirroring conventions already established for other Flexi adapter wrappers (`FlexiStockTakingDomainServiceTests.cs`, `FlexiLotsClientTests.cs`, `LedgerServiceTests.cs`). No new modules, no new packages, no cross-module dependency. It fits cleanly into the existing test pyramid (`docs/architecture/testing-strategy.md`): `FlexiBankStatementImportService` is a domain-facing adapter service (implements `IBankStatementImportService`), and per that doc, "Domain Services" and "MediatR Handlers... business logic, validation, error scenarios" are required unit-test territory. "Mock external dependencies only" is the doc's explicit best practice, which is the key constraint that drives the design decision below.

## Proposed Architecture

### Component Overview

```
FlexiBankStatementImportServiceTests (new, xUnit)
        │
        │ constructs (real, in-process)
        ▼
FlexiBankStatementImportService  (SUT — implements IBankStatementImportService)
        │
        │ depends on (real, in-process — NOT mocked)
        ▼
FlexiBankAccountClient           (concrete adapter class — kept real)
        │
        │ depends on (mocked at the test boundary)
        ▼
IBankAccountClient (Rem.FlexiBeeSDK.Client.Clients.BankAccounts)  ── Mock<IBankAccountClient>
```

The test mocks only at the true external boundary — the FlexiBee SDK interface `IBankAccountClient` — and lets both `FlexiBankStatementImportService` and `FlexiBankAccountClient` run as real, connected objects. This is the same pattern already used by `FlexiLotsClientTests` (mocks `ILotsClient`, constructs a real `FlexiLotsClient`).

### Key Design Decisions

#### Decision 1: Test seam — mock `IBankAccountClient`, not `FlexiBankAccountClient`
**Options considered:**
1. Mock `FlexiBankAccountClient` directly with Moq (as the issue's "Suggested approach" literally reads).
2. Mock `IBankAccountClient` (the FlexiBee SDK interface `FlexiBankAccountClient` wraps) and inject a real `FlexiBankAccountClient` into the SUT.
3. Extract a new `IFlexiBankAccountClient` interface and change `FlexiBankStatementImportService`'s constructor to depend on it.

**Chosen approach:** Option 2.

**Rationale:** `FlexiBankAccountClient.ImportStatementAsync` is a concrete, non-`virtual` method on a concrete class with no interface. Moq can only intercept interface members or `virtual`/`abstract` members — it cannot mock this method as written, so option 1 is not actually executable without a production change, despite being how the issue phrased the "suggested approach" (the issue is describing *intent* — "mock the FlexiBee dependency" — not literally requiring `Mock<FlexiBankAccountClient>`). Option 3 would touch production code and the constructor signature of `FlexiBankStatementImportService` for a pure test-coverage task — unjustified surface area for a ~1 hour fix, and explicitly against this task's "no production behavior change" NFR. Option 2 requires zero production code changes, mocks at the genuine external boundary (the FlexiBee SDK call), and is the exact pattern this codebase already uses for the sibling `Flexi*Client` wrapper classes (see `FlexiLotsClientTests`). It also matches `testing-strategy.md`'s explicit rule: "Mock external dependencies only."

One consequence: this test exercises `FlexiBankAccountClient`'s own try/catch and Result-mapping logic too (it's a thin real object in the chain), not just `FlexiBankStatementImportService`'s. That's acceptable and even desirable here — `FlexiBankAccountClient` has an equivalent uncovered-branch shape (its own success/failure/exception paths) and picks up incidental coverage for free with no extra test-authoring cost. It is not a substitute for the FR-1..FR-4 assertions, which must all be expressed against `FlexiBankStatementImportService.ImportStatementAsync`'s return value.

#### Decision 2: Constructing the SDK-level result to mock
**Options considered:**
1. Reflect/construct the SDK's own result type (returned by `IBankAccountClient.ImportStatement`) directly, matching its `IsSuccess`/`GetErrorMessage()` shape used in `FlexiBankAccountClient`.
2. Add a factory/test-helper method.

**Chosen approach:** Option 1 — construct the SDK result type directly in each test's Arrange step, same as `FlexiLotsClientTests` does for `ProductLot` from `Rem.FlexiBeeSDK.Model.Products.Lots`. No shared helper needed for four test cases.

**Rationale:** Minimal, keeps each test self-contained and readable (AAA pattern per `testing-strategy.md`). The developer implementing this must first inspect the actual `IBankAccountClient.ImportStatement` signature and its result type in `Rem.FlexiBeeSDK.Client` (NuGet package `rem.flexibeesdk.client`, installed at `~/.nuget/packages/rem.flexibeesdk.client/0.1.142/`) to get exact member names — `dotnet build` will surface any mismatch immediately, so this is low-risk to defer to implementation time rather than fully resolve here.

## Implementation Guidance

### Directory / Module Structure
- New file: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Bank/FlexiBankStatementImportServiceTests.cs` (new `Bank/` subfolder inside the test project, mirroring the production `Bank/` subfolder and the existing `Stock/`, `Lots/`, `Accounting/` test subfolders).
- No production file is created or modified.
- No new package references — `Moq`, `FluentAssertions`, `xunit` are already in `Anela.Heblo.Adapters.Flexi.Tests.csproj`.

### Interfaces and Contracts
- SUT: `FlexiBankStatementImportService` (`Anela.Heblo.Adapters.Flexi.Bank` namespace), constructed with a real `FlexiBankAccountClient` and a test `ILogger<FlexiBankStatementImportService>` (use `Mock<ILogger<...>>(MockBehavior.Loose).Object`, or `NullLogger<FlexiBankStatementImportService>.Instance` if already used elsewhere in this test project — grep for existing `ILogger` mocking convention in the adapter test project before choosing; both are acceptable, prefer whichever pattern is already dominant there for consistency).
- Constructed collaborator (real, not mocked): `FlexiBankAccountClient`, built with `Mock<IBankAccountClient>(MockBehavior.Loose).Object` and a logger.
- Mocked boundary: `IBankAccountClient.ImportStatement(int accountId, string aboData)` (namespace `Rem.FlexiBeeSDK.Client.Clients.BankAccounts`), whose return type must be inspected at implementation time (`ilspycmd`/IDE "Go to definition", or `dotnet build` trial-and-error against the installed NuGet package) to construct success/failure results with the right constructor/property shape.

### Data Flow
1. Arrange: `Mock<IBankAccountClient>` set up to return/throw per scenario; construct real `FlexiBankAccountClient(mockSdkClient.Object, logger)`; construct real `FlexiBankStatementImportService(flexiBankAccountClient, logger)`.
2. Act: call `sut.ImportStatementAsync(accountId, statementData)` with arbitrary valid `int`/`string` arguments (values are not asserted on; any fixed literal, e.g. `1`, `"statement-data"`, is fine — AutoFixture is optional here and adds no value for two scalar args).
3. Assert: on the returned `Result<bool>` — `IsSuccess`, `Value` (success case), and the error/message text (failure cases) via FluentAssertions, matching FR-1..FR-4 in `spec.r1.md` exactly.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `IBankAccountClient.ImportStatement`'s exact result-type shape (property names, constructor) is unknown until the developer opens the SDK in an IDE/decompiler | Low | Not a design risk — resolved trivially at implementation time via `dotnet build` errors or IDE navigation; does not affect the four required assertions or the chosen test seam. |
| `Result<bool>`'s error/message accessor name (`.Error`, `.ErrorMessage`, `.Message`, etc.) must be confirmed before asserting FR-2/FR-3/FR-4 | Low | Inspect `Anela.Heblo.Domain.Shared.Result`/`Result<T>` (used pervasively elsewhere in this codebase, e.g. already imported by the SUT itself) before writing assertions; trivial, no ambiguity in behavior, only in accessor naming. |
| Testing through `FlexiBankAccountClient` (a real object) instead of mocking it directly means a bug specifically inside `FlexiBankAccountClient` could mask or interact with `FlexiBankStatementImportService`'s own logic | Low | Acceptable per Decision 1 — this is the established, doc-sanctioned pattern in this codebase; `FlexiBankAccountClient`'s own mapping logic is a thin passthrough with the same shape as the SUT's, so behavior is not obscured, only co-covered. |
| None of this requires touching `docs/integrations/shoptet-api.md`-style "must document before use" rules | N/A | Not applicable — this task is FlexiBee, not Shoptet, and involves no new live API calls (SDK interface is mocked, no real network access at all). |

## Specification Amendments
None. `spec.r1.md`'s FR-1..FR-4 stand as written and are directly testable through the chosen seam. The spec's Open Questions note about the test seam is resolved by Decision 1 above.

## Prerequisites
None. No migrations, no config, no infrastructure changes. The test project already references everything needed.
