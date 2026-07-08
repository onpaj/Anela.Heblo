# Architecture Review: Remove unused methods from IClassificationHistoryRepository

## Skip Design: true

## Architectural Fit Assessment
This is a pure dead-code removal within a single vertical slice (`InvoiceClassification`), touching only the `Anela.Heblo.Domain` and `Anela.Heblo.Persistence` projects. It does not cross module boundaries, does not touch DI registration, and does not change any MediatR request/response contract or controller surface. It fully aligns with the project's Vertical Slice Architecture and the "no speculative methods" principle already codified in `docs/architecture/development_guidelines.md` (§ Cross-Module Communication Example: "exposing only the operations it actually consumes (no speculative methods)"). There is no new architectural surface introduced — the task is a subtraction, not an addition — so no new pattern, ADR, or design decision is required.

Verification performed directly against the working tree (not just the spec's claims):
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs` — confirmed 4 members: `AddAsync`, `GetHistoryAsync` (line 7), `GetHistoryByInvoiceIdAsync` (line 9), `GetPagedHistoryAsync`.
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs` — confirmed `GetHistoryAsync` implementation spans lines 22–30, `GetHistoryByInvoiceIdAsync` spans lines 32–39, both using `_context.ClassificationHistory` with `.Include(h => h.ClassificationRule)`.
- Codebase-wide grep for `GetHistoryAsync(` and `GetHistoryByInvoiceIdAsync(` restricted to InvoiceClassification-related files returned zero call sites (the unqualified `GetHistoryAsync` name collides with an unrelated `FlexiManufactureHistoryClientTests.cs` method in the Manufacture module — a false positive on naming, not a real usage of this interface).
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs` — mocks `IClassificationHistoryRepository` via `Mock<IClassificationHistoryRepository>` but only ever sets up `.Setup(x => x.AddAsync(...))`. No `.Setup` or `.Verify` call references either target method. Since Moq mocks are interface-driven, no code change is needed here even after the methods are removed from the interface — the mock will simply no longer expose them.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassificationHistoryRepositoryTests.cs` — grepped for both method names, zero matches. This test only exercises `GetPagedHistoryAsync` (and implicitly `AddAsync` for fixture setup, per the spec).
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationHistory/GetClassificationHistoryHandler.cs` — confirmed it calls only `_historyRepository.GetPagedHistoryAsync(...)`.
- Only 6 files in the whole repo reference `IClassificationHistoryRepository` by name: the interface, the implementation, the two consumers above, `InvoiceClassificationModule.cs` (DI registration), and `InvoiceClassificationServiceTests.cs`. None of the latter four need any edit.

All spec claims are verified accurate. No additional dead-code or hidden coupling was found beyond what the spec already identified.

## Proposed Architecture

### Component Overview
No component, layer, or dependency graph changes. The slice's shape before and after:

```
Anela.Heblo.Domain/Features/InvoiceClassification/
  IClassificationHistoryRepository.cs   <- 2 methods removed (AddAsync, GetPagedHistoryAsync remain)

Anela.Heblo.Persistence/InvoiceClassification/
  ClassificationHistoryRepository.cs    <- 2 implementations removed

Anela.Heblo.Application/Features/InvoiceClassification/
  Services/InvoiceClassificationService.cs        <- unchanged, uses AddAsync
  UseCases/GetClassificationHistory/GetClassificationHistoryHandler.cs <- unchanged, uses GetPagedHistoryAsync
  InvoiceClassificationModule.cs                   <- unchanged, DI binding unaffected
```

No sequence diagram is warranted — no runtime behavior changes for any existing consumer.

### Key Design Decisions

#### Decision 1: Delete outright vs. deprecate first
**Options considered:**
- (a) Mark `[Obsolete]` and delete in a later PR.
- (b) Delete immediately.

**Chosen approach:** (b) Delete immediately.

**Rationale:** These are `internal`-scope backend contract methods (not a published/public API, not consumed by any external module or the frontend). There is no external consumer to give a deprecation window to — grep confirms zero call sites. `[Obsolete]` would only add ceremony without protecting any real caller, and the spec's own YAGNI framing ("add them back when the consumer exists") supports a clean, immediate deletion over staged deprecation.

#### Decision 2: Scope of the change
**Options considered:**
- (a) Also address the `AbraInvoiceId`/`InvoiceNumber` naming ambiguity mentioned in the brief.
- (b) Leave that naming question untouched, as the spec's "Out of Scope" section directs.

**Chosen approach:** (b).

**Rationale:** The spec explicitly tracks the naming ambiguity as a separate companion issue. `GetHistoryByInvoiceIdAsync` (the only method that filtered on `AbraInvoiceId`) is being deleted entirely, which removes the ambiguous surface as a side effect — there is nothing left to rename. Mixing an unrelated rename into a dead-code-removal PR would violate the project's "surgical changes" rule (CLAUDE.md: "Touch only what the task requires").

## Implementation Guidance

### Directory / Module Structure
No new files, directories, or modules. Exactly two existing files are edited:

1. `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs`
   - Delete line 7: `Task<List<ClassificationHistory>> GetHistoryAsync(int skip = 0, int take = 50);`
   - Delete the blank line after it and line 9: `Task<List<ClassificationHistory>> GetHistoryByInvoiceIdAsync(string abraInvoiceId);`
   - Delete the blank line that separated it from `GetPagedHistoryAsync`, OR keep exactly one blank line — match the existing blank-line-between-members style already used before `AddAsync`/`GetPagedHistoryAsync`, i.e. leave one blank line between `AddAsync` and `GetPagedHistoryAsync` after the middle two are removed. Resulting file body:
     ```csharp
     namespace Anela.Heblo.Domain.Features.InvoiceClassification;

     public interface IClassificationHistoryRepository
     {
         Task<ClassificationHistory> AddAsync(ClassificationHistory history);

         Task<(List<ClassificationHistory> Items, int TotalCount)> GetPagedHistoryAsync(
             int page = 1,
             int pageSize = 20,
             DateTime? fromDate = null,
             DateTime? toDate = null,
             string? invoiceNumber = null,
             string? companyName = null);
     }
     ```

2. `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs`
   - Delete the `GetHistoryAsync` method body (currently lines 22–30, including its blank surrounding lines as needed to avoid double blank lines).
   - Delete the `GetHistoryByInvoiceIdAsync` method body (currently lines 32–39).
   - Leave `AddAsync` and `GetPagedHistoryAsync` byte-for-byte unchanged; only remove the two method blocks and normalize surrounding whitespace to a single blank line between remaining members, matching existing style.

No changes to `using` directives are needed in either file — `Skip`/`Take`/`Where` LINQ extensions used only inside the deleted methods come from `Microsoft.EntityFrameworkCore`, which remains needed for `GetPagedHistoryAsync`'s own `Skip`/`Take`/`Where` calls (verified: `GetPagedHistoryAsync` already uses `.Where`, `.Skip`, `.Take`, `.CountAsync`, `.OrderByDescending`, `.ToListAsync` from the same namespace).

### Interfaces and Contracts
Post-change interface (already validated against source, matches the spec's "After" block exactly):

```csharp
public interface IClassificationHistoryRepository
{
    Task<ClassificationHistory> AddAsync(ClassificationHistory history);
    Task<(List<ClassificationHistory> Items, int TotalCount)> GetPagedHistoryAsync(
        int page = 1, int pageSize = 20, DateTime? fromDate = null,
        DateTime? toDate = null, string? invoiceNumber = null, string? companyName = null);
}
```

No other module, contract, or DTO depends on this interface's shape — it is not referenced from `Contracts/` (MediatR request/response) types, so this is not a breaking API change from any HTTP-facing perspective.

### Data Flow
Unaffected. `InvoiceClassificationService.ClassifyInvoiceAsync` continues to call `AddAsync` after processing an invoice; `GetClassificationHistoryHandler.Handle` continues to call `GetPagedHistoryAsync` in response to `GetClassificationHistoryRequest`. Both paths are independently verified as untouched by this change.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A hidden caller outside the grepped surface (e.g. reflection-based invocation, a Razor/dynamic call) breaks at runtime instead of compile time | Very Low | Both methods are called only via the strongly-typed `IClassificationHistoryRepository` interface with no reflection usage found anywhere in the repo for this type; `dotnet build` will fail immediately if any remaining reference exists, since C# is statically typed and this is not a virtual/dynamic dispatch scenario. |
| Whitespace/formatting drift after deletion (stray blank lines, inconsistent brace style) | Very Low | Run `dotnet format` after the edit, per CLAUDE.md's mandatory validation step; visually diff both files before committing to confirm exactly the two method blocks were removed and nothing else changed. |
| Future developer re-adds one of these methods speculatively | Low | Spec's "Out of Scope" section already states the YAGNI stance: re-add only when a concrete consumer exists. No code-level guard is needed beyond this documented intent — do not add an analyzer/test to "forbid" unused methods, as that is disproportionate to a two-method cleanup. |

## Specification Amendments
None. The spec's file/line references, before/after interface listing, and consumer analysis were all independently verified against the working tree and are accurate as written. No functional requirement needs correction.

One clarifying note for the implementer (not a spec change, just an implementation detail worth calling out explicitly since the spec doesn't spell it out): when deleting the two method blocks from `ClassificationHistoryRepository.cs`, delete one full blank line between remaining methods (not zero, not two) to match the file's existing one-blank-line-between-methods convention, consistent with FR-1's "no blank/orphaned lines or stray whitespace" acceptance criterion, which is written for the interface file but should be applied identically to the implementation file for consistency.

## Prerequisites
None. No migrations, config, feature flags, or infrastructure changes are needed — this is a same-commit, two-file, non-breaking source change. Standard validation (`dotnet build`, `dotnet format`, and the existing test suite: `InvoiceClassificationServiceTests`, `ClassificationHistoryRepositoryTests`) is sufficient to confirm correctness; no new tests are required since no new behavior is introduced.
