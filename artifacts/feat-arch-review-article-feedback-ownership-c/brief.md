## Module
Article

## Finding
`SubmitArticleFeedbackHandler.cs:35-40`:

```csharp
var user = _currentUser.GetCurrentUser();
if (!string.Equals(article.RequestedBy, user.Name, StringComparison.Ordinal))
{
    return new SubmitArticleFeedbackResponse(ErrorCodes.Forbidden, ...);
}
```

`article.RequestedBy` is set in `GenerateArticleHandler.cs:46` from `currentUser.Name` — the display name:

```csharp
RequestedBy = currentUser.IsAuthenticated ? currentUser.Name : null,
```

`CurrentUser` (`CurrentUser.cs`) has three identity fields: `Id` (stable Entra OID), `Name` (display name), and `Email`. The `GetIdentifier()` extension (`CurrentUserExtensions.cs:12`) exists precisely to return the stable identifier: `user.Id ?? user.Email ?? "system"`.

## Why it matters
- **Name collisions**: two users with identical display names would share ownership of each other's articles; either could submit (or block) feedback on the other's work.
- **Name changes**: if a user renames their Azure AD display name, they permanently lose the ability to submit feedback on their own previously-generated articles.
- **Wrong abstraction**: `CurrentUserExtensions.GetIdentifier()` is the documented stable-ID helper for exactly this kind of ownership comparison — it is unused here.

## Suggested fix
Store the stable identifier at creation time and compare against it:

```csharp
// GenerateArticleHandler.cs:46
RequestedBy = currentUser.IsAuthenticated ? currentUser.GetIdentifier() : null,
```

```csharp
// SubmitArticleFeedbackHandler.cs:36
if (!string.Equals(article.RequestedBy, user.GetIdentifier(), StringComparison.Ordinal))
```

`Article.RequestedBy` stores whatever token was used at creation; switching to `GetIdentifier()` in both places makes them consistent. A database migration is needed if existing rows store display names (rename or backfill).

---
_Filed by daily arch-review routine on 2026-05-25._