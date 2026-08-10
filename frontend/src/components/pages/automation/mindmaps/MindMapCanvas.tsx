import React, { useMemo } from "react";
import { Background, Controls, ReactFlow } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { MindMapDocument } from "./mindMapDocument";
import { toFlowGraph } from "./mindMapFlow";
import MindMapFlowNode from "./MindMapFlowNode";

const nodeTypes = { mindMapNode: MindMapFlowNode };

interface MindMapCanvasProps {
  document: MindMapDocument;
  isReadOnly: boolean;
  selectedNodeId: string | null;
  onSelectNode: (nodeId: string | null) => void;
  onNodeDragStop: (nodeId: string, position: { x: number; y: number }) => void;
  onNodeDoubleClick: (nodeId: string) => void;
}

const MindMapCanvas: React.FC<MindMapCanvasProps> = ({
  document: doc,
  isReadOnly,
  selectedNodeId,
  onSelectNode,
  onNodeDragStop,
  onNodeDoubleClick,
}) => {
  const { nodes, edges } = useMemo(() => toFlowGraph(doc), [doc]);
  const nodesWithSelection = useMemo(
    () => nodes.map((n) => ({ ...n, selected: n.id === selectedNodeId, draggable: !isReadOnly })),
    [nodes, selectedNodeId, isReadOnly],
  );

  return (
    <div data-testid="mindmap-canvas" className="h-full w-full">
      <ReactFlow
        nodes={nodesWithSelection}
        edges={edges}
        nodeTypes={nodeTypes}
        fitView
        nodesConnectable={false}
        onNodeClick={(_e, node) => onSelectNode(node.id)}
        onPaneClick={() => onSelectNode(null)}
        onNodeDragStop={(_e, node) => onNodeDragStop(node.id, node.position)}
        onNodeDoubleClick={(_e, node) => onNodeDoubleClick(node.id)}
        proOptions={{ hideAttribution: true }}
      >
        <Background gap={16} />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  );
};

export default MindMapCanvas;
