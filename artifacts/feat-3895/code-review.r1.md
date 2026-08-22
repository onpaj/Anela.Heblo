## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs:50` — `embeddingOptions` is materialized into a local variable here while `SearchDocumentsHandler.cs:47` and `KnowledgeBaseDocIndexingStrategy.cs:44` inline the equivalent `_options.ToEmbeddingOptions()` call directly at the call site. Harmless either way (spec explicitly leaves a shared-helper refactor out of scope), but picking one style consistently across the four call sites would read slightly cleaner. Not worth a change on its own.
