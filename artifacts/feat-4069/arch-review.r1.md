# Architecture Review: Extract Invoice Import Job Name Prefix Into a Shared Constant

## Skip Design: true

## Architectural Fit Assessment
This is a same-module, same-layer constant extraction inside the existing `Invoices` vertical slice (`Anela.Heblo.Application/Features/Invoices/`). All three touched files — `Services/IInvoiceImportService.cs`, `Services/InvoiceImportService.cs`, `UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs` — already live in the same feature and the same assembly, so no module-boundary, contract, or DI-registration rules are implicated. `docs/architecture/development_guidelines.md`'s "Communication between modules exclusively through `contracts/`" rule does not apply here: the handler reading a constant out of `Services/` is an internal reference within one feature, not a cross-module dependency. `docs/architecture/filesystem.md` already documents a per-feature `{Feature}Constants.cs` convention (`CatalogConstants.cs`, `ManufactureConstants.cs`, etc.), confirming small static-constant classes are an established pattern here — this change just needs the right granularity (feature-wide vs. service-scoped) applied to it. No new dependencies, no persistence, no API surface change.

## Proposed Architecture

### Component Overview
```
Features/Invoices/
├── Services/
│   ├── InvoiceImportServiceConstants.cs   [NEW] holds ImportPrefix + derived DisplayName format
│   ├── IInvoiceImportService.cs           [DisplayName(InvoiceImportServiceConstants.DisplayNameFormat)]
│   └── InvoiceImportService.cs            [DisplayName(InvoiceImportServiceConstants.DisplayNameFormat)]
└── UseCases/GetRunningInvoiceImportJobs/
    └── GetRunningInvoiceImportJobsHandler.cs
          .Where(job.JobName.StartsWith(InvoiceImportServiceConstants.ImportPrefix, ...))
```
Both the attribute and the filter now read from one class; there is exactly one physical occurrence of the literal `"Import faktur:"` in the codebase.

### Key Design Decisions

#### Decision 1: Where the constant lives
**Options considered:**
- (a) Nest a static class directly inside `IInvoiceImportService.cs`, as the brief suggested.
- (b) A new dedicated file `Services/InvoiceImportServiceConstants.cs`.
- (c) A feature-wide `InvoicesConstants.cs` at `Features/Invoices/` root, matching `CatalogConstants.cs`/`ManufactureConstants.cs`.

**Chosen approach:** (b) — a new file `Services/InvoiceImportServiceConstants.cs`.

**Rationale:** The `{Feature}Constants.cs`-at-root convention in this codebase (option c) is used for constants that apply across a feature's multiple use cases. This constant is scoped to one thing — the invoice-import Hangfire job's display name — not to `Invoices` broadly (e.g. it has nothing to do with `IssuedInvoice` CRUD or other Invoices use cases). Putting it in `Services/`, next to the interface and implementation it names, keeps the blast radius honest and avoids overloading a feature-root file with a single-purpose value. Option (a) was rejected only because `IInvoiceImportService.cs` is an interface-declaration file; adding a concrete static class into it mixes an abstraction with a concrete constant holder for no benefit over a same-folder sibling file.

#### Decision 2: How `[DisplayName]` stays in sync with the constant — interpolate, don't just comment
**Options considered:**
- (a) Interpolate the constant directly into the attribute argument: `[DisplayName(InvoiceImportServiceConstants.DisplayNameFormat)]` where `DisplayNameFormat` is itself declared as `public const string DisplayNameFormat = $"{ImportPrefix} {{0}}";`.
- (b) Fall back to plain `+` concatenation: `public const string DisplayNameFormat = ImportPrefix + " {0}";`.
- (c) Fall back described in the spec/brief: keep the attribute's literal string as-is and add a paired code comment linking it to the constant.

**Chosen approach:** (a) — a `const string` built via string interpolation from another `const string`.

