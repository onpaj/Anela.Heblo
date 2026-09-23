# Architecture Review: Relocate GraphService exception types out of UserManagement/Contracts

## Skip Design: true
Pure backend refactor — file move, namespace rename, and `using` fix-ups only. No UI, no new visual components, no API contract change (the moved types are internal exception types, not DTOs on any generated OpenAPI client).

## Architectural Fit Assessment
The spec's proposed target — `Infrastructure/Exceptions/` — is directly validated against the codebase and docs:

- `docs/architecture/filesystem.md` (feature directory template) documents `Infrastructure/` containing an `Exceptions/` subfolder, alongside `{Entity}Scheduler.cs`, `{Entity}FeatureFlags.cs`.
- `Contracts/` in the same doc is documented as "Shared DTOs across use cases" (`{Entity}Dto.cs`, `[Other shared DTOs]`) — confirmed in the actual `UserManagement/Contracts/` folder, which today holds `DepartmentDto.cs`, `UserDto.cs`, and the two misplaced exception files.
- `UserManagement/Infrastructure/` already exists and holds `EntraAccessUserSourceAdapter.cs` and `GraphArticleUserResolver.cs` — the two internal adapters that are the primary throw/catch sites for these exceptions. Nesting `Exceptions/` under this existing folder (rather than inventing a new feature-root `Exceptions/` sibling to `Contracts/`) matches the documented template exactly and keeps exception types colocated with the infrastructure code that raises them, which is also where Graph SDK-specific failure translation happens (`EntraAccessSourceAuthException`, `ArticleUserResolverAuthException`, etc. already live via those adapters).

This confirms the spec's FR-1 choice of `Infrastructure/Exceptions/` over a bare feature-root `Exceptions/` folder — the latter is the issue's parenthetical alternative but not what the documented template shows.

I read all four files the issue named, and then ran a full-repository grep for `GraphServiceAuthException`/`GraphServiceException` rather than trusting the issue's file list — the issue undercounts the reference surface. It missed the actual `IGraphService` implementation and three unit test files entirely. Verified, complete file-by-file inventory:

| File | Uses `UserDto` by name (needs `Contracts` using) | Uses the two exceptions (needs new `Infrastructure.Exceptions` using) |
|---|---|---|
| `Services/IGraphService.cs` | Yes (`List<UserDto>` return types) | Yes (XML `<exception cref>` doc comments) |
| `UseCases/GetGroupMembers/GetGroupMembersHandler.cs` | Yes (`new List<UserDto>()`) | Yes (2 catch blocks) |
| `Infrastructure/EntraAccessUserSourceAdapter.cs` | Yes (`List<UserDto> users;`) | Yes (2 catch blocks) |
| `Infrastructure/GraphArticleUserResolver.cs` | **No** — `var members = await _graph.GetGroupMembersAsync(...)` is inferred; no `UserDto`/DTO type named explicitly in this file | Yes (2 catch blocks) |
| `src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` (**not in issue** — the concrete `IGraphService` implementation) | Yes (throughout, `List<UserDto>` locals/returns) | Yes (`throw new GraphServiceException(...)` / `throw new GraphServiceAuthException(...)`, 4 throw sites + 1 catch) |
| `test/.../GetGroupMembersHandlerTests.cs` (**not in issue**) | Yes (`List<UserDto>`) | Yes (mocked `ThrowsAsync`) |
| `test/.../GraphServiceTests.cs` (**not in issue**) | Yes (`List<UserDto>`) | Yes (`Assert.ThrowsAsync<...>`) |
| `test/.../EntraAccessUserSourceAdapterTests.cs` (**not in issue**) | Yes (`List<UserDto>`) | Yes (mocked `ThrowsAsync`) |

This means `GraphArticleUserResolver.cs`'s existing `using Anela.Heblo.Application.Features.UserManagement.Contracts;` is used *only* for the two exception types today — it should be **replaced** with the new `Infrastructure.Exceptions` using, not kept alongside it (an unused `using` is a style/lint smell this task should not introduce). Every other file in the table above references `UserDto` by name and must **keep** its `Contracts` using **and add** the new `Infrastructure.Exceptions` using.

