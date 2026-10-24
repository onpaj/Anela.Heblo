import { useEffect, useState } from "react";
import { FileText, X } from "lucide-react";

interface PendingAttachmentsProps {
  files: File[];
  onRemove: (index: number) => void;
  disabled: boolean;
}

function ImagePreview({ file }: { file: File }) {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    const objectUrl = URL.createObjectURL(file);
    setUrl(objectUrl);
    return () => URL.revokeObjectURL(objectUrl);
  }, [file]);

  if (!url) return null;
  return <img src={url} alt="" className="h-8 w-8 rounded object-cover" />;
}

function PendingAttachments({ files, onRemove, disabled }: PendingAttachmentsProps) {
  if (files.length === 0) return null;

  return (
    <ul className="flex flex-wrap gap-2" data-testid="pending-attachments">
      {files.map((file, index) => (
        <li
          key={`${file.name}-${file.size}-${file.lastModified}-${index}`}
          className="flex items-center gap-2 rounded-md border border-gray-200 dark:border-graphite-border bg-gray-50 dark:bg-graphite-surface-2 px-2 py-1 text-xs text-gray-700 dark:text-graphite-text"
        >
          {file.type.startsWith("image/") ? (
            <ImagePreview file={file} />
          ) : (
            <FileText className="h-4 w-4" />
          )}
          <span className="max-w-[160px] truncate">{file.name}</span>
          <button
            type="button"
            onClick={() => onRemove(index)}
            disabled={disabled}
            aria-label={`Odebrat ${file.name}`}
            className="rounded p-0.5 text-gray-400 hover:bg-gray-200 dark:hover:bg-white/10 hover:text-gray-600 disabled:opacity-50"
          >
            <X className="h-3 w-3" />
          </button>
        </li>
      ))}
    </ul>
  );
}

export default PendingAttachments;