**Rationale — verified, not assumed:** The spec explicitly flagged uncertainty here and asked for verification "since attribute arguments must be compile-time constants." I compiled a standalone `net8.0` project (this project's actual TFM) with no explicit `LangVersion` — which resolves to the SDK default (C# 12 for `net8.0`) — declaring:
```csharp
public const string ImportPrefix = "Import faktur:";
public const string InterpConst = $"{ImportPrefix} {{0}}";
[DisplayName(InterpConst)]
public void Bar() {}
```
**This compiles and runs correctly** (`InterpConst` evaluates to `"Import faktur: {0}"`), and the attribute accepts it as a valid constant argument. This is the C# 10+ "constant interpolated strings" feature: an interpolated string is a valid constant expression as long as every interpolation hole is itself a constant and no format/alignment specifier is used — which is exactly this case (`{ImportPrefix}` is a hole, `{{0}}` is escaped literal braces, not a hole). Since this project targets `net8.0` with no `LangVersion` pin, it defaults to C# 12, well above the C# 10 floor for this feature — so this is safe.
Given that, **the brief's own suggested fallback (option c: interpolation "not usable... fall back to comments") does not apply here** — direct interpolation is both usable and strictly better: it makes the two occurrences *compiler-enforced* identical rather than merely comment-documented, closing the exact class of silent-drift bug this ticket exists to fix. Option (b), plain `+` concatenation, would also compile, but interpolation reads more clearly next to the original attribute text and is the idiomatic modern form — use it.

## Implementation Guidance

### Directory / Module Structure
Add exactly one new file:
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportServiceConstants.cs`

No other files, folders, or module registrations are added or changed.

### Interfaces and Contracts
```csharp
namespace Anela.Heblo.Application.Features.Invoices.Services;

public static class InvoiceImportServiceConstants
{
    /// <summary>Prefix used both in the Hangfire [DisplayName] and in
    /// GetRunningInvoiceImportJobsHandler's job-name filter. Keep these in sync
    /// by only ever changing this value — DisplayNameFormat below and the
    /// attribute usages derive from it at compile time.</summary>
    public const string ImportPrefix = "Import faktur:";

    /// <summary>Full Hangfire [DisplayName] format string, e.g. "Import faktur: {0}".
    /// Compiler-derived from ImportPrefix — do not hand-edit independently.</summary>
    public const string DisplayNameFormat = $"{ImportPrefix} {{0}}";
}
```
- `IInvoiceImportService.cs`: `[DisplayName(InvoiceImportServiceConstants.DisplayNameFormat)]`
- `InvoiceImportService.cs`: `[DisplayName(InvoiceImportServiceConstants.DisplayNameFormat)]`
- `GetRunningInvoiceImportJobsHandler.cs` line 55: `job.JobName.StartsWith(InvoiceImportServiceConstants.ImportPrefix, StringComparison.OrdinalIgnoreCase)` (requires adding `using Anela.Heblo.Application.Features.Invoices.Services;` to the handler file — same assembly, same feature, no project-reference change needed).

No new/changed public API contracts, DTOs, or MediatR request/response shapes. This is not a DTO, so the project's "DTOs must be classes, never records" rule is not implicated — `InvoiceImportServiceConstants` is a plain `static class` holding `const string` fields, consistent with the existing `{Feature}Constants.cs` pattern elsewhere in the codebase.

### Data Flow
Unchanged. Both compile-time consumers (`[DisplayName]` on the interface and implementation) and the one runtime consumer (`GetRunningInvoiceImportJobsHandler`'s `StartsWith` filter) now resolve to the same literal via one constant instead of two independently typed literals. No behavior, caching, or response-shape change; `GET /api/invoices/import/running-jobs` and `InvoiceImportRunningIndicator` are unaffected (confirmed: the frontend component only consumes the existing `useRunningInvoiceImportJobs` hook's response array, no rendering or shape change involved).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing tests assert the literal `"Import faktur: {0}"` / `"Import faktur: ..."` directly (`InvoiceImportServiceTests.cs:50`, `GetRunningInvoiceImportJobsHandlerTests.cs`) | Low | Leave these test literals as-is per spec's Out-of-Scope — they assert observable behavior/output, not internal wiring, and will keep passing unmodified since the resolved runtime value is unchanged. |
| A future edit changes `ImportPrefix` without noticing it changes the Hangfire dashboard's displayed job name | Low | Now caught by the two `Assert.Equal("Import faktur: {0}", ...)`-style tests already in `InvoiceImportServiceTests.cs`, which will fail loudly if the derived constant's value changes — this is a net improvement over today, where nothing links the two literals at all. |

## Specification Amendments
- **FR-3 acceptance criteria should be updated**: the spec hedges on whether interpolation is usable and offers a comment-based fallback as the likely outcome. Per the verification above, interpolation **is** usable and compiles cleanly on this project's toolchain (`net8.0`, default C# 12). Implementation must use the interpolated-constant form (`DisplayNameFormat`), not the comment-only fallback — this is a strictly stronger and equally simple guarantee.
- **FR-1 placement**: the spec offers "inside `IInvoiceImportService.cs` or a new small file in `Services/`" as equally acceptable options. This review resolves that choice: use the new file `Services/InvoiceImportServiceConstants.cs` (see Decision 1).

## Prerequisites
None. No migrations, config, or infrastructure changes are required — this is a pure compile-time refactor confined to three existing files plus one new constants file, all within the already-registered `Invoices` module.