Additionally, `test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — a reflection-based module-boundary fitness test — contains three comments/assertion messages that name-check "UserManagement.Contracts" as the defined location of these two exception types (around its `SdkExceptionAllowlist` field and its `Application_types_should_not_catch_SDK_exception_types_directly` test). The test's actual enforcement is namespace-prefix-based (`Anela.Heblo.Application`) via reflection, so it will not break, but the comment text becomes inaccurate documentation of exactly the thing this task is fixing and should be corrected alongside the move.

## Proposed Architecture

### Component Overview
```
Features/UserManagement/
├── Contracts/
│   ├── DepartmentDto.cs
│   └── UserDto.cs                          (unchanged — exceptions removed from here)
├── Infrastructure/
│   ├── Exceptions/                         (NEW)
│   │   ├── GraphServiceAuthException.cs    (moved, renamed namespace)
│   │   └── GraphServiceException.cs        (moved, renamed namespace)
│   ├── EntraAccessUserSourceAdapter.cs     (using list updated)
│   └── GraphArticleUserResolver.cs         (using list updated: Contracts → Infrastructure.Exceptions)
├── Services/
│   └── IGraphService.cs                    (using list updated: add Infrastructure.Exceptions, keep Contracts)
└── UseCases/GetGroupMembers/
    └── GetGroupMembersHandler.cs           (using list updated: add Infrastructure.Exceptions, keep Contracts)
