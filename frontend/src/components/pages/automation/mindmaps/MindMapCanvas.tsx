import React, { useCallback, useMemo, useRef } from "react";
import { Background, Controls, OnInit, ReactFlow } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { useTheme } from "../../../../contexts/ThemeContext";
import { MindMapDocument } from "./mindMapDocument";
import { MIND_MAP_EDGE_TYPE, MIND_MAP_NODE_TYPE, toFlowGraph } from "./mindMapFlow";
import MindMapFlowNode from "./MindMapFlowNode";
import MindMapCurvedEdge from "./MindMapCurvedEdge";
import { MindMapInteraction, MindMapInteractionContext } from "./mindMapInteraction";

const nodeTypes = { [MIND_MAP_NODE_TYPE]: MindMapFlowNode };
const edgeTypes = { [MIND_MAP_EDGE_TYPE]: MindMapCurvedEdge };

const FIT_VIEW_OPTIONS = { padding: 0.15, maxZoom: 1.15, minZoom: 0.15, duration: 200 };

export interface MindMapCanvasProps {
  document: MindMapDocument;
  isReadOnly: boolean;
  selectedNodeId: string | null;
  editingNodeId: string | null;
  onSelectNode: (nodeId: string | null) => void;
  onNodeDoubleClick: (nodeId: string) => void;
  onCommitEdit: (nodeId: string, title: string) => void;
  onCancelEdit: () => void;
  onCommitAndAddSibling: (nodeId: string, title: string) => void;
  onToggleCollapsed: (nodeId: string) => void;
  onKeyDown: (event: React.KeyboardEvent<HTMLElement>) => void;
  /** Hands the page a way to re-centre the map ("Vycentrovat" / after expand-all). */
  onFitViewReady?: (fitView: () => void) => void;
}

const MindMapCanvas: React.FC<MindMapCanvasProps> = ({
  document: doc,
  isReadOnly,
  selectedNodeId,
  editingNodeId,
  onSelectNode,
  onNodeDoubleClick,
  onCommitEdit,
  onCancelEdit,
  onCommitAndAddSibling,
  onToggleCollapsed,
  onKeyDown,
  onFitViewReady,
}) => {
  const { theme } = useTheme();
  const containerRef = useRef<HTMLDivElement>(null);

  // Positions come entirely from our own layout — there is no drag, so React Flow's
  // `nodes` prop can stay a pure projection of the document with no local mirror.
  const { nodes, edges } = useMemo(() => toFlowGraph(doc), [doc]);

  const nodesWithSelection = useMemo(
    () => nodes.map((n) => (n.id === selectedNodeId ? { ...n, selected: true } : n)),
    [nodes, selectedNodeId],
  );

  const interaction = useMemo<MindMapInteraction>(
    () => ({
      editingNodeId,
      isReadOnly,
      isDark: theme === "dark",
      onCommitEdit,
      onCancelEdit,
      onCommitAndAddSibling,
      onToggleCollapsed,
    }),
    [editingNodeId, isReadOnly, theme, onCommitEdit, onCancelEdit, onCommitAndAddSibling, onToggleCollapsed],
  );

  // Keyboard shortcuts are scoped to this container, so typing in the side panel is
  // never intercepted. Clicking anywhere on the canvas gives it focus.
  const focusCanvas = useCallback(() => containerRef.current?.focus(), []);

  const handleInit = useCallback<OnInit>(
    (instance) => {
      onFitViewReady?.(() => {
        void instance.fitView(FIT_VIEW_OPTIONS);
      });
    },
    [onFitViewReady],
  );

  return (
    <div
      data-testid="mindmap-canvas"
      ref={containerRef}
      tabIndex={0}
      onKeyDown={onKeyDown}
      className="h-full w-full outline-none"
    >
      <MindMapInteractionContext.Provider value={interaction}>
        <ReactFlow
          nodes={nodesWithSelection}
          edges={edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          fitView
          fitViewOptions={FIT_VIEW_OPTIONS}
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable
          colorMode={theme}
          deleteKeyCode={null}
          onInit={handleInit}
          onNodeClick={(_e, node) => {
            focusCanvas();
            onSelectNode(node.id);
          }}
          onPaneClick={() => {
            focusCanvas();
            onSelectNode(null);
          }}
          onNodeDoubleClick={(_e, node) => onNodeDoubleClick(node.id)}
          proOptions={{ hideAttribution: true }}
        >
          <Background gap={16} />
          <Controls showInteractive={false} />
        </ReactFlow>
      </MindMapInteractionContext.Provider>
    </div>
  );
};

export default MindMapCanvas;
