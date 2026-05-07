import React from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import PhotoThumbnail from "./PhotoThumbnail";
import type { PhotoDto } from "../../../api/hooks/usePhotobank";

interface PhotoGridProps {
  photos: PhotoDto[];
  selectedPhotoId: number | null;
  total: number;
  page: number;
  pageSize: number;
  isLoading: boolean;
  onPhotoSelect: (photo: PhotoDto) => void;
  onPageChange: (page: number) => void;
  selectedIds: Set<number>;
  onTogglePhotoSelection: (photoId: number, withRange: boolean) => void;
  canSelect: boolean;
}

const PhotoGrid: React.FC<PhotoGridProps> = ({
  photos,
  selectedPhotoId,
  total,
  page,
  pageSize,
  isLoading,
  onPhotoSelect,
  onPageChange,
  selectedIds,
  onTogglePhotoSelection,
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
        <div className="grid grid-cols-[repeat(auto-fill,minmax(160px,1fr))] gap-3">
          {photos.map((photo) => {
            const isSelected = photo.id === selectedPhotoId;
            const isChecked = selectedIds.has(photo.id);
            return (
              <div
                key={photo.id}
                className={[
                  "group relative aspect-square rounded-lg overflow-hidden border-2 transition-all",
                  isSelected
                    ? "border-primary-blue ring-2 ring-primary-blue ring-offset-1"
                    : "border-transparent hover:border-gray-300",
                ].join(" ")}
              >
                {canSelect && (
                  <div
                    className={[
                      "absolute top-1 left-1 z-10",
                      isChecked
                        ? "opacity-100"
                        : "opacity-0 group-hover:opacity-100 focus-within:opacity-100",
                    ].join(" ")}
                  >
                    <input
                      type="checkbox"
                      checked={isChecked}
                      aria-label="Vybrat fotku"
                      data-testid={`photo-select-checkbox-${photo.id}`}
                      onChange={(e) => {
                        onTogglePhotoSelection(
                          photo.id,
                          e.nativeEvent instanceof MouseEvent && (e.nativeEvent as MouseEvent).shiftKey,
                        );
                      }}
                      className="w-4 h-4 accent-primary-blue cursor-pointer"
                    />
                  </div>
                )}
                <button
                  onClick={() => onPhotoSelect(photo)}
                  className="w-full h-full focus:outline-none focus:ring-2 focus:ring-primary-blue focus:ring-offset-2"
                  aria-pressed={isSelected}
                  aria-label={photo.name}
                >
                  <PhotoThumbnail
                    photoId={photo.id}
                    modifiedAt={photo.lastModifiedAt}
                    alt={photo.name}
                    className="w-full h-full"
                    size="medium"
                  />
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
