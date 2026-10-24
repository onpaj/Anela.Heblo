## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs:318` — The `requests` list is passed to several tests that never assert on it (case-sensitivity test); harmless, could be dropped.

All six new tests cover FR-1..FR-5 of spec.r1.md; ShoptetOrderClientTests: 14/14 pass.
