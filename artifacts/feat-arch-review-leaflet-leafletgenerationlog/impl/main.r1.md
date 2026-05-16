Both reviews passed. The implementation is complete.

---

# Implementation: Rename `LeafletGenerationLoggingBehavior` to `LeafletGenerationPersistenceBehavior`

## What was implemented
Pure identifier rename across three artifacts in the Leaflet vertical slice. No logic, no tests, no DI lifetime, and no runtime behavior changed — only the type name, file names, and logger generic argument.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehavior.cs` — renamed (git mv) + class/constructor/`ILogger<T>` generic updated
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs` — DI registration updated to new type name
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehaviorTests.cs` — renamed (git mv) + test class, mock generic, `CreateBehavior` return type updated

## Tests
`LeafletGenerationPersistenceBehaviorTests` — all 3 tests pass:
- `Handle_SavesGenerationRow_AndSetsResponseId`
- `Handle_WhenDbWriteFails_StillReturnsResponse_WithNullId`
- `Handle_ReturnsOriginalResponse`

## How to verify
```bash
# Build
dotnet build backend/Anela.Heblo.sln

# Run renamed fixture
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~LeafletGenerationPersistenceBehaviorTests" --no-build

# Confirm old name gone from source/test
rg "LeafletGenerationLoggingBehavior" backend/src/ backend/test/
# Expected: no output (exit 1)
```

## Notes
- Frozen historical plan `docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md` intentionally left untouched per spec FR-4.
- `QuestionLoggingBehavior` in the KnowledgeBase slice has the same misnomer but is explicitly out of scope per arch review — tracked as a separate follow-up.

## PR Summary
Renames `LeafletGenerationLoggingBehavior` to `LeafletGenerationPersistenceBehavior` to reflect the behavior's true responsibility: persisting the `LeafletGeneration` record and stamping `GenerateLeafletResponse.Id`. The previous "Logging" name misled readers about where the core persistence write happens. Pure rename — method body, DI lifetime, test assertions, and all runtime behavior are unchanged.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehavior.cs` — renamed + class/constructor/logger generic updated
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs` — DI registration updated
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehaviorTests.cs` — renamed + test class/mock/factory updated

## Status
DONE