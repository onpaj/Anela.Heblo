## Module
Users (frontend)

## Finding
`frontend/src/pages/UserDetailPage.tsx` does not handle the error state from the `useUsers()` query, nor does it show anything meaningful when the URL param ID doesn't match any user.

The control flow is:
- Line 117: `const isLoading = usersQuery.isLoading;`
- Line 120–126: renders a loading spinner while `isLoading` is true
- Line 128: `if (!draft || !user) return null;`

When `usersQuery.isError` is true (network failure, 500, etc.), `isLoading` becomes false and `usersQuery.data` is undefined, so `user` is `undefined` and `draft` is `null`. The component silently returns `null` — the user sees an empty page with no message.

The same silent `null` also triggers if someone navigates directly to `/admin/access/users/` with a valid API response but an ID that doesn't exist in the user list.

Compare: `UsersGrid.tsx` correctly checks `users.isError` (line 100–102) and renders an `<ErrorAlert>` component.

## Why it matters
A user whose session has an API error while on the detail page will see a blank page with no indication of what went wrong and no path to recover. The inconsistency with `UsersGrid` (which does handle errors) means the pattern exists in the codebase and was just missed here. Violates the "missing error/loading states" criterion for frontend code.

## Suggested fix
Add an explicit `usersQuery.isError` guard before the `!draft || !user` check:

```tsx
if (usersQuery.isError) {
  return <ErrorAlert />;
}

if (!isLoading && !user) {
  return (
    <Container>
      <p>User not found.</p>
    </Container>
  );
}
```

This mirrors the existing pattern in `UsersGrid.tsx:96–102`.

---
_Filed by daily arch-review routine on 2026-06-23._
