import React from "react";
import { EdgeProps } from "@xyflow/react";
import { MindMapFlowEdge } from "./mindMapFlow";
import { NEUTRAL_BRANCH_COLOR } from "./mindMapTheme";

// Curved branch link, ported from the template: a cubic Bézier whose two control
// points sit on the horizontal midpoint between parent and child. That is what makes
// the link leave the parent horizontally and arrive at the child horizontally,
// instead of the orthogonal step React Flow's built-in `smoothstep` draws.
const ROOT_EDGE_WIDTH = 2.2;
const BRANCH_EDGE_WIDTH = 1.5;
const ROOT_EDGE_OPACITY = 0.85;
const BRANCH_EDGE_OPACITY = 0.42;

const MindMapCurvedEdge: React.FC<EdgeProps<MindMapFlowEdge>> = ({
  sourceX,
  sourceY,
  targetX,
  targetY,
  data,
}) => {
  const midX = (sourceX + targetX) / 2;
  const path = `M${sourceX},${sourceY} C${midX},${sourceY} ${midX},${targetY} ${targetX},${targetY}`;
  const isRootEdge = Boolean(data?.isRootEdge);

  return (
    <path
      data-testid="mindmap-edge"
      d={path}
      fill="none"
      stroke={data?.color ?? NEUTRAL_BRANCH_COLOR}
      strokeOpacity={isRootEdge ? ROOT_EDGE_OPACITY : BRANCH_EDGE_OPACITY}
      strokeWidth={isRootEdge ? ROOT_EDGE_WIDTH : BRANCH_EDGE_WIDTH}
      strokeLinecap="round"
    />
  );
};

export default MindMapCurvedEdge;
