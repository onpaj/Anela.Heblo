# Implementation: parse-and-normalize-tests (r1)

Added `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsNegativeKeywordHandlerTests.cs` (16 test cases, all passing; no production code changed).

Covered: unsupported levels, non-numeric target ids, missing Text/MatchType keys, 80-char and 10-word boundaries, whitespace-only text, whitespace collapsing in the sent text, normalized/case-insensitive Present match, different text reads Absent.

Deviation from task text: `AdEntityLevel` has no `Account` member (values: Campaign, AdGroup, Keyword, NegativeKeyword, Ad), so the unsupported-level theory uses Ad, Keyword and NegativeKeyword.
