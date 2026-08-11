import { useCallback, useRef, useState } from "react";
import { MindMapDocument } from "./mindMapDocument";

/** Matches the template's history depth. */
export const UNDO_DEPTH = 60;

export interface MindMapUndo {
  canUndo: boolean;
  /** Record the pre-edit document. Call BEFORE applying a mutation. */
  push: (doc: MindMapDocument) => void;
  /** Returns the previous document, or null when the history is empty. */
  pop: () => MindMapDocument | null;
  clear: () => void;
}

/**
 * Undo history for the mind map editor. Snapshots are structural clones taken before
 * each edit, so an undone document shares nothing with the live one.
 */
export function useMindMapUndo(): MindMapUndo {
  const stackRef = useRef<MindMapDocument[]>([]);
  const [canUndo, setCanUndo] = useState(false);

  const push = useCallback((doc: MindMapDocument) => {
    const snapshot = JSON.parse(JSON.stringify(doc)) as MindMapDocument;
    stackRef.current = [...stackRef.current, snapshot].slice(-UNDO_DEPTH);
    setCanUndo(true);
  }, []);

  const pop = useCallback((): MindMapDocument | null => {
    const stack = stackRef.current;
    if (stack.length === 0) return null;
    const previous = stack[stack.length - 1];
    stackRef.current = stack.slice(0, -1);
    setCanUndo(stackRef.current.length > 0);
    return previous;
  }, []);

  const clear = useCallback(() => {
    stackRef.current = [];
    setCanUndo(false);
  }, []);

  return { canUndo, push, pop, clear };
}
