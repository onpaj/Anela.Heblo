## Module
ExpeditionListArchive

## Finding
`ReprintExpeditionListHandler.cs:29` constructs a temp path with:

```csharp
var tempFile = Path.GetTempFileName() + ".pdf";
```

`Path.GetTempFileName()` immediately creates a zero-byte file on disk and returns its path (e.g. `tmp1A2B.tmp`). Appending `".pdf"` produces a different string (`tmp1A2B.tmp.pdf`) — a path that does not yet exist. The handler then writes the blob into the `.pdf` path and deletes it in `DeleteTempFile`, but the original `.tmp` file created by `GetTempFileName()` is never touched again and accumulates on the server with each reprint call.

## Why it matters
Every successful or failed reprint leaks one temp file. On a busy server this fills `/tmp` silently. The `DeleteTempFile` helper already exists and the intent to clean up is clear, so the leak is purely due to the wrong API usage.

## Suggested fix
Replace the two-step approach with a single random path that is never pre-created:

```csharp
var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
```

This avoids creating an extra `.tmp` file and keeps the `.pdf` extension the CUPS sink may rely on.

---
_Filed by daily arch-review routine on 2026-05-26._