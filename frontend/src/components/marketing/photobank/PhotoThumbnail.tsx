import React, { useState, useEffect } from "react";
import { ImageIcon } from "lucide-react";
import { getConfig } from "../../../config/runtimeConfig";
import { authenticatedFetch } from "../../../api/client";

interface PhotoThumbnailProps {
  photoId: number;
  modifiedAt: string;
  alt: string;
  className?: string;
  size?: "medium" | "large";
}

function PhotoThumbnail({
  photoId,
  modifiedAt,
  alt,
  className = "",
  size = "medium",
}: PhotoThumbnailProps) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [hasError, setHasError] = useState(false);

  const version = new Date(modifiedAt).getTime();
  const url = `${getConfig().apiUrl}/api/photobank/photos/${photoId}/thumbnail/${size}?v=${version}`;

  useEffect(() => {
    setHasError(false);
    let cancelled = false;
    let blobUrl: string | null = null;

    authenticatedFetch(url)
      .then((res) => {
        if (!res.ok) throw new Error(`${res.status}`);
        return res.blob();
      })
      .then((blob) => {
        if (cancelled) return;
        blobUrl = URL.createObjectURL(blob);
        setObjectUrl(blobUrl);
      })
      .catch(() => {
        if (!cancelled) setHasError(true);
      });

    return () => {
      cancelled = true;
      if (blobUrl) URL.revokeObjectURL(blobUrl);
      setObjectUrl(null);
    };
  }, [url]);

  if (hasError) {
    return (
      <div
        className={`flex items-center justify-center bg-gray-100 dark:bg-graphite-surface-2 rounded text-gray-400 dark:text-graphite-faint ${className}`}
        aria-label="Náhled není k dispozici"
      >
        <ImageIcon className="w-8 h-8" />
      </div>
    );
  }

  if (!objectUrl) {
    return (
      <div
        className={`bg-gray-200 dark:bg-graphite-hover rounded animate-pulse ${className}`}
        aria-label={alt}
      />
    );
  }

  return (
    <img
      src={objectUrl}
      alt={alt}
      className={`object-cover rounded ${className}`}
    />
  );
}

export default PhotoThumbnail;
