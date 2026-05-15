import React from "react";
import { render, screen } from "@testing-library/react";
import MessageBubble from "../MessageBubble";
import { MessageDto } from "../../../../api/hooks/useSmartsupp";

const baseMessage: MessageDto = {
  id: "m1",
  authorType: "agent",
  authorName: "Petr Novák",
  content: "Dobrý den, jak mohu pomoci?",
  createdAt: "2026-05-15T10:00:00Z",
  agentId: "a-petr",
  subType: "agent",
  deliveryStatus: "delivered",
  deliveredAt: "2026-05-15T10:00:01Z",
  responseTime: 120,
  isFirstReply: true,
  pageUrl: null,
};

describe("MessageBubble", () => {
  it("renders an agent bubble on the right with agent badge + delivery icon", () => {
    render(<MessageBubble message={baseMessage} />);
    expect(screen.getByText("Dobrý den, jak mohu pomoci?")).toBeInTheDocument();
    expect(screen.getByTestId("agent-badge")).toHaveTextContent("Petr Novák");
    expect(screen.getByTestId("delivery-icon")).toHaveAttribute("title", "Doručeno");
  });

  it("renders the response-time pill only on the first agent reply", () => {
    render(<MessageBubble message={baseMessage} />);
    expect(screen.getByText("Odpověď za 2 m")).toBeInTheDocument();
  });

  it("does not render the response-time pill on follow-up agent replies", () => {
    render(<MessageBubble message={{ ...baseMessage, isFirstReply: false }} />);
    expect(screen.queryByText(/Odpověď za/)).not.toBeInTheDocument();
  });

  it("renders a visitor bubble on the left without agent badge", () => {
    render(
      <MessageBubble
        message={{ ...baseMessage, authorType: "visitor", agentId: null, authorName: "Jana", deliveryStatus: null }}
      />
    );
    expect(screen.queryByTestId("agent-badge")).not.toBeInTheDocument();
    expect(screen.queryByTestId("delivery-icon")).not.toBeInTheDocument();
  });

  it("renders system events as a centered slate pill (not a bubble)", () => {
    render(
      <MessageBubble
        message={{ ...baseMessage, authorType: "system", subType: "system", content: "Konverzace přiřazena agentovi" }}
      />
    );
    const pill = screen.getByTestId("system-event");
    expect(pill).toHaveTextContent("Konverzace přiřazena agentovi");
    expect(pill).toHaveClass("bg-slate-100");
  });

  it("renders a bot message as a centered italic line", () => {
    render(
      <MessageBubble
        message={{ ...baseMessage, authorType: "bot", agentId: null, content: "Automatická odpověď" }}
      />
    );
    expect(screen.getByText("Automatická odpověď")).toHaveClass("italic");
  });
});
