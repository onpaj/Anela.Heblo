# Box Detail — Product Thumbnails

**Date:** 2026-05-16
**Status:** Approved

## Summary

Show a 48×48 product thumbnail inline next to the product name in the transport box items table. The backend already sends `imageUrl` per item; this is a frontend-only change.

## Affected File

`frontend/src/components/transport/box-detail/TransportBoxItems.tsx`

## Design

### Name cell layout

The existing name `<td>` becomes a flex row:

```
[ image | name (truncated)      ]
[       | Lot: …  Exp: …        ]
```

- Image / placeholder: `w-12 h-12` (48×48 px), `flex-shrink-0`, `rounded`, `object-cover`
- Text block: `min-w-0 flex-1` so truncation continues to work

### Image rendering rules

| Condition | Rendered element |
|-----------|-----------------|
| `item.imageUrl` is set and loads successfully | `<img src={item.imageUrl} … />` |
| `item.imageUrl` is set but fails to load (`onError`) | grey placeholder `<div>` |
| `item.imageUrl` is null/undefined | grey placeholder `<div>` directly |

Grey placeholder: `w-12 h-12 bg-gray-200 rounded flex-shrink-0`

### Row height

Rows grow from ~40 px to ~56 px to accommodate the image. This is acceptable.

### No backend changes

`TransportBoxItemDto.ImageUrl` is already populated by `GetTransportBoxByCodeHandler` (from `catalogItem.Image`). The TypeScript API client already exposes the field.

## Out of scope

- Extracting a reusable `ProductThumbnail` component (no other consumer yet)
- Showing images in the add-item panels (manufactured / catalog tabs)
- Backend changes
