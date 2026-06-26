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
              className="aspect-square animate-pulse bg-gray-200 dark:bg-graphite-hover rounded-lg"
            />
          ))}
        </div>
      </div>
    );
  }

  if (photos.length === 0) {
    return (
      <div className="flex-1 flex items-center justify-center text-gray-400 dark:text-graphite-faint">
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
                      : "border-transparent hover:border-gray-300 dark:hover:border-graphite-border",
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
                  aria-expanded={isSelected}
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
      <div className="flex-shrink-0 border-t border-gray-200 dark:border-graphite-border px-4 py-2 flex items-center justify-between bg-white dark:bg-graphite-surface">
        <span className="text-xs text-gray-500 dark:text-graphite-muted">
          {total} {total === 1 ? "fotografie" : total < 5 ? "fotografie" : "fotografií"}
        </span>
        <div className="flex items-center gap-2">
          <button
            onClick={() => onPageChange(page - 1)}
            disabled={!canGoPrev}
            className="p-1 rounded text-gray-500 dark:text-graphite-muted hover:bg-gray-100 dark:hover:bg-white/5 disabled:opacity-40 disabled:cursor-not-allowed"
            aria-label="Předchozí stránka"
          >
            <ChevronLeft className="w-4 h-4" />
          </button>
          <span className="text-xs text-gray-600 dark:text-graphite-muted">
            Stránka {page} z {totalPages}
          </span>
          <button
            onClick={() => onPageChange(page + 1)}
            disabled={!canGoNext}
            className="p-1 rounded text-gray-500 dark:text-graphite-muted hover:bg-gray-100 dark:hover:bg-white/5 disabled:opacity-40 disabled:cursor-not-allowed"
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
