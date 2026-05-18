import React from "react";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import ConversationDetail from "../ConversationDetail";
import { ConversationDto } from "../../../../api/hooks/useSmartsupp";

jest.mock("../../../../api/hooks/useSmartsupp", () => {
  const actual = jest.requireActual("../../../../api/hooks/useSmartsupp");
  return {
    ...actual,
    useSmartsuppConversation: () => ({
      data: {
        success: true,
        conversation: null,
        messages: [
          {
            id: "m1",
            authorType: "visitor",
            authorName: "Jana",
            content: "Dotaz",
            createdAt: new Date().toISOString(),
            isFirstReply: false,
          },
        ],
      },
      isLoading: false,
    }),
  };
});

const conv: ConversationDto = {
  id: "c1",
  subject: null,
  contactName: "Jana Nováková",
  contactEmail: "jana@example.com",
  contactAvatarUrl: null,
  status: "open",
  isUnread: false,
  lastMessageAt: new Date().toISOString(),
  lastMessagePreview: null,
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
  assignedAgentIds: ["a-petr"],
  isServed: true,
  tags: [],
};

const wrap = (ui: React.ReactNode) => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{ui}</QueryClientProvider>;
};

beforeAll(() => {
  Element.prototype.scrollIntoView = jest.fn();
});

describe("ConversationDetail", () => {
  it("renders the contact name in the header", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.getByText("Jana Nováková")).toBeInTheDocument();
  });

  it("renders the status pill in the header", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.getByTestId("status-pill")).toHaveTextContent("Aktivní");
  });

  it("renders the assigned agent badges in the header", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.getAllByTestId("agent-badge").length).toBeGreaterThanOrEqual(1);
  });

  it("renders a day separator before the message group", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.getByTestId("day-separator")).toBeInTheDocument();
  });

  it("renders the composer at the bottom", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.getByPlaceholderText("Napište odpověď...")).toBeInTheDocument();
  });

  it("pre-fills the composer textarea with initialDraft", () => {
    render(
      wrap(
        <ConversationDetail
          conversationId="c1"
          conversation={conv}
          initialDraft="Předvyplněný text"
          onDraftChange={jest.fn()}
        />,
      ),
    );
    const textarea = screen.getByPlaceholderText("Napište odpověď...") as HTMLTextAreaElement;
    expect(textarea.value).toBe("Předvyplněný text");
  });
});
