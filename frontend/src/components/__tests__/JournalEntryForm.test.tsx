import React from "react";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import JournalEntryForm from "../JournalEntryForm";
import { useCreateJournalEntry, useUpdateJournalEntry, useJournalTags, useCreateJournalTag } from "../../api/hooks/useJournal";
import { useCatalogAutocomplete } from "../../api/hooks/useCatalogAutocomplete";
import { ThemeProvider } from "../../contexts/ThemeContext";
import {
  ErrorCodes,
  CreateJournalEntryRequest,
  UpdateJournalEntryRequest,
  GetJournalEntryResponse,
} from "../../api/generated/api-client";

jest.mock("../../api/hooks/useJournal");
jest.mock("../../api/hooks/useCatalogAutocomplete");
jest.mock("../../contexts/ThemeContext", () => ({
  useTheme: () => ({ theme: "light", toggle: jest.fn() }),
  ThemeProvider: ({ children }: any) => children,
}));
const mockNavigate = jest.fn();
jest.mock("react-router-dom", () => ({ useNavigate: () => mockNavigate }));
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
  <QueryClientProvider client={createTestQueryClient()}>
    <ThemeProvider>{children}</ThemeProvider>
  </QueryClientProvider>
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

describe("JournalEntryForm - handleSave", () => {
  const createMutateAsync = jest.fn();
  const updateMutateAsync = jest.fn();
  let consoleErrorSpy: jest.SpyInstance;

  const entryFixture = Object.assign(new GetJournalEntryResponse(), {
    entry: {
      id: 7,
      title: "Existing title",
      content: "Existing content",
      entryDate: new Date("2024-03-15T00:00:00"),
      tags: [],
      associatedProducts: [],
    },
  }) as GetJournalEntryResponse;

  const renderForm = (props: React.ComponentProps<typeof JournalEntryForm> = {}) =>
    render(
      <TestWrapper>
        <JournalEntryForm {...props} />
      </TestWrapper>
    );

  const fillTitleAndContent = (title: string, content: string) => {
    fireEvent.change(screen.getByPlaceholderText("Zadejte název záznamu"), { target: { value: title } });
    fireEvent.change(screen.getByPlaceholderText("Zadejte obsah záznamu..."), { target: { value: content } });
  };

  beforeEach(() => {
    jest.clearAllMocks();
    mockNavigate.mockClear();
    createMutateAsync.mockReset();
    updateMutateAsync.mockReset();
    mockUseCreateJournalEntry.mockReturnValue({ mutateAsync: createMutateAsync, isPending: false });
    mockUseUpdateJournalEntry.mockReturnValue({ mutateAsync: updateMutateAsync, isPending: false });
    mockUseJournalTags.mockReturnValue({ data: { tags: [] } });
    mockUseCreateJournalTag.mockReturnValue({ mutateAsync: jest.fn(), isPending: false });
    mockUseCatalogAutocomplete.mockReturnValue({ data: { items: [] }, isLoading: false, error: null });
    consoleErrorSpy = jest.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    consoleErrorSpy.mockRestore();
  });

  it("shows title error and does not save when title is empty", async () => {
    renderForm();
    fillTitleAndContent("", "Some content");
    fireEvent.click(screen.getByRole("button", { name: /Vytvořit záznam/ }));

    expect(await screen.findByText("Název je povinný")).toBeInTheDocument();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(updateMutateAsync).not.toHaveBeenCalled();
  });

  it("shows content error when content is only whitespace", async () => {
    renderForm();
    fillTitleAndContent("Title", "   ");
    fireEvent.click(screen.getByRole("button", { name: /Vytvořit záznam/ }));

    expect(await screen.findByText("Obsah je povinný")).toBeInTheDocument();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(updateMutateAsync).not.toHaveBeenCalled();
  });

  it("shows date error when the date is cleared", async () => {
    renderForm();
    fillTitleAndContent("Title", "Content");
    fireEvent.change(screen.getByLabelText(/Datum záznamu/), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: /Vytvořit záznam/ }));

    expect(await screen.findByText("Datum je povinné")).toBeInTheDocument();
    expect(createMutateAsync).not.toHaveBeenCalled();
    expect(updateMutateAsync).not.toHaveBeenCalled();
  });

  it("creates an entry with trimmed fields and calls onSave", async () => {
    const onSave = jest.fn();
    createMutateAsync.mockResolvedValue({ id: 1 });
    renderForm({ onSave });
    fillTitleAndContent("  My title  ", "  My content  ");
    fireEvent.click(screen.getByRole("button", { name: /Vytvořit záznam/ }));

    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ id: 1 }));
    expect(createMutateAsync).toHaveBeenCalledTimes(1);
    const request = createMutateAsync.mock.calls[0][0];
    expect(request).toBeInstanceOf(CreateJournalEntryRequest);
    expect(request.title).toBe("My title");
    expect(request.content).toBe("My content");
    expect(updateMutateAsync).not.toHaveBeenCalled();
  });

  it("navigates to /journal after create when no onSave is provided", async () => {
    createMutateAsync.mockResolvedValue({ id: 1 });
    renderForm();
    fillTitleAndContent("Title", "Content");
    fireEvent.click(screen.getByRole("button", { name: /Vytvořit záznam/ }));

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith("/journal"));
  });

  it("updates an existing entry in edit mode", async () => {
    const onSave = jest.fn();
    updateMutateAsync.mockResolvedValue({ id: 7 });
    renderForm({ isEdit: true, entry: entryFixture, onSave });
    fireEvent.click(screen.getByRole("button", { name: /Uložit změny/ }));

    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ id: 7 }));
    expect(updateMutateAsync).toHaveBeenCalledWith({
      id: 7,
      request: expect.any(UpdateJournalEntryRequest),
    });
    expect(createMutateAsync).not.toHaveBeenCalled();
  });

  it("shows title error without logging when the server returns InvalidJournalTitle", async () => {
    const onSave = jest.fn();
    createMutateAsync.mockRejectedValue({ errorCode: ErrorCodes.InvalidJournalTitle });
    renderForm({ onSave });
    fillTitleAndContent("Title", "Content");
    fireEvent.click(screen.getByRole("button", { name: /Vytvořit záznam/ }));

    expect(await screen.findByText("Název je povinný")).toBeInTheDocument();
    expect(consoleErrorSpy).not.toHaveBeenCalled();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("logs unexpected errors and shows no validation message", async () => {
    const onSave = jest.fn();
    createMutateAsync.mockRejectedValue(new Error("boom"));
    renderForm({ onSave });
    fillTitleAndContent("Title", "Content");
    fireEvent.click(screen.getByRole("button", { name: /Vytvořit záznam/ }));

    await waitFor(() =>
      expect(consoleErrorSpy).toHaveBeenCalledWith("Error saving journal entry:", expect.any(Error))
    );
    expect(screen.queryByText("Název je povinný")).not.toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
  });
});
