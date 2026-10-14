# KB Question Tags — Design

**Date:** 2026-10-14
**Status:** Approved design, pending spec review
**Module:** KnowledgeBase (Poradenství), Shared/Rag

## Problem

The advisory team wants to analyse questions asked in Poradenství (KB):

1. most frequent questions in a given period
2. most frequent questions about a specific product
3. most frequent questions about a specific problem (akné, dermatitida, …)

Every KB Ask is already logged in `RagInteractionLogs` (`Feature = KnowledgeBase`), but rows carry no
product or problem information, and free-text questions cannot be counted by `GROUP BY Question`.

### Production data (2026-10-14, read-only query on Heblo_V3)

- 775 KB questions since 2026-04-01, from 15 staff users, ~120/month and rising (172 in Sept).
- They are real customer questions pasted in by staff, e.g. *"Tatínek 88 oteklé nohy… pomůže máslo hořčíkové"*.
- Customers name products in questions ("hořčíkové máslo", "klidné nožky", "mycí olej").
- Some questions are operational, not advisory ("proč nejde objednat mycí olej?").
- `ExpandedQuery` is populated only from 2026-07-09 onward (335 older rows have none).

## Decisions

| # | Decision | Reason |
|---|---|---|
| D1 | Tag at ingest by extending the existing LLM query-expansion call | No extra call or latency; the expander already pulls out the problem (`Problém: …`) |
| D2 | Open, self-building tag vocabulary that starts empty | No taxonomy to maintain; at ~120 questions/month a usable library forms within weeks |
| D3 | Free-text product names, not catalog codes | Consistent with D2; doesn't put the whole catalog into every prompt |
| D4 | Products *recommended* come from existing middleware, deterministically | Exact validated codes for free; no LLM tagging of answers |
| D5 | Tags stored as `text[]` with exact-match filtering, not full-text search | Dropdowns of existing tags; canonical tags avoid the Czech declension problem; exact counts |
| D6 | No backfill in this iteration | **Next step.** Tagging must be callable outside the Ask path so the backfill is a loop, not a refactor |
| D7 | No answer-side LLM tags | YAGNI — products recommended are covered by D4 |

## Data model

New columns on `RagInteractionLogs` (manual migration, as with every migration in this repo):

| Column | Type | Source | Example |
|---|---|---|---|
| `ProblemTags` | `text[] NOT NULL DEFAULT '{}'` | expander LLM, from the question | `{akné, růžovka}` |
| `ProductTags` | `text[] NOT NULL DEFAULT '{}'` | expander LLM, from the question | `{hořčíkové máslo}` |
| `RecommendedProductCodes` | `text[] NOT NULL DEFAULT '{}'` | `PostAnswerEnrichmentMiddleware` | `{ABC123}` |

A GIN index on each of `ProblemTags` and `ProductTags`. Mapped as `List<string>` on the domain entity
(Npgsql primitive collections, EF Core 8).

Existing rows get empty arrays and stay untagged until the backfill (D6).

## Components

### 1. Expander returns structure — `Shared/Rag`

```csharp
public sealed record RagExpansionResult(
    string QueryToEmbed,
    IReadOnlyList<string> ProblemTags,
    IReadOnlyList<string> ProductTags);

Task<RagExpansionResult> ExpandAsync(string query, RagQueryExpansionConfig config, CancellationToken ct);
```

Internal application type, so a record is fine (the "DTOs are classes" rule applies to API contracts only).

**Why structure is required:** today the expander's string output is fed straight into the embedding.
If tags were appended to that string they would pollute the query vector and hurt retrieval.

**Response parsing** — a new `RagExpansionParser`, a pure static function that is unit-tested on its own:

1. Strip a surrounding ```` ```json ```` / ```` ``` ```` fence if present (Haiku often adds one).
2. If the trimmed text starts with `{`, try to deserialize `{ "expanded": string, "problemTags": string[], "productTags": string[] }`.
3. On success with a non-blank `expanded`: return it plus the normalised tags.
4. Otherwise: return the **whole raw response** as `QueryToEmbed` with empty tags. This is exactly today's
   behaviour, so a bad prompt or malformed output only loses the tags, never the answer. A warning is
   logged only when the text started with `{` but failed to parse (so Leaflet's plain-text prompt, which
   never starts with `{`, produces no log noise).

