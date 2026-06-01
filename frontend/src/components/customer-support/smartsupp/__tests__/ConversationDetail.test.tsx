import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import ConversationDetail from "../ConversationDetail";
import {
  ConversationDto,
  useSmartsuppConversation,
  useCloseConversation,
} from "../../../../api/hooks/useSmartsupp";

jest.mock("react-hot-toast", () => ({
  toast: {
    success: jest.fn(),
    error: jest.fn(),
  },
}));

jest.mock("../../../../api/hooks/useSmartsupp", () => {
  const actual = jest.requireActual("../../../../api/hooks/useSmartsupp");
  return {
    ...actual,
    useSmartsuppConversation: jest.fn(),
    useCloseConversation: jest.fn(),
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

const defaultMessages = [
  {
    id: "m1",
    authorType: "visitor",
    authorName: "Jana",
    content: "Dotaz",
    createdAt: new Date().toISOString(),
    isFirstReply: false,
  },
];

const wrap = (ui: React.ReactNode) => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{ui}</QueryClientProvider>;
};

beforeAll(() => {
  Element.prototype.scrollIntoView = jest.fn();
});

beforeEach(() => {
  jest.mocked(useSmartsuppConversation).mockReturnValue({
    data: { success: true, conversation: null, messages: defaultMessages, agentNames: {} },
    isLoading: false,
  } as ReturnType<typeof useSmartsuppConversation>);

  jest.mocked(useCloseConversation).mockReturnValue({
    mutate: jest.fn(),
    isPending: false,
  } as unknown as ReturnType<typeof useCloseConversation>);
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

  it("renders a back button when onBack is provided", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} onBack={jest.fn()} />));
    expect(screen.getByTestId("back-to-list-btn")).toBeInTheDocument();
  });

  it("does not render a back button when onBack is omitted", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.queryByTestId("back-to-list-btn")).not.toBeInTheDocument();
  });

  it("calls onBack when the back button is clicked", () => {
    const onBack = jest.fn();
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} onBack={onBack} />));
    fireEvent.click(screen.getByTestId("back-to-list-btn"));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it("renders an info button when onOpenContactDetails is provided", () => {
    render(
      wrap(
        <ConversationDetail
          conversationId="c1"
          conversation={conv}
          onOpenContactDetails={jest.fn()}
        />,
      ),
    );
    expect(screen.getByTestId("open-contact-details-btn")).toBeInTheDocument();
  });

  it("does not render an info button when onOpenContactDetails is omitted", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.queryByTestId("open-contact-details-btn")).not.toBeInTheDocument();
  });

  it("calls onOpenContactDetails when the info button is clicked", () => {
    const onOpenContactDetails = jest.fn();
    render(
      wrap(
        <ConversationDetail
          conversationId="c1"
          conversation={conv}
          onOpenContactDetails={onOpenContactDetails}
        />,
      ),
    );
    fireEvent.click(screen.getByTestId("open-contact-details-btn"));
    expect(onOpenContactDetails).toHaveBeenCalledTimes(1);
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

  it("renders a close button when conversation status is 'open'", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.getByTestId("close-conversation-btn")).toBeInTheDocument();
  });

  it("does not render a close button when conversation status is not 'open'", () => {
    const resolvedConv = { ...conv, status: "resolved" };
    render(wrap(<ConversationDetail conversationId="c1" conversation={resolvedConv} />));
    expect(screen.queryByTestId("close-conversation-btn")).not.toBeInTheDocument();
  });

  it("calls mutate with conversationId when the close button is clicked", () => {
    const mockMutate = jest.fn();
    jest.mocked(useCloseConversation).mockReturnValue({
      mutate: mockMutate,
      isPending: false,
    } as unknown as ReturnType<typeof useCloseConversation>);
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    fireEvent.click(screen.getByTestId("close-conversation-btn"));
    expect(mockMutate).toHaveBeenCalledWith(
      "c1",
      expect.objectContaining({ onSuccess: expect.any(Function), onError: expect.any(Function) }),
    );
  });

  it("hides the close button when detail query returns resolved status even if prop status is open", () => {
    jest.mocked(useSmartsuppConversation).mockReturnValue({
      data: {
        success: true,
        conversation: { ...conv, status: "resolved" },
        messages: [],
        agentNames: {},
      },
      isLoading: false,
    } as ReturnType<typeof useSmartsuppConversation>);
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.queryByTestId("close-conversation-btn")).not.toBeInTheDocument();
  });

  it("shows resolved status pill when detail query returns resolved status", () => {
    jest.mocked(useSmartsuppConversation).mockReturnValue({
      data: {
        success: true,
        conversation: { ...conv, status: "resolved" },
        messages: [],
        agentNames: {},
      },
      isLoading: false,
    } as ReturnType<typeof useSmartsuppConversation>);
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.getByTestId("status-pill")).not.toHaveTextContent("Aktivní");
  });
});
