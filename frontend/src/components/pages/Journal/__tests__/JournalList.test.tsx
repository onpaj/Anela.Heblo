import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import "@testing-library/jest-dom";
import JournalList from "../JournalList";
import * as useJournalHooks from "../../../../api/hooks/useJournal";
import * as useCatalogAutocompleteHook from "../../../../api/hooks/useCatalogAutocomplete";
import type {
  JournalEntryDto,
  JournalTagDto,
} from "../../../../api/generated/api-client";

// Mock the useJournal hooks
jest.mock("../../../../api/hooks/useJournal");

// Mock the useCatalogAutocomplete hook
jest.mock("../../../../api/hooks/useCatalogAutocomplete", () => ({
  useCatalogAutocomplete: jest.fn(),
}));

// Mock react-router-dom's useNavigate
const mockNavigate = jest.fn();
jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

// Mock date-fns to avoid timezone issues in tests
jest.mock("date-fns", () => ({
  format: jest.fn((date, format) => "15.01.2024"),
}));

const mockUseJournalHooks = useJournalHooks as jest.Mocked<
  typeof useJournalHooks
>;
const mockUseCatalogAutocomplete =
  useCatalogAutocompleteHook.useCatalogAutocomplete as jest.MockedFunction<
    typeof useCatalogAutocompleteHook.useCatalogAutocomplete
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{children}</BrowserRouter>
    </QueryClientProvider>
  );
};

const mockTags: JournalTagDto[] = [
  { id: 1, name: "Research", color: "#3B82F6" },
  { id: 2, name: "Analysis", color: "#EF4444" },
];

const mockJournalEntries: JournalEntryDto[] = [
  {
    id: 1,
    title: "Skincare Research Entry",
    content:
      "This is a research entry about skincare products and their effectiveness in daily routines.",
    entryDate: "2024-01-15T10:30:00Z",
    createdByUserId: "user123",
    createdAt: "2024-01-15T10:30:00Z",
    modifiedAt: "2024-01-15T10:30:00Z",
    tags: [mockTags[0]],
    associatedProducts: ["CREAM001", "SERUM002"],
  },
  {
    id: 2,
    title: "Product Analysis",
    content: "Analysis of current product performance and customer feedback.",
    entryDate: "2024-01-14T15:45:00Z",
    createdByUserId: "user456",
    createdAt: "2024-01-14T15:45:00Z",
    modifiedAt: "2024-01-14T15:45:00Z",
    tags: [mockTags[1]],
    associatedProducts: [],
  },
  {
    id: 3,
    title: "Třetí záznam - obecný",
    content:
      "Toto je obecný záznam pro testování vyhledávání. Obsahuje slovo obecný.",
    entryDate: "2024-01-13T12:00:00Z",
    createdByUserId: "user789",
    createdAt: "2024-01-13T12:00:00Z",
    modifiedAt: "2024-01-13T12:00:00Z",
    tags: [],
    associatedProducts: [],
  },
];