**Tag normalisation** (in the parser): trim, `ToLowerInvariant`, collapse inner whitespace, drop empty
values, de-duplicate, and keep at most `MAX_TAGS_PER_KIND = 5` per kind.

Existing failure handling is unchanged: when expansion is disabled or the call fails, the result is
`(query, [], [])`.

**Call sites:**
- `SearchDocumentsHandler` uses `QueryToEmbed` for the embedding and records the tags (see 3).
- `GenerateLeafletHandler` uses `QueryToEmbed` only. Its prompt is unchanged and its behaviour stays identical.

### 2. Tag library — `KnowledgeBase`

`IQuestionTagLibrary.BuildExpansionConfigAsync(RagQueryExpansionConfig baseConfig, CancellationToken)`:

- Loads existing tag counts via `IRagInteractionLogRepository.GetTagCountsAsync(RagFeature.KnowledgeBase, from: null, to: null)`.
- Takes the top `MAX_LIBRARY_TAGS_PER_KIND = 200` per kind by count. This bounds prompt size; at the
  current volume the cap won't be reached for months.
- Replaces the `{EXISTING_TAGS}` placeholder in the prompt with two lists ("Problémy: …", "Produkty: …"),
  or "(zatím žádné)" when empty. A prompt without the placeholder is returned unchanged.

Lives in KnowledgeBase, not Shared, because the vocabulary belongs to KB. Calling it from a future backfill
job, together with `ExpandAsync`, is the whole tagging pipeline for one question (D6).

No caching: at ~120 Asks/month the query is a `GROUP BY` over a few hundred rows per Ask.

### 3. Prompt — `KnowledgeBaseOptions.QueryExpansionPrompt`

The code default changes. It keeps the current Problém/Kontext/Doporučení/Ingredience structure inside
`expanded`, so the embedding input stays the same in substance. It also asks for:

- JSON output only: `{"expanded": "...", "problemTags": [...], "productTags": [...]}`
- the `{EXISTING_TAGS}` library, with the instruction: reuse an existing tag when it fits; create a new one
  only when none fits
- tags as short noun phrases, lowercase, **nominative singular** ("růžovka", not "růžovku")
- `problemTags` = skin/body problems or care goals (akné, suchá pleť, otoky nohou); empty for operational
  questions (ordering, shipping, availability)
- `productTags` = only products the customer names explicitly; never products the model would recommend

The exact prompt wording is settled during implementation and checked against a handful of the real
questions sampled above.

### 4. Recording

`IRagInteractionRecorder`:
- `RecordRetrieval(expandedQuery, topK, chunks, problemTags, productTags)`, called from `SearchDocumentsHandler`
- `RecordRecommendedProducts(IReadOnlyList<string> codes)`, called from `AskQuestionHandler`

`RagInteractionLogFactory` copies all three lists onto the entity.

**Shared retrieval path.** `SearchDocumentsHandler` also serves Smartsupp draft replies, article
generation, MCP `SearchKnowledgeBase` and the search endpoint. All use the KB expansion config, so all
will produce tags. That is accepted: `QueryToEmbed` is unchanged, Smartsupp rows simply carry tags too,
and the library and stats are filtered to `Feature = KnowledgeBase` (component 2 and 6). Each such
search also pays one small extra DB query for the library.

### 5. Products recommended — `PostAnswerEnrichmentMiddleware`

The middleware is a keyed **singleton** and the recorder is **scoped**, so the middleware cannot write to
the recorder. Instead it collects the codes it resolved (known codes only, de-duplicated, in order of
first appearance) and puts them in `ChatResponse.AdditionalProperties["anela.recommendedProductCodes"]`
(a named constant). `AskQuestionHandler` reads that key and calls `RecordRecommendedProducts`. When the key
is missing, it records an empty list.

### 6. Querying

`IRagInteractionLogRepository`:
- `GetFeedbackLogsPagedAsync` takes a filter object instead of the growing positional parameter list:
  `RagFeedbackLogFilter(Feature, HasFeedback, UserId, CreatedFrom, CreatedTo, ProblemTag, ProductTag)`.
  Tag filters are exact matches (`"ProblemTags" @> ARRAY[@tag]`).
- `GetTagCountsAsync(RagFeature feature, DateTimeOffset? from, DateTimeOffset? to)` returns
  `(Kind, Tag, Count)` rows. It may be raw SQL over `unnest` if EF can't translate the `SelectMany`+`GroupBy`
  (keep the parameter types explicit; see memory gotcha on raw-SQL timestamp typing).

