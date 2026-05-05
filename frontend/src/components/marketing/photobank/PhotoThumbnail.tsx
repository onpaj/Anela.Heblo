import React, { useState } from "react";
import { ImageIcon } from "lucide-react";
import { getConfig } from "../../../config/runtimeConfig";

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
  const [hasError, setHasError] = useState(false);

  const version = new Date(modifiedAt).getTime();
  const url = `${getConfig().apiUrl}/api/photobank/photos/${photoId}/thumbnail/${size}?v=${version}`;

  if (hasError) {
    return (
      <div
        className={`flex items-center justify-center bg-gray-100 rounded text-gray-400 ${className}`}
        aria-label="Náhled není k dispozici"
      >
        <ImageIcon className="w-8 h-8" />
      </div>
    );
  }

  return (
    <img
      src={url}
      alt={alt}
      loading="lazy"
      onError={() => setHasError(true)}
      className={`object-cover rounded ${className}`}
    />
  );
}

export default PhotoThumbnail;
