## Module / File
`backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphFolderResolver.cs`

## Coverage
Line coverage: 37.7% (filter threshold: 60%)

## What's not tested
`ResolveOrCreateAsync` uses `lastSlash <= 0` to decide whether to fetch the root ID — `lastSlash == 0` (leading slash) and `lastSlash < 0` (flat name) receive identical treatment, which may create a subfolder in the drive root incorrectly. Non-404 HTTP errors call `EnsureSuccessStatusCode()` before the recursive branch, so a 403 or 409 would throw without context. The 60-minute cache for both root ID and folder IDs is never exercised in tests.

## Why it matters
Incorrect parent-folder resolution silently creates documents in the wrong OneDrive path; data is not lost but is effectively unreachable by the RAG retrieval path, degrading knowledge-base quality silently.

## Suggested approach
Unit tests with a mocked `HttpClient`: flat path (no slash) → expect root parent; deep path `a/b/c` → recursive resolution with expected Graph API calls; 404 on GET + 200 on POST create; 500 on GET → assert wrapped exception. ~S effort.

---
_Filed by weekly coverage-gap routine on 2026-10-26. Based on CI run #39780303564 (cad5f9218f4b3982c6605f346e1549562e175d67)._
