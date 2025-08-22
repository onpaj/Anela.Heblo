import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import '@testing-library/jest-dom';
import JournalList from '../JournalList';
import * as useJournalHooks from '../../../../api/hooks/useJournal';
import type { JournalEntryDto, JournalTagDto } from '../../../../api/generated/api-client';

// Mock the useJournal hooks
jest.mock('../../../../api/hooks/useJournal');

// Mock react-router-dom's useNavigate
const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => ({
  ...jest.requireActual('react-router-dom'),
  useNavigate: () => mockNavigate,
}));

// Mock date-fns to avoid timezone issues in tests
jest.mock('date-fns', () => ({
  format: jest.fn((date, format) => '15.01.2024'),
}));

const mockUseJournalHooks = useJournalHooks as jest.Mocked<typeof useJournalHooks>;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {children}
      </BrowserRouter>
    </QueryClientProvider>
  );
};

const mockTags: JournalTagDto[] = [
  { id: 1, name: 'Research', color: '#3B82F6' },
  { id: 2, name: 'Analysis', color: '#EF4444' },
];

const mockJournalEntries: JournalEntryDto[] = [
  {
    id: 1,
    title: 'Skincare Research Entry',
    content: 'This is a research entry about skincare products and their effectiveness in daily routines.',
    entryDate: '2024-01-15T10:30:00Z',
    createdByUserId: 'user123',
    createdAt: '2024-01-15T10:30:00Z',
    modifiedAt: '2024-01-15T10:30:00Z',
    tags: [mockTags[0]],
    associatedProducts: ['CREAM001', 'SERUM002'],
  },
  {
    id: 2,
    title: 'Product Analysis',
    content: 'Analysis of current product performance and customer feedback.',
    entryDate: '2024-01-14T15:45:00Z',
    createdByUserId: 'user456',
    createdAt: '2024-01-14T15:45:00Z',
    modifiedAt: '2024-01-14T15:45:00Z',
    tags: [mockTags[1]],
    associatedProducts: [],
  },
  {
    id: 3,
    title: 'Třetí záznam - obecný',
    content: 'Toto je obecný záznam pro testování vyhledávání. Obsahuje slovo obecný.',
    entryDate: '2024-01-13T12:00:00Z',
    createdByUserId: 'user789',
    createdAt: '2024-01-13T12:00:00Z',
    modifiedAt: '2024-01-13T12:00:00Z',
    tags: [],
    associatedProducts: [],
  },
];

