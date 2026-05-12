# Photobank Modifier-Click Multi-Select Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the per-photo checkbox UI with modifier-key + click selection (Cmd/Ctrl = toggle, Shift = range replace) on photo tiles and list rows, showing selection via a highlighted frame instead of a checkbox.

**Architecture:** Selection state lives in `PhotobankPage.tsx` (`selectedIds: Set<number>`, `selectionAnchorId`). The child components `PhotoGrid` and `PhotoList` intercept modifier keys in their tile/row `onClick` handlers and call a renamed `onPhotoSelection(id, mode)` prop. The existing `PhotobankBulkActionBar` is unaffected — it already reads `selectedIds.size`.

**Tech Stack:** React 18, TypeScript, Tailwind CSS, Jest + React Testing Library

---

## File Map

| File | Change |
|------|--------|
| `frontend/src/components/marketing/photobank/PhotoGrid.tsx` | Remove checkbox block, add modifier-click logic, update prop type + styling |
| `frontend/src/components/marketing/photobank/PhotoList.tsx` | Same as PhotoGrid for list rows |
| `frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx` | Rename prop in `sharedPhotoProps`, switch handler to `mode`-based replace semantics |
| `frontend/src/components/marketing/photobank/__tests__/PhotoGrid.test.tsx` | Replace checkbox tests with modifier-click tests; update aria-pressed tests |
| `frontend/src/components/marketing/photobank/__tests__/PhotoList.test.tsx` | Mirror PhotoGrid test changes for list rows |

---

## Task 1: Update PhotoGrid — remove checkbox, add modifier-click, update styling

**Files:**
- Modify: `frontend/src/components/marketing/photobank/PhotoGrid.tsx`

### Background

Current `PhotoGridProps` has:
```ts
onTogglePhotoSelection: (photoId: number, withRange: boolean) => void;
```
Current grid renders a `<input type="checkbox">` inside `{canSelect && (...)}` (lines 83–106). The `<button>` onClick only calls `onPhotoSelect(photo)`.

After this task:
- Prop renamed to `onPhotoSelection: (photoId: number, mode: "toggle" | "range") => void`
- Checkbox block removed entirely
- Button onClick intercepts `shiftKey` / `metaKey` / `ctrlKey`
- `aria-pressed` reflects `isChecked` (multi-select state), not `isSelected` (drawer state)
- Selected tile gets strong ring; drawer-open tile gets subtle border; `select-none` on grid wrapper

- [ ] **Step 1: Replace the entire file**

Replace `frontend/src/components/marketing/photobank/PhotoGrid.tsx` with:

