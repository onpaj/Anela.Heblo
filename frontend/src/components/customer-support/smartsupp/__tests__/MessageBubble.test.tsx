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

  describe("links and attachments", () => {
    it("renders URLs as safe external links", () => {
      render(<MessageBubble message={{ ...baseMessage, content: "Viz https://anela.cz/produkt." }} />);
      const link = screen.getByRole("link", { name: "https://anela.cz/produkt" });
      expect(link).toHaveAttribute("href", "https://anela.cz/produkt");
      expect(link).toHaveAttribute("target", "_blank");
      expect(link).toHaveAttribute("rel", "noopener noreferrer");
    });

    it("renders asterisks literally without emphasis", () => {
      render(<MessageBubble message={{ ...baseMessage, content: "*tucne*" }} />);
      expect(screen.getByText("*tucne*")).toBeInTheDocument();
      expect(screen.queryByRole("emphasis")).not.toBeInTheDocument();
      expect(screen.queryByRole("strong")).not.toBeInTheDocument();
    });

    it("renders an upload-only message as a linked image thumbnail", () => {
      const message = {
        ...baseMessage,
        authorType: "visitor",
        content: null,
        attachments: [
          { type: "image", fileName: "foto.jpg", url: "https://files.smartsupp.com/foto.jpg", thumbUrl: "https://files.smartsupp.com/thumb.jpg" },
        ],
      } as unknown as MessageDto;
      render(<MessageBubble message={message} />);
      const img = screen.getByRole("img", { name: "foto.jpg" });
      expect(img).toHaveAttribute("src", "https://files.smartsupp.com/thumb.jpg");
      expect(screen.getByRole("link")).toHaveAttribute("href", "https://files.smartsupp.com/foto.jpg");
      expect(screen.queryByText(/\S/, { selector: "p" })).not.toBeInTheDocument();
    });

    it("renders a file attachment chip with name and size", () => {
      const message = {
        ...baseMessage,
        content: null,
        attachments: [{ type: "file", fileName: "objednavka.pdf", url: "https://files.smartsupp.com/o.pdf", size: 598612 }],
      } as unknown as MessageDto;
      render(<MessageBubble message={message} />);
      const link = screen.getByRole("link");
      expect(link).toHaveTextContent("objednavka.pdf");
      expect(link).toHaveTextContent("584 kB");
    });

    it("renders nothing for an attachment with an http url", () => {
      const message = {
        ...baseMessage,
        content: null,
        attachments: [{ type: "file", fileName: "x.pdf", url: "http://files.smartsupp.com/x.pdf" }],
      } as unknown as MessageDto;
      render(<MessageBubble message={message} />);
      expect(screen.queryByRole("link")).not.toBeInTheDocument();
    });

    it("allows blob previews only for optimistic messages", () => {
      const attachments = [{ type: "image", fileName: "a.png", url: "blob:http://localhost/1" }];
      const optimistic = { ...baseMessage, id: "optimistic-1", content: null, attachments } as unknown as MessageDto;
      const { rerender } = render(<MessageBubble message={optimistic} />);
      expect(screen.getByRole("img")).toBeInTheDocument();
      rerender(<MessageBubble message={{ ...optimistic, id: "m9" }} />);
      expect(screen.queryByRole("img")).not.toBeInTheDocument();
    });
  });
});
