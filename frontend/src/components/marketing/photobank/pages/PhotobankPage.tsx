import React, { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Grid3x3, List, Settings, Tag } from "lucide-react";
import { usePermissionsContext } from "../../../../auth/PermissionsContext";
import TagSidebar from "../TagSidebar";
import PhotoGrid from "../PhotoGrid";
import PhotoList from "../PhotoList";
import PhotoDrawer from "../PhotoDrawer";
import PhotoViewToggle from "../PhotoViewToggle";
import BulkTagButton from "../BulkTagButton";
import BulkTagDialog from "../BulkTagDialog";
import PhotobankBulkActionBar from "../PhotobankBulkActionBar";
import { usePhotos, usePhotoTags, useBulkAddPhotoTagByIds, useRetagPhotos } from "../../../../api/hooks/usePhotobank";
import type { PhotoDto } from "../../../../api/hooks/usePhotobank";
import { useScreenView } from "../../../../telemetry/useScreenView";

const DEFAULT_PAGE_SIZE = 48;
const SIDEBAR_WIDTH = "220px";
const STORAGE_KEY = "photobank.view";
const STORAGE_KEY_TAGS_ON_TILES = "photobank.tagsOnTiles";

type ViewMode = "tiles" | "list";

const VIEW_OPTIONS = [
  { value: "tiles" as const, icon: <Grid3x3 className="w-4 h-4" />, label: "Dlaždice" },
  { value: "list" as const, icon: <List className="w-4 h-4" />, label: "Seznam" },
];

function readViewMode(): ViewMode {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === "list" ? "list" : "tiles";
  } catch {
    return "tiles";
  }
}

function readTagsOnTiles(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY_TAGS_ON_TILES) === "1";
  } catch {
    return false;
  }
}

