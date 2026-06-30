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
            <div className="w-20 h-20 bg-gray-200 dark:bg-graphite-hover rounded-lg flex-shrink-0" />
            <div className="flex-1 space-y-2 py-1">
              <div className="h-4 bg-gray-200 dark:bg-graphite-hover rounded w-3/4" />
              <div className="h-3 bg-gray-200 dark:bg-graphite-hover rounded w-1/2" />
              <div className="h-3 bg-gray-200 dark:bg-graphite-hover rounded w-1/4" />
            </div>
          </div>
        ))}
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
      <div className="flex-1 overflow-y-auto divide-y divide-gray-200 dark:divide-graphite-border">
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
                    : "hover:bg-gray-50 dark:hover:bg-white/5",
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
                aria-expanded={isSelected}
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
                  <span className="text-base font-medium text-gray-900 dark:text-graphite-text truncate">
                    {photo.name}
                  </span>
                  <span className="text-sm text-gray-500 dark:text-graphite-muted truncate">
                    {photo.folderPath}
                  </span>
                  {photo.tags.length > 0 && (
                    <div className="flex flex-wrap gap-1">
                      {visibleTags.map((tag) => (
                        <TagBadge key={tag.id} name={tag.name} />
                      ))}
                      {overflowCount > 0 && (
                        <span className="inline-flex items-center px-1.5 py-0.5 bg-gray-100 dark:bg-graphite-surface-2 text-gray-600 dark:text-graphite-muted rounded-full text-xs">
                          +{overflowCount}
                        </span>
                      )}
                    </div>
                  )}
                  <div className="flex items-center gap-3 text-xs text-gray-500 dark:text-graphite-muted">
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
}

export default PhotoList;
