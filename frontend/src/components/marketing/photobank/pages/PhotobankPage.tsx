import React, { useCallback, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Settings } from "lucide-react";
import { useMsal } from "@azure/msal-react";
import TagSidebar from "../TagSidebar";
import PhotoGrid from "../PhotoGrid";
import PhotoDrawer from "../PhotoDrawer";
import { usePhotos, usePhotoTags } from "../../../../api/hooks/usePhotobank";
import type { PhotoDto } from "../../../../api/hooks/usePhotobank";

const ADMIN_ROLE = "administrator";

const DEFAULT_PAGE_SIZE = 48;
const SIDEBAR_WIDTH = "220px";

const PhotobankPage: React.FC = () => {
  const { accounts } = useMsal();
  const isAdmin =
    (accounts[0]?.idTokenClaims as any)?.roles?.includes(ADMIN_ROLE) ?? false;

  const [selectedTagIds, setSelectedTagIds] = useState<number[]>([]);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [selectedPhoto, setSelectedPhoto] = useState<PhotoDto | null>(null);

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

  const handleClearFilters = useCallback(() => {
    setSelectedTagIds([]);
    setSearch("");
    setPage(1);
  }, []);

  const handlePhotoSelect = useCallback(
    (photo: PhotoDto) => {
      setSelectedPhoto((prev) => (prev?.id === photo.id ? null : photo));
    },
    [],
  );

  const handleDrawerClose = useCallback(() => {
    setSelectedPhoto(null);
  }, []);

  const handlePageChange = useCallback((newPage: number) => {
    setPage(newPage);
    setSelectedPhoto(null);
  }, []);

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
          onTagToggle={handleTagToggle}
          onSearchChange={handleSearchChange}
          onClearFilters={handleClearFilters}
        />
      </div>

      {/* Main photo grid */}
      <div className="flex-1 flex overflow-hidden">
        <PhotoGrid
          photos={photosData?.items ?? []}
          selectedPhotoId={selectedPhoto?.id ?? null}
          total={photosData?.total ?? 0}
          page={photosData?.page ?? page}
          pageSize={DEFAULT_PAGE_SIZE}
          isLoading={photosLoading}
          onPhotoSelect={handlePhotoSelect}
          onPageChange={handlePageChange}
        />
      </div>

      {/* Right detail drawer */}
      {selectedPhoto && (
        <PhotoDrawer photo={selectedPhoto} onClose={handleDrawerClose} />
      )}
      </div>
    </div>
  );
};

export default PhotobankPage;
