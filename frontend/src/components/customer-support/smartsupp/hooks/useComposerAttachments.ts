import { useCallback, useEffect, useRef, useState } from "react";
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
  const filesRef = useRef<File[]>([]);

  const updateFiles = useCallback((next: File[]) => {
    filesRef.current = next;
    setFiles(next);
  }, []);

  const clear = useCallback(() => {
    updateFiles([]);
    setError(null);
  }, [updateFiles]);

  useEffect(() => {
    clear();
  }, [conversationId, clear]);

  const add = useCallback(
    (incoming: File[]) => {
      if (incoming.length === 0) return;
      const { accepted, error: validationError } = validateNewFiles(filesRef.current, incoming);
      updateFiles([...filesRef.current, ...accepted]);
      setError(validationError);
    },
    [updateFiles],
  );

  const remove = useCallback((index: number) => {
    updateFiles(filesRef.current.filter((_, i) => i !== index));
    setError(null);
  }, [updateFiles]);

  return { files, error, add, remove, clear };
}
