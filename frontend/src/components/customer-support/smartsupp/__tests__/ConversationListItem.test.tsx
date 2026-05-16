import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import ConversationListItem from "../ConversationListItem";
import { ConversationDto } from "../../../../api/hooks/useSmartsupp";

const baseConv: ConversationDto = {
  id: "c1",
  subject: null,
  contactName: "Jana Nováková",
  contactEmail: "jana@example.com",
  contactAvatarUrl: null,
  status: "open",
  isUnread: false,
  lastMessageAt: new Date().toISOString(),
  lastMessagePreview: "Mám dotaz na produkt.",
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
  assignedAgentIds: [],
  isServed: false,
  tags: [],
};

describe("ConversationListItem", () => {
  it("renders the contact name and last message preview", () => {
    render(<ConversationListItem conversation={baseConv} isSelected={false} onClick={() => {}} />);
    expect(screen.getByText("Jana Nováková")).toBeInTheDocument();
    expect(screen.getByText("Mám dotaz na produkt.")).toBeInTheDocument();
  });

  it("renders the status pill matching the conversation status", () => {
    render(<ConversationListItem conversation={baseConv} isSelected={false} onClick={() => {}} />);
    expect(screen.getByTestId("status-pill")).toHaveTextContent("Aktivní");
  });

  it("adds a left accent border when the conversation is unread", () => {
    render(
      <ConversationListItem conversation={{ ...baseConv, isUnread: true }} isSelected={false} onClick={() => {}} />
    );
    const button = screen.getByRole("button");
    expect(button.className).toMatch(/border-l-4 border-l-blue-500/);
  });

  it("does NOT render the left accent border when unread is false", () => {
    render(<ConversationListItem conversation={baseConv} isSelected={false} onClick={() => {}} />);
    const button = screen.getByRole("button");
    expect(button.className).not.toMatch(/border-l-blue-500/);
  });

  it("calls onClick when clicked", () => {
    const onClick = jest.fn();
    render(<ConversationListItem conversation={baseConv} isSelected={false} onClick={onClick} />);
    fireEvent.click(screen.getByRole("button"));
    expect(onClick).toHaveBeenCalled();
  });

  it("renders bold name when unread", () => {
    render(
      <ConversationListItem conversation={{ ...baseConv, isUnread: true }} isSelected={false} onClick={() => {}} />
    );
    expect(screen.getByText("Jana Nováková").className).toMatch(/font-semibold/);
  });
});