```tsx
import React from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import PhotoThumbnail from "./PhotoThumbnail";
import { TagBadge } from "../../ui/TagBadge";
import type { PhotoDto } from "../../../api/hooks/usePhotobank";

const TILE_MAX_VISIBLE_TAGS = 3;

interface PhotoGridProps {
  photos: PhotoDto[];
  selectedPhotoId: number | null;
  total: number;
  page: number;
  pageSize: number;
  isLoading: boolean;
  showTags?: boolean;
  onPhotoSelect: (photo: PhotoDto) => void;
  onPageChange: (page: number) => void;
  selectedIds: Set<number>;
  onPhotoSelection: (photoId: number, mode: "toggle" | "range") => void;
  canSelect: boolean;
}

const PhotoGrid: React.FC<PhotoGridProps> = ({
  photos,
  selectedPhotoId,
  total,
  page,
  pageSize,
  isLoading,
  showTags = false,
  onPhotoSelect,
  onPageChange,
  selectedIds,
  onPhotoSelection,
  canSelect,
}) => {
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const canGoPrev = page > 1;
  const canGoNext = page < totalPages;

  if (isLoading) {
    return (
      <div className="flex-1 p-4">
        <div className="grid grid-cols-[repeat(auto-fill,minmax(160px,1fr))] gap-3">
          {Array.from({ length: 12 }).map((_, i) => (
            <div
              key={i}
              className="aspect-square animate-pulse bg-gray-200 rounded-lg"
            />
          ))}
        </div>
      </div>
    );
  }

  if (photos.length === 0) {
    return (
      <div className="flex-1 flex items-center justify-center text-gray-400">
        <p className="text-sm">Žádné fotografie nenalezeny</p>
      </div>
    );
  }

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Photo grid */}
      <div className="flex-1 overflow-y-auto p-4">
        <div className="grid grid-cols-[repeat(auto-fill,minmax(160px,1fr))] gap-3 select-none">
          {photos.map((photo) => {
            const isSelected = photo.id === selectedPhotoId;
            const isChecked = selectedIds.has(photo.id);
            return (
              <div
                key={photo.id}
                className={[
                  "group relative aspect-square rounded-lg overflow-hidden border-2 transition-all",
                  isChecked
                    ? "border-primary-blue ring-2 ring-primary-blue ring-offset-1"
                    : isSelected
                      ? "border-primary-blue/60"
                      : "border-transparent hover:border-gray-300",
                ].join(" ")}
              >
                <button
                  data-testid={`photo-tile-${photo.id}`}
                  onClick={(e) => {
                    if (!canSelect) {
                      onPhotoSelect(photo);
                      return;
                    }
                    if (e.shiftKey) {
                      e.preventDefault();
                      onPhotoSelection(photo.id, "range");
                      return;
                    }
                    if (e.metaKey || e.ctrlKey) {
                      e.preventDefault();
                      onPhotoSelection(photo.id, "toggle");
                      return;
                    }
                    onPhotoSelect(photo);
                  }}
                  className="w-full h-full focus:outline-none focus:ring-2 focus:ring-primary-blue focus:ring-offset-2"
                  aria-pressed={isChecked}
                  aria-label={photo.name}
                >
                  <PhotoThumbnail
                    photoId={photo.id}
                    modifiedAt={photo.lastModifiedAt}
                    alt={photo.name}
                    className="w-full h-full"
                    size="medium"
                  />
                  {showTags && photo.tags.length > 0 && (
                    <div className="absolute top-1 left-1 right-1 flex flex-wrap gap-1 pointer-events-none z-10">
                      {photo.tags.slice(0, TILE_MAX_VISIBLE_TAGS).map((tag) => (
                        <TagBadge key={tag.id} name={tag.name} variant="overlay" />
                      ))}
                      {photo.tags.length > TILE_MAX_VISIBLE_TAGS && (
                        <span className="inline-flex items-center px-1.5 py-0.5 bg-black/50 text-white rounded-full text-xs">
                          +{photo.tags.length - TILE_MAX_VISIBLE_TAGS}
                        </span>
                      )}
                    </div>
                  )}
                  <div className="absolute bottom-0 left-0 right-0 bg-gradient-to-t from-black/60 to-transparent p-1.5 opacity-0 hover:opacity-100 transition-opacity">
                    <p className="text-white text-xs truncate">{photo.name}</p>
                  </div>
                </button>
              </div>
            );
          })}
        </div>
      </div>

      {/* Pagination */}
      <div className="flex-shrink-0 border-t border-gray-200 px-4 py-2 flex items-center justify-between bg-white">
        <span className="text-xs text-gray-500">
          {total} {total === 1 ? "fotografie" : total < 5 ? "fotografie" : "fotografií"}
        </span>
        <div className="flex items-center gap-2">
          <button
            onClick={() => onPageChange(page - 1)}
            disabled={!canGoPrev}
            className="p-1 rounded text-gray-500 hover:bg-gray-100 disabled:opacity-40 disabled:cursor-not-allowed"
            aria-label="Předchozí stránka"
          >
            <ChevronLeft className="w-4 h-4" />
          </button>
          <span className="text-xs text-gray-600">
            Stránka {page} z {totalPages}
          </span>
          <button
            onClick={() => onPageChange(page + 1)}
            disabled={!canGoNext}
            className="p-1 rounded text-gray-500 hover:bg-gray-100 disabled:opacity-40 disabled:cursor-not-allowed"
            aria-label="Další stránka"
          >
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
};

export default PhotoGrid;
```

- [ ] **Step 2: Verify TypeScript compiles (expect error in PhotobankPage — will fix in Task 3)**

```bash
cd frontend && npx tsc --noEmit 2>&1 | grep photobank | head -20
```

Expected: Error about `onTogglePhotoSelection` not existing / `onPhotoSelection` missing in `PhotobankPage.tsx`. That is expected at this stage.

---

## Task 2: Update PhotoList — same changes as grid

**Files:**
- Modify: `frontend/src/components/marketing/photobank/PhotoList.tsx`

- [ ] **Step 1: Replace the entire file**

Replace `frontend/src/components/marketing/photobank/PhotoList.tsx` with:

