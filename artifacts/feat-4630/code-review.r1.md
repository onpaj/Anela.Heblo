## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/components/common/__tests__/ErrorBoundary.test.tsx:82` — The "Neznámá chyba" test passes an empty message but the error still has a stack, so it only verifies the message fallback branch (as intended by the spec); acceptable.
