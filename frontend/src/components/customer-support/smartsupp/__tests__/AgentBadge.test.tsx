import React from "react";
import { render, screen } from "@testing-library/react";
import AgentBadge from "../AgentBadge";

describe("AgentBadge", () => {
  it("renders the agent name", () => {
    render(<AgentBadge agentId="a1" name="Petr Novák" />);
    expect(screen.getByText("Petr Novák")).toBeInTheDocument();
  });

  it("renders the initials when no name is provided", () => {
    render(<AgentBadge agentId="a1" name={null} />);
    expect(screen.getAllByText("?").length).toBeGreaterThanOrEqual(1);
  });

  it("uses the same color for the same agentId across renders", () => {
    const { rerender } = render(<AgentBadge agentId="a1" name="Petr" />);
    const first = screen.getByTestId("agent-badge").className;
    rerender(<AgentBadge agentId="a1" name="Petr" />);
    const second = screen.getByTestId("agent-badge").className;
    expect(first).toBe(second);
  });

  it("renders different colors for different agentIds (sanity)", () => {
    const { rerender } = render(<AgentBadge agentId="aaa" name="A" />);
    const colorA = screen.getByTestId("agent-badge").className;
    rerender(<AgentBadge agentId="zzz" name="Z" />);
    const colorZ = screen.getByTestId("agent-badge").className;
    expect(colorA).not.toBe(colorZ);
  });
});
