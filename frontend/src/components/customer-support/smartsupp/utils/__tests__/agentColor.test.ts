import { getAgentColor, AGENT_COLOR_PALETTE } from "../agentColor";

describe("agentColor", () => {
  it("returns the same palette entry for the same agentId across calls", () => {
    const first = getAgentColor("agent-42");
    const second = getAgentColor("agent-42");
    expect(first).toEqual(second);
  });

  it("returns an entry from the fixed palette", () => {
    const color = getAgentColor("agent-x");
    expect(AGENT_COLOR_PALETTE).toContainEqual(color);
  });

  it("returns the neutral fallback for null/empty agentId", () => {
    expect(getAgentColor(null)).toEqual({ bg: "bg-slate-100", text: "text-slate-700", ring: "ring-slate-200" });
    expect(getAgentColor("")).toEqual({ bg: "bg-slate-100", text: "text-slate-700", ring: "ring-slate-200" });
  });

  it("distributes agentIds across more than one palette entry", () => {
    const seen = new Set(
      ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"].map((id) => getAgentColor(id).bg)
    );
    expect(seen.size).toBeGreaterThan(1);
  });
});