```

No new abstractions, interfaces, or dependency edges are introduced. This is a pure namespace/location correction of two existing sealed exception classes; call sites, catch/throw semantics, and constructor signatures are untouched.

### Key Design Decisions

#### Decision 1: Target folder — `Infrastructure/Exceptions/` vs. feature-root `Exceptions/`
**Options considered:**
1. `Features/UserManagement/Exceptions/` (issue's primary suggestion)
2. `Features/UserManagement/Infrastructure/Exceptions/` (issue's parenthetical alternative, and what filesystem.md's template actually shows)

**Chosen approach:** Option 2, `Infrastructure/Exceptions/`.

**Rationale:** `docs/architecture/filesystem.md` documents `Exceptions/` explicitly as a child of `Infrastructure/`, not as a feature-root sibling of `Contracts/`. Following the documented template exactly (rather than the issue's first-listed option) keeps this feature consistent with whatever convention other features already follow or will follow, and avoids introducing a second, undocumented exceptions convention. Per CLAUDE.md's "no architectural changes without consulting these first" directive, the documented path wins.

#### Decision 2: Namespace
**Options considered:** Keep exceptions in the `Contracts` namespace physically moved to a different folder (namespace/folder mismatch) vs. rename namespace to match new folder.

**Chosen approach:** Rename namespace to `Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions`, matching the new folder path (this codebase follows folder-matches-namespace convention throughout, as seen in every file reviewed).

**Rationale:** Consistency with existing convention; avoids a confusing folder/namespace mismatch that would itself become a future "arch-review" finding.

#### Decision 3: Per-file `using` handling
**Options considered:** (a) Blanket-add the new using to all four referencing files and leave existing `Contracts` usings untouched everywhere; (b) inspect each file's actual name references and only keep/add usings that are actually used.

**Chosen approach:** (b), per-file inspection (see table above).

**Rationale:** `GraphArticleUserResolver.cs` does not reference any `Contracts` DTO by name — leaving its `Contracts` using in place after removing the only symbols it resolved (the two exceptions) would create a genuinely unused `using`, which most C# analyzers (and `dotnet format`) will flag. The other three files do reference `UserDto`/`GetGroupMembersResponse` by name and must keep their `Contracts` using. This decision does not change spec FR-3's acceptance criteria (which already say "do not remove the Contracts using where DTOs from that namespace are still referenced" — the implication being it should be removed where they are not).

## Implementation Guidance

### Directory / Module Structure
Create `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/` and move both files there via `git mv` (preserves history) rather than delete+recreate.

### Interfaces and Contracts
No interface changes. `IGraphService`'s XML doc `<exception cref="GraphServiceAuthException">` / `<exception cref="GraphServiceException">` comments must resolve under the new using — add `using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;` to `IGraphService.cs` alongside its existing `Contracts` using (still needed for `UserDto`).

### Data Flow
Unchanged. `IGraphService` implementations still throw `GraphServiceAuthException`/`GraphServiceException`; `GetGroupMembersHandler`, `EntraAccessUserSourceAdapter`, and `GraphArticleUserResolver` still catch them at the same call sites with identical translation logic (error codes, wrapped domain exceptions). Only the import path changes.

### File-by-file using changes
- `Contracts/GraphServiceAuthException.cs` → move to `Infrastructure/Exceptions/GraphServiceAuthException.cs`, change namespace.
- `Contracts/GraphServiceException.cs` → move to `Infrastructure/Exceptions/GraphServiceException.cs`, change namespace.
- `Services/IGraphService.cs` → add `using ...Infrastructure.Exceptions;`; keep `using ...Contracts;` (for `UserDto`).
- `UseCases/GetGroupMembers/GetGroupMembersHandler.cs` → add `using ...Infrastructure.Exceptions;`; keep `using ...Contracts;` (for `UserDto`).
- `Infrastructure/EntraAccessUserSourceAdapter.cs` → add `using ...Infrastructure.Exceptions;`; keep `using ...Contracts;` (for `UserDto`). Note: since this file already lives under `Infrastructure/`, and the new exceptions namespace is `...Infrastructure.Exceptions`, a fully-qualified reference would also work without a new using line as long as the `using` is added for clarity/consistency with the other files — the review recommends adding it explicitly rather than relying on any implicit resolution, since C# does not implicitly search child namespaces.
- `Infrastructure/GraphArticleUserResolver.cs` → replace `using ...Contracts;` with `using ...Infrastructure.Exceptions;` (this file has no other reason to import `Contracts`).
- `src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` → add `using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;`; keep `using ...Contracts;` (for `UserDto`).
- `test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs`, `GraphServiceTests.cs`, `EntraAccessUserSourceAdapterTests.cs` → each add `using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;`; keep `using ...Contracts;` (for `UserDto`).
- `test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` → text-only edit: update the three comment/message occurrences of "UserManagement.Contracts" (describing where `GraphServiceAuthException`/`GraphServiceException` are defined) to "UserManagement.Infrastructure.Exceptions". No logic change.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missing a reference site not caught by the grep-based inventory (this review already found 4 files the issue itself missed — a repo-wide grep, not the issue's file list, is the source of truth) | Low | Run `dotnet build` after the move; the compiler will surface any missed reference as CS0246 (type/namespace not found). Re-run `grep -rl "GraphServiceAuthException\|GraphServiceException" backend/` before declaring done and confirm it matches the 8 files enumerated here. |
| Leaving an unused `Contracts` using in `GraphArticleUserResolver.cs` (or removing a still-needed one elsewhere) | Low | Per-file table above enumerates exactly what each file needs; `dotnet format`/analyzer warnings on unused usings will catch any mistake. |
| XML doc `<exception cref>` in `IGraphService.cs` failing to resolve silently (cref warnings are easy to miss, not build-breaking by default) | Low | Explicitly add the new using to `IGraphService.cs` per the file-by-file guidance above, regardless of whether cref resolution alone would trigger a build error. |

## Specification Amendments
- Spec FR-3 should be read together with this review's per-file table: `GraphArticleUserResolver.cs`'s `Contracts` using is **replaced**, not kept-alongside, since it has no DTO reference in that file. The other three referencing files keep `Contracts` and add the new using. This is a clarification of FR-3, not a scope change — no new acceptance criteria are needed beyond what FR-3 already states ("do not remove the Contracts using where DTOs from that namespace are still referenced" implies removing it where they are not).
- No other amendments. The spec's FR-1 choice of `Infrastructure/Exceptions/` is confirmed correct against the documented filesystem template, not just a "less bad than the alternative" pick.

## Prerequisites
None. No migrations, config, or infrastructure changes needed — this can be implemented directly.
