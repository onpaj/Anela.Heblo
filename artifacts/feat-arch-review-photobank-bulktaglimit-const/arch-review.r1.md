All inputs verified — the conventions (file-scoped namespace, `public static class`, sibling sample `CatalogConstants.cs`), the handler line references, and the test file paths all match the spec exactly.

# Architecture Review: Extract `BulkTagLimit` to Shared `PhotobankConstants`

## Skip Design: true

## Architectural Fit Assessment
This refactor aligns perfectly with an established project convention. Six sibling features in `backend/src/Anela.Heblo.Application/Features/` already expose a `<Feature>Constants.cs` file at the feature root (`CatalogConstants.cs`, `ManufactureConstants.cs`, `MeetingTasksConstants.cs`, `AnalyticsConstants.cs`, `InventoryConstants.cs`, and `ManufactureAnalysisConstants.cs`). The Photobank feature is the only one with cross-handler duplication that lacks such a file. Introducing `PhotobankConstants` fills the gap with zero architectural novelty.

The handlers being modified are MediatR `IRequestHandler` implementations in the `UseCases/<UseCase>/<Handler>.cs` vertical-slice layout. They already share `ErrorCodes.BulkTagLimitExceeded` and an identical `Params` payload shape — extracting the numeric limit is the obvious DRY completion of that existing sharing.

The integration points are narrow and well-bounded:
1. **New compile-time constant** in the `Anela.Heblo.Application.Features.Photobank` namespace.
2. **Two callers** in the same namespace tree — no new `using` directive required (the file shares the feature root namespace; the handlers reside under `Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag(ByIds)`, which transitively sees the parent namespace).

## Proposed Architecture

### Component Overview
```
Features/Photobank/
├── PhotobankConstants.cs        ← NEW (public static class, public const int BulkTagLimit = 5_000;)
├── UseCases/
│   ├── BulkAddPhotoTag/
│   │   └── BulkAddPhotoTagHandler.cs        ← references PhotobankConstants.BulkTagLimit
│   └── BulkAddPhotoTagByIds/
│       └── BulkAddPhotoTagByIdsHandler.cs   ← references PhotobankConstants.BulkTagLimit
```

No DI registration, no wiring change — `const int` is inlined at compile time.

### Key Design Decisions

#### Decision 1: Visibility — `public static class` with `public const int`
**Options considered:**
- (a) `public static class` + `public const int` — matches `CatalogConstants.cs`, `ManufactureConstants.cs`, `AnalyticsConstants.cs`.
- (b) `internal static class` + `internal const` — matches `MeetingTasksConstants.cs`.

**Chosen approach:** Option (a) — `public static class PhotobankConstants` with `public const int BulkTagLimit = 5_000;`.

**Rationale:** The brief and spec explicitly specify `public`. The majority convention in the codebase is also `public`. `MeetingTasksConstants` is the outlier because it holds a single internal-use chat-client key. A `BulkTagLimit` is the kind of operational threshold that may legitimately be read by tests, validators, or controllers in the future, so the looser visibility is forward-compatible. Bumping visibility later is a breaking-change rename of the symbol's accessibility; widening from internal forces a refactor. Start public.

#### Decision 2: Location — Application layer, feature root
**Options considered:**
- (a) `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankConstants.cs` (Application layer, beside `PhotobankRepository.cs`).
- (b) `backend/src/Anela.Heblo.Domain/Features/Photobank/...` (Domain layer).

**Chosen approach:** Option (a). The spec mandates this path; it also matches every sibling `*Constants.cs` placement.

**Rationale:** The constant gates an *Application*-level use-case rule (request size cap), not a domain invariant. It does not protect domain aggregates; it bounds an inbound request. Application is the correct layer. Per-feature roots, not a project-wide `Constants` namespace, are this codebase's convention.

#### Decision 3: Naming and formatting — preserve `5_000` digit separator and PascalCase
**Chosen approach:** `public const int BulkTagLimit = 5_000;`.

**Rationale:** Keeps the symbol name unchanged so handler call sites are minimally disturbed (only the qualifier changes). Preserves the digit-group separator already present in both handlers — readability and zero behavioral diff. PascalCase matches `CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD` is the lone SCREAMING_SNAKE_CASE outlier, while `ManufactureConstants` / `MeetingTasksConstants` use PascalCase; PascalCase is the dominant pattern and matches the original `BulkTagLimit` identifier.

