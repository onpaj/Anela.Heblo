# Implementation: rebind-adapter-embedding-defaults-to-openai-config-keys

## What was implemented
Changed the OpenAI adapter's DI-time embedding option binding from the `KnowledgeBase:*` config
keys to neutral `OpenAI:*` keys (`OpenAI:EmbeddingModel`, `OpenAI:EmbeddingDimensions`), matching
the adjacent `OpenAI:ApiKey` line and the `Anthropic:*` convention used by the sibling Anthropic
adapter. Every current call site now supplies its own per-feature `EmbeddingGenerationOptions`
(via `RagFeatureOptions.ToEmbeddingOptions`), so this binding is now purely a fallback for future
consumers that resolve `IEmbeddingGenerator` without passing explicit options — it should no
longer be scoped to the KnowledgeBase feature's config namespace. Neither `OpenAI:EmbeddingModel`
nor `OpenAI:EmbeddingDimensions` exists in `appsettings*.json`, so the class defaults
(`"text-embedding-3-large"` / `1536`) apply unchanged — identical to what the old
`KnowledgeBase:*` binding previously resolved to, since those keys were also absent.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiAdapterServiceCollectionExtensions.cs` —
  lines 16-17 rebound from `KnowledgeBase:EmbeddingModel`/`KnowledgeBase:EmbeddingDimensions` to
  `OpenAI:EmbeddingModel`/`OpenAI:EmbeddingDimensions`, with a comment explaining this is now a
  pure fallback binding.
- `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj` —
  added `Microsoft.Extensions.Configuration` 8.0.0 and `Microsoft.Extensions.DependencyInjection`
  8.0.1 package references so the test project has the concrete `ConfigurationBuilder` and
  `ServiceCollection` types (previously only the abstractions were available transitively).
- `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiAdapterServiceCollectionExtensionsTests.cs`
  (new) — binding tests for `AddOpenAiAdapter`.

## Tests
`OpenAiAdapterServiceCollectionExtensionsTests` (new, 3 tests):
- `AddOpenAiAdapter_NoEmbeddingKeys_UsesClassDefaults` — with only `OpenAI:ApiKey` set, confirms
  `EmbeddingModel`/`EmbeddingDimensions` fall back to the class defaults.
- `AddOpenAiAdapter_OpenAiEmbeddingKeys_OverrideClassDefaults` — confirms `OpenAI:EmbeddingModel`
  and `OpenAI:EmbeddingDimensions` config keys now override the defaults.
- `AddOpenAiAdapter_KnowledgeBaseEmbeddingKeys_AreIgnored` — confirms `KnowledgeBase:EmbeddingModel`
  / `KnowledgeBase:EmbeddingDimensions` are no longer read by the adapter binding (regression guard
  against re-coupling the adapter to the KnowledgeBase feature's config namespace).

Pre-change run (`--filter FullyQualifiedName~OpenAiAdapterServiceCollectionExtensionsTests`)
confirmed the expected red state: `AddOpenAiAdapter_NoEmbeddingKeys_UsesClassDefaults` passed,
the other two failed with the exact messages predicted in the task context. After the source
change, the full adapter test project passes: 16/16.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj
```
Expected: Passed! - Failed: 0, Passed: 16, Skipped: 0, Total: 16.

Also verified no other source code (only docs/plan artifacts) references the retired
`KnowledgeBase:EmbeddingModel` / `KnowledgeBase:EmbeddingDimensions` keys, via
`grep -r "KnowledgeBase:Embedding"` across the repo.

## Notes
No deviations from the task context. Implemented exactly steps 1-5 as specified; step 6 (commit)
is handled by the orchestrator's standard commit/push flow at the end of this unit rather than as
a separate commit inside this artifact step, consistent with how prior tasks in this feature were
closed out.

## PR Summary
Rebinds the OpenAI adapter's DI-time embedding options fallback from `KnowledgeBase:EmbeddingModel`
/ `KnowledgeBase:EmbeddingDimensions` to neutral `OpenAI:EmbeddingModel` / `OpenAI:EmbeddingDimensions`
keys, matching the adjacent `OpenAI:ApiKey` binding and the `Anthropic:*` convention on the sibling
adapter. This binding is now a pure fallback — every call site added earlier in this feature already
passes its own per-feature embedding options — so it should no longer be named after the
KnowledgeBase feature. Neither key is set in `appsettings*.json`, so the class defaults
(`text-embedding-3-large` / `1536`) continue to apply, unchanged in effect from before.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiAdapterServiceCollectionExtensions.cs` — rebind embedding option keys from `KnowledgeBase:*` to `OpenAI:*`
- `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/Anela.Heblo.Adapters.OpenAI.Tests.csproj` — add `Microsoft.Extensions.Configuration`/`Microsoft.Extensions.DependencyInjection` package references
- `backend/test/Anela.Heblo.Adapters.OpenAI.Tests/OpenAiAdapterServiceCollectionExtensionsTests.cs` — new binding tests covering defaults, `OpenAI:*` override, and `KnowledgeBase:*` no longer being read

## Status
DONE