Application:
- `GetFeedbackListRequest` gains `DateFrom`, `DateTo` (`DateOnly?`, interpreted as **inclusive** calendar days
  in Europe/Prague and converted to a half-open `[from, to+1d)` UTC range), `ProblemTag`, `ProductTag`.
- New use case `GetQuestionTagStats` (`DateFrom`, `DateTo`) returns the most frequent problem tags, product
  tags and recommended product codes (code resolved to a name via `IProductEnrichmentCache`) with counts.
  `GetQuestionTagStatsResponse : BaseResponse`.

API (`KnowledgeBaseController`, same `[FeatureAuthorize(Customer_KnowledgeBase, Write)]` as `feedback/list`):
- `GET feedback/list` gains the new query parameters
- `GET feedback/tag-stats?dateFrom=&dateTo=` is new

### 7. Frontend — Poradenství (KB) tab of the feedback page

- Date-range inputs (from/to) and two tag dropdowns (Problém, Produkt) next to the existing filters. The
  options come from `tag-stats` for the selected range, each label with its count.
- A tag-frequency panel above the table: top problems, top products asked about, top products recommended.
  Clicking a problem or product-asked tag sets the matching dropdown.
- Tag chips on each row and in the detail modal.
- The KB adapter (`useKbFeedbackAdapter`) passes the new filters through. The other feedback tabs
  (Article, Leaflet, Smartsupp) are unaffected; the new filter controls render only when the adapter
  supports them.
- API hooks use absolute URLs (`${apiClient.baseUrl}${relativeUrl}`). Dates returned by raw-JSON hooks are
  strings (memory gotcha).

## Error handling

| Failure | Behaviour |
|---|---|
| Expansion LLM call fails | Unchanged: raw query embedded, no tags |
| LLM returns non-JSON / malformed JSON | Raw response embedded (today's behaviour), no tags, warning only for `{…}` that failed to parse |
| Tag library query fails | Log an error, continue with "(zatím žádné)": tagging still works, just without reuse guidance |
| Logging the interaction fails | Unchanged: `QuestionLoggingBehavior` logs and swallows; the Ask still succeeds |
| Unknown tag in a filter | Empty result, not an error |

## Testing

- **Unit, `RagExpansionParser`:** valid JSON; fenced JSON; plain text (Leaflet-style); malformed `{`;
  blank `expanded`; tag normalisation (case, whitespace, duplicates, empty values, cap of 5).
- **Unit, `RagQueryExpander`:** returns parser output; disabled/failure paths return `(query, [], [])`.
- **Unit, `QuestionTagLibrary`:** placeholder substitution, empty library, cap of 200, prompt without placeholder unchanged, repository failure.
- **Unit, `PostAnswerEnrichmentMiddleware`:** codes in `AdditionalProperties`; unknown codes excluded; duplicates collapsed.
- **Unit, handlers:** `SearchDocumentsHandler` embeds `QueryToEmbed` and records tags; `AskQuestionHandler`
  records recommended codes and handles a missing key; `GetFeedbackList` maps the new filters (including the
  inclusive Prague `DateTo`); `GetQuestionTagStats` shape and code→name resolution.
- **Integration (real Postgres, `Category=Integration`):** `text[]` round-trip, `@>` tag filter, date
  range, `GetTagCountsAsync` grouping and `Feature` scoping. InMemory can't check how Npgsql translates
  array operators, and CI excludes Integration tests, so these are run locally (`podman machine start`).
- **Frontend:** filter controls, tag panel click → filter, adapter param mapping; gate on `CI=false npm run build`.
- **Manual check after deploy:** ask 3–5 real questions from the sample on staging and confirm tags,
  stable retrieval (same top sources as before for the same question), and the stats panel.

## Out of scope / next steps

1. **Backfill (next step).** A one-off Hangfire job: for each untagged KB row, run the tag library plus `ExpandAsync`
   on the raw `Question` and persist only the tags (do not overwrite `ExpandedQuery`). About 775 Haiku
   calls. Needs `[AutomaticRetry(Attempts = 0)]` plus resumability, because of the single Hangfire worker.
2. **Tag merge/rename admin action.** Near-duplicates are expected in the first weeks of an empty library.
3. Answer-side LLM tags (D7).

## Rollout

Single deploy. The migration is applied manually before the deploy. Because of the parser fallback,
deploying code and prompt together is safe: the worst case is untagged rows, never broken answers.
