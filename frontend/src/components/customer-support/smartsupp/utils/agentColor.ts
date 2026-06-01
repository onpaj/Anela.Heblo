export interface AgentColor {
  bg: string;
  text: string;
  ring: string;
}

export const AGENT_COLOR_PALETTE: readonly AgentColor[] = [
  { bg: "bg-blue-100", text: "text-blue-700", ring: "ring-blue-200" },
  { bg: "bg-emerald-100", text: "text-emerald-700", ring: "ring-emerald-200" },
  { bg: "bg-violet-100", text: "text-violet-700", ring: "ring-violet-200" },
  { bg: "bg-amber-100", text: "text-amber-700", ring: "ring-amber-200" },
  { bg: "bg-rose-100", text: "text-rose-700", ring: "ring-rose-200" },
  { bg: "bg-cyan-100", text: "text-cyan-700", ring: "ring-cyan-200" },
  { bg: "bg-fuchsia-100", text: "text-fuchsia-700", ring: "ring-fuchsia-200" },
  { bg: "bg-teal-100", text: "text-teal-700", ring: "ring-teal-200" },
] as const;

const NEUTRAL_AGENT_COLOR: AgentColor = {
  bg: "bg-slate-100",
  text: "text-slate-700",
  ring: "ring-slate-200",
};

export function getAgentColor(agentId?: string | null): AgentColor {
  if (!agentId) return NEUTRAL_AGENT_COLOR;
  let hash = 0;
  for (let i = 0; i < agentId.length; i++) {
    hash = (hash * 31 + agentId.charCodeAt(i)) >>> 0;
  }
  return AGENT_COLOR_PALETTE[hash % AGENT_COLOR_PALETTE.length];
}
