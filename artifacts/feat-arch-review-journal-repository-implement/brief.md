## Module
Journal

## Finding
The Journal repository implementations are stored inside a `Catalog` sub-folder of the Persistence layer:

```
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalTagRepository.cs
```

Their namespace reflects this: `namespace Anela.Heblo.Persistence.Catalog.Journal`.

Per `docs/architecture/filesystem.md`, the Persistence layer layout is:
```
Anela.Heblo.Persistence/
└── {Feature}/          ← feature-specific persistence (e.g. Journal/)
    ├── {Entity}Configuration.cs
    └── {Entity}Repository.cs
```

Journal is an independent module with no structural relationship to Catalog. The correct path is `Persistence/Journal/`.

## Why it matters
- Any developer looking for Journal's persistence code will search `Persistence/Journal/` and not find it.
- The `JournalModule.cs` DI registration already imports `Anela.Heblo.Persistence.Catalog.Journal`, propagating the wrong namespace into the Application layer.
- It implies a historical artifact (Journal may have been scaffolded inside Catalog) that was never cleaned up, creating a false coupling visible in every `using` statement.

## Suggested fix
Move both files to `backend/src/Anela.Heblo.Persistence/Journal/`, update their namespace to `Anela.Heblo.Persistence.Journal`, and fix the `using` in `JournalModule.cs` (or, better, do this as part of fixing issue #2513 which moves those registrations to `PersistenceModule.cs`).

---
_Filed by daily arch-review routine on 2026-06-04._