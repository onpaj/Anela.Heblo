## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx:1024` — the first status-badge test's `importResult: 'OK'` override is redundant with the implementation no longer reading `importResult` at all; a mismatched value (e.g. `importResult: 'FAILED'`, `errorType: null`) would demonstrate the decoupling from `importResult` more directly than the current title ("regardless of importResult text") implies. Not a functional gap — verified the implementation branches solely on `errorType`.

## Notes
- Verified against the real diff (`frontend/src/components/customer/tabs/ImportTab.tsx` lines 245-267 and 494-496), not just the developer's summary. `getImportStatusIcon` now takes `errorType: string | null | undefined`, branches on `errorType == null` (loose equality catches both `null` and `undefined`), and the error branch renders `errorType || "Chyba"`. The call site passes `statement.errorType`.
- Confirmed via repo-wide grep (`frontend/src`) that `getImportStatusIcon` has exactly one definition and one call site — no other caller was missed.
- Confirmed via grep that the literal `"OK"` no longer appears in any comparison/logic in `ImportTab.tsx` — its only remaining occurrence is inside the explanatory code comment on line 249, which documents the change rather than performing a check. Satisfies spec FR-1's acceptance criterion.
- Confirmed the empty-string edge case is handled correctly: `errorType == null` is `false` for `""`, so an empty-but-defined `errorType` still falls into the error branch, and `"" || "Chyba"` yields `"Chyba"`. This is the trickiest part of spec FR-1 (loose-equality vs. truthiness) and both the implementation and its test (`falls back to "Chyba" when errorType is set but empty`) get it right.
- Checked the actual generated DTO type (`frontend/src/api/generated/api-client.ts:17769`): `BankStatementImportDto.errorType` is `string | undefined` (no `null` in the generated type). The function parameter's wider type (`string | null | undefined`) is a safe superset and compiles cleanly; `== null` still correctly treats `undefined` as success. Matches NFR-1.
- No backend files are touched in this diff (`BankStatementImportDto.cs` / `ImportStatus` unmodified) — satisfies NFR-2.
- FR-3 (pixel-identical behavior for existing "OK" data): for a statement with `importResult === "OK"`, the backend's `ErrorType` getter produces `null`, so `errorType == null` is true and the same green "Úspěch" badge renders as before. For a non-"OK" `importResult`, `ErrorType` equals `ImportResult`, so the red badge renders with the same text as before (previously `importResult || "Chyba"`, now `errorType || "Chyba"`, and `errorType === importResult` whenever both are non-null per the DTO's definition). Behavior is preserved.
- Three new tests (success / error-with-message / empty-string-fallback) directly cover the branching logic; the pre-existing `describe('ImportTab filters', ...)` block is untouched in the diff.
