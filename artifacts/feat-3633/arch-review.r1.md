# Architecture Review: Inject TimeProvider into three Manufacture handlers

## Skip Design: true

## Architectural Fit Assessment
This is a pure conformance fix, not new architecture. The Manufacture module already has an established pattern — constructor-inject `TimeProvider` and call `_timeProvider.GetUtcNow().DateTime` — used consistently by `UpdateManufactureOrderStatusHandler`, `ConfirmProductCompletionWorkflow`, all four `DashboardTiles`, and `ConfirmSemiProductManufactureWorkflow`. `GetManufactureProtocolHandler`, `ResolveManualActionHandler`, and `GetSemiproductRecipePdfHandler` are the only outliers still calling `DateTime.UtcNow`/`DateTime.Now` directly. `TimeProvider` is registered once, module-wide, as `services.AddSingleton(TimeProvider.System);` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:131` — confirmed by direct inspection, no new DI registration is needed. No integration points, contracts, or data model change; this is a mechanical widening of an existing, already-correct pattern.

## Proposed Architecture

### Component Overview
No new components. Three existing MediatR handlers gain one constructor dependency each, drawn from the DI container that already provides it:

```
ServiceCollectionExtensions.cs
  services.AddSingleton(TimeProvider.System)  ← already present, unchanged
        │
        ├── UpdateManufactureOrderStatusHandler   (already injects, reference pattern)
        ├── GetManufactureProtocolHandler         (add TimeProvider param)
        ├── ResolveManualActionHandler             (add TimeProvider param)
        └── GetSemiproductRecipePdfHandler         (add TimeProvider param)
```

### Key Design Decisions

#### Decision 1: Reuse the exact `UpdateManufactureOrderStatusHandler` pattern, no new abstraction
**Options considered:**
- (a) Inject `TimeProvider` directly into each handler, matching the existing convention verbatim.
- (b) Introduce a module-local `IClock`/`IDateTimeProvider` wrapper around `TimeProvider` for extra testability.

**Chosen approach:** (a). Verified against `UpdateManufactureOrderStatusHandler.cs:18` (`private readonly TimeProvider _timeProvider;`), `:25` (constructor parameter `TimeProvider timeProvider`), `:33` (`_timeProvider = timeProvider;`), and usage at `:69`, `:76`, `:82`, `:162`, `:218` (`_timeProvider.GetUtcNow().DateTime`).

**Rationale:** `TimeProvider` is the .NET 8 BCL abstraction, already registered and already the module standard in five other handlers/workflows. A wrapper would add an indirection layer with no behavioral benefit and would itself be an inconsistency with the rest of the module. Do not introduce one.

#### Decision 2: Field placement and constructor parameter ordering
**Options considered:**
- (a) Match field/parameter position used in `UpdateManufactureOrderStatusHandler` (dependency ordered near the top, after the primary repository dependency).
- (b) Append `TimeProvider` as the last constructor parameter in each of the three handlers regardless of existing ordering.

**Chosen approach:** (b) — append as the last parameter in each handler's existing constructor, rather than reshuffling existing parameter order.

**Rationale:** The spec's acceptance criteria (FR-1–FR-3) only require the field to exist and be used; it does not require matching parameter *position*, only the *usage pattern*. Reordering existing constructor parameters is out of scope per CLAUDE.md's "surgical changes" rule and risks accidentally breaking positional-argument test call sites that aren't using named arguments. Appending is the minimal-diff option and is safe for both production DI (which resolves by type, not position) and tests that use named arguments; tests using positional arguments must add `TimeProvider.System` as the last argument.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Modify in place:
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction/ResolveManualActionHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs`

Corresponding test files to update:
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ResolveManualActionHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetSemiproductRecipePdfHandlerTests.cs`

Reference file (read, do not modify): `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs` and its test `UpdateManufactureOrderStatusHandlerTests.cs:62`, which already passes `TimeProvider.System` positionally — follow that exact test convention for the three updated test files.

### Interfaces and Contracts
No public interface or MediatR contract changes. Each handler's constructor signature grows by one parameter:

```csharp
private readonly TimeProvider _timeProvider;
// ...
public XxxHandler(..., TimeProvider timeProvider)
{
    // ...
    _timeProvider = timeProvider;
}
```

Replacements (exact, per spec):
- `GetManufactureProtocolHandler.cs:85` — `GeneratedAt = DateTime.UtcNow,` → `GeneratedAt = _timeProvider.GetUtcNow().DateTime,`
- `ResolveManualActionHandler.cs:54` — `order.ErpDiscardResidueDocumentNumberDate = DateTime.UtcNow;` → `order.ErpDiscardResidueDocumentNumberDate = _timeProvider.GetUtcNow().DateTime;`
- `ResolveManualActionHandler.cs:66` — `CreatedAt = DateTime.UtcNow,` → `CreatedAt = _timeProvider.GetUtcNow().DateTime,`
- `GetSemiproductRecipePdfHandler.cs:65` — `PrintedAt = DateTime.Now,` → `PrintedAt = _timeProvider.GetUtcNow().DateTime,`

No other lines in these three files change. `TimeProvider` is a BCL type (`System.TimeProvider`); no new `using` directive is needed since the type is in the globally-available `System` namespace already implicitly referenced by these files (confirm no `ImplicitUsings` conflicts — `UpdateManufactureOrderStatusHandler.cs` has no explicit `using System;` either, so none is required).

### Data Flow
Unchanged. Each handler still receives its request, does its existing work, and stamps a timestamp at the same point in the same code path — only the source of "now" changes from a static BCL call to the injected, container-resolved `TimeProvider.System` instance (in production) or a test-supplied `TimeProvider` (in tests). No new data enters or leaves the handler; no serialization or storage shape changes except the intended UTC-vs-local-time value correction in `GetSemiproductRecipePdfHandler` (FR-3), which is presentational-only (a printed timestamp on a generated PDF, not persisted).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing tests break at compile time because constructors gain a required parameter | Low | Mechanical, exhaustive fix — update all three test files in the same change (FR-4), following `UpdateManufactureOrderStatusHandlerTests.cs:62`'s `TimeProvider.System` convention. `dotnet build` will surface any missed call site immediately. |
| `GetSemiproductRecipePdfHandler`'s `PrintedAt` value shifts by the server's UTC offset (local→UTC fix) | Low | Intentional bug fix, explicitly called out in brief and spec (FR-3, Data Model section). Value is presentational-only on a generated PDF; no downstream consumer parses or compares it. No mitigation beyond the fix itself is needed. |
| A production DI call site (outside the three handlers/tests) constructs one of these handlers manually and is missed | Very Low | All three handlers are resolved exclusively through MediatR's container-based `IRequestHandler<,>` resolution registered via assembly scanning — there are no manual `new XxxHandler(...)` call sites in production code. Confirm during implementation with a repo-wide search for `new GetManufactureProtocolHandler(`, `new ResolveManualActionHandler(`, `new GetSemiproductRecipePdfHandler(` to be certain before marking done. |

## Specification Amendments
None. The spec's line references, acceptance criteria, and replacement snippets were verified against the current source and are accurate as written. One clarification for the implementer (not a spec change): append `TimeProvider timeProvider` as the **last** constructor parameter in each of the three handlers (see Decision 2) rather than matching `UpdateManufactureOrderStatusHandler`'s parameter position — the spec only mandates matching the *usage* pattern, and appending is the smaller, safer diff.

## Prerequisites
None. `TimeProvider.System` is already registered as a singleton in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:131`. No migrations, config, or infrastructure changes are required before implementation can start.
