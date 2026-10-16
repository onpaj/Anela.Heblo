import { useCallback, useEffect, useState } from "react";
import { validateNewFiles } from "../utils/attachmentPolicy";

interface UseComposerAttachmentsResult {
  files: File[];
  error: string | null;
  add: (incoming: File[]) => void;
  remove: (index: number) => void;
  clear: () => void;
}

export function useComposerAttachments(conversationId: string | null): UseComposerAttachmentsResult {
  const [files, setFiles] = useState<File[]>([]);
  const [error, setError] = useState<string | null>(null);

  const clear = useCallback(() => {
    setFiles([]);
    setError(null);
  }, []);

  useEffect(() => {
    clear();
  }, [conversationId, clear]);

  const add = useCallback(
    (incoming: File[]) => {
      if (incoming.length === 0) return;
      const { accepted, error: validationError } = validateNewFiles(files, incoming);
      setFiles([...files, ...accepted]);
      setError(validationError);
    },
    [files],
  );

  const remove = useCallback((index: number) => {
    setFiles((current) => current.filter((_, i) => i !== index));
    setError(null);
  }, []);

  return { files, error, add, remove, clear };
}
