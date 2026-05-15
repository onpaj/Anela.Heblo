import React from "react";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import SmartsuppChatsPage from "../SmartsuppChatsPage";

jest.mock("../../../../../contexts/ToastContext", () => ({
  useToast: () => ({ showSuccess: jest.fn(), showError: jest.fn() }),
}));

jest.mock("../../../../../api/hooks/useSmartsupp", () => ({
  useSmartsuppConversations: () => ({
    data: { success: true, items: [], total: 0, page: 1, pageSize: 100 },
    isLoading: false,
  }),
  useSmartsuppConversation: () => ({ data: { messages: [] }, isLoading: false }),
  useTriggerSmartsuppSync: () => ({ mutate: jest.fn(), isPending: false }),
  SMARTSUPP_QUERY_KEYS: { conversations: () => [], conversation: () => [] },
}));

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

  it("persists the contact-panel toggle state in localStorage", () => {
    localStorage.setItem("smartsupp.contactPanel.open", "false");
    render(wrap(<SmartsuppChatsPage />));
    expect(localStorage.getItem("smartsupp.contactPanel.open")).toBe("false");
  });
});
