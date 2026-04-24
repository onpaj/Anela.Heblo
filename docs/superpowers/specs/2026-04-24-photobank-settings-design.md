# Photobank Admin Settings Page — Design Spec

**Issue:** #760  
**Epic:** #755 Photobank  
**Branch:** feat/755-photobank  
**Date:** 2026-04-24

---

## Overview

Admin-only settings page at `/marketing/photobank/settings` for managing SharePoint index roots and tag rules. Two tabs: **Index Roots** and **Tag Rules**.

---

## Scope

This spec covers only issue #760. Backend handlers and endpoints already exist. The work is:
1. Fix two backend DTOs to expose fields the domain entity already stores.
2. Build the frontend settings page, hooks, and tab components.

---

## Backend Changes

### Problem

`PhotobankIndexJob` requires `DriveId != null && RootItemId != null` to process a root, but the current `AddRootRequest` doesn't accept those fields. Users adding a root via the API today would create an inert entry the job silently skips.

### Changes

#### `IndexRootDto`
Add fields that the domain entity already has:

```csharp
public int Id { get; set; }
public string SharePointPath { get; set; } = null!;
public string? DisplayName { get; set; }
public string? DriveId { get; set; }
public string? RootItemId { get; set; }
public bool IsActive { get; set; }
public DateTime CreatedAt { get; set; }
public DateTime? LastIndexedAt { get; set; }
```

#### `AddRootRequest`
Add required fields for the indexer:

```csharp
public string SharePointPath { get; set; } = null!;   // folderPath
public string? DisplayName { get; set; }
public string DriveId { get; set; } = null!;           // required
public string RootItemId { get; set; } = null!;        // required
```

#### `GetRootsHandler`
Map new fields in the projection:
```csharp
DriveId = r.DriveId,
RootItemId = r.RootItemId,
LastIndexedAt = r.LastIndexedAt,
```

#### `AddRootHandler`
Set new fields when creating the entity:
```csharp
DriveId = request.DriveId.Trim(),
RootItemId = request.RootItemId.Trim(),
```

### Backend Tests

- `GetRootsHandlerTests` — verify `DriveId`, `RootItemId`, `LastIndexedAt` are mapped from entity to DTO
- `AddRootHandlerTests` — verify `DriveId` and `RootItemId` are stored on the created entity

No new handlers, no new migrations.

---

## Frontend Architecture

### File Structure

```
frontend/src/
├── api/hooks/
│   ├── usePhotobank.ts                      (existing)
│   └── usePhotobankSettings.ts              ← new
└── components/marketing/photobank/
    ├── pages/
    │   ├── PhotobankPage.tsx                (modify: add settings gear link for admins)
    │   └── PhotobankSettingsPage.tsx        ← new
    ├── settings/
    │   ├── IndexRootsTab.tsx                ← new
    │   └── TagRulesTab.tsx                  ← new
    └── __tests__/
        ├── (existing tests)
        ├── usePhotobankSettings.test.ts     ← new
        ├── IndexRootsTab.test.tsx           ← new
        └── TagRulesTab.test.tsx             ← new
```

### Routing (App.tsx)

Add alongside the existing photobank route:
```tsx
<Route path="/marketing/photobank/settings" element={<PhotobankSettingsPage />} />
```

---

## `usePhotobankSettings.ts`

Follows the same pattern as `usePhotobank.ts`: `getClientAndBaseUrl()` + `apiFetch`/`apiPost`/`apiDelete` helpers + React Query.

### Types

```typescript
export interface IndexRootDto {
  id: number;
  sharePointPath: string;
  displayName: string | null;
  driveId: string | null;
  rootItemId: string | null;
  isActive: boolean;
  createdAt: string;
  lastIndexedAt: string | null;
}

export interface TagRuleDto {
  id: number;
  pathPattern: string;
  tagName: string;
  isActive: boolean;
  sortOrder: number;
}

export interface ReapplyRulesResult {
  photosUpdated: number;
}
```

### Hooks

| Hook | Method | Endpoint |
|------|--------|----------|
| `useIndexRoots()` | GET | `/api/photobank/settings/roots` |
| `useAddIndexRoot()` | POST | `/api/photobank/settings/roots` |
| `useDeleteIndexRoot()` | DELETE | `/api/photobank/settings/roots/{id}` |
| `useTagRules()` | GET | `/api/photobank/settings/rules` |
| `useAddTagRule()` | POST | `/api/photobank/settings/rules` |
| `useDeleteTagRule()` | DELETE | `/api/photobank/settings/rules/{id}` |
| `useReapplyTagRules()` | POST | `/api/photobank/settings/rules/reapply` |

Each mutation invalidates its respective query on success via `queryClient.invalidateQueries`.

---

## `PhotobankSettingsPage.tsx`

### Admin guard

