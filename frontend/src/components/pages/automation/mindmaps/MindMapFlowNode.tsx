import React from "react";
import { Handle, NodeProps, Position } from "@xyflow/react";
import { ChevronDown, ChevronRight, Lock } from "lucide-react";
import { MindMapFlowNode as FlowNodeType } from "./mindMapFlow";

const STATUS_ACCENT: Record<string, string> = {
  active: "border-l-sky-500",
  done: "border-l-emerald-500",
  blocked: "border-l-red-500",
  idea: "border-l-amber-400",
};

const MindMapFlowNode: React.FC<NodeProps<FlowNodeType>> = ({ data, selected }) => (
  <div
    data-testid="mindmap-node"
    className={`w-[220px] rounded-md border border-l-4 bg-white dark:bg-graphite-surface px-3 py-2 shadow-sm
      ${STATUS_ACCENT[data.status] ?? "border-l-neutral-300"}
      ${selected ? "ring-2 ring-sky-400" : ""}`}
  >
    <Handle type="target" position={Position.Left} className="!bg-neutral-400" />
    <div className="flex items-center gap-1">
      <span className={`truncate text-sm ${data.isRoot ? "font-semibold" : ""}`}>{data.title}</span>
      {data.isLocked && (
        <Lock data-testid="mindmap-node-lock" className="h-3.5 w-3.5 shrink-0 text-neutral-500" />
      )}
      {data.childCount > 0 &&
        (data.collapsed ? (
          <ChevronRight className="h-3.5 w-3.5 shrink-0 text-neutral-400" />
        ) : (
          <ChevronDown className="h-3.5 w-3.5 shrink-0 text-neutral-400" />
        ))}
    </div>
    {data.owner && <div className="truncate text-xs text-neutral-500">{data.owner}</div>}
    <Handle type="source" position={Position.Right} className="!bg-neutral-400" />
  </div>
);

export default MindMapFlowNode;
