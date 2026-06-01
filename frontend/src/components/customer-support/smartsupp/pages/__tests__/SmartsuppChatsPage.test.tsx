import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import SmartsuppChatsPage from "../SmartsuppChatsPage";

jest.mock("../../../../../api/hooks/useSmartsupp", () => ({
  useSmartsuppConversations: () => ({
    isFetching: false,
    data: {
      success: true,
      items: [
        {
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
          contactPhone: null,
          contactNote: null,
          contactTags: [],
          contactProperties: {},
          locationIp: null,
          variables: {},
          otherConversations: [],
        },
        {
          id: "c2",
          subject: null,
          contactName: "Pavel Novák",
          contactEmail: "pavel@example.com",
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
          contactPhone: null,
          contactNote: null,
          contactTags: [],
          contactProperties: {},
          locationIp: null,
          variables: {},
          otherConversations: [],
        },
      ],
      total: 2,
      page: 1,
      pageSize: 100,
    },
    isLoading: false,
  }),
  useSmartsuppConversation: () => ({ data: { messages: [] }, isLoading: false }),
  useSmartsuppShoptetInfo: () => ({ data: null, isLoading: false }),
  useSmartsuppVisitorInfo: () => ({ data: null, isLoading: false }),
  useCloseConversation: () => ({ mutate: jest.fn(), isPending: false }),
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
  it("renders the refresh button", () => {
    render(wrap(<SmartsuppChatsPage />));
    expect(screen.getByRole("button", { name: /obnovit/i })).toBeInTheDocument();
  });

  it("renders an empty-state message when no conversation is selected", () => {
    render(wrap(<SmartsuppChatsPage />));
    expect(screen.getByText("Vyberte konverzaci")).toBeInTheDocument();
  });

  it("shows the conversation list by default", () => {
    render(wrap(<SmartsuppChatsPage />));
    expect(screen.getByText("Všechny konverzace")).toBeInTheDocument();
  });

  it("persists the collapsed state to localStorage when the left rail is clicked", () => {
    render(wrap(<SmartsuppChatsPage />));

    fireEvent.click(screen.getByTestId("collapsible-rail-left"));

    expect(localStorage.getItem("smartsupp.listPanel.open")).toBe("false");
  });

  it("shows the conversation list in list view even when listPanel.open is stored as false", () => {
    localStorage.setItem("smartsupp.listPanel.open", "false");
    render(wrap(<SmartsuppChatsPage />));
    // Mobile (jsdom) always renders the list in list view regardless of listPanelOpen
    expect(screen.getByText("Všechny konverzace")).toBeInTheDocument();
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

  it("preserves draft text when switching between conversations", () => {
    render(wrap(<SmartsuppChatsPage />));

    // Select first conversation and type a draft
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    fireEvent.change(screen.getByPlaceholderText(/napište odpověď/i), {
      target: { value: "draft A" },
    });

    // Switch to second conversation — textarea should be empty
    fireEvent.click(screen.getByRole("button", { name: /Pavel Novák/ }));
    expect(
      (screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement).value,
    ).toBe("");

    // Switch back to first — draft must be restored
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    expect(
      (screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement).value,
    ).toBe("draft A");
  });
});