describe('JournalList', () => {
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
      data: mockTags,
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
  });

  it('should render journal list with entries', () => {
    render(<JournalList />, { wrapper: createWrapper });

    expect(screen.getByText('Deník')).toBeInTheDocument();
    expect(screen.getByText('Nový záznam')).toBeInTheDocument();
    expect(screen.getByText('Skincare Research Entry')).toBeInTheDocument();
    expect(screen.getByText('Product Analysis')).toBeInTheDocument();
  });

  it('should show loading state', () => {
    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    expect(screen.getByText('Načítání deníku...')).toBeInTheDocument();
  });

  it('should show error state', () => {
    const errorMessage = 'Failed to load journal entries';
    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: { message: errorMessage },
      refetch: jest.fn(),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    expect(screen.getByText(`Chyba při načítání deníku: ${errorMessage}`)).toBeInTheDocument();
  });

  it('should show empty state when no entries', () => {
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

    expect(screen.getByText('Zatím nemáte žádné záznamy v deníku.')).toBeInTheDocument();
    expect(screen.getByText('Vytvořit první záznam')).toBeInTheDocument();
  });

  it('should handle search input and apply search', async () => {
    const mockSearchRefetch = jest.fn().mockResolvedValue({});
    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [mockJournalEntries[0]], // Return first entry as search result
        totalCount: 1,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: mockSearchRefetch,
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    const searchInput = screen.getByPlaceholderText('Hledat v záznamech...');
    const searchButton = screen.getByText('Hledat');

    fireEvent.change(searchInput, { target: { value: 'skincare' } });
    fireEvent.click(searchButton);

    await waitFor(() => {
      expect(mockSearchRefetch).toHaveBeenCalled();
    });

    expect(screen.getByText('Nalezeno 1 záznamů pro "skincare"')).toBeInTheDocument();
  });

  it('should handle Enter key press in search input', async () => {
    const mockSearchRefetch = jest.fn().mockResolvedValue({});
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
      refetch: mockSearchRefetch,
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    const searchInput = screen.getByPlaceholderText('Hledat v záznamech...');

    fireEvent.change(searchInput, { target: { value: 'test search' } });
    fireEvent.keyDown(searchInput, { key: 'Enter', code: 'Enter' });

    await waitFor(() => {
      expect(mockSearchRefetch).toHaveBeenCalled();
    });
  });

  it('should clear search and return to normal mode', async () => {
    const mockRefetch = jest.fn().mockResolvedValue({});
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
      refetch: mockRefetch,
    } as any);

    // Start with search mode active
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
      refetch: jest.fn(),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Trigger search first
    const searchInput = screen.getByPlaceholderText('Hledat v záznamech...');
    fireEvent.change(searchInput, { target: { value: 'test' } });
    fireEvent.keyDown(searchInput, { key: 'Enter' });

    await waitFor(() => {
      expect(screen.getByText('Zrušit')).toBeInTheDocument();
    });

    // Clear search
    const clearButton = screen.getByText('Zrušit');
    fireEvent.click(clearButton);

    await waitFor(() => {
      expect(mockRefetch).toHaveBeenCalled();
    });
  });

  it('should navigate to new journal entry page', () => {
    render(<JournalList />, { wrapper: createWrapper });

    const newEntryButton = screen.getByText('Nový záznam');
    fireEvent.click(newEntryButton);

    expect(mockNavigate).toHaveBeenCalledWith('/journal/new');
  });

  it('should navigate to journal entry detail on content click', () => {
    render(<JournalList />, { wrapper: createWrapper });

    const entryContent = screen.getByText(/This is a research entry about skincare/);
    fireEvent.click(entryContent);

    expect(mockNavigate).toHaveBeenCalledWith('/journal/1');
  });

  it('should navigate to edit journal entry', () => {
    render(<JournalList />, { wrapper: createWrapper });

    const editButtons = screen.getAllByTitle('Edit');
    fireEvent.click(editButtons[0]);

    expect(mockNavigate).toHaveBeenCalledWith('/journal/1/edit');
  });

  it('should show delete confirmation modal', async () => {
    render(<JournalList />, { wrapper: createWrapper });

    const deleteButtons = screen.getAllByTitle('Trash2');
    fireEvent.click(deleteButtons[0]);

    await waitFor(() => {
      expect(screen.getByText('Smazat záznam')).toBeInTheDocument();
    });
    
    expect(screen.getByText('Opravdu chcete smazat tento záznam? Tuto akci nelze vrátit zpět.')).toBeInTheDocument();
  });

  it('should delete journal entry after confirmation', async () => {
    const mockDeleteMutation = jest.fn().mockResolvedValue({ success: true });
    mockUseJournalHooks.useDeleteJournalEntry.mockReturnValue({
      mutateAsync: mockDeleteMutation,
      isLoading: false,
      error: null,
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Open delete modal
    const deleteButtons = screen.getAllByTitle('Trash2');
    fireEvent.click(deleteButtons[0]);

    await waitFor(() => {
      expect(screen.getByText('Smazat')).toBeInTheDocument();
    });

    // Confirm deletion
    const confirmDeleteButton = screen.getByText('Smazat');
    fireEvent.click(confirmDeleteButton);

    await waitFor(() => {
      expect(mockDeleteMutation).toHaveBeenCalledWith(1);
    });
  });

  it('should close delete confirmation modal on cancel', async () => {
    render(<JournalList />, { wrapper: createWrapper });

    // Open delete modal
    const deleteButtons = screen.getAllByTitle('Trash2');
    fireEvent.click(deleteButtons[0]);

    await waitFor(() => {
      expect(screen.getByText('Zrušit')).toBeInTheDocument();
    });

    // Cancel deletion
    const cancelButton = screen.getByText('Zrušit');
    fireEvent.click(cancelButton);

    await waitFor(() => {
      expect(screen.queryByText('Smazat záznam')).not.toBeInTheDocument();
    });
  });

  it('should display tags correctly', () => {
    render(<JournalList />, { wrapper: createWrapper });

    expect(screen.getByText('Research')).toBeInTheDocument();
    expect(screen.getByText('Analysis')).toBeInTheDocument();
  });

  it('should display product associations', () => {
    render(<JournalList />, { wrapper: createWrapper });

    expect(screen.getByText('Produkty:')).toBeInTheDocument();
    expect(screen.getByText('CREAM001')).toBeInTheDocument();
    expect(screen.getByText('SERUM002')).toBeInTheDocument();
    expect(screen.getByText('CREAM*')).toBeInTheDocument();
  });

  it('should show pagination when there are more entries than page size', () => {
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

    expect(screen.getByText('Zobrazeno 1 až 2 z 25 záznamů')).toBeInTheDocument();
    expect(screen.getByText('Stránka 1 z 2')).toBeInTheDocument();
  });

  it('should truncate long content', () => {
    const longContent = 'A'.repeat(250); // Longer than 200 character limit
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

    // Should show truncated content with ellipsis
    // The content should be truncated and contain ellipsis at the end
    const contentElement = screen.getByText((content, element) => {
      return element?.textContent && 
             element.textContent.includes('AAA') &&
             element.textContent.includes('...') &&
             element.textContent.length < longContent.length;
    });
    expect(contentElement).toBeInTheDocument();
  });
});