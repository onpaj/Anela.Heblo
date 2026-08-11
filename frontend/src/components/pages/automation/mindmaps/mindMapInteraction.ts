import { createContext, useContext } from "react";

/**
 * Canvas-scoped interaction callbacks. Passing these through React Flow's node `data`
 * would rebuild every node's data object on each render of the page; a context keeps
 * `toFlowGraph` pure and the node data plain.
 */
export interface MindMapInteraction {
  /** Node currently being edited inline, or null. */
  editingNodeId: string | null;
  isReadOnly: boolean;
  isDark: boolean;
  onCommitEdit: (nodeId: string, title: string) => void;
  onCancelEdit: () => void;
  /** Enter pressed while editing: commit, then start a sibling. */
  onCommitAndAddSibling: (nodeId: string, title: string) => void;
  onToggleCollapsed: (nodeId: string) => void;
}

const NOOP_INTERACTION: MindMapInteraction = {
  editingNodeId: null,
  isReadOnly: true,
  isDark: false,
  onCommitEdit: () => {},
  onCancelEdit: () => {},
  onCommitAndAddSibling: () => {},
  onToggleCollapsed: () => {},
};

export const MindMapInteractionContext = createContext<MindMapInteraction>(NOOP_INTERACTION);

export function useMindMapInteraction(): MindMapInteraction {
  return useContext(MindMapInteractionContext);
}