## Implementation Guidance

### Directory / Module Structure
**Create:**
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankConstants.cs`

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTag/BulkAddPhotoTagHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTagByIds/BulkAddPhotoTagByIdsHandler.cs`

**Do not modify:**
- Test files (`BulkAddPhotoTagHandlerTests.cs`, `BulkAddPhotoTagByIdsHandlerTests.cs`) — value is unchanged; their `"5000"` assertions remain valid. Per spec FR-4 and Out of Scope.

### Interfaces and Contracts

New symbol:
```csharp
namespace Anela.Heblo.Application.Features.Photobank;

public static class PhotobankConstants
{
    public const int BulkTagLimit = 5_000;
}
```

Style requirements:
- File-scoped namespace (matches all sibling constants files).
- No `using` directives (none needed).
- No XML doc comment required — the symbol is self-documenting and matches `MeetingTasksConstants` and `ManufactureConstants` minimalism. (Optional one-line `<summary>` is acceptable but not required.)

Handler call-site contract: replace every `BulkTagLimit` token with `PhotobankConstants.BulkTagLimit`. **No `using` directive needs to be added** — both handler namespaces (`Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag` and `Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds`) are nested under `Anela.Heblo.Application.Features.Photobank`, so the constants class is in scope by C#'s nested-namespace resolution rules. Verify by compile; do not preemptively add an import.

Both call sites in each handler:
1. The `if (... > BulkTagLimit)` guard.
2. The `BulkTagLimit.ToString()` invocation inside the `Params` dictionary.

### Data Flow
Unchanged. The constant is consumed at exactly the same two points in each handler's `Handle(...)` method; the request → guard → response pipeline is byte-identical.

```
Request → CountFilteredPhotosAsync / PhotoIds.Count
        → compare against PhotobankConstants.BulkTagLimit  (was: local BulkTagLimit)
        → if exceeded → ErrorCodes.BulkTagLimitExceeded + Params{Count, Limit="5000"}
        → else → proceed with tag-add path (unchanged)
```

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Adding `using` directive that creates a namespace ambiguity or unused-import warning | Low | Don't add a `using` — handler namespaces nest under the constants' namespace. Let `dotnet build` confirm; if it fails, only then add `using Anela.Heblo.Application.Features.Photobank;`. |
| Test assertions on the literal string `"5000"` break | Very Low | The constant value remains `5_000`; `(5_000).ToString()` → `"5000"`. Spec FR-4 confirms; no test edits needed. |
| Style drift triggers `dotnet format` diff | Low | Mirror `CatalogConstants.cs` exactly (file-scoped namespace, blank line after, no trailing newline beyond convention). Run `dotnet format` on the touched files. |
| Future contributor adds another duplicated Photobank literal and skips the constants class | Low | Out of scope for this PR. Mention as a follow-up in the PR description; do not expand the change set. |
| `public` visibility leaks an implementation detail | Very Low | The limit is a stable, named threshold tied to an existing public `ErrorCodes.BulkTagLimitExceeded` contract. Public visibility is consistent with sibling features and the spec's explicit instruction. |

## Specification Amendments
None required. The spec is precise, complete, and consistent with project conventions. Two minor clarifications worth stating in implementation (not amendments):
- **Namespace resolution:** the spec correctly notes "no new `using` directive is required." Confirmed: both handler namespaces nest under `Anela.Heblo.Application.Features.Photobank`, so the constants class is implicitly visible. Implementation must rely on this rather than add an explicit `using`.
- **PascalCase vs SCREAMING_SNAKE_CASE:** the spec preserves the original PascalCase identifier `BulkTagLimit`. This is correct; do not "normalize" to `BULK_TAG_LIMIT` even though `CatalogConstants` uses that style. Consistency with the original handler identifier wins, and PascalCase is the majority convention.

## Prerequisites
None.

- No migrations, no config, no infrastructure changes.
- No new NuGet packages.
- No DI registration changes.
- Verification commands (per `CLAUDE.md`): `dotnet build`, `dotnet format`, `dotnet test` for `Anela.Heblo.Tests` (Photobank suite must pass with no test edits).