```tsx
import React from "react";
import { ChevronLeft, ChevronRight, ExternalLink } from "lucide-react";
import PhotoThumbnail from "./PhotoThumbnail";
import { TagBadge } from "../../../components/ui/TagBadge";
import type { PhotoDto } from "../../../api/hooks/usePhotobank";

const MAX_VISIBLE_TAGS = 5;

function formatFileSize(bytes: number | null): string {
  if (bytes == null) return "—";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

interface PhotoListProps {
  photos: PhotoDto[];
  selectedPhotoId: number | null;
  total: number;
  page: number;
  pageSize: number;
  isLoading: boolean;
  onPhotoSelect: (photo: PhotoDto) => void;
  onPageChange: (page: number) => void;
  selectedIds: Set<number>;
  onPhotoSelection: (photoId: number, mode: "toggle" | "range") => void;
  canSelect: boolean;
}

function PhotoList({
  photos,
  selectedPhotoId,
  total,
  page,
  pageSize,
  isLoading,
  onPhotoSelect,
  onPageChange,
  selectedIds,
  onPhotoSelection,
  canSelect,
}: PhotoListProps) {
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const canGoPrev = page > 1;
  const canGoNext = page < totalPages;

  if (isLoading) {
    return (
      <div className="flex-1 p-4 space-y-3" data-testid="loading-skeleton">
        {Array.from({ length: 6 }).map((_, i) => (
          <div key={i} className="flex gap-3 animate-pulse">
            <div className="w-20 h-20 bg-gray-200 rounded-lg flex-shrink-0" />
            <div className="flex-1 space-y-2 py-1">
              <div className="h-4 bg-gray-200 rounded w-3/4" />
              <div className="h-3 bg-gray-200 rounded w-1/2" />
              <div className="h-3 bg-gray-200 rounded w-1/4" />
            </div>
          </div>
        ))}
      </div>
    );
  }

  if (photos.length === 0) {
    return (
      <div className="flex-1 flex items-center justify-center text-gray-400">
        <p className="text-sm">Žádné fotografie nenalezeny</p>
      </div>
    );
  }

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="flex-1 overflow-y-auto divide-y divide-gray-200">
        {photos.map((photo) => {
          const isSelected = photo.id === selectedPhotoId;
          const isChecked = selectedIds.has(photo.id);
          const visibleTags = photo.tags.slice(0, MAX_VISIBLE_TAGS);
          const overflowCount = photo.tags.length - MAX_VISIBLE_TAGS;

          return (
            <div
              key={photo.id}
              className={[
                "w-full flex items-center gap-3 px-4 py-3 select-none",
                isChecked
                  ? "border-l-4 border-primary-blue bg-secondary-blue-pale ring-1 ring-inset ring-primary-blue/30"
                  : isSelected
                    ? "border-l-2 border-primary-blue bg-secondary-blue-pale"
                    : "hover:bg-gray-50",
              ].join(" ")}
            >
              <button
                data-testid={`photo-row-${photo.id}`}
                onClick={(e) => {
                  if (!canSelect) {
                    onPhotoSelect(photo);
                    return;
                  }
                  if (e.shiftKey) {
                    e.preventDefault();
                    onPhotoSelection(photo.id, "range");
                    return;
                  }
                  if (e.metaKey || e.ctrlKey) {
                    e.preventDefault();
                    onPhotoSelection(photo.id, "toggle");
                    return;
                  }
                  onPhotoSelect(photo);
                }}
                className="flex items-center gap-3 flex-1 min-w-0 text-left focus:outline-none focus:ring-2 focus:ring-inset focus:ring-primary-blue"
                aria-pressed={isChecked}
                aria-label={photo.name}
              >
                <PhotoThumbnail
                  photoId={photo.id}
                  modifiedAt={photo.lastModifiedAt}
                  alt={photo.name}
                  className="w-20 h-20 rounded-lg object-cover flex-shrink-0"
                  size="medium"
                />
                <div className="flex flex-col gap-0.5 flex-1 min-w-0">
                  <span className="text-base font-medium text-gray-900 truncate">
                    {photo.name}
                  </span>
                  <span className="text-sm text-gray-500 truncate">
                    {photo.folderPath}
                  </span>
                  {photo.tags.length > 0 && (
                    <div className="flex flex-wrap gap-1">
                      {visibleTags.map((tag) => (
                        <TagBadge key={tag.id} name={tag.name} />
                      ))}
                      {overflowCount > 0 && (
                        <span className="inline-flex items-center px-1.5 py-0.5 bg-gray-100 text-gray-600 rounded-full text-xs">
                          +{overflowCount}
                        </span>
                      )}
                    </div>
                  )}
                  <div className="flex items-center gap-3 text-xs text-gray-500">
                    <span>{formatFileSize(photo.fileSizeBytes)}</span>
                    <span>
                      {new Date(photo.lastModifiedAt).toLocaleDateString("cs-CZ")}
                    </span>
                    {photo.sharePointWebUrl && (
                      <a
                        href={photo.sharePointWebUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        onClick={(e) => e.stopPropagation()}
                        className="text-primary-blue flex items-center gap-1"
                      >
                        <ExternalLink className="w-3 h-3" />
                      </a>
                    )}
                  </div>
                </div>
              </button>
            </div>
          );
        })}
      </div>

      <div className="flex-shrink-0 border-t border-gray-200 px-4 py-2 flex items-center justify-between bg-white">
        <span className="text-xs text-gray-500">
          {total} {total === 1 ? "fotografie" : total < 5 ? "fotografie" : "fotografií"}
        </span>
        <div className="flex items-center gap-2">
          <button
            onClick={() => onPageChange(page - 1)}
            disabled={!canGoPrev}
            className="p-1 rounded text-gray-500 hover:bg-gray-100 disabled:opacity-40 disabled:cursor-not-allowed"
            aria-label="Předchozí stránka"
          >
            <ChevronLeft className="w-4 h-4" />
          </button>
          <span className="text-xs text-gray-600">
            Stránka {page} z {totalPages}
          </span>
          <button
            onClick={() => onPageChange(page + 1)}
            disabled={!canGoNext}
            className="p-1 rounded text-gray-500 hover:bg-gray-100 disabled:opacity-40 disabled:cursor-not-allowed"
            aria-label="Další stránka"
          >
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
}

export default PhotoList;
```

