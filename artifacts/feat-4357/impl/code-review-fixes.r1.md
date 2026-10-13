# Code review fix r1

Fixed both Blocking findings in `DownloadFromUrlHandler.cs`:
- Entry log now computes `redactedUrl` first and logs `{RedactedUrl}`.
- Cancellation log now logs `{RedactedUrl}` with `redactedUrl`.

`dotnet build` of Application project: 0 errors. No raw `request.FileUrl` is logged any more.
Advisory (handler-level log test) not addressed; non-blocking.
