## Module
Analytics

## Finding
`TopProductDto` exposes two computed read-only properties that are pure backward-compatibility shims, explicitly annotated as such:

```csharp
// Application/Features/Analytics/Contracts/TopProductDto.cs, lines 26-27
// Keep for backward compatibility
public string ProductCode => GroupKey;
public string ProductName => DisplayName;
```

File: `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/TopProductDto.cs`, lines 25–27.

## Why it matters
CLAUDE.md explicitly prohibits this pattern: *"Avoid backwards-compatibility hacks like renaming unused _vars, re-exporting types, adding // removed comments for removed code, etc. If you are certain that something is unused, you can delete it completely."* These shims add noise to the OpenAPI schema (they appear as extra read-only properties on the generated TypeScript client) and keep two names alive for the same data, causing confusion about which property callers should use.

## Suggested fix
1. Search for all call-sites that access `.ProductCode` or `.ProductName` on a `TopProductDto` (TypeScript frontend, tests):
   ```
   grep -r "\.ProductCode\|\.ProductName" frontend/src backend/test
   ```
2. Update them to use `.GroupKey` / `.DisplayName`.
3. Remove the two shim properties from `TopProductDto`.

---
_Filed by daily arch-review routine on 2026-06-07._