---

## Task 3: Update PhotobankPage — rename prop, implement replace semantics

**Files:**
- Modify: `frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx`

### Background

Current handler (lines 164–198) uses `withRange: boolean` and is **additive** for ranges. Need to:
1. Rename to `handlePhotoSelection(photoId, mode: "toggle" | "range")`
2. Change range branch to `setSelectedIds(new Set(rangeIds))` — replace, not merge
3. Update `sharedPhotoProps` key from `onTogglePhotoSelection` to `onPhotoSelection`

- [ ] **Step 1: Replace the handler and sharedPhotoProps**

In `PhotobankPage.tsx`, replace lines 164–233 (the handler + sharedPhotoProps object) with:

```ts
  const handlePhotoSelection = useCallback(
    (photoId: number, mode: "toggle" | "range") => {
      const items = photosData?.items ?? [];

      if (mode === "range" && selectionAnchorId !== null) {
        const anchorIdx = items.findIndex((p) => p.id === selectionAnchorId);
        const targetIdx = items.findIndex((p) => p.id === photoId);

        if (anchorIdx !== -1 && targetIdx !== -1) {
          const lo = Math.min(anchorIdx, targetIdx);
          const hi = Math.max(anchorIdx, targetIdx);
          setSelectedIds(new Set(items.slice(lo, hi + 1).map((p) => p.id)));
          return;
        }
      }

      setSelectedIds((prev) => {
        const next = new Set(prev);
        if (next.has(photoId)) {
          next.delete(photoId);
        } else {
          next.add(photoId);
        }
        return next;
      });
      setSelectionAnchorId(photoId);
    },
    [photosData?.items, selectionAnchorId],
  );

  const bulkAddByIdsMutation = useBulkAddPhotoTagByIds();
  const retagMutation = useRetagPhotos();

  const handleApplyBulkTag = useCallback(
    async (tagName: string) => {
      await bulkAddByIdsMutation.mutateAsync({
        photoIds: Array.from(selectedIds),
        tagName,
      });
      handleClearSelection();
    },
    [selectedIds, bulkAddByIdsMutation, handleClearSelection],
  );

  const handleAutoTagSelected = useCallback(() => {
    retagMutation.mutate({
      photoIds: Array.from(selectedIds),
      clearExistingAiTags: false,
    });
  }, [selectedIds, retagMutation]);

  const sharedPhotoProps = {
    photos: photosData?.items ?? [],
    selectedPhotoId: selectedPhotoId,
    total: photosData?.total ?? 0,
    page: photosData?.page ?? page,
    pageSize: DEFAULT_PAGE_SIZE,
    isLoading: photosLoading,
    onPhotoSelect: handlePhotoSelect,
    onPageChange: handlePageChange,
    selectedIds,
    onPhotoSelection: handlePhotoSelection,
    canSelect: canBulkTag,
  };
```

- [ ] **Step 2: Verify TypeScript compiles cleanly**

```bash
cd frontend && npx tsc --noEmit 2>&1 | grep -E "(error|photobank)" | head -20
```

Expected: No errors. If errors about `onTogglePhotoSelection` remain, check that the old name was fully replaced in `sharedPhotoProps`.

---

## Task 4: Update PhotoGrid tests

**Files:**
- Modify: `frontend/src/components/marketing/photobank/__tests__/PhotoGrid.test.tsx`

### Background

The test file has a `selection` describe block (lines 157–197) that references `data-testid="photo-select-checkbox-{id}"` and `onTogglePhotoSelection`. These test targets no longer exist. Also, the `aria-pressed` tests (lines 75–91) currently check via `selectedPhotoId` (drawer state), but `aria-pressed` now reflects `isChecked` (multi-select state).

All other tests (pagination, thumbnails, tags) are unaffected.

- [ ] **Step 1: Update the `renderGrid` defaults and rewrite `selection` + `aria-pressed` tests**

Replace the entire file content:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PhotoGrid from "../PhotoGrid";
import type { PhotoDto } from "../../../../api/hooks/usePhotobank";

// Stub PhotoThumbnail to avoid MSAL/fetch dependencies in unit tests
jest.mock("../PhotoThumbnail", () => ({
  __esModule: true,
  default: ({ alt }: { alt: string }) => <div data-testid="thumbnail">{alt}</div>,
}));

const makePhoto = (overrides: Partial<PhotoDto> = {}): PhotoDto => ({
  id: 1,
  sharePointFileId: "file-001",
  driveId: "drive-xyz",
  name: "photo-01.jpg",
  folderPath: "/Fotky/2026",
  sharePointWebUrl: "https://sp.example.com/photo-01.jpg",
  fileSizeBytes: 2048,
  lastModifiedAt: "2026-04-01T10:00:00Z",
  tags: [],
  ...overrides,
});

