## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs:1-91` — This new file duplicates ~90 lines of infrastructure verbatim from `LeafletRepositoryIntegrationTests.cs`: the `static` constructor disabling the Ryuk reaper, the `_container`/`_context`/`_repository` fields, `InitializeAsync`/`DisposeAsync`, and the entire `SetupSchemaAsync` SQL string. Since both files now exist side by side in the same `Integration` folder, this is a good candidate for a shared abstract base class (e.g. `LeafletIntegrationTestBase`) that owns the container lifecycle and schema setup, with each subclass only adding its own `MakeDocument` helper and test methods. Not required for this PR, but worth doing before a third integration test file in this folder repeats the pattern again.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs:98-116` — The new `MakeDocument` helper is a near-superset of the one already in `LeafletRepositoryIntegrationTests.cs` (adds `status`, `contentType`, `indexedAt`, `ingestedAt` parameters, drops `driveId`/`graphItemId`). If the shared base class above is introduced, a single `MakeDocument` with all optional parameters (matching the union of both signatures) would remove the need to maintain two independently-evolving copies of the same builder.

