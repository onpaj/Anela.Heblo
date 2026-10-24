## Module / File
`backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/GoogleAdsNegativeKeywordHandler.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
The handler is the sole path for adding and removing negative keywords from live Google Ads campaigns/ad groups. No execution path is covered:

1. **`TryParse` validation** — rejects wrong target level, non-numeric target ID, blank/oversized keyword text, and unknown match type. These branches guard every Execute/Revert/Read call; a silent failure here sends malformed mutations to the Google Ads API.
2. **Keyword normalization** (`Normalize`) — collapses whitespace and trims. The deduplication check in `ReadCurrentAsync` uses `string.Equals(Normalize(...), Text, OrdinalIgnoreCase)`; if normalization is wrong, duplicate negative keywords can be added.
3. **`ExecuteAsync` 200-but-no-criterion guard** — if Google returns 200 but omits the criterion resource name, the response is turned into a failure with a warning message. If this guard is missing, the keyword is added to the campaign but can never be reverted through Heblo.
4. **`RevertAsync` resource-name prefix check** — validates that `PlatformResourceId` belongs to the same campaign/ad group in the same account; rejects mismatched resource names before calling the API. A regression here would either prevent valid reverts or permit reverts of foreign criteria.
5. **Campaign vs. AdGroup routing** — separate mutations and queries for each level; untested branching.

## Why it matters
These are mutations to live production Google Ads accounts. A normalization bug can result in duplicate negative keywords silently accepted by the API. A broken revert guard can make an executed proposal permanently un-reversible. The 200-but-no-criterion guard prevents Heblo from losing track of added keywords; if it regresses, the ad platform diverges from Heblo's proposal audit log.

## Suggested approach
Unit-test with a mock `IGoogleAdsApiClient`:
- `TryParse` rejection cases: wrong level, non-numeric ID, empty text, text > 80 chars, > 10 words, unknown match type.
- `ExecuteAsync` happy path at Campaign level, AdGroup level; verify correct mutation chosen.
- `ExecuteAsync` when API returns 200 with no `resourceName` — expect failure result with warning.
- `RevertAsync` with resource name from a different account — expect failure, no API call.
- `ReadCurrentAsync` keyword match: same text different case → matches; different text → absent.

Effort: ~2–3 h.

---
_Filed by weekly coverage-gap routine on 2026-10-19. Based on CI run #38726815029 (263a263200d3e8a3df47581d57e8bd8c148a7336)._