function PhotobankPage() {
  const { hasPermission } = usePermissionsContext();
  const isAdmin = hasPermission('marketing.photobank.admin');
  const canBulkTag = hasPermission('marketing.photobank.write');

  const [selectedTagIds, setSelectedTagIds] = useState<number[]>([]);
  const [search, setSearch] = useState("");
  const [withoutTags, setWithoutTags] = useState(false);
  const [useRegex, setUseRegex] = useState(false);
  const [page, setPage] = useState(1);
  const [selectedPhotoId, setSelectedPhotoId] = useState<number | null>(null);
  const [view, setView] = useState<ViewMode>(readViewMode);
  const [tagsOnTiles, setTagsOnTiles] = useState<boolean>(readTagsOnTiles);
  const [bulkTagDialogOpen, setBulkTagDialogOpen] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [selectionAnchorId, setSelectionAnchorId] = useState<number | null>(null);

  useScreenView('Marketing', 'Photobank', view === 'tiles' ? 'TilesView' : 'ListView');

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, view);
    } catch {
      // private browsing
    }
  }, [view]);

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY_TAGS_ON_TILES, tagsOnTiles ? "1" : "0");
    } catch {
      // private browsing
    }
  }, [tagsOnTiles]);

  const { data: tagsData } = usePhotoTags();

  const selectedTagNames = useMemo(
    () =>
      selectedTagIds
        .map((id) => tagsData?.find((t) => t.id === id)?.name)
        .filter((name): name is string => name !== undefined),
    [selectedTagIds, tagsData],
  );

  const { data: photosData, isLoading: photosLoading, isError: photosError } = usePhotos({
    tags: selectedTagNames.length > 0 ? selectedTagNames : undefined,
    search: search || undefined,
    useRegex: useRegex || undefined,
    withoutTags: withoutTags || undefined,
    page,
    pageSize: DEFAULT_PAGE_SIZE,
  });

  const selectedPhoto = useMemo(
    () => (selectedPhotoId != null ? (photosData?.items.find((p) => p.id === selectedPhotoId) ?? null) : null),
    [selectedPhotoId, photosData],
  );

  const searchErrorMessage = photosError && useRegex
    ? "Neplatný regulární výraz"
    : null;

  const handleTagToggle = useCallback((tagId: number) => {
    setSelectedTagIds((prev) =>
      prev.includes(tagId) ? prev.filter((id) => id !== tagId) : [...prev, tagId],
    );
    setPage(1);
    setSelectedIds(new Set());
    setSelectionAnchorId(null);
  }, []);

  const handleSearchChange = useCallback((value: string) => {
    setSearch(value);
    setPage(1);
    setSelectedIds(new Set());
    setSelectionAnchorId(null);
  }, []);

  const handleRegexChange = useCallback((value: boolean) => {
    setUseRegex(value);
    setPage(1);
  }, []);

  const handleClearFilters = useCallback(() => {
    setSelectedTagIds([]);
    setSearch("");
    setWithoutTags(false);
    setUseRegex(false);
    setPage(1);
    setSelectedIds(new Set());
    setSelectionAnchorId(null);
  }, []);

  const handlePhotoSelect = useCallback((photo: PhotoDto) => {
    setSelectedPhotoId((prev) => (prev === photo.id ? null : photo.id));
  }, []);

  const handleDrawerClose = useCallback(() => {
    setSelectedPhotoId(null);
  }, []);

  const handlePageChange = useCallback((newPage: number) => {
    setPage(newPage);
    setSelectedPhotoId(null);
    setSelectedIds(new Set());
    setSelectionAnchorId(null);
  }, []);

  const handleOpenBulkTagDialog = useCallback(() => setBulkTagDialogOpen(true), []);
  const handleCloseBulkTagDialog = useCallback(() => setBulkTagDialogOpen(false), []);

  const handleClearSelection = useCallback(() => {
    setSelectedIds(new Set());
    setSelectionAnchorId(null);
  }, []);

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

  return (
    <div className="flex flex-col h-full overflow-hidden">
      {isAdmin && (
        <div className="flex justify-end px-3 py-1.5 border-b border-gray-100">
          <Link
            to="/marketing/photobank/settings"
            aria-label="Nastavení fotobanky"
            className="text-gray-400 hover:text-gray-600"
          >
            <Settings className="w-4 h-4" />
          </Link>
        </div>
      )}
      <div className="flex flex-1 overflow-hidden">
        {/* Left sidebar */}
        <div style={{ width: SIDEBAR_WIDTH }} className="flex-shrink-0">
          <TagSidebar
            tags={tagsData ?? []}
            selectedTagIds={selectedTagIds}
            search={search}
            withoutTags={withoutTags}
            useRegex={useRegex}
            onTagToggle={handleTagToggle}
            onSearchChange={handleSearchChange}
            onWithoutTagsToggle={() => { setWithoutTags((v) => !v); setPage(1); }}
            onClearFilters={handleClearFilters}
            onRegexChange={handleRegexChange}
            errorMessage={searchErrorMessage}
          />
        </div>

        {/* Main content area */}
        <div className="flex-1 flex flex-col overflow-hidden">
          {/* View toggle + bulk tag bar */}
          <div className="flex items-center justify-between px-4 py-2 border-b border-gray-100">
            <div>
              {canBulkTag && (
                <BulkTagButton
                  search={search}
                  selectedTagNames={selectedTagNames}
                  totalMatching={photosData?.total ?? 0}
                  onOpenDialog={handleOpenBulkTagDialog}
                />
              )}
            </div>
            <div className="flex items-center">
              <PhotoViewToggle
                options={VIEW_OPTIONS}
                value={view}
                onChange={(v) => setView(v as ViewMode)}
              />
              {view === "tiles" && (
                <button
                  type="button"
                  title="Zobrazit štítky na dlaždicích"
                  aria-pressed={tagsOnTiles}
                  onClick={() => setTagsOnTiles((v) => !v)}
                  className={[
                    "w-8 h-8 flex items-center justify-center rounded ml-2",
                    tagsOnTiles
                      ? "bg-primary-blue text-white"
                      : "bg-white text-gray-600 border border-gray-300 hover:bg-gray-50",
                  ].join(" ")}
                >
                  <Tag className="w-4 h-4" />
                </button>
              )}
            </div>
          </div>
          {/* Bulk selection action bar */}
          {canBulkTag && selectedIds.size > 0 && (
            <PhotobankBulkActionBar
              selectedCount={selectedIds.size}
              existingTags={tagsData ?? []}
              isApplying={bulkAddByIdsMutation.isPending}
              isAutoTagging={retagMutation.isPending}
              onApplyTag={handleApplyBulkTag}
              onAutoTag={handleAutoTagSelected}
              onClear={handleClearSelection}
            />
          )}
          {/* Photo grid or list */}
          <div className="flex-1 flex overflow-hidden">
            {view === "tiles" ? (
              <PhotoGrid {...sharedPhotoProps} showTags={tagsOnTiles} />
            ) : (
              <PhotoList {...sharedPhotoProps} />
            )}
          </div>
        </div>

        {/* Right detail drawer */}
        {selectedPhoto && (
          <PhotoDrawer photo={selectedPhoto} onClose={handleDrawerClose} />
        )}
      </div>
      {bulkTagDialogOpen && (
        <BulkTagDialog
          search={search}
          selectedTagNames={selectedTagNames}
          totalMatching={photosData?.total ?? 0}
          existingTags={tagsData ?? []}
          onClose={handleCloseBulkTagDialog}
        />
      )}
    </div>
  );
}

export default PhotobankPage;
