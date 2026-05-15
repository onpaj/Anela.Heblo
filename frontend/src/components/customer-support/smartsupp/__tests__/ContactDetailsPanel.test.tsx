import React from "react";
import { render, screen } from "@testing-library/react";
import ContactDetailsPanel from "../ContactDetailsPanel";
import { ConversationDto } from "../../../../api/hooks/useSmartsupp";

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
};

describe("ContactDetailsPanel", () => {
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

  it("renders all tags", () => {
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
});