const mockPhotos: PhotoDto[] = [
  makePhoto({ id: 1, name: "photo-01.jpg" }),
  makePhoto({ id: 2, name: "photo-02.jpg", sharePointFileId: "file-002" }),
  makePhoto({ id: 3, name: "photo-03.jpg", sharePointFileId: "file-003" }),
];

function renderGrid(overrides: Partial<React.ComponentProps<typeof PhotoGrid>> = {}) {
  const defaults: React.ComponentProps<typeof PhotoGrid> = {
    photos: mockPhotos,
    selectedPhotoId: null,
    total: 3,
    page: 1,
    pageSize: 48,
    isLoading: false,
    onPhotoSelect: jest.fn(),
    onPageChange: jest.fn(),
    selectedIds: new Set<number>(),
    onPhotoSelection: jest.fn(),
    canSelect: false,
    ...overrides,
  };
  return { ...render(<PhotoGrid {...defaults} />), props: defaults };
}

describe("PhotoGrid", () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  test("renders photo thumbnails for each photo", () => {
    // Arrange & Act
    renderGrid();

    // Assert
    const thumbnails = screen.getAllByTestId("thumbnail");
    expect(thumbnails).toHaveLength(3);
  });

  test("clicking a photo calls onPhotoSelect with the correct photo", () => {
    // Arrange
    const onPhotoSelect = jest.fn();
    renderGrid({ onPhotoSelect });

    // Act
    fireEvent.click(screen.getByLabelText("photo-01.jpg"));

    // Assert
    expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
  });

  test("multi-selected photo has aria-pressed true", () => {
    // Arrange
    renderGrid({ selectedIds: new Set([2]) });

    // Assert
    const btn = screen.getByLabelText("photo-02.jpg");
    expect(btn).toHaveAttribute("aria-pressed", "true");
  });

  test("non-selected photo has aria-pressed false", () => {
    // Arrange
    renderGrid({ selectedIds: new Set([2]) });

    // Assert
    const btn = screen.getByLabelText("photo-01.jpg");
    expect(btn).toHaveAttribute("aria-pressed", "false");
  });

  test("pagination shows correct page info", () => {
    // Arrange
    renderGrid({ total: 100, page: 2, pageSize: 48 });

    // Assert
    expect(screen.getByText("Stránka 2 z 3")).toBeInTheDocument();
  });

  test("Prev button is disabled on first page", () => {
    // Arrange
    renderGrid({ total: 100, page: 1, pageSize: 48 });

    // Assert
    expect(screen.getByLabelText("Předchozí stránka")).toBeDisabled();
  });

  test("Next button is disabled on last page", () => {
    // Arrange
    renderGrid({ total: 48, page: 1, pageSize: 48 });

    // Assert
    expect(screen.getByLabelText("Další stránka")).toBeDisabled();
  });

  test("clicking Next calls onPageChange with incremented page", () => {
    // Arrange
    const onPageChange = jest.fn();
    renderGrid({ total: 100, page: 1, pageSize: 48, onPageChange });

    // Act
    fireEvent.click(screen.getByLabelText("Další stránka"));

    // Assert
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  test("clicking Prev calls onPageChange with decremented page", () => {
    // Arrange
    const onPageChange = jest.fn();
    renderGrid({ total: 100, page: 3, pageSize: 48, onPageChange });

    // Act
    fireEvent.click(screen.getByLabelText("Předchozí stránka"));

    // Assert
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  test("shows skeleton placeholders while loading", () => {
    // Arrange
    renderGrid({ isLoading: true, photos: [] });

    // Assert — skeletons are rendered, no thumbnails
    expect(screen.queryByTestId("thumbnail")).not.toBeInTheDocument();
  });

  test("shows empty state message when no photos", () => {
    // Arrange
    renderGrid({ photos: [], total: 0, isLoading: false });

    // Assert
    expect(screen.getByText("Žádné fotografie nenalezeny")).toBeInTheDocument();
  });

  describe("selection", () => {
    test("no checkboxes are rendered regardless of canSelect", () => {
      // Arrange & Act
      renderGrid({ canSelect: true, selectedIds: new Set<number>() });

      // Assert — checkboxes are gone; selection is frame-only
      expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    });

    test("selected tile gets ring class when canSelect=true", () => {
      // Arrange & Act
      renderGrid({ canSelect: true, selectedIds: new Set([1]) });

      // Assert — the tile wrapper for photo 1 has the ring class
      const tile = screen.getByTestId("photo-tile-1").parentElement;
      expect(tile?.className).toContain("ring-2");
    });

    test("plain click calls onPhotoSelect and does NOT call onPhotoSelection", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderGrid({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-tile-1"));

      // Assert
      expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
      expect(onPhotoSelection).not.toHaveBeenCalled();
    });

    test("Cmd+click calls onPhotoSelection(id, toggle) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderGrid({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-tile-1"), { metaKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(1, "toggle");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("Ctrl+click calls onPhotoSelection(id, toggle) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderGrid({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-tile-1"), { ctrlKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(1, "toggle");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("Shift+click calls onPhotoSelection(id, range) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderGrid({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-tile-2"), { shiftKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(2, "range");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("when canSelect=false, Cmd+click falls through to onPhotoSelect", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderGrid({ canSelect: false, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-tile-1"), { metaKey: true });

      // Assert — read-only users always open drawer regardless of modifier
      expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
      expect(onPhotoSelection).not.toHaveBeenCalled();
    });
  });

  test("renders tag overlay when showTags is true and photo has tags", () => {
    // Arrange
    const photo = makePhoto({
      id: 1,
      name: "tagged.jpg",
      tags: [
        { id: 10, name: "Léto", source: "manual" },
        { id: 11, name: "Příroda", source: "manual" },
      ],
    });

    // Act
    renderGrid({ photos: [photo], total: 1, showTags: true });

    // Assert — tag text is visible, confirming overlay rendered
    expect(screen.getByText("Léto")).toBeInTheDocument();
    expect(screen.getByText("Příroda")).toBeInTheDocument();
  });

  test("does not render tag overlay when showTags is false", () => {
    // Arrange
    const photo = makePhoto({
      id: 1,
      name: "tagged.jpg",
      tags: [{ id: 10, name: "Léto", source: "manual" }],
    });

    // Act
    renderGrid({ photos: [photo], total: 1, showTags: false });

    // Assert
    expect(screen.queryByText("Léto")).not.toBeInTheDocument();
  });

  test("does not render tag overlay when showTags is omitted (default false)", () => {
    // Arrange
    const photo = makePhoto({
      id: 1,
      name: "tagged.jpg",
      tags: [{ id: 10, name: "Léto", source: "manual" }],
    });

    // Act — no showTags prop passed
    renderGrid({ photos: [photo], total: 1 });

    // Assert
    expect(screen.queryByText("Léto")).not.toBeInTheDocument();
  });

  test("shows overflow chip when photo has more than 3 tags", () => {
    // Arrange
    const photo = makePhoto({
      id: 1,
      name: "many-tags.jpg",
      tags: [
        { id: 1, name: "Tag1", source: "manual" },
        { id: 2, name: "Tag2", source: "manual" },
        { id: 3, name: "Tag3", source: "manual" },
        { id: 4, name: "Tag4", source: "manual" },
        { id: 5, name: "Tag5", source: "manual" },
      ],
    });

    // Act
    renderGrid({ photos: [photo], total: 1, showTags: true });

    // Assert — first 3 tags visible, rest truncated, overflow chip shows "+2"
    expect(screen.getByText("Tag1")).toBeInTheDocument();
    expect(screen.getByText("Tag2")).toBeInTheDocument();
    expect(screen.getByText("Tag3")).toBeInTheDocument();
    expect(screen.queryByText("Tag4")).not.toBeInTheDocument();
    expect(screen.queryByText("Tag5")).not.toBeInTheDocument();
    expect(screen.getByText("+2")).toBeInTheDocument();
  });

  test("overlay has pointer-events-none so click reaches the button", () => {
    // Arrange
    const onPhotoSelect = jest.fn();
    const photo = makePhoto({
      id: 1,
      name: "click-test.jpg",
      tags: [{ id: 10, name: "Léto", source: "manual" }],
    });

    // Act
    renderGrid({ photos: [photo], total: 1, showTags: true, onPhotoSelect });
    fireEvent.click(screen.getByLabelText("click-test.jpg"));

    // Assert — click propagated through the overlay to the button
    expect(onPhotoSelect).toHaveBeenCalledWith(photo);
  });
});
```

- [ ] **Step 2: Run tests and confirm they pass**

```bash
cd frontend && npx jest src/components/marketing/photobank/__tests__/PhotoGrid.test.tsx --no-coverage 2>&1 | tail -20
```

Expected: All tests pass (PASS).

---

## Task 5: Update PhotoList tests

**Files:**
- Modify: `frontend/src/components/marketing/photobank/__tests__/PhotoList.test.tsx`

- [ ] **Step 1: Replace the entire file**

Replace `frontend/src/components/marketing/photobank/__tests__/PhotoList.test.tsx` with:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PhotoList from "../PhotoList";
import type { PhotoDto } from "../../../../api/hooks/usePhotobank";

jest.mock("../PhotoThumbnail", () => ({
  __esModule: true,
  default: ({ alt }: { alt: string }) => <img alt={alt} />,
}));

function makePhoto(overrides: Partial<PhotoDto> = {}): PhotoDto {
  return {
    id: 1,
    sharePointFileId: "file-1",
    driveId: "drive-1",
    name: "photo.jpg",
    folderPath: "/Marketing",
    sharePointWebUrl: "https://example.com/photo",
    fileSizeBytes: 2048,
    lastModifiedAt: "2024-01-15T10:00:00Z",
    tags: [],
    ...overrides,
  };
}

const mockPhotos: PhotoDto[] = [
  makePhoto({ id: 1, name: "photo-01.jpg" }),
  makePhoto({ id: 2, name: "photo-02.jpg", sharePointFileId: "file-2" }),
  makePhoto({ id: 3, name: "photo-03.jpg", sharePointFileId: "file-3" }),
];

function renderList(overrides: Partial<React.ComponentProps<typeof PhotoList>> = {}) {
  const defaults: React.ComponentProps<typeof PhotoList> = {
    photos: mockPhotos,
    selectedPhotoId: null,
    total: 3,
    page: 1,
    pageSize: 48,
    isLoading: false,
    onPhotoSelect: jest.fn(),
    onPageChange: jest.fn(),
    selectedIds: new Set<number>(),
    onPhotoSelection: jest.fn(),
    canSelect: false,
    ...overrides,
  };
  return { ...render(<PhotoList {...defaults} />), props: defaults };
}

describe("PhotoList", () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  test("renders one row per photo", () => {
    // Arrange & Act
    renderList();

    // Assert — each photo renders an img with its name as alt
    expect(screen.getByAltText("photo-01.jpg")).toBeInTheDocument();
    expect(screen.getByAltText("photo-02.jpg")).toBeInTheDocument();
    expect(screen.getByAltText("photo-03.jpg")).toBeInTheDocument();
  });

  test("formats lastModifiedAt as Czech date", () => {
    // Arrange & Act
    renderList({ photos: [makePhoto({ lastModifiedAt: "2024-01-15T10:00:00Z" })] });

    // Assert
    const expected = new Date("2024-01-15T10:00:00Z").toLocaleDateString("cs-CZ");
    expect(screen.getByText(expected)).toBeInTheDocument();
  });

  test("formats fileSizeBytes: 1024 bytes → 1.0 KB", () => {
    // Arrange & Act
    renderList({ photos: [makePhoto({ fileSizeBytes: 1024 })] });

    // Assert
    expect(screen.getByText("1.0 KB")).toBeInTheDocument();
  });

  test("formats fileSizeBytes: null → —", () => {
    // Arrange & Act
    renderList({ photos: [makePhoto({ fileSizeBytes: null })] });

    // Assert
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  test("shows all tags when count is ≤5", () => {
    // Arrange
    const tags = [
      { id: 1, name: "Tag1", source: "Manual" as const },
      { id: 2, name: "Tag2", source: "Manual" as const },
      { id: 3, name: "Tag3", source: "AI" as const },
    ];

    // Act
    renderList({ photos: [makePhoto({ tags })] });

    // Assert
    expect(screen.getByText("Tag1")).toBeInTheDocument();
    expect(screen.getByText("Tag2")).toBeInTheDocument();
    expect(screen.getByText("Tag3")).toBeInTheDocument();
    expect(screen.queryByText(/^\+/)).not.toBeInTheDocument();
  });

  test("shows +N chip when tag count exceeds 5", () => {
    // Arrange
    const tags = [
      { id: 1, name: "Tag1", source: "Manual" as const },
      { id: 2, name: "Tag2", source: "Manual" as const },
      { id: 3, name: "Tag3", source: "AI" as const },
      { id: 4, name: "Tag4", source: "Rule" as const },
      { id: 5, name: "Tag5", source: "Manual" as const },
      { id: 6, name: "Tag6", source: "AI" as const },
      { id: 7, name: "Tag7", source: "Rule" as const },
    ];

    // Act
    renderList({ photos: [makePhoto({ tags })] });

    // Assert: first 5 visible, +2 overflow chip
    expect(screen.getByText("Tag1")).toBeInTheDocument();
    expect(screen.getByText("Tag5")).toBeInTheDocument();
    expect(screen.queryByText("Tag6")).not.toBeInTheDocument();
    expect(screen.getByText("+2")).toBeInTheDocument();
  });

  test("clicking a row calls onPhotoSelect with the photo", () => {
    // Arrange
    const onPhotoSelect = jest.fn();
    renderList({ onPhotoSelect });

    // Act
    fireEvent.click(screen.getByLabelText("photo-01.jpg"));

    // Assert
    expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
  });

  test("multi-selected row has aria-pressed true", () => {
    // Arrange
    renderList({ selectedIds: new Set([2]) });

    // Assert
    expect(screen.getByLabelText("photo-02.jpg")).toHaveAttribute("aria-pressed", "true");
  });

  test("non-selected row has aria-pressed false", () => {
    // Arrange
    renderList({ selectedIds: new Set([2]) });

    // Assert
    expect(screen.getByLabelText("photo-01.jpg")).toHaveAttribute("aria-pressed", "false");
  });

  test("clicking SharePoint link does NOT fire onPhotoSelect", () => {
    // Arrange
    const onPhotoSelect = jest.fn();
    renderList({
      photos: [makePhoto({ sharePointWebUrl: "https://example.com/photo" })],
      onPhotoSelect,
    });

    // Act — click the SharePoint link
    const link = screen.getByRole("link");
    fireEvent.click(link);

    // Assert
    expect(onPhotoSelect).not.toHaveBeenCalled();
  });

  test("shows loading skeleton when isLoading is true", () => {
    // Arrange & Act
    renderList({ isLoading: true, photos: [] });

    // Assert — no photos rendered, skeleton present via animate-pulse divs
    expect(screen.queryByRole("button", { name: /photo/ })).not.toBeInTheDocument();
    expect(screen.getByTestId("loading-skeleton")).toBeInTheDocument();
  });

  test("shows empty state when photos is empty and not loading", () => {
    // Arrange & Act
    renderList({ photos: [], total: 0, isLoading: false });

    // Assert
    expect(screen.getByText("Žádné fotografie nenalezeny")).toBeInTheDocument();
  });

  describe("selection", () => {
    test("no checkboxes are rendered regardless of canSelect", () => {
      // Arrange & Act
      renderList({ canSelect: true, selectedIds: new Set<number>() });

      // Assert — checkboxes gone
      expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    });

    test("plain click calls onPhotoSelect and does NOT call onPhotoSelection", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderList({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-row-1"));

      // Assert
      expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
      expect(onPhotoSelection).not.toHaveBeenCalled();
    });

    test("Cmd+click calls onPhotoSelection(id, toggle) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderList({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-row-1"), { metaKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(1, "toggle");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("Ctrl+click calls onPhotoSelection(id, toggle) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderList({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-row-1"), { ctrlKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(1, "toggle");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("Shift+click calls onPhotoSelection(id, range) and does NOT open drawer", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderList({ canSelect: true, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-row-2"), { shiftKey: true });

      // Assert
      expect(onPhotoSelection).toHaveBeenCalledWith(2, "range");
      expect(onPhotoSelect).not.toHaveBeenCalled();
    });

    test("when canSelect=false, Cmd+click falls through to onPhotoSelect", () => {
      // Arrange
      const onPhotoSelect = jest.fn();
      const onPhotoSelection = jest.fn();
      renderList({ canSelect: false, onPhotoSelect, onPhotoSelection });

      // Act
      fireEvent.click(screen.getByTestId("photo-row-1"), { metaKey: true });

      // Assert
      expect(onPhotoSelect).toHaveBeenCalledWith(mockPhotos[0]);
      expect(onPhotoSelection).not.toHaveBeenCalled();
    });
  });
});
```

- [ ] **Step 2: Run tests and confirm they pass**

```bash
cd frontend && npx jest src/components/marketing/photobank/__tests__/PhotoList.test.tsx --no-coverage 2>&1 | tail -20
```

Expected: All tests pass (PASS).

---

## Task 6: Run all photobank tests and full type check

- [ ] **Step 1: Run all photobank tests**

```bash
cd frontend && npx jest src/components/marketing/photobank --no-coverage 2>&1 | tail -30
```

Expected: All test suites pass. No failures.

- [ ] **Step 2: Full TypeScript check**

```bash
cd frontend && npx tsc --noEmit 2>&1 | grep -c "error TS" || echo "0 errors"
```

Expected: `0 errors`

- [ ] **Step 3: Commit**

```bash
cd frontend && git add \
  src/components/marketing/photobank/PhotoGrid.tsx \
  src/components/marketing/photobank/PhotoList.tsx \
  src/components/marketing/photobank/pages/PhotobankPage.tsx \
  src/components/marketing/photobank/__tests__/PhotoGrid.test.tsx \
  src/components/marketing/photobank/__tests__/PhotoList.test.tsx

git commit -m "feat: replace photobank checkboxes with modifier-click multi-select

- Drop per-photo checkboxes; selection shown via frame/ring on tile/row
- Cmd/Ctrl+click toggles individual photo, Shift+click replaces selection with range
- Plain click still opens the detail drawer
- Rename onTogglePhotoSelection prop to onPhotoSelection with mode union
- Fix Shift+click range to REPLACE existing selection (was additive)"
```

---

## Verification Checklist (manual, dev server)

After implementation, verify these scenarios manually in the browser (logged in as `marketing_writer`):

- [ ] Plain click on a tile → drawer opens; no frame/ring appears
- [ ] Cmd+click (Mac) or Ctrl+click (Win) a tile → ring frame appears; drawer does NOT open; bulk action bar shows "1 vybráno"
- [ ] Cmd+click 2 more tiles → 3 frames visible; bar shows count 3
- [ ] Shift+click a tile 4 rows below the last Cmd-clicked one → frames span contiguous range; only range visible (replace, not additive)
- [ ] Switch to list view → same four behaviors on rows
- [ ] Change search/tag filter → selection clears, anchor clears
- [ ] Switch page → selection clears
- [ ] Read-only user (no `marketing_writer`) → modifier+click behaves like plain click; no frame ever shows; no bulk bar visible
