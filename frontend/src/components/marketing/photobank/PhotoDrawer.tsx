import React, { useCallback, useState } from "react";
import { useMsal } from "@azure/msal-react";
import { X, ExternalLink, Copy, Check, Plus, Tag } from "lucide-react";
import PhotoThumbnail from "./PhotoThumbnail";
import { useAddPhotoTag, useRemovePhotoTag } from "../../../api/hooks/usePhotobank";
import type { PhotoDto } from "../../../api/hooks/usePhotobank";

interface PhotoDrawerProps {
  photo: PhotoDto;
  onClose: () => void;
}

const ADMIN_ROLE = "administrator";
const DRAWER_WIDTH_PX = 280;

const PhotoDrawer: React.FC<PhotoDrawerProps> = ({ photo, onClose }) => {
  const { accounts } = useMsal();
  const [newTagName, setNewTagName] = useState("");
  const [copySuccess, setCopySuccess] = useState(false);

  const isAdmin =
    (accounts[0]?.idTokenClaims as any)?.roles?.includes(ADMIN_ROLE) ?? false;

  const addTagMutation = useAddPhotoTag(photo.id);
  const removeTagMutation = useRemovePhotoTag(photo.id);

  const handleCopyLink = useCallback(async () => {
    if (!photo.sharePointWebUrl) return;
    try {
      await navigator.clipboard.writeText(photo.sharePointWebUrl);
      setCopySuccess(true);
      setTimeout(() => setCopySuccess(false), 2000);
    } catch {
      // Clipboard not available — silently ignore
    }
  }, [photo.sharePointWebUrl]);

  const handleAddTag = useCallback(
    async (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key !== "Enter") return;
      const name = newTagName.trim();
      if (!name) return;
      await addTagMutation.mutateAsync(name);
      setNewTagName("");
    },
    [newTagName, addTagMutation],
  );

  const handleRemoveTag = useCallback(
    (tagId: number) => {
      removeTagMutation.mutate(tagId);
    },
    [removeTagMutation],
  );

  // Format file size
  const formatFileSize = (bytes: number | null): string => {
    if (bytes == null) return "—";
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  return (
    <aside className="flex flex-col border-l border-gray-200 bg-white overflow-hidden flex-shrink-0" style={{ width: DRAWER_WIDTH_PX }}>
      {/* Header */}
      <div className="flex items-center justify-between p-3 border-b border-gray-200">
        <h2 className="text-sm font-semibold text-gray-700 truncate" title={photo.name}>
          {photo.name}
        </h2>
        <button
          onClick={onClose}
          className="flex-shrink-0 ml-2 p-1 rounded text-gray-400 hover:bg-gray-100 hover:text-gray-600"
          aria-label="Zavřít detail"
        >
          <X className="w-4 h-4" />
        </button>
      </div>

      {/* Thumbnail */}
      <div className="p-3 border-b border-gray-100">
        <PhotoThumbnail
          driveId={photo.driveId}
          fileId={photo.sharePointFileId}
          alt={photo.name}
          className="w-full aspect-square"
          size="large"
        />
      </div>

      {/* Metadata */}
      <div className="p-3 border-b border-gray-100 space-y-1.5">
        <DetailRow label="Složka" value={photo.folderPath} />
        <DetailRow label="Velikost" value={formatFileSize(photo.fileSizeBytes)} />
        <DetailRow
          label="Upraveno"
          value={new Date(photo.lastModifiedAt).toLocaleDateString("cs-CZ")}
        />
      </div>

      {/* Actions */}
      <div className="p-3 border-b border-gray-100 flex flex-col gap-2">
        {photo.sharePointWebUrl && (
          <a
            href={photo.sharePointWebUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-1.5 text-sm text-primary-blue hover:underline"
          >
            <ExternalLink className="w-3.5 h-3.5" />
            Otevřít v SharePointu
          </a>
        )}
        {photo.sharePointWebUrl && (
          <button
            onClick={handleCopyLink}
            className="flex items-center gap-1.5 text-sm text-gray-600 hover:text-gray-900"
          >
            {copySuccess ? (
              <>
                <Check className="w-3.5 h-3.5 text-green-500" />
                <span className="text-green-600">Zkopírováno!</span>
              </>
            ) : (
              <>
                <Copy className="w-3.5 h-3.5" />
                Kopírovat odkaz
              </>
            )}
          </button>
        )}
      </div>

      {/* Tags */}
      <div className="flex-1 overflow-y-auto p-3">
        <div className="flex items-center gap-1.5 mb-2">
          <Tag className="w-3.5 h-3.5 text-gray-400" />
          <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
            Štítky
          </span>
        </div>

        {photo.tags.length === 0 ? (
          <p className="text-xs text-gray-400 mb-2">Žádné štítky</p>
        ) : (
          <div className="flex flex-wrap gap-1.5 mb-3">
            {photo.tags.map((tag) => (
              <span
                key={tag.id}
                className="inline-flex items-center gap-1 px-2 py-0.5 bg-secondary-blue-pale text-primary-blue rounded-full text-xs"
              >
                {tag.name}
                {isAdmin && (
                  <button
                    onClick={() => handleRemoveTag(tag.id)}
                    disabled={removeTagMutation.isPending}
                    className="hover:text-red-500 disabled:opacity-50"
                    aria-label={`Odebrat štítek ${tag.name}`}
                  >
                    <X className="w-3 h-3" />
                  </button>
                )}
              </span>
            ))}
          </div>
        )}

        {isAdmin && (
          <div className="relative">
            <Plus className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400 pointer-events-none" />
            <input
              type="text"
              value={newTagName}
              onChange={(e) => setNewTagName(e.target.value)}
              onKeyDown={handleAddTag}
              placeholder="Přidat štítek (Enter)"
              className="w-full pl-8 pr-3 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
              disabled={addTagMutation.isPending}
              aria-label="Přidat nový štítek"
            />
          </div>
        )}
      </div>
    </aside>
  );
};

interface DetailRowProps {
  label: string;
  value: string;
}

const DetailRow: React.FC<DetailRowProps> = ({ label, value }) => (
  <div className="flex items-start gap-2 text-xs">
    <span className="text-gray-400 flex-shrink-0 w-16">{label}</span>
    <span className="text-gray-700 break-all">{value}</span>
  </div>
);

export default PhotoDrawer;
