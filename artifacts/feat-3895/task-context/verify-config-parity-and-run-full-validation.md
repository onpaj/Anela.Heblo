### task: verify-config-parity-and-run-full-validation


Runs NFR-3's required manual verification (the spec calls for it and no automated test covers it) plus the repo's completion gate from `CLAUDE.md`: `dotnet build`, `dotnet format`, full backend test run.

**Files:**
- Read-only: `backend/src/Anela.Heblo.API/appsettings.json`, `backend/src/Anela.Heblo.API/appsettings.Production.json`
- Possibly modified by `dotnet format`: any file touched by earlier tasks

- [ ] **Step 1: Confirm no `IEmbeddingGenerator` call site was missed**

```bash
grep -rn "GenerateAsync" --include=*.cs backend/src/ | grep -i "embedding"
```

Expected: exactly five Application-layer hits, each now passing an options argument, plus the adapter's own definition:

```
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs:  var embeddings = await _embeddingGenerator.GenerateAsync(
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs:  ... _embeddingGenerator.GenerateAsync(topics, _options.ToEmbeddingOptions(), ct)
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs:  ... _embeddingGenerator.GenerateAsync(summaries, _options.ToEmbeddingOptions(), ct)
backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs:  ... _embeddings.GenerateAsync([queryToEmbed], embeddingOptions, ct)
backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs:  ... _embeddings.GenerateAsync(inputs, _options.ToEmbeddingOptions(), ct)
```

If any hit still uses `cancellationToken: ct` with no options argument, add the missing pass-through before continuing.

- [ ] **Step 2: Confirm the `KnowledgeBase:*` embedding binding is gone from the adapter**

```bash
grep -rn "KnowledgeBase:Embedding" --include=*.cs backend/src/
```

Expected: no output (FR-5 acceptance criterion 1).

- [ ] **Step 3: Run the NFR-3 config-parity check**

```bash
grep -n "EmbeddingModel\|EmbeddingDimensions" backend/src/Anela.Heblo.API/appsettings.json \
                                               backend/src/Anela.Heblo.API/appsettings.Production.json
grep -rn "\"OpenAI\"" backend/src/Anela.Heblo.API/appsettings.json \
                      backend/src/Anela.Heblo.API/appsettings.Production.json
```

Expected output and the reasoning it must confirm:

- `appsettings.json:212` → `Leaflet.EmbeddingModel = "text-embedding-3-large"`; no `Leaflet.EmbeddingDimensions` key, so `RagFeatureOptions.EmbeddingDimensions = 1536` applies.
- `appsettings.json:239-240` → `KnowledgeBase.EmbeddingModel = "text-embedding-3-large"`, `KnowledgeBase.EmbeddingDimensions = 1536`.
- `appsettings.Production.json:109` → `Leaflet.EmbeddingModel = "text-embedding-3-large"`; no `Leaflet.EmbeddingDimensions`, no `KnowledgeBase` embedding overrides, so both fall through to `appsettings.json`.
- No `OpenAI:EmbeddingModel` / `OpenAI:EmbeddingDimensions` keys exist, so `OpenAiEmbeddingOptions` keeps its class defaults `"text-embedding-3-large"` / `1536`.

Conclusion to confirm explicitly: every feature now resolves `text-embedding-3-large` / `1536` — byte-identical to what the adapter resolved before this change from `KnowledgeBase:*`. No re-embedding, no pgvector dimension migration, and `KnowledgeBaseChunks.Embedding` / `LeafletChunks.Embedding` stay `vector(1536)`. **If any of these values differs from the above, stop and report it — that would mean this change alters production embeddings, which is explicitly out of scope.**

- [ ] **Step 4: Build the solution**

