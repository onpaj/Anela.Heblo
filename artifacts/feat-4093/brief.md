## Module / File
`backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/BlobPathValidator.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)
No test file exists for this class.

## What's not tested
`BlobPathValidator.IsValid` applies four sequential guards, none currently exercised:
1. **Null/empty** — returns `false` for whitespace or empty string.
2. **Path traversal** — returns `false` when the path contains `".."`.
3. **Regex mismatch** — returns `false` when the path does not match `^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$`.
4. **Invalid date segment** — returns `false` when the date-shaped prefix (e.g. `2026-13-01`) fails `DateOnly.TryParseExact`.

The `.Contains("..")` check is the security-critical branch. If a caller passes a path like `2026-01-01/../../secrets.pdf`, the traversal check blocks it — but only if this path is exercised in tests so a refactor cannot silently remove it.

## Why it matters
`BlobPathValidator.IsValid` is the sole guard between a user-supplied blob path and the Azure Blob Storage download call in `DownloadExpeditionListHandler`. If the traversal check regresses, an attacker with `ExpeditionListArchive.read` permission could request arbitrary blobs by embedding `..` segments. A regression in the date-parse check would allow serving blobs from paths that look structurally valid but are not in the expected date-folder layout.

## Suggested approach
Pure unit tests on the static method — no infrastructure:
- `null`, `""`, `"   "` → `false`.
- `"2026-01-01/../../admin.pdf"` → `false` (traversal blocked).
- `"2026-01-01/report.pdf"` → `true` (happy path).
- `"2026-13-01/report.pdf"` → `false` (month 13 fails date parse).
- `"notadate/report.pdf"` → `false` (regex rejects non-date prefix).
- `"2026-01-01/subdir/report.pdf"` → `false` (extra slash rejected by regex).
- `"2026-01-01/report.xlsx"` → `false` (wrong extension).

Effort: very small — no mocks needed, pure in-process logic.

---
_Filed by weekly coverage-gap routine on 2026-09-07. Based on CI run #33791274852 (a21f134808e61a338c3261f8523316d2752ebea3)._