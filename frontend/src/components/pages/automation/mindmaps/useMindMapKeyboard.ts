import React, { useCallback } from "react";
import { childrenOf, MindMapDocument } from "./mindMapDocument";
import { branchSideOf } from "./mindMapLayout";

/**
 * The template's keyboard model, scoped to the canvas element rather than the
 * document. Scoping matters: Enter/Tab/Space/Backspace are ordinary typing keys, and
 * a document-level listener would swallow them while the user is filling in the side
 * panel's title, notes or owner fields.
 */
export interface MindMapKeyboardHandlers {
  onSelect: (nodeId: string | null) => void;
  onStartEdit: (nodeId: string) => void;
  onAddSibling: (nodeId: string) => void;
  onAddChild: (nodeId: string) => void;
  onDelete: (nodeId: string) => void;
  onToggleCollapsed: (nodeId: string) => void;
  onIndent: (nodeId: string) => void;
  onOutdent: (nodeId: string) => void;
  onMove: (nodeId: string, delta: number) => void;
  onUndo: () => void;
}

export interface MindMapKeyboardOptions {
  document: MindMapDocument | null;
  selectedNodeId: string | null;
  /** False while the map is locked by a background update, or a node is being edited. */
  isEnabled: boolean;
  handlers: MindMapKeyboardHandlers;
}

function firstVisibleChild(doc: MindMapDocument, nodeId: string): string | null {
  const children = childrenOf(doc, nodeId);
  return children.length > 0 ? children[0].id : null;
}

function siblingAt(doc: MindMapDocument, nodeId: string, delta: number): string | null {
  const node = doc.nodes.find((n) => n.id === nodeId);
  if (!node || node.parentId === null) return null;
  const siblings = childrenOf(doc, node.parentId);
  const index = siblings.findIndex((n) => n.id === nodeId);
  const target = index + delta;
  return target >= 0 && target < siblings.length ? siblings[target].id : null;
}

/** Root's arrows pick the first branch on the matching side; other nodes walk in/out. */
function navigateHorizontally(
  doc: MindMapDocument,
  nodeId: string,
  key: "ArrowLeft" | "ArrowRight",
): string | null {
  if (nodeId === doc.rootNodeId) {
    const branches = childrenOf(doc, doc.rootNodeId);
    const wanted = key === "ArrowRight" ? 1 : -1;
    return branches.find((b) => branchSideOf(doc, b.id) === wanted)?.id ?? null;
  }

  const node = doc.nodes.find((n) => n.id === nodeId);
  if (!node) return null;

  const side = branchSideOf(doc, nodeId);
  const isOutward = side === -1 ? key === "ArrowRight" : key === "ArrowLeft";
  if (isOutward) return node.parentId;
  return node.collapsed ? null : firstVisibleChild(doc, nodeId);
}

export function useMindMapKeyboard({
  document: doc,
  selectedNodeId,
  isEnabled,
  handlers,
}: MindMapKeyboardOptions): (event: React.KeyboardEvent<HTMLElement>) => void {
  return useCallback(
    (event: React.KeyboardEvent<HTMLElement>) => {
      if (!doc || !isEnabled) return;
      // Inline editing and any focused form control own their own keys.
      const target = event.target as HTMLElement | null;
      if (target?.isContentEditable) return;
      if (target && ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName)) return;

      const isMeta = event.metaKey || event.ctrlKey;

      if (isMeta && event.key.toLowerCase() === "z") {
        event.preventDefault();
        handlers.onUndo();
        return;
      }

      const selected = selectedNodeId;
      if (!selected) {
        // Nothing selected yet: any arrow key drops the user onto the root.
        if (event.key.startsWith("Arrow")) {
          event.preventDefault();
          handlers.onSelect(doc.rootNodeId);
        }
        return;
      }

      if (isMeta) {
        switch (event.key) {
          case "ArrowRight":
            event.preventDefault();
            return handlers.onIndent(selected);
          case "ArrowLeft":
            event.preventDefault();
            return handlers.onOutdent(selected);
          case "ArrowUp":
            event.preventDefault();
            return handlers.onMove(selected, -1);
          case "ArrowDown":
            event.preventDefault();
            return handlers.onMove(selected, 1);
          default:
            return;
        }
      }

      switch (event.key) {
        case "ArrowUp":
        case "ArrowDown": {
          event.preventDefault();
          const next = siblingAt(doc, selected, event.key === "ArrowUp" ? -1 : 1);
          if (next) handlers.onSelect(next);
          return;
        }
        case "ArrowLeft":
        case "ArrowRight": {
          event.preventDefault();
          const next = navigateHorizontally(doc, selected, event.key);
          if (next) handlers.onSelect(next);
          return;
        }
        case "Enter":
          event.preventDefault();
          return handlers.onAddSibling(selected);
        case "Tab":
          event.preventDefault();
          return handlers.onAddChild(selected);
        case " ":
          event.preventDefault();
          return handlers.onToggleCollapsed(selected);
        case "Backspace":
        case "Delete":
          event.preventDefault();
          return handlers.onDelete(selected);
        case "F2":
          event.preventDefault();
          return handlers.onStartEdit(selected);
        default:
      }
    },
    [doc, selectedNodeId, isEnabled, handlers],
  );
}