```bash
dotnet build
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or no new warnings relative to the pre-change baseline).

- [ ] **Step 5: Format**

```bash
dotnet format
dotnet format --verify-no-changes
```

Expected: the second command exits 0 with no output.

- [ ] **Step 6: Run the full backend test suite**

```bash
dotnet test
```

Expected: PASS — all test projects green, including `Anela.Heblo.Adapters.OpenAI.Tests` (16 tests) and `Anela.Heblo.Tests`.

- [ ] **Step 7: Commit any formatting changes**

Only if `dotnet format` modified files:

```bash
git add -A
git commit -m "style: apply dotnet format"
```

If `dotnet format` changed nothing, skip this step — there is nothing to commit.

---

## Self-Review

**1. Spec coverage**

| Spec item | Task |
|---|---|
| FR-1 (`options.ModelId` honored, per-model client cache, test seam preserved, 7 existing tests unmodified) | `add-per-model-embedding-client-cache` |
| FR-2 (`options.Dimensions` honored) | `honor-options-dimensions-in-embedding-generator` |
| FR-3 (Leaflet indexing + query call sites pass `LeafletOptions`) | `pass-embedding-options-from-leaflet-indexing-service`, `pass-embedding-options-from-generate-leaflet-handler` |
| FR-4 (KnowledgeBase call site passes `KnowledgeBaseOptions`) | `pass-embedding-options-from-knowledgebase-indexing-strategy` (+ `…-conversation-indexing-strategy`, `…-search-documents-handler` for the two call sites the spec missed) |
| FR-5 (`KnowledgeBase:*` → `OpenAI:*` fallback binding, all three acceptance criteria) | `rebind-adapter-embedding-defaults-to-openai-config-keys` |
| NFR-1 (at most one client construction per model, O(1) lookup) | `add-per-model-embedding-client-cache` steps 2 & 4 (`GenerateAsync_SameModelIdTwice_ConstructsClientOnce`, `Lazy<T>`-wrapped `GetOrAdd`) |
| NFR-2 (no public signature changes) | Only the `internal` constructor gains an optional parameter; `GenerateAsync`'s signature is untouched — see "Deviations" note 1 |
| NFR-3 (no config edits, no re-indexing) | `verify-config-parity-and-run-full-validation` step 3 |
| Arch review Amendment 1 (assert `model` from the request body) | `honor-options-dimensions-in-embedding-generator` step 2 extends `BuildEmbeddingResponse`; `add-per-model-embedding-client-cache` asserts on `capturedModels` |
| Arch review Amendment 2 (`ToEmbeddingOptions()` helper) | `add-ragfeatureoptions-toembeddingoptions-helper` |
| Arch review Amendment 3 (seed cache under `_options.EmbeddingModel`) | `add-per-model-embedding-client-cache` step 4 constructor body |
| Arch review risk "land FR-3/FR-4 with FR-5, no intermediate regression" | Task ordering — the config rename is the last production change |
| Out of scope: model/dimension value changes, backfilling, `vector(N)` guardrails, `AnthropicChatClient` changes, shared chat/embedding resolve helper | No task touches any of these |

Design doc coverage: component responsibilities, the `ConcurrentDictionary<string, Lazy<EmbeddingClient>>` shape, the deferred-construction rule, the test-seam seeding rule, and the unchanged persistence schema are all reflected in the tasks above. Open Questions: none in the spec.

**2. Placeholder scan** — every code step contains complete, compilable code copied against the actual current file contents; every test step shows the full test body; every run step gives an exact command and its expected pass/fail outcome. No "TBD", no "add error handling", no "similar to earlier task".

**3. Type consistency** — `RagFeatureOptions.ToEmbeddingOptions()` returns `Microsoft.Extensions.AI.EmbeddingGenerationOptions`, which is exactly the second parameter type of `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync`, and exactly the `MeaiOptions? options` parameter the adapter now reads. `OpenAiEmbeddingOptions.EmbeddingModel` (`string`) / `.EmbeddingDimensions` (`int`) match `RagFeatureOptions.EmbeddingModel` (`string`) / `.EmbeddingDimensions` (`int`), so `options?.ModelId ?? _options.EmbeddingModel` yields `string` and `options?.Dimensions ?? _options.EmbeddingDimensions` yields `int` — both non-nullable, as `global::OpenAI.Embeddings.EmbeddingGenerationOptions.Dimensions` (`int?`) accepts. The internal constructor's new parameter `Func<string, EmbeddingClient>? clientFactory = null` is satisfied by `RecordingClientFactory.Create(string model)` returning `EmbeddingClient`. `ConversationIndexingStrategy`'s new `IOptions<KnowledgeBaseOptions>` parameter matches what `KnowledgeBaseDocIndexingStrategy` already takes and what `KnowledgeBaseModule` already registers.
