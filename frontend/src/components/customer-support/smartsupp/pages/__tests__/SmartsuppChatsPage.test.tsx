import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import SmartsuppChatsPage from "../SmartsuppChatsPage";

jest.mock("../../../../../contexts/ToastContext", () => ({
  useToast: () => ({ showSuccess: jest.fn(), showError: jest.fn() }),
}));

const mockConversationItem = {
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
  assignedAgentIds: [],
  isServed: true,
  tags: [],
};

jest.mock("../../../../../api/hooks/useSmartsupp", () => ({
  useSmartsuppConversations: () => ({
    data: { success: true, items: [mockConversationItem], total: 1, page: 1, pageSize: 100 },
    isLoading: false,
  }),
  useSmartsuppConversation: () => ({ data: { messages: [] }, isLoading: false }),
  useTriggerSmartsuppSync: () => ({ mutate: jest.fn(), isPending: false }),
  SMARTSUPP_QUERY_KEYS: { conversations: () => [], conversation: () => [] },
}));

beforeAll(() => {
  Element.prototype.scrollIntoView = jest.fn();
});

beforeEach(() => {
  localStorage.clear();
});

const wrap = (ui: React.ReactNode) => {
  const qc = new QueryClient();
  return <QueryClientProvider client={qc}>{ui}</QueryClientProvider>;
};

describe("SmartsuppChatsPage", () => {
  it("renders the sync button", () => {
    render(wrap(<SmartsuppChatsPage />));
    expect(screen.getByRole("button", { name: /sync/i })).toBeInTheDocument();
  });

  it("renders an empty-state message when no conversation is selected", () => {
    render(wrap(<SmartsuppChatsPage />));
    expect(screen.getByText("Vyberte konverzaci")).toBeInTheDocument();
  });

  it("shows the conversation list by default", () => {
    render(wrap(<SmartsuppChatsPage />));
    expect(screen.getByText("Všechny konverzace")).toBeInTheDocument();
  });

  it("collapses the conversation list when its rail is clicked", () => {
    render(wrap(<SmartsuppChatsPage />));

    fireEvent.click(screen.getByTestId("collapsible-rail-left"));

    expect(screen.queryByText("Všechny konverzace")).not.toBeInTheDocument();
    expect(localStorage.getItem("smartsupp.listPanel.open")).toBe("false");
  });

  it("restores a collapsed conversation list from localStorage", () => {
    localStorage.setItem("smartsupp.listPanel.open", "false");
    render(wrap(<SmartsuppChatsPage />));
    expect(screen.queryByText("Všechny konverzace")).not.toBeInTheDocument();
  });

  it("keeps the contact panel collapsed by default once a conversation is selected", () => {
    render(wrap(<SmartsuppChatsPage />));

    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));

    expect(
      screen.getByRole("button", { name: "Rozbalit Detail kontaktu" }),
    ).toBeInTheDocument();
  });

  it("expands the contact panel and persists the state when its rail is clicked", () => {
    render(wrap(<SmartsuppChatsPage />));
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));

    fireEvent.click(screen.getByTestId("collapsible-rail-right"));

    expect(localStorage.getItem("smartsupp.contactPanel.open")).toBe("true");
  });

  it("shows navigation buttons in ConversationDetail after a conversation is selected", () => {
    render(wrap(<SmartsuppChatsPage />));
    expect(screen.queryByTestId("back-to-list-btn")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    expect(screen.getByTestId("back-to-list-btn")).toBeInTheDocument();
    expect(screen.getByTestId("open-contact-details-btn")).toBeInTheDocument();
  });

  it("clicking the info button shows the mobile contact subpage", () => {
    render(wrap(<SmartsuppChatsPage />));
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    expect(screen.queryByTestId("mobile-contact-subpage")).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId("open-contact-details-btn"));
    expect(screen.getByTestId("mobile-contact-subpage")).toBeInTheDocument();
  });

  it("the contact back button hides the mobile contact subpage", () => {
    render(wrap(<SmartsuppChatsPage />));
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    fireEvent.click(screen.getByTestId("open-contact-details-btn"));
    expect(screen.getByTestId("mobile-contact-subpage")).toBeInTheDocument();
    fireEvent.click(screen.getByTestId("back-to-chat-btn"));
    expect(screen.queryByTestId("mobile-contact-subpage")).not.toBeInTheDocument();
  });

  it("the chat back button does not show the mobile contact subpage", () => {
    render(wrap(<SmartsuppChatsPage />));
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    fireEvent.click(screen.getByTestId("back-to-list-btn"));
    expect(screen.queryByTestId("mobile-contact-subpage")).not.toBeInTheDocument();
  });
});
