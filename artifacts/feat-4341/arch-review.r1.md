# Architecture Review: Coverage gap — FlexiBankStatementImportService failure/exception paths

## Skip Design: true

Backend-only test addition. No UI, no visual components, no user-facing surface at all — pure unit-test coverage work.

## Architectural Fit Assessment

This is a narrow, well-bounded addition: one new xUnit test class in the existing `Anela.Heblo.Adapters.Flexi.Tests` project, plus one `virtual` modifier in production code to make it testable (see Decision 1). It follows the same "mock the direct collaborator" shape as other adapter tests in this project (`FlexiStockTakingDomainServiceTests.cs`, `LedgerServiceTests.cs`, both of which mock the SUT's immediate dependencies). No new modules, no new packages, no cross-module dependency. It fits cleanly into the existing test pyramid (`docs/architecture/testing-strategy.md`): `FlexiBankStatementImportService` is a domain-facing adapter service (implements `IBankStatementImportService`), and per that doc, "Domain Services" and "MediatR Handlers... business logic, validation, error scenarios" are required unit-test territory. "Mock external dependencies only" is the doc's explicit best practice — `FlexiBankAccountClient` is that external dependency from the SUT's point of view, which is the key constraint that drives the design decision below.

## Proposed Architecture

### Component Overview

```
FlexiBankStatementImportServiceTests (new, xUnit)
        │
        │ constructs SUT with a mock collaborator
        ▼
FlexiBankStatementImportService  (SUT — implements IBankStatementImportService)
        │
        │ depends on (mocked directly at the test boundary)
        ▼
FlexiBankAccountClient   ── Mock<FlexiBankAccountClient>(sdkClient, logger)
        (ImportStatementAsync made `virtual` — see Decision 1 — so Moq can
         intercept it; the mock never executes the real method body, so its
         own constructor args are unused dummies)
```

The test mocks `FlexiBankAccountClient` itself — the SUT's direct collaborator — exactly as the issue's own "Suggested approach" describes ("Unit tests with a mocked `FlexiBankAccountClient`"). `IBankAccountClient` (the underlying FlexiBee SDK interface) is never touched by the test at all.

### Key Design Decisions

#### Decision 1: Test seam — mock `FlexiBankAccountClient` directly (requires making `ImportStatementAsync` `virtual`)
**Options considered:**
1. Mock `FlexiBankAccountClient` directly with Moq (the issue's literal "Suggested approach").
2. Mock `IBankAccountClient` (the FlexiBee SDK interface `FlexiBankAccountClient` wraps) and inject a real `FlexiBankAccountClient` into the SUT — the initially-attractive "zero production changes" option.
3. Extract a new `IFlexiBankAccountClient` interface and change `FlexiBankStatementImportService`'s constructor to depend on it.

**Chosen approach:** Option 1, enabled by a one-line, behavior-preserving production change: add the `virtual` modifier to `FlexiBankAccountClient.ImportStatementAsync`'s signature (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/FlexiBankAccountClient.cs`). Nothing else in that file changes.

**Rationale — option 2 was tried first and empirically falsified.** `FlexiBankAccountClient.ImportStatementAsync`'s entire body is wrapped in a single try/catch that converts *every* exception from `_client.ImportStatement(...)` into a `Result.Failure<bool>($"Exception during FlexiBee import: {ex.Message}")` and returns normally — it never rethrows. This was verified by reading the source directly (not assumed). Consequence: if the test only mocks `IBankAccountClient` and lets a real `FlexiBankAccountClient` sit in between, there is **no mocked SDK behavior that can make an exception propagate out of `FlexiBankAccountClient.ImportStatementAsync`** — it always returns a `Result<bool>`, success or failure, never throws. That means `FlexiBankStatementImportService`'s own catch block (lines 40–44, `"Exception during import: {ex.Message}"`) — FR-4, one of the three explicitly-named coverage gaps in the issue — would be **structurally unreachable** under option 2, no matter how the SDK mock is configured. This was caught by tracing the exact exception-swallowing behavior in `FlexiBankAccountClient`, not assumed from the class name.

This directly falsifies the "zero production changes" appeal of option 2: it cannot deliver the one requirement (FR-4) the issue explicitly filed this ticket for. Option 3 (extract an interface, change the SUT's constructor) is a larger, unnecessary surface change for a coverage-only task. Option 1 is minimal (one modifier keyword, no logic change, no signature change, no new interface, no constructor change anywhere), and this exact "make a wrapped dependency virtual so it can be mocked" is already an established pattern in this codebase: the adapter project's `.csproj` already declares `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` (Moq/Castle DynamicProxy's generated-proxy assembly), meaning this project is already set up for Moq to proxy concrete classes in it. This satisfies `spec.r1.md`'s NFR-2 escape hatch verbatim: "a minimal, purely mechanical change... is strictly required to make the class testable... behavior must remain byte-for-byte identical." Adding `virtual` changes only the dispatch mechanism (vtable vs. direct call); for the one real caller (`FlexiBankStatementImportService`, calling through a normal reference, not a derived override), the runtime behavior is identical.

This also matches `testing-strategy.md`'s "Mock external dependencies only" — `FlexiBankAccountClient` **is** the external-facing dependency from `FlexiBankStatementImportService`'s point of view; that is the whole point of the wrapper class.

#### Decision 2: Constructing the mock's return values
**Options considered:**
1. Have `Mock<FlexiBankAccountClient>.Setup(x => x.ImportStatementAsync(...))` return/throw the domain-level `Result<bool>` / `Anela.Heblo.Domain.Shared.Result` types directly — the same types `FlexiBankStatementImportService` itself consumes.
2. Reflect/construct the FlexiBee SDK's own `OperationResult<OperationResultDetail>` type and drive the test through a real `FlexiBankAccountClient` (option 2 of Decision 1) — rejected above.

**Chosen approach:** Option 1. `Result.Success(true)`, `Result.Failure<bool>("some FlexiBee error")`, and `Result.Failure<bool>(null!)` (the `null!` null-forgiving operator suppresses the harmless nullable-warning on `Result<T>.Failure`'s `string` parameter — verified: `Result<T>.ErrorMessage` is `string?` and the private constructor accepts a null `errorMessage` with no runtime validation, see `Anela.Heblo.Domain.Shared.Result` source) are all that is needed to drive FR-1..FR-3; `.ThrowsAsync(new InvalidOperationException("boom"))` on the same setup drives FR-4.

**Rationale:** Far simpler than constructing FlexiBee SDK types, needs no NuGet-package-internals spelunking, and asserts the SUT's contract using the exact same `Result<T>` type the production code operates on end-to-end. No shared test helper is needed for four cases.

## Implementation Guidance

### Directory / Module Structure
- New file: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Bank/FlexiBankStatementImportServiceTests.cs` (new `Bank/` subfolder inside the test project, mirroring the production `Bank/` subfolder and the existing `Stock/`, `Lots/`, `Accounting/` test subfolders).
- One production file modified, one line: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/FlexiBankAccountClient.cs` — add `virtual` to the `ImportStatementAsync` method signature (see Decision 1). No other production line changes.
- No new package references — `Moq`, `FluentAssertions`, `xunit` are already in `Anela.Heblo.Adapters.Flexi.Tests.csproj`.

### Interfaces and Contracts
- SUT: `FlexiBankStatementImportService` (`Anela.Heblo.Adapters.Flexi.Bank` namespace), constructed with `Mock<FlexiBankAccountClient>.Object` and a test `ILogger<FlexiBankStatementImportService>` (`Mock.Of<ILogger<FlexiBankStatementImportService>>()`).
- Mocked collaborator: `Mock<FlexiBankAccountClient>`, constructed with dummy ctor args (`Mock.Of<IBankAccountClient>()`, `Mock.Of<ILogger<FlexiBankAccountClient>>()` — never exercised, since `ImportStatementAsync` is fully overridden by the mock setup). Its `ImportStatementAsync(int, string)` — now `virtual` — is set up per test to return a `Result<bool>` or throw.
- No SDK types (`IBankAccountClient`, `OperationResult<T>`) appear anywhere in the test's Assert or Arrange beyond that one unused dummy constructor arg.

### Data Flow
1. Arrange: `new Mock<FlexiBankAccountClient>(Mock.Of<IBankAccountClient>(), Mock.Of<ILogger<FlexiBankAccountClient>>())`, then `.Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))` returning/throwing per scenario; construct `new FlexiBankStatementImportService(mockClient.Object, Mock.Of<ILogger<FlexiBankStatementImportService>>())`.
2. Act: call `sut.ImportStatementAsync(accountId, statementData)` with arbitrary valid `int`/`string` arguments (values are not asserted on; any fixed literal, e.g. `1`, `"statement-data"`, is fine).
3. Assert: on the returned `Result<bool>` — `IsSuccess`, `Value` (success case), and `ErrorMessage` (failure cases) via FluentAssertions, matching FR-1..FR-4 in `spec.r1.md` exactly.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Moq cannot generate a proxy for `FlexiBankAccountClient` if any *other* member Moq needs to touch (constructor, non-overridden members) is inaccessible | Low | Verified: the constructor `FlexiBankAccountClient(IBankAccountClient, ILogger<FlexiBankAccountClient>)` is public, the class is public and not sealed, and the adapter project's `.csproj` already grants `InternalsVisibleTo` to Moq's proxy assembly (`DynamicProxyGenAssembly2`) — this exact scenario is already anticipated by the project setup. |
| A future refactor could accidentally remove `virtual` from `ImportStatementAsync`, silently breaking this test's ability to intercept it (Moq would then invoke the real, unmocked method against dummy SDK args and likely throw a `NullReferenceException`-class failure, not a silent false pass) | Low | Acceptable — if that happens, this test fails loudly (compile-time nothing breaks, but the mock setup becomes a no-op and the real body runs against `Mock.Of<IBankAccountClient>()`'s default loose-mock `null`/default return, causing an observable test failure, not a silently-wrong pass). No mitigation needed beyond normal CI. |
| Testing via a fully-mocked `FlexiBankAccountClient` means this test provides no incidental coverage of `FlexiBankAccountClient`'s own body | Low | Acceptable and intentional — this task's scope (per `spec.r1.md`'s Out of Scope) is `FlexiBankStatementImportService` only; `FlexiBankAccountClient`'s own coverage is a separate, not-yet-filed concern. |
| None of this requires touching `docs/integrations/shoptet-api.md`-style "must document before use" rules | N/A | Not applicable — this task is FlexiBee, not Shoptet, and involves no live network calls at all (fully mocked). |

## Specification Amendments
`spec.r1.md`'s NFR-2 is exercised, not amended: it already anticipated exactly this outcome ("unless the architecture phase determines a minimal, purely mechanical change... is strictly required to make the class testable"). This architecture review is that determination: add `virtual` to `FlexiBankAccountClient.ImportStatementAsync`. The spec's Open Questions note about the test seam is resolved by Decision 1 above — no spec content needs to change.

## Prerequisites
One production line change must land in the same PR as the tests (not a separate prerequisite PR, since the tests do not compile/pass without it): `virtual` added to `FlexiBankAccountClient.ImportStatementAsync`'s signature. No migrations, no config, no infrastructure changes.