Reads `isAdmin` from MSAL accounts using the existing pattern:
```typescript
const ADMIN_ROLE = "administrator";
const isAdmin = (accounts[0]?.idTokenClaims as any)?.roles?.includes(ADMIN_ROLE) ?? false;
```

If `isAdmin` is false, renders a centered "403 – Přístup odepřen" message and nothing else. Does not redirect.

### Tab state

Local `useState<'roots' | 'rules'>('roots')` — no URL-based tab routing needed.

### Layout

```
Page header: "Nastavení fotobanky" + breadcrumb back to /marketing/photobank
Tab bar: [Index Roots] [Tag Rules]
Tab content: <IndexRootsTab /> or <TagRulesTab />
```

---

## `IndexRootsTab.tsx`

### Roots list

Table with columns:
- **Cesta** (`sharePointPath`)
- **Název** (`displayName || —`)
- **Drive ID** (`driveId || —`)
- **Root Item ID** (`rootItemId || —`)
- **Aktivní** (checkbox/badge, read-only)
- **Poslední indexace** (`lastIndexedAt` formatted as local date, or `Nikdy`)
- **Smazat** (trash icon button → `useDeleteIndexRoot`)

Empty state: "Žádné kořeny nejsou nakonfigurovány."

### Add form

Below the table, a simple form:
- `folderPath` — text input, required (maps to `sharePointPath`)
- `displayName` — text input, optional
- `driveId` — text input, required
- `rootItemId` — text input, required
- Submit button: "Přidat kořen" — disabled while mutation is pending

On success: reset form fields, list auto-refreshes via query invalidation.

---

## `TagRulesTab.tsx`

### Rules list

Table ordered by `sortOrder` ASC, columns:
- **Vzor cesty** (`pathPattern`)
- **Štítek** (`tagName`)
- **Pořadí** (`sortOrder`)
- **Aktivní** (badge, read-only)
- **Smazat** (trash icon button → `useDeleteTagRule`)

Empty state: "Žádná pravidla nejsou nakonfigurována."

### Add form

- `pathPattern` — text input, required (e.g. `/Fotky/Produkty/*`)
- `tagName` — text input, required
- `sortOrder` — number input, default `0`
- Submit button: "Přidat pravidlo"

### Re-apply button

"Re-aplikovat pravidla" button with:
- Spinner icon while `useReapplyTagRules` is pending
- On success: show `"Pravidla aplikována na N fotek"` inline success message for 5 seconds
- On error: show `"Chyba při aplikaci pravidel"` error message

---

## Settings link in `PhotobankPage`

Add a gear icon link in the header area, visible only when `isAdmin === true`:
```tsx
{isAdmin && (
  <Link to="/marketing/photobank/settings" aria-label="Nastavení fotobanky">
    <Settings className="w-4 h-4" />
  </Link>
)}
```

---

## Frontend Tests

### `usePhotobankSettings.test.ts`

Mock `getAuthenticatedApiClient` with `mockReturnValue` (synchronous). Test:
- `useIndexRoots` calls correct URL and returns parsed roots
- `useTagRules` calls correct URL and returns parsed rules
- `useAddIndexRoot` POSTs to correct URL, invalidates `useIndexRoots`
- `useDeleteIndexRoot` DELETEs correct URL, invalidates `useIndexRoots`
- `useAddTagRule` POSTs to correct URL, invalidates `useTagRules`
- `useDeleteTagRule` DELETEs correct URL, invalidates `useTagRules`
- `useReapplyTagRules` POSTs to reapply URL, returns `{ photosUpdated: N }`

### `IndexRootsTab.test.tsx`

- Renders roots list with all columns
- "Žádné kořeny" shown when list is empty
- Delete button calls `useDeleteIndexRoot` with correct id
- Add form: submit with valid data calls `useAddIndexRoot`
- Add form: submit blocked when required fields empty

### `TagRulesTab.test.tsx`

- Renders rules list ordered by sortOrder
- "Žádná pravidla" shown when list is empty
- Delete button calls `useDeleteTagRule` with correct id
- Add form: submit calls `useAddTagRule`
- Reapply button: shows spinner during pending, shows count message on success

---

## Acceptance Criteria Mapping

| Criterion | Implementation |
|-----------|---------------|
| Index roots CRUD works | `useIndexRoots` + `useAddIndexRoot` + `useDeleteIndexRoot` + `IndexRootsTab` |
| Tag rules CRUD works | `useTagRules` + `useAddTagRule` + `useDeleteTagRule` + `TagRulesTab` |
| Re-apply shows updated count | `useReapplyTagRules` result → inline success message in `TagRulesTab` |
| Page not accessible to non-admins | `isAdmin` guard in `PhotobankSettingsPage` shows 403 message |
| `npm run build` succeeds | Verified as final gate before commit |
