## Module
OrgChart

## Finding
`backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs` is an HTTP client adapter — it calls an external URL, receives raw JSON, and deserializes it. It contains zero domain or business logic. Yet it lives in `Services/`, a folder that `docs/architecture/filesystem.md` defines as holding **"Domain services and business logic"**.

The same filesystem doc defines `Features/{Feature}/Infrastructure/` as the home for **"Feature infrastructure"**, which is exactly what an external-API adapter is.

## Why it matters
Misplacing infrastructure code in the `Services/` layer blurs the distinction between domain logic and I/O concerns. A future developer looking for "where HTTP calls happen" will not find it in `Infrastructure/`, and a developer scanning `Services/` for business rules will wade through HTTP plumbing. The naming mismatch also makes it harder to enforce Clean Architecture boundaries via static analysis.

## Suggested fix
Move the file (and nothing else — no logic changes needed):

```
Before: Application/Features/OrgChart/Services/OrgChartService.cs
After:  Application/Features/OrgChart/Infrastructure/OrgChartService.cs
```

`IOrgChartService` stays in `Services/` (it is the domain-facing contract); only the concrete implementation moves to `Infrastructure/`. Update the namespace accordingly.

---
_Filed by daily arch-review routine on 2026-05-19._