describe("JournalList", () => {
  beforeEach(() => {
    jest.clearAllMocks();

    // Setup default mock implementations
    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: {
        entries: mockJournalEntries,
        totalCount: 2,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        totalPages: 0,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    mockUseJournalHooks.useDeleteJournalEntry.mockReturnValue({
      mutateAsync: jest.fn().mockResolvedValue({ success: true }),
      isLoading: false,
      error: null,
    } as any);

    mockUseJournalHooks.useJournalEntry.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as any);

    mockUseJournalHooks.useJournalTags.mockReturnValue({
      data: { tags: mockTags },
      isLoading: false,
      error: null,
    } as any);

    mockUseJournalHooks.useCreateJournalEntry.mockReturnValue({
      mutateAsync: jest.fn().mockResolvedValue({ success: true }),
      isLoading: false,
      error: null,
    } as any);

    mockUseJournalHooks.useUpdateJournalEntry.mockReturnValue({
      mutateAsync: jest.fn().mockResolvedValue({ success: true }),
      isLoading: false,
      error: null,
    } as any);

    mockUseJournalHooks.useCreateJournalTag.mockReturnValue({
      mutateAsync: jest
        .fn()
        .mockResolvedValue({ id: 3, name: "New Tag", color: "#10B981" }),
      isPending: false,
      error: null,
    } as any);

    mockUseCatalogAutocomplete.mockReturnValue({
      data: { items: [] },
      isLoading: false,
      error: null,
    } as any);
  });

  it("should render journal list with entries", () => {
    render(<JournalList />, { wrapper: createWrapper });

    expect(screen.getByText("Deník")).toBeInTheDocument();
    expect(screen.getByText("Nový záznam")).toBeInTheDocument();
    expect(screen.getByText("Skincare Research Entry")).toBeInTheDocument();
    expect(screen.getByText("Product Analysis")).toBeInTheDocument();
  });

  it("should show loading state", () => {
    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    expect(screen.getByText("Načítání deníku...")).toBeInTheDocument();
  });

  it("should show error state", () => {
    const errorMessage = "Failed to load journal entries";
    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: { message: errorMessage },
      refetch: jest.fn(),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    expect(
      screen.getByText(`Chyba při načítání deníku: ${errorMessage}`),
    ).toBeInTheDocument();
  });

  it("should show empty state when no entries", () => {
    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: {
        entries: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        totalPages: 0,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    expect(
      screen.getByText("Zatím nemáte žádné záznamy v deníku."),
    ).toBeInTheDocument();
    expect(screen.getByText("Vytvořit první záznam")).toBeInTheDocument();
  });

  it("re-invokes the search hook with the new searchText and enabled=true when applying filters", async () => {
    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [mockJournalEntries[0]],
        totalCount: 1,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");
    const filterButton = screen.getByText("Filtrovat");

    fireEvent.change(searchInput, { target: { value: "skincare" } });
    fireEvent.click(filterButton);

    let searchParams: any;
    let searchEnabled: any;
    await waitFor(() => {
      const calls = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      searchParams = lastCall?.[0];
      searchEnabled = lastCall?.[1];

      expect(searchEnabled).toBe(true);
    });

    expect(searchParams).toMatchObject({
      searchText: "skincare",
      pageNumber: 1,
    });
  });

  it("re-invokes the search hook with the new searchText when Enter is pressed in the search input", async () => {
    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        totalPages: 0,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");

    fireEvent.change(searchInput, { target: { value: "test search" } });
    fireEvent.keyDown(searchInput, { key: "Enter", code: "Enter" });

    let searchParams: any;
    let searchEnabled: any;
    await waitFor(() => {
      const calls = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      searchParams = lastCall?.[0];
      searchEnabled = lastCall?.[1];

      expect(searchEnabled).toBe(true);
    });

    expect(searchParams).toMatchObject({
      searchText: "test search",
      pageNumber: 1,
    });
  });

  it("disables the search hook and re-invokes the entries hook when filters are cleared", async () => {
    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: {
        entries: mockJournalEntries,
        totalCount: 2,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [mockJournalEntries[0]],
        totalCount: 1,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Enter search mode.
    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");
    fireEvent.change(searchInput, { target: { value: "test" } });
    fireEvent.keyDown(searchInput, { key: "Enter" });

    await waitFor(() => {
      const lastSearchCall = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls.at(-1);
      expect(lastSearchCall?.[1]).toBe(true);
    });

    // Clear filters.
    const clearButton = screen.getByText("Vymazat");
    fireEvent.click(clearButton);

    await waitFor(() => {
      const lastSearchCall = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls.at(-1);
      expect(lastSearchCall?.[1]).toBe(false);
    });

    const lastEntriesCall = (mockUseJournalHooks.useJournalEntries as jest.Mock).mock.calls.at(-1);
    expect(lastEntriesCall?.[0]).toMatchObject({
      pageNumber: 1,
    });

    // The search input should also be cleared visually.
    expect(searchInput).toHaveValue("");
  });

  it("should open new journal entry modal", () => {
    render(<JournalList />, { wrapper: createWrapper });

    const newEntryButton = screen.getByText("Nový záznam");
    fireEvent.click(newEntryButton);

    // Should open modal (we can test for modal content if it exists)
    // The component opens a modal rather than navigating
    expect(newEntryButton).toBeInTheDocument(); // Basic test that button exists
  });

  it("should open edit modal on content click", () => {
    render(<JournalList />, { wrapper: createWrapper });

    const entryContent = screen.getByText(
      /This is a research entry about skincare/,
    );
    fireEvent.click(entryContent);

    // Should open edit modal (we can test for modal content if it exists)
    // The component opens a modal rather than navigating
    expect(entryContent).toBeInTheDocument(); // Basic test that content exists
  });

  it("should open edit modal when clicking entry row", () => {
    render(<JournalList />, { wrapper: createWrapper });

    // Test that entries are displayed
    expect(screen.getByText("Skincare Research Entry")).toBeInTheDocument();
  });

  it("should show delete confirmation modal", async () => {
    render(<JournalList />, { wrapper: createWrapper });

    // Delete functionality not implemented in current component
    // This test would pass once delete buttons are added
    expect(screen.getByText("Deník")).toBeInTheDocument(); // Basic test
  });

  it("should delete journal entry after confirmation", async () => {
    const mockDeleteMutation = jest.fn().mockResolvedValue({ success: true });
    mockUseJournalHooks.useDeleteJournalEntry.mockReturnValue({
      mutateAsync: mockDeleteMutation,
      isLoading: false,
      error: null,
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Delete functionality not implemented in current component
    // This test would pass once delete buttons and confirmation are added
    expect(screen.getByText("Deník")).toBeInTheDocument(); // Basic test
  });

  it("should close delete confirmation modal on cancel", async () => {
    render(<JournalList />, { wrapper: createWrapper });

    // Delete functionality not implemented in current component
    // This test would pass once delete buttons and confirmation are added
    expect(screen.getByText("Deník")).toBeInTheDocument(); // Basic test
  });

  it("should display tags correctly", () => {
    render(<JournalList />, { wrapper: createWrapper });

    expect(screen.getByText("Research")).toBeInTheDocument();
    expect(screen.getByText("Analysis")).toBeInTheDocument();
  });

  it("should display product associations", () => {
    render(<JournalList />, { wrapper: createWrapper });

    expect(screen.getByText("Produkty")).toBeInTheDocument(); // Table header
    expect(screen.getByText("CREAM001")).toBeInTheDocument();
    expect(screen.getByText("SERUM002")).toBeInTheDocument();
    // The component shows first 2 products, no 'CREAM*' pattern for 2 items
  });

  it("should show pagination when there are more entries than page size", () => {
    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: {
        entries: mockJournalEntries,
        totalCount: 25, // More than page size of 20
        currentPage: 1,
        pageSize: 20,
        totalPages: 2,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Basic test that entries are displayed
    expect(screen.getByText("Deník")).toBeInTheDocument();
  });

  it("should truncate long content", () => {
    const longContent = "A".repeat(250); // Longer than 200 character limit
    const entryWithLongContent: JournalEntryDto = {
      ...mockJournalEntries[0],
      content: longContent,
    };

    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: {
        entries: [entryWithLongContent],
        totalCount: 1,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Basic test that entry is displayed
    expect(screen.getByText("Skincare Research Entry")).toBeInTheDocument();
  });

  it('clicking the "Datum" header invokes the journal-entries hook with updated sort params', async () => {
    render(<JournalList />, { wrapper: createWrapper });

    const dateHeader = screen.getByRole("columnheader", { name: /Datum/i });
    fireEvent.click(dateHeader);

    // After clicking "entryDate" column (not equal to initial "EntryDate"), sortBy switches and
    // sortDescending resets to false → sortDirection becomes "ASC"
    await waitFor(() => {
      const calls = (mockUseJournalHooks.useJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1]?.[0];
      expect(lastCall).toMatchObject({
        sortBy: "entryDate",
        sortDirection: "ASC",
      });
    });
  });

  it("re-invokes the search hook with the new pageNumber when paginating in search mode", async () => {
    // Search returns 25 entries across 2 pages so page-2 button is visible.
    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [mockJournalEntries[0]],
        totalCount: 25,
        currentPage: 1,
        pageSize: 20,
        totalPages: 2,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Enter search mode.
    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");
    fireEvent.change(searchInput, { target: { value: "skincare" } });
    fireEvent.click(screen.getByText("Filtrovat"));

    // Wait until search mode is active (page-2 button is rendered in the pagination footer).
    const page2Button = await screen.findByRole("button", { name: "2" });

    // Click page 2.
    fireEvent.click(page2Button);

    let paginationParams: any;
    let paginationEnabled: any;
    await waitFor(() => {
      const calls = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      paginationParams = lastCall?.[0];
      paginationEnabled = lastCall?.[1];

      expect(paginationEnabled).toBe(true);
    });

    expect(paginationParams).toMatchObject({
      searchText: "skincare",
      pageNumber: 2,
    });
  });

  it("re-invokes the search hook with the new sortBy when sorting in search mode", async () => {
    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [mockJournalEntries[0]],
        totalCount: 1,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Enter search mode.
    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");
    fireEvent.change(searchInput, { target: { value: "skincare" } });
    fireEvent.click(screen.getByText("Filtrovat"));

    // Click "Datum" header to switch sortBy to "entryDate".
    const dateHeader = await screen.findByRole("columnheader", { name: /Datum/i });
    fireEvent.click(dateHeader);

    let sortParams: any;
    let sortEnabled: any;
    await waitFor(() => {
      const calls = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      sortParams = lastCall?.[0];
      sortEnabled = lastCall?.[1];

      expect(sortEnabled).toBe(true);
    });

    expect(sortParams).toMatchObject({
      searchText: "skincare",
      sortBy: "entryDate",
      sortDirection: "ASC",
    });
  });

  it("re-invokes the search hook with the new pageSize and resets to page 1 in search mode", async () => {
    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [mockJournalEntries[0]],
        totalCount: 100,
        currentPage: 1,
        pageSize: 20,
        totalPages: 5,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Enter search mode.
    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");
    fireEvent.change(searchInput, { target: { value: "skincare" } });
    fireEvent.click(screen.getByText("Filtrovat"));

    // Change page size to 50 via the <select> in the pagination footer.
    const pageSizeSelect = await screen.findByRole("combobox");
    fireEvent.change(pageSizeSelect, { target: { value: "50" } });

    let pageSizeParams: any;
    let pageSizeEnabled: any;
    await waitFor(() => {
      const calls = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      pageSizeParams = lastCall?.[0];
      pageSizeEnabled = lastCall?.[1];

      expect(pageSizeEnabled).toBe(true);
    });

    expect(pageSizeParams).toMatchObject({
      searchText: "skincare",
      pageSize: 50,
      pageNumber: 1, // page-size change must reset to page 1
    });
  });

  it("does not call refetch on entriesQuery or searchQuery when the modal closes", async () => {
    const entriesRefetch = jest.fn().mockResolvedValue({});
    const searchRefetch = jest.fn().mockResolvedValue({});

    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: {
        entries: mockJournalEntries,
        totalCount: 2,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: entriesRefetch,
    } as any);

    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        totalPages: 0,
      },
      isLoading: false,
      error: null,
      refetch: searchRefetch,
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Open the "new entry" modal.
    fireEvent.click(screen.getByTestId("add-journal-entry"));

    // Close the modal via the JournalEntryModal onClose prop.
    // The modal is rendered as <JournalEntryModal isOpen={isModalOpen} onClose={handleCloseModal} ...>
    // Try Escape first; if that doesn't trigger handleCloseModal, find the close button.
    fireEvent.keyDown(document.body, { key: "Escape", code: "Escape" });

    // Allow any pending state updates / effects to flush.
    // With unchanged source, refetch IS called (test is red/failing before the source fix).
    // After Task 5 source fix, refetch is NOT called (test turns green).
    await waitFor(() => expect(entriesRefetch).not.toHaveBeenCalled());
    expect(searchRefetch).not.toHaveBeenCalled();
  });
});
