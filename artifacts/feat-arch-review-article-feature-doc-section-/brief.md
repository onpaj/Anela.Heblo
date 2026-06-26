## Module
Article

## Finding

There is a three-way inconsistency between the feature spec, the `AuthorizationConstants` XML comments, and the actual implementation for which role grants access to `POST /api/Articles/generate`.

**Feature doc (`docs/features/article-generation.md`, line ~367, section 14):**
```
Uses the unified `marketing_writer` role — assigned to Heblo human users with content-creation responsibilities (admin assigns)
[Authorize(Roles = "marketing_writer")] on POST /generate
```

**Actual controller (`backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:29`):**
```csharp
[Authorize(Policy = AuthorizationConstants.Policies.MarketingReader)]
public async Task<ActionResult<GenerateArticleResponse>> Generate(...)
```
`AuthorizationConstants.Policies.MarketingReader` resolves to the `marketing_reader` role
(confirmed in `AuthenticationExtensions.cs:116–118`).

**`AuthorizationConstants` (`backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs`):**
```csharp
// Line 41 — MarketingReader role
/// <summary>Role required for generating leaflets and articles (GenAI features)</summary>
public const string MarketingReader = "marketing_reader";

// Line 47 — MarketingWriter role
/// <summary>Role for tagging photos in the photobank</summary>
public const string MarketingWriter = "marketing_writer";
```

The code and the `MarketingReader` constant comment agree that `marketing_reader` is the correct role for article generation. The `MarketingWriter` constant's comment ("tagging photos in the photobank") is internally consistent with the code as well. The **feature doc section 14 is the odd one out** — it says `marketing_writer` but the implementation always used `marketing_reader`.

## Why it matters

- A developer reading the feature spec and then writing a new endpoint (or setting up Azure AD role assignments) would grant `marketing_writer` instead of `marketing_reader`, leaving users unable to trigger article generation.
- The spec's closing note ("The earlier `article_generator` role was removed and replaced by the unified `marketing_writer` role") further reinforces the wrong role name and makes the spec actively misleading.
- The doc is the authoritative reference for this module; if it contradicts the code on an auth decision, the discrepancy will surface at the worst possible time (production access issue).

## Suggested fix

Minimal: correct the feature doc. Update `docs/features/article-generation.md`, section 14, to match the implementation:

```diff
-Uses the unified `marketing_writer` role — assigned to Heblo human users with content-creation responsibilities (admin assigns)
-`[Authorize(Roles = "marketing_writer")]` on `POST /generate`
+Uses the `marketing_reader` role — assigned to Heblo users with GenAI content-generation responsibilities (admin assigns)
+`[Authorize(Policy = AuthorizationConstants.Policies.MarketingReader)]` on `POST /generate`
```

Also update the closing note in section 14:
```diff
-**Note:** The earlier `article_generator` role was removed and replaced by the unified `marketing_writer` role.
+**Note:** The earlier `article_generator` role was removed. Access is granted via the `marketing_reader` role (`AuthorizationConstants.Roles.MarketingReader`).
```

No code change is needed — the implementation is correct.

---
_Filed by daily arch-review routine on 2026-05-29._