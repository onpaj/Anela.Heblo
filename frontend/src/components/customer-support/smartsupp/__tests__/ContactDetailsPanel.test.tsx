import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import ContactDetailsPanel from "../ContactDetailsPanel";
import { ConversationDto } from "../../../../api/hooks/useSmartsupp";

jest.mock("../../../../api/hooks/useSmartsupp", () => ({
  ...jest.requireActual("../../../../api/hooks/useSmartsupp"),
  useSmartsuppShoptetInfo: () => ({ data: null, isLoading: false }),
  useSmartsuppVisitorInfo: () => ({ data: null, isLoading: false }),
}));

const fullConv: ConversationDto = {
  id: "c1",
  subject: null,
  contactName: "Jana Nováková",
  contactEmail: "jana@example.com",
  contactAvatarUrl: null,
  status: "closed",
  isUnread: false,
  lastMessageAt: "2026-05-15T10:00:00Z",
  lastMessagePreview: null,
  createdAt: "2026-05-15T09:00:00Z",
  updatedAt: "2026-05-15T10:00:00Z",
  rating: 5,
  ratingText: "Skvělé!",
  closeType: "agent_closed",
  closedByAgentId: "a-petr",
  assignedAgentIds: ["a-petr", "a-anna"],
  channel: "chat",
  isServed: true,
  finishedAt: "2026-05-15T10:00:00Z",
  domain: "www.anela.cz",
  referer: "https://www.anela.cz/produkt/abc",
  locationCountry: "CZ",
  locationCity: "Praha",
  locationCode: "CZ-10",
  tags: ["doprava", "reklamace"],
  contactPhone: "+420 600 123 456",
  contactNote: "VIP zákazník",
  contactTags: ["vip", "stala"],
  contactProperties: { shoptet_guid: "abc-123", membership: "gold" },
  locationIp: "1.2.3.4",
  variables: { shoptet_shop: "anela", cart_value: "250" },
  otherConversations: [
    {
      id: "c2",
      status: "Resolved",
      lastMessageAt: "2026-05-10T10:00:00Z",
      lastMessagePreview: "Děkuji za pomoc",
      isUnread: false,
    },
  ],
};

describe("ContactDetailsPanel", () => {
  // — Existing tests (unchanged behavior) —

  it("renders the contact name and email", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText("Jana Nováková")).toBeInTheDocument();
    expect(screen.getByText("jana@example.com")).toBeInTheDocument();
  });

  it("renders the status pill", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByTestId("status-pill")).toHaveTextContent("Vyřešeno");
  });

  it("renders the rating with stars and text", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByTestId("rating")).toHaveTextContent("5");
    expect(screen.getByText(/Skvělé!/)).toBeInTheDocument();
  });

  it("renders the location (city, country)", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText(/Praha/)).toBeInTheDocument();
    expect(screen.getByText(/CZ/)).toBeInTheDocument();
  });

  it("renders conversation tags", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText("doprava")).toBeInTheDocument();
    expect(screen.getByText("reklamace")).toBeInTheDocument();
  });

  it("renders the assigned agents as agent badges", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    const badges = screen.getAllByTestId("agent-badge");
    expect(badges.length).toBeGreaterThanOrEqual(2);
  });

  it("renders the channel and domain", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText("chat")).toBeInTheDocument();
    expect(screen.getByText("www.anela.cz")).toBeInTheDocument();
  });

  it("omits the rating block when no rating is set", () => {
    render(<ContactDetailsPanel conversation={{ ...fullConv, rating: null, ratingText: null }} />);
    expect(screen.queryByTestId("rating")).not.toBeInTheDocument();
  });

  // — Phase 1 new tests —

  it("renders contact phone number", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText("+420 600 123 456")).toBeInTheDocument();
  });

  it("renders location IP", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText("1.2.3.4")).toBeInTheDocument();
  });

  it("renders flag emoji for Czech Republic", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText(/🇨🇿/)).toBeInTheDocument();
  });

  it("renders contact note in Poznámka section", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText("VIP zákazník")).toBeInTheDocument();
  });

  it("omits Poznámka section when contactNote is empty", () => {
    render(<ContactDetailsPanel conversation={{ ...fullConv, contactNote: null }} />);
    expect(screen.queryByText("VIP zákazník")).not.toBeInTheDocument();
  });

  it("renders contact tags", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText("vip")).toBeInTheDocument();
    expect(screen.getByText("stala")).toBeInTheDocument();
  });

  it("renders other conversations list with preview", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText(/Děkuji za pomoc/)).toBeInTheDocument();
  });

  it("calls onSelectConversation when other conversation is clicked", () => {
    const calls: string[] = [];
    render(
      <ContactDetailsPanel
        conversation={fullConv}
        onSelectConversation={(id) => calls.push(id)}
      />
    );
    fireEvent.click(screen.getByTestId("other-conversation-c2"));
    expect(calls).toContain("c2");
  });

  it("renders variables and contactProperties in Informace section", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.getByText("shoptet_guid")).toBeInTheDocument();
    expect(screen.getByText("abc-123")).toBeInTheDocument();
    expect(screen.getByText("shoptet_shop")).toBeInTheDocument();
    expect(screen.getByText("anela")).toBeInTheDocument();
  });

  it("omits Informace section when variables and contactProperties are empty", () => {
    render(
      <ContactDetailsPanel
        conversation={{ ...fullConv, variables: {}, contactProperties: {} }}
      />
    );
    expect(screen.queryByText("shoptet_guid")).not.toBeInTheDocument();
  });

  it("does not show visits/chats row when visitor info is unavailable", () => {
    render(<ContactDetailsPanel conversation={fullConv} />);
    expect(screen.queryByTestId("visits-count")).not.toBeInTheDocument();
    expect(screen.queryByTestId("chats-count")).not.toBeInTheDocument();
  });
});
