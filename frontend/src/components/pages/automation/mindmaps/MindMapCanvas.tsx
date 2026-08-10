import React, { useEffect, useMemo, useState } from "react";
import { applyNodeChanges, Background, Controls, NodeChange, ReactFlow } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { useTheme } from "../../../../contexts/ThemeContext";
import { MindMapDocument } from "./mindMapDocument";
import { toFlowGraph, MindMapFlowNode as FlowNodeType } from "./mindMapFlow";
import MindMapFlowNode from "./MindMapFlowNode";

const nodeTypes = { mindMapNode: MindMapFlowNode };

// Only these change types are ones this canvas actually owns (live drag position,
// selection, measured dimensions). React Flow also emits `remove`/`add`/`replace`
// changes; mirroring `remove` in particular would let Backspace (React Flow's
// default `deleteKeyCode`, with nodes deletable by default) erase a selected node
// from `renderedNodes` while `localDoc` — and the side panel, and `isDirty` — never
// find out, so it silently reappears on the next document edit. `deleteKeyCode={null}`
// on `<ReactFlow>` makes the key inert at the source; this filter is the second,
// defense-in-depth layer. Real deletion has exactly one path: the side panel's
// "Smazat uzel", which goes through the document and correctly marks it dirty.
const MIRRORED_CHANGE_TYPES = new Set(["position", "select", "dimensions"]);

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
  const { theme } = useTheme();
  const { nodes, edges } = useMemo(() => toFlowGraph(doc), [doc]);

  // React Flow's `nodes` prop is controlled. Without a local mirror fed by
  // `onNodesChange`, every change React Flow emits during a drag is dropped —
  // the node stays frozen under the cursor for the whole gesture and only
  // snaps into its final place on release. Mirroring `nodes` into state and
  // applying changes via `applyNodeChanges` restores live drag feedback;
  // `onNodeDragStop` (below) still owns persisting the final position.
  const [renderedNodes, setRenderedNodes] = useState<FlowNodeType[]>(nodes);

  useEffect(() => {
    setRenderedNodes(nodes);
  }, [nodes]);

  const handleNodesChange = (changes: NodeChange[]) => {
    const mirrored = changes.filter((change) => MIRRORED_CHANGE_TYPES.has(change.type));
    setRenderedNodes((nds) => applyNodeChanges(mirrored, nds) as FlowNodeType[]);
  };

  const nodesWithSelection = useMemo(
    () => renderedNodes.map((n) => ({ ...n, selected: n.id === selectedNodeId, draggable: !isReadOnly })),
    [renderedNodes, selectedNodeId, isReadOnly],
  );

  return (
    <div data-testid="mindmap-canvas" className="h-full w-full">
      <ReactFlow
        nodes={nodesWithSelection}
        edges={edges}
        nodeTypes={nodeTypes}
        fitView
        nodesConnectable={false}
        colorMode={theme}
        deleteKeyCode={null}
        onNodesChange={handleNodesChange}
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
