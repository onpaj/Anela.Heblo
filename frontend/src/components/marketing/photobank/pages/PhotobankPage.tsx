import React, { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Grid3x3, List, Settings, Tag } from "lucide-react";
import { useMsal } from "@azure/msal-react";
import TagSidebar from "../TagSidebar";
import PhotoGrid from "../PhotoGrid";
import PhotoList from "../PhotoList";
import PhotoDrawer from "../PhotoDrawer";
import PhotoViewToggle from "../PhotoViewToggle";
import BulkTagButton from "../BulkTagButton";
import BulkTagDialog from "../BulkTagDialog";
import { usePhotos, usePhotoTags } from "../../../../api/hooks/usePhotobank";
import type { PhotoDto } from "../../../../api/hooks/usePhotobank";

const ADMIN_ROLE = "super_user";
const TAGGER_ROLE = "marketing_writer";

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
  const { accounts } = useMsal();
  const roles = (accounts[0]?.idTokenClaims as any)?.roles as string[] | undefined;
  const isAdmin = roles?.includes(ADMIN_ROLE) ?? false;
  const canBulkTag = roles?.includes(TAGGER_ROLE) ?? false;

  const [selectedTagIds, setSelectedTagIds] = useState<number[]>([]);
  const [search, setSearch] = useState("");
  const [folderPath, setFolderPath] = useState("");
  const [withoutTags, setWithoutTags] = useState(false);
  const [useRegex, setUseRegex] = useState(false);
  const [page, setPage] = useState(1);
  const [selectedPhoto, setSelectedPhoto] = useState<PhotoDto | null>(null);
  const [view, setView] = useState<ViewMode>(readViewMode);
  const [tagsOnTiles, setTagsOnTiles] = useState<boolean>(readTagsOnTiles);
  const [bulkTagDialogOpen, setBulkTagDialogOpen] = useState(false);

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

  const { data: photosData, isLoading: photosLoading } = usePhotos({
    tags: selectedTagNames.length > 0 ? selectedTagNames : undefined,
    search: search || undefined,
    useRegex: useRegex || undefined,
    folderPath: folderPath || undefined,
    withoutTags: withoutTags || undefined,
    page,
    pageSize: DEFAULT_PAGE_SIZE,
  });

  const handleTagToggle = useCallback((tagId: number) => {
    setSelectedTagIds((prev) =>
      prev.includes(tagId) ? prev.filter((id) => id !== tagId) : [...prev, tagId],
    );
    setPage(1);
  }, []);

  const handleSearchChange = useCallback((value: string) => {
    setSearch(value);
    setPage(1);
  }, []);

  const handleFolderPathChange = useCallback((value: string) => {
    setFolderPath(value);
    setPage(1);
  }, []);

  const handleRegexChange = useCallback((value: boolean) => {
    setUseRegex(value);
    setPage(1);
  }, []);

  const handleClearFilters = useCallback(() => {
    setSelectedTagIds([]);
    setSearch("");
    setFolderPath("");
    setWithoutTags(false);
    setUseRegex(false);
    setPage(1);
  }, []);

  const handlePhotoSelect = useCallback((photo: PhotoDto) => {
    setSelectedPhoto((prev) => (prev?.id === photo.id ? null : photo));
  }, []);

  const handleDrawerClose = useCallback(() => {
    setSelectedPhoto(null);
  }, []);

  const handlePageChange = useCallback((newPage: number) => {
    setPage(newPage);
    setSelectedPhoto(null);
  }, []);

  const handleOpenBulkTagDialog = useCallback(() => setBulkTagDialogOpen(true), []);
  const handleCloseBulkTagDialog = useCallback(() => setBulkTagDialogOpen(false), []);

  const sharedPhotoProps = {
    photos: photosData?.items ?? [],
    selectedPhotoId: selectedPhoto?.id ?? null,
    total: photosData?.total ?? 0,
    page: photosData?.page ?? page,
    pageSize: DEFAULT_PAGE_SIZE,
    isLoading: photosLoading,
    onPhotoSelect: handlePhotoSelect,
    onPageChange: handlePageChange,
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
            folderPath={folderPath}
            withoutTags={withoutTags}
            useRegex={useRegex}
            onTagToggle={handleTagToggle}
            onSearchChange={handleSearchChange}
            onFolderPathChange={handleFolderPathChange}
            onWithoutTagsToggle={() => { setWithoutTags((v) => !v); setPage(1); }}
            onClearFilters={handleClearFilters}
            onRegexChange={handleRegexChange}
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
                  folderPath={folderPath}
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
          folderPath={folderPath}
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
