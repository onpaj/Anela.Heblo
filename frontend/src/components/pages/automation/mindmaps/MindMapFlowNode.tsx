import React, { useEffect, useRef } from "react";
import { Handle, NodeProps, Position } from "@xyflow/react";
import { ChevronDown, Lock, StickyNote } from "lucide-react";
import { HANDLE_IDS, MindMapFlowNode as FlowNodeType } from "./mindMapFlow";
import { useMindMapInteraction } from "./mindMapInteraction";
import {
  MIND_MAP_FONT_FAMILY,
  TIER_METRICS,
  themedBranchColor,
} from "./mindMapTheme";

// Root card is inverted against the page: near-black on the light theme, near-white on
// the dark one. Either way it reads as "this is the centre" without needing a colour.
const ROOT_LIGHT = { background: "#2B2724", color: "#FFFFFF", border: "#2B2724" };
const ROOT_DARK = { background: "#EDE7DF", color: "#2B2724", border: "#EDE7DF" };

/** Leaf borders are drawn at partial alpha so the card reads lighter than its branch. */
const LEAF_BORDER_ALPHA = "99";
const IDEA_BORDER_ALPHA = "66";

const HIDDEN_HANDLE_STYLE: React.CSSProperties = {
  opacity: 0,
  width: 1,
  height: 1,
  minWidth: 1,
  minHeight: 1,
  border: "none",
  pointerEvents: "none",
};

const MindMapFlowNode: React.FC<NodeProps<FlowNodeType>> = ({ id, data, selected }) => {
  const { editingNodeId, isReadOnly, isDark, onCommitEdit, onCancelEdit, onCommitAndAddSibling, onToggleCollapsed } =
    useMindMapInteraction();
  const editorRef = useRef<HTMLDivElement>(null);
  const isEditing = editingNodeId === id;

  useEffect(() => {
    const editor = editorRef.current;
    if (!isEditing || !editor) return;
    editor.textContent = data.title;
    editor.focus();
    const range = window.document.createRange();
    range.selectNodeContents(editor);
    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);
  }, [isEditing, data.title]);

  const metrics = TIER_METRICS[data.tier];
  const branchColor = themedBranchColor(data.color, isDark);
  const isIdea = data.status === "idea";
  const isDone = data.status === "done";
  const isBlocked = data.status === "blocked";

  const cardStyle: React.CSSProperties = {
    width: data.width,
    minHeight: data.height,
    fontFamily: MIND_MAP_FONT_FAMILY,
    fontSize: metrics.fontSize,
    fontWeight: metrics.fontWeight,
    lineHeight: `${metrics.lineHeight}px`,
    padding: `${metrics.paddingY}px ${metrics.paddingX}px`,
    borderWidth: metrics.borderWidth,
    borderStyle: isIdea ? "dashed" : "solid",
    borderRadius: data.tier === "root" ? 14 : 9,
    fontStyle: isIdea ? "italic" : "normal",
  };

  if (data.tier === "root") {
    const palette = isDark ? ROOT_DARK : ROOT_LIGHT;
    cardStyle.background = palette.background;
    cardStyle.color = palette.color;
    cardStyle.borderColor = palette.border;
  } else if (data.tier === "branch") {
    cardStyle.borderColor = branchColor;
    cardStyle.color = branchColor;
  } else {
    cardStyle.borderColor = `${branchColor}${isIdea ? IDEA_BORDER_ALPHA : LEAF_BORDER_ALPHA}`;
  }

  const handleKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    // The canvas-level shortcut handler must never see keys typed into a card.
    event.stopPropagation();
    if (event.key === "Escape") {
      event.preventDefault();
      onCancelEdit();
      return;
    }
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      onCommitAndAddSibling(id, editorRef.current?.innerText ?? data.title);
    }
  };

  return (
    <div
      data-testid="mindmap-node"
      data-tier={data.tier}
      className={`bg-white dark:bg-graphite-surface shadow-sm transition-shadow hover:shadow-md ${
        selected ? "ring-2 ring-offset-1 ring-sky-500 dark:ring-offset-graphite-bg" : ""
      } ${isDone ? "opacity-60" : ""}`}
      style={cardStyle}
    >
      <Handle id={HANDLE_IDS.targetLeft} type="target" position={Position.Left} style={HIDDEN_HANDLE_STYLE} />
      <Handle id={HANDLE_IDS.targetRight} type="target" position={Position.Right} style={HIDDEN_HANDLE_STYLE} />

      {isEditing ? (
        <div
          ref={editorRef}
          data-testid="mindmap-node-editor"
          role="textbox"
          tabIndex={0}
          aria-label="Text uzlu"
          className="nodrag nopan outline-none"
          style={{ whiteSpace: "pre-wrap", cursor: "text", userSelect: "text" }}
          contentEditable
          suppressContentEditableWarning
          onKeyDown={handleKeyDown}
          onBlur={() => onCommitEdit(id, editorRef.current?.innerText ?? data.title)}
        />
      ) : (
        <span style={{ whiteSpace: "pre" }}>{data.lines.join("\n")}</span>
      )}

      {isBlocked && (
        <span
          data-testid="mindmap-node-blocked"
          title="Blokováno"
          className="ml-2 inline-block h-2 w-2 rounded-full bg-red-500 align-middle"
        />
      )}

      {data.ownerBadge && (
        <span
          data-testid="mindmap-node-owner"
          className="ml-2 inline-block rounded-[5px] bg-black/[.06] px-1.5 py-0.5 text-[10.5px] font-bold not-italic tracking-wide text-neutral-500 dark:bg-white/10 dark:text-graphite-muted"
        >
          {data.ownerBadge}
        </span>
      )}

      {data.isLocked && (
        <Lock
          data-testid="mindmap-node-lock"
          className="ml-1 inline-block h-3.5 w-3.5 shrink-0 align-text-bottom text-neutral-400"
        />
      )}

      {data.hasNotes && (
        <StickyNote
          data-testid="mindmap-node-notes"
          className="ml-1 inline-block h-3.5 w-3.5 shrink-0 align-text-bottom text-neutral-400"
        />
      )}

      {data.childCount > 0 &&
        (data.collapsed ? (
          <button
            type="button"
            data-testid="mindmap-node-count"
            title="Rozbalit větev"
            disabled={isReadOnly}
            onClick={(e) => {
              e.stopPropagation();
              onToggleCollapsed(id);
            }}
            className="nodrag ml-2 inline-block rounded-full px-1.5 text-[11px] font-bold not-italic text-white disabled:cursor-not-allowed"
            style={{ background: branchColor }}
          >
            {data.childCount}
          </button>
        ) : (
          <button
            type="button"
            data-testid="mindmap-node-collapse"
            title="Sbalit větev"
            disabled={isReadOnly}
            onClick={(e) => {
              e.stopPropagation();
              onToggleCollapsed(id);
            }}
            className="nodrag ml-1 inline-block align-text-bottom text-neutral-400 hover:text-neutral-600 disabled:cursor-not-allowed dark:hover:text-graphite-text"
          >
            <ChevronDown className="h-3.5 w-3.5" />
          </button>
        ))}

      <Handle id={HANDLE_IDS.sourceLeft} type="source" position={Position.Left} style={HIDDEN_HANDLE_STYLE} />
      <Handle id={HANDLE_IDS.sourceRight} type="source" position={Position.Right} style={HIDDEN_HANDLE_STYLE} />
    </div>
  );
};

export default MindMapFlowNode;
