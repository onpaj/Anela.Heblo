# Architecture Review: JournalEntry clock injection

## Skip Design: true
Backend-only domain refactor with no UI.

## Architectural Fit Assessment
Fits the existing pattern: `JournalEntry.Create(..., DateTime now)` is created in the handler with `var now = DateTime.UtcNow`. This change applies the same convention to `Update` and `SoftDelete`. The domain layer stays free of clock access. Per `docs/architecture/development_guidelines.md`, the domain does not reach for infrastructure, and this moves it closer to that.

## Proposed Architecture

### Component Overview
Handler (captures `now`) -> `JournalEntry.Update/SoftDelete(..., now)` -> repository. No new components.

### Key Design Decisions

#### Decision 1: Parameter style
**Options considered:** (a) trailing `DateTime now` parameter; (b) inject `TimeProvider` into handlers; (c) optional parameter defaulting to `UtcNow`.
**Chosen approach:** (a), required parameter, no default.
**Rationale:** It matches `Create` and the issue's suggested fix. A default (c) would keep the hidden clock dependency. `TimeProvider` (b) would be broader than the finding asks for. It can be a separate follow-up.

#### Decision 2: Single timestamp in SoftDelete
`DeletedAt` and `ModifiedAt` both take the same `now`. This also removes a small skew between the two former `UtcNow` calls.

## Implementation Guidance

### Directory / Module Structure
Modify only:
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs`
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/DeleteJournalEntry/DeleteJournalEntryHandler.cs`
- Tests: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs`, `.../Domain/Journal/SimpleJournalTests.cs`, `.../Features/Journal/UpdateJournalEntryHandlerTests.cs`, and the Delete handler tests if they call these methods.

### Interfaces and Contracts
```csharp
public void Update(string title, string content, DateTime entryDate, string userId, string username, DateTime now)
public void SoftDelete(string userId, string username, DateTime now)
```

### Data Flow
Handler `var now = DateTime.UtcNow` -> entity method -> `ModifiedAt/DeletedAt = now` -> `UpdateAsync` + `SaveChangesAsync`. The response `ModifiedAt` is read from the entity, unchanged.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed caller causes a compile error | Low | A compile error is the safe outcome. Run `dotnet build` for the whole solution and grep for `.SoftDelete(` and `.Update(`. |
| Process docs check on owned code | Low | Behaviour is unchanged. If `docs/processes` owns Journal code and CI flags it, bump `verified_at` and run `python3 scripts/process-docs/check.py index`. |

## Specification Amendments
None.

## Prerequisites
None. No migration is needed.
