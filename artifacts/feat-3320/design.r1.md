## Design

Add one JSX block after the `setCanPack.isError` block (line 200):

```tsx
{setActive.isError && (
  <p className="text-sm text-red-600">Failed to update user status. Please try again.</p>
)}
```

Matches existing style exactly.
