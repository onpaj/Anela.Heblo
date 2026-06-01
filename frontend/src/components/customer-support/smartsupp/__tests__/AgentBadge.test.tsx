import React from "react";
import { render, screen } from "@testing-library/react";
import AgentBadge from "../AgentBadge";

describe("AgentBadge", () => {
  it("renders the agent name as the label", () => {
    render(<AgentBadge agentId="a1" name="Petr Novák" />);
    expect(screen.getByText("Petr Novák")).toBeInTheDocument();
  });

  it("renders initials from the name when name is provided", () => {
    render(<AgentBadge agentId="a1" name="Petr Novák" />);
    expect(screen.getByText("PN")).toBeInTheDocument();
  });

  it("renders agent label and agentId-derived initials when name is null", () => {
    render(<AgentBadge agentId="a1" name={null} />);
    expect(screen.getByText("Agent")).toBeInTheDocument();
    expect(screen.getByText("A1")).toBeInTheDocument();
    expect(screen.queryByText("?")).not.toBeInTheDocument();
  });

  it("resolves name from agentNames when name prop is null", () => {
    render(<AgentBadge agentId="12" name={null} agentNames={{ "12": "Ondra" }} />);
    expect(screen.getByText("Ondra")).toBeInTheDocument();
    expect(screen.queryByText("Agent")).not.toBeInTheDocument();
  });

  it("prefers explicit name prop over agentNames lookup", () => {
    render(<AgentBadge agentId="12" name="Jana" agentNames={{ "12": "Ondra" }} />);
    expect(screen.getByText("Jana")).toBeInTheDocument();
    expect(screen.queryByText("Ondra")).not.toBeInTheDocument();
  });

  it("renders ? initials and Agent label when both name and agentId are absent", () => {
    render(<AgentBadge agentId={null} name={null} />);
    expect(screen.getByText("?")).toBeInTheDocument();
    expect(screen.getByText("Agent")).toBeInTheDocument();
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
