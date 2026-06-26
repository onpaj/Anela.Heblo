## Module
Leaflet

## Finding
`backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs` is named and registered as a "logging" pipeline behavior, but its `Handle` method (lines 27–65) does two non-logging things:

1. **Persists the generation record** (lines 42–56):
   ```csharp
   await _repository.SaveGenerationAsync(generation, cancellationToken);
   ```
   This is a core business write operation — creating the `LeafletGeneration` record that the feedback and history features depend on.

2. **Mutates the handler's response** (line 57):
   ```csharp
   response.Id = generation.Id;
   ```
   The `GenerateLeafletResponse.Id` that is returned to the client is set here, not in the handler. The handler returns `Id = null`.

The class name suggests cross-cutting observability infrastructure. A developer searching for "where is the generation record saved" would look in `GenerateLeafletHandler` and not find it.

## Why it matters
Single Responsibility: the behavior conflates observability (timing/logging) with domain persistence (saving a generation) and response shaping (setting the ID). The name actively misleads. If the behavior is disabled or removed for any reason, a core write is silently lost. Tests for the generation save must mock `ILeafletRepository` in a pipeline behavior, not in a handler.

## Suggested fix
Rename the class to `LeafletGenerationPersistenceBehavior` (or `SaveLeafletGenerationBehavior`) to accurately describe its responsibility. If timing/logging is also desired, extract that into a separate `LeafletGenerationLoggingBehavior`. This is a rename + optional split — no logic needs to change.

---
_Filed by daily arch-review routine on 2026-05-14._