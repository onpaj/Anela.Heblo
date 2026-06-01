import React from "react";
import { render, screen, fireEvent, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import JournalEntryForm from "../JournalEntryForm";
import { useCreateJournalEntry, useUpdateJournalEntry, useJournalTags, useCreateJournalTag } from "../../api/hooks/useJournal";
import { useCatalogAutocomplete } from "../../api/hooks/useCatalogAutocomplete";

jest.mock("../../api/hooks/useJournal");
jest.mock("../../api/hooks/useCatalogAutocomplete");
jest.mock("react-router-dom", () => ({ useNavigate: () => jest.fn() }));
jest.mock("react-select", () => ({
  __esModule: true,
  default: jest.fn(() => null),
  components: { Option: () => null, SingleValue: () => null },
}));

const mockUseCreateJournalEntry = useCreateJournalEntry as jest.Mock;
const mockUseUpdateJournalEntry = useUpdateJournalEntry as jest.Mock;
const mockUseJournalTags = useJournalTags as jest.Mock;
const mockUseCreateJournalTag = useCreateJournalTag as jest.Mock;
const mockUseCatalogAutocomplete = useCatalogAutocomplete as jest.Mock;

const createTestQueryClient = () =>
  new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });

const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <QueryClientProvider client={createTestQueryClient()}>{children}</QueryClientProvider>
);

describe("JournalEntryForm — associated products", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUseCreateJournalEntry.mockReturnValue({ mutateAsync: jest.fn(), isPending: false });
    mockUseUpdateJournalEntry.mockReturnValue({ mutateAsync: jest.fn(), isPending: false });
    mockUseJournalTags.mockReturnValue({ data: { tags: [] } });
    mockUseCreateJournalTag.mockReturnValue({ mutateAsync: jest.fn(), isPending: false });
    mockUseCatalogAutocomplete.mockReturnValue({ data: { items: [] }, isLoading: false, error: null });
  });

  it("shows autocomplete (no plain text input) when in Autocomplete mode", () => {
    render(
      <TestWrapper>
        <JournalEntryForm />
      </TestWrapper>
    );

    // Plain text product input is not rendered in autocomplete mode
    expect(screen.queryByPlaceholderText(/produktový prefix/)).not.toBeInTheDocument();
  });

  it("shows a plain text input when Text mode is selected", async () => {
    const user = userEvent.setup();

    render(
      <TestWrapper>
        <JournalEntryForm />
      </TestWrapper>
    );

    const textButton = screen.getByRole("button", { name: /Text/i });
    await user.click(textButton);

    expect(
      screen.getByPlaceholderText(/produktový prefix/)
    ).toBeInTheDocument();
  });

  it("adds a product on Enter in Text mode", async () => {
    const user = userEvent.setup();

    render(
      <TestWrapper>
        <JournalEntryForm />
      </TestWrapper>
    );

    await user.click(screen.getByRole("button", { name: /Text/i }));

    const input = screen.getByPlaceholderText(/produktový prefix/);
    await user.type(input, "DEO");
    await user.keyboard("{Enter}");

    expect(screen.getByText("DEO")).toBeInTheDocument();
    // Input is cleared after adding
    expect(input).toHaveValue("");
  });

  it("adds a product on blur in Text mode", async () => {
    const user = userEvent.setup();

    render(
      <TestWrapper>
        <JournalEntryForm />
      </TestWrapper>
    );

    await user.click(screen.getByRole("button", { name: /Text/i }));

    const input = screen.getByPlaceholderText(/produktový prefix/);
    await user.type(input, "COSM");
    await user.tab(); // triggers blur

    expect(screen.getByText("COSM")).toBeInTheDocument();
  });

  it("does not add duplicate products in Text mode", async () => {
    const user = userEvent.setup();

    render(
      <TestWrapper>
        <JournalEntryForm />
      </TestWrapper>
    );

    await user.click(screen.getByRole("button", { name: /Text/i }));
    const input = screen.getByPlaceholderText(/produktový prefix/);

    await user.type(input, "DEO");
    await user.keyboard("{Enter}");
    await user.type(input, "DEO");
    await user.keyboard("{Enter}");

    const badges = screen.getAllByText("DEO");
    expect(badges).toHaveLength(1);
  });
});
