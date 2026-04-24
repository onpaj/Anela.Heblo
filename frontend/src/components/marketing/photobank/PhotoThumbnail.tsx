import React, { useEffect, useState } from "react";
import { useMsal } from "@azure/msal-react";
import { InteractionRequiredAuthError } from "@azure/msal-browser";
import { ImageIcon } from "lucide-react";

interface PhotoThumbnailProps {
  driveId: string | null;
  fileId: string;
  alt: string;
  className?: string;
  size?: "medium" | "large";
}

const FILES_READ_SCOPE = "Files.Read.All";

const PhotoThumbnail: React.FC<PhotoThumbnailProps> = ({
  driveId,
  fileId,
  alt,
  className = "",
  size = "medium",
}) => {
  const { instance, accounts } = useMsal();
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [hasError, setHasError] = useState(false);

  useEffect(() => {
    if (!driveId) {
      setIsLoading(false);
      setHasError(true);
      return;
    }

    let cancelled = false;
    let createdObjectUrl: string | null = null;

    const fetchThumbnail = async () => {
      setIsLoading(true);
      setHasError(false);

      try {
        const account = accounts[0];
        if (!account) {
          setHasError(true);
          return;
        }

        let tokenResponse = null;
        try {
          tokenResponse = await instance.acquireTokenSilent({
            scopes: [FILES_READ_SCOPE],
            account,
          });
        } catch (err) {
          if (err instanceof InteractionRequiredAuthError) {
            setHasError(true);
            return;
          }
          throw err;
        }

        const url =
          `https://graph.microsoft.com/v1.0/drives/${driveId}/items/${fileId}` +
          `/thumbnails/0/${size}/content`;

        const response = await fetch(url, {
          headers: { Authorization: `Bearer ${tokenResponse.accessToken}` },
        });

        if (!response.ok) {
          setHasError(true);
          return;
        }

        const blob = await response.blob();
        if (cancelled) return;

        createdObjectUrl = URL.createObjectURL(blob);
        setObjectUrl(createdObjectUrl);
      } catch {
        if (!cancelled) setHasError(true);
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    };

    fetchThumbnail();

    return () => {
      cancelled = true;
      if (createdObjectUrl) {
        URL.revokeObjectURL(createdObjectUrl);
      }
    };
  }, [driveId, fileId, size, instance, accounts]);

  if (isLoading) {
    return (
      <div
        className={`animate-pulse bg-gray-200 rounded ${className}`}
        aria-label="Načítání náhledu..."
      />
    );
  }

  if (hasError || !objectUrl) {
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
      src={objectUrl}
      alt={alt}
      className={`object-cover rounded ${className}`}
    />
  );
};

export default PhotoThumbnail;
