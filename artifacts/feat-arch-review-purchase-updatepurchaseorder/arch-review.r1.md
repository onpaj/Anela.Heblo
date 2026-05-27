# Architecture Review: Relocate UpdatePurchaseOrderRequestValidator to Correct Use Case Folder

## Skip Design: true

Backend-only file move + namespace correction with no UI surface and no behavioral change.

## Architectural Fit Assessment

The change fully aligns with the established Vertical Slice Architecture used across `backend/src/Anela.Heblo.Application/Features/`. Every other use case folder co-locates its validator next to its handler/request/response (`UseCases/UpdateLot/UpdateLotRequestValidator.cs`, `UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderRequestValidator.cs`, `UseCases/CreatePurchaseOrder/CreatePurchaseOrderRequestValidator.cs`). The current placement under `CreatePurchaseOrder/` is the lone outlier — restoring it is a pure consistency fix.

Integration points are minimal and all internal to the Purchase module:
1. **DI registration** in `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` (line 23)
2. **Validator → DTO coupling** with `UpdatePurchaseOrderRequest` (already in the target namespace)
3. **MediatR validation pipeline** (resolves `IValidator<UpdatePurchaseOrderRequest>` from DI — unchanged)

No callers reference the validator by its incorrect namespace — verified with grep across solution.

## Proposed Architecture

### Component Overview

```
Features/Purchase/
├── PurchaseModule.cs                          (DI registration — unchanged)
└── UseCases/
    ├── CreatePurchaseOrder/
    │   ├── CreatePurchaseOrderHandler.cs
    │   ├── CreatePurchaseOrderRequest.cs
    │   ├── CreatePurchaseOrderRequestValidator.cs
    │   ├── CreatePurchaseOrderResponse.cs
    │   └── UpdatePurchaseOrderRequestValidator.cs   ← REMOVE
    └── UpdatePurchaseOrder/
        ├── UpdatePurchaseOrderHandler.cs
        ├── UpdatePurchaseOrderRequest.cs
        ├── UpdatePurchaseOrderRequestValidator.cs   ← ADD (same content, corrected namespace)
        └── UpdatePurchaseOrderResponse.cs
```

### Key Design Decisions

#### Decision 1: Pure file move vs. split-and-rewrite

**Options considered:**
- (A) Move the file as-is and adjust namespace + redundant `using`.
- (B) Split `UpdatePurchaseOrderLineRequestValidator` into its own file during the move.

**Chosen approach:** (A) — single-file move, namespace fix only.

**Rationale:** YAGNI + surgical change. The sibling `CreatePurchaseOrderRequestValidator.cs` co-locates its line validator in the same file (lines 69–91). Matching that convention keeps the diff minimal and avoids drift. The spec explicitly forbids non-structural changes.

#### Decision 2: Trust explicit DI registration over assembly scanning

**Options considered:**
- (A) Rely on existing explicit DI registration in `PurchaseModule.cs`.
- (B) Switch to `AddValidatorsFromAssembly` while in the neighborhood.

**Chosen approach:** (A) — leave DI registration untouched.

**Rationale:** Spec FR-3 references `AddValidatorsFromAssembly`, but the actual code path is explicit registration:
```csharp
services.AddScoped<IValidator<UpdatePurchaseOrderRequest>, UpdatePurchaseOrderRequestValidator>();
```
This is namespace-agnostic — the type reference will continue to resolve because `PurchaseModule.cs` already imports both namespaces (lines 3–4). Switching to assembly scanning is out of scope and would be a separate architectural decision affecting all features.

## Implementation Guidance

### Directory / Module Structure

**Create:** `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`

**Delete:** `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`

Prefer `git mv` so file history is preserved.

### Interfaces and Contracts

No interface or contract changes. The moved file must:

1. Declare: `namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;`
2. **Remove** the now-redundant import: `using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;`
3. **Retain** `using FluentValidation;` (still needed for `AbstractValidator<T>`).
4. **Add** `using System;` only if `dotnet format` flags `DateTime` as ambiguous — likely not needed; `System` is implicit.
5. **Retain** both classes (`UpdatePurchaseOrderRequestValidator` and `UpdatePurchaseOrderLineRequestValidator`) in the same file.

Final file shape (illustrative skeleton, content unchanged from current):
```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;

public class UpdatePurchaseOrderRequestValidator : AbstractValidator<UpdatePurchaseOrderRequest> { /* …unchanged… */ }
public class UpdatePurchaseOrderLineRequestValidator : AbstractValidator<UpdatePurchaseOrderLineRequest> { /* …unchanged… */ }
```

### Data Flow

Unchanged. MediatR pipeline → resolves `IValidator<UpdatePurchaseOrderRequest>` from DI → fluent rules evaluate → `UpdatePurchaseOrderHandler.Handle` runs on validation success.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale build artifacts cache the old namespace | LOW | Run `dotnet clean` before `dotnet build` if anything looks odd. |
| `dotnet format` reorders or strips additional `using` directives | LOW | Run `dotnet format` after the move and visually diff to confirm only the redundant `UpdatePurchaseOrder` using is removed. |
| Future maintainer rediscovers the same misplacement elsewhere | LOW | Out of scope here (per spec). Owner should consider a follow-up arch-review pass across all `Features/*/UseCases/*` folders. |
| DI resolution breaks because `PurchaseModule.cs` loses ability to locate the type | NONE | `PurchaseModule.cs` already imports `UseCases.UpdatePurchaseOrder` (line 4) and references the validator by type, not by fully-qualified name. No change required. |

## Specification Amendments

1. **Correct FR-3 mechanism.** The spec states that "FluentValidation auto-registration (via `AddValidatorsFromAssembly` or equivalent)" handles discovery. In this codebase, validators are registered **explicitly** in `PurchaseModule.cs` (lines 22–23), not via assembly scanning. The acceptance criterion still holds — the existing explicit registration will continue to work because it binds by type — but the spec should reflect the actual mechanism. Suggested replacement wording:

   > FR-3 acceptance: The explicit DI registration in `PurchaseModule.cs` (`services.AddScoped<IValidator<UpdatePurchaseOrderRequest>, UpdatePurchaseOrderRequestValidator>()`) continues to resolve the moved type without modification, because `PurchaseModule.cs` already imports the `UpdatePurchaseOrder` namespace.

2. **Clarify FR-2 import handling.** Only one `using` directive becomes redundant (`using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;`). Keep `using FluentValidation;`. `System` types (`DateTime`) are covered by implicit usings in this project (verify via `Directory.Build.props` or the `.csproj` if any analyzer complains; not expected).

3. **Add explicit "preserve git history" guidance.** Use `git mv` rather than delete + create so blame/history follow the file.

## Prerequisites

None. This is a self-contained refactor:

- No migrations
- No config changes
- No infrastructure changes
- No new dependencies
- No coordination with other workstreams

Validation gate before merge (per project CLAUDE.md):
- `dotnet build` — zero errors, zero new warnings
- `dotnet format` — clean on the moved file
- `dotnet test` — all existing tests pass unchanged (no validator-specific tests exist today; integration of validation through the MediatR pipeline is exercised by handler-level tests if present)