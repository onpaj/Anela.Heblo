## Problem

`backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs:51-65`:

```csharp
public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
    IEnumerable<string> values,
    MeaiOptions? options = null,           // accepted, never read below
    CancellationToken cancellationToken = default)
{
    ...
    var embeddingOptions = new global::OpenAI.Embeddings.EmbeddingGenerationOptions { Dimensions = _options.EmbeddingDimensions };
    ...
    var result = await Pipeline.ExecuteAsync(
        async token => await _client.Value.GenerateEmbeddingsAsync(chunk, embeddingOptions, cancellationToken: token),
        cancellationToken);
```

`_client` is a `Lazy<EmbeddingClient>` built once from `_options.EmbeddingModel` (line 46), and `_options` is bound solely from `configuration["KnowledgeBase:EmbeddingModel"]` / `configuration["KnowledgeBase:EmbeddingDimensions"]` (`OpenAiAdapterServiceCollectionExtensions.cs:16-17`). The incoming `MeaiOptions? options` parameter — `Microsoft.Extensions.AI.EmbeddingGenerationOptions`, which exposes a `ModelId` property for exactly this purpose — is never referenced anywhere in the method body.

## Rule violated / established pattern broken

`AnthropicChatClient.GetResponseAsync`, in the same part, gets this right: `var model = options?.ModelId ?? _options.Model;` (`AnthropicChatClient.cs:77`). Leaflet relies on precisely that mechanism — `GenerateLeafletHandler.cs:102` sets `ChatOptions.ModelId = _options.ChatModel` (bound from `Leaflet:ChatModel` in `appsettings.json:210`) to run its chat calls on its own model independent of `KnowledgeBase:ChatModel`. There is no equivalent path for embeddings: neither `LeafletIndexingService.cs:61` nor `KnowledgeBaseDocIndexingStrategy.cs:44` passes an `options` argument to `GenerateAsync` at all, and even if one did, the adapter would silently drop it.

A prior finding (#3770, closed) flagged this exact `KnowledgeBase:*`-prefixed config coupling in the adapter DI extensions as worth a look but didn't investigate the consequence; this finding is the concrete follow-through.

## Why it matters (concrete)

- `appsettings.json:212` sets `Leaflet:EmbeddingModel` to `"text-embedding-3-large"`. Nothing in `LeafletIndexingService` or `GenerateLeafletHandler` ever reads `LeafletOptions.EmbeddingModel` into a call — it is dead configuration that reads as a working per-feature override and is not one.
- `KnowledgeBaseChunks.Embedding` and `LeafletChunks.Embedding` are two independently-declared `vector(1536)` pgvector columns (`Migrations/20260331070417_UpgradeEmbeddingTo3Large.cs:19`, `Migrations/20260430170922_AddLeafletStore.cs:68`). Both currently agree at 1536 only because `KnowledgeBase:EmbeddingDimensions` happens to be the sole source both features draw from. If `KnowledgeBase:EmbeddingDimensions` is ever changed for KnowledgeBase's own reasons, every `LeafletIndexingService` write breaks (`vector(N)` column vs. an embedding of the wrong length returned by OpenAI), or — if the Leaflet migration were updated in step but its config wasn't — similarity search silently degrades against mismatched vectors. The config key's name (`KnowledgeBase:*`) makes this cross-feature blast radius invisible to whoever changes it.

## Suggested direction

Either honor `options?.ModelId` (and a dimensions equivalent) the same way `AnthropicChatClient` honors `ChatOptions.ModelId`, or — if a single shared embedding model/dimension is actually intentional — rename the config section away from the feature-scoped `KnowledgeBase:*` prefix to something that reads as shared, and delete the dead `Leaflet:EmbeddingModel` key, so the configuration's apparent scope matches its real scope either way.

