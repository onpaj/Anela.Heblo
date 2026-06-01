import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import { MemoryRouter } from 'react-router-dom';
import LeafletDocumentsTab from '../LeafletDocumentsTab';
import * as useLeafletHooks from '../../../api/hooks/useLeaflet';

jest.mock('lucide-react', () => ({
  Trash2: () => <svg data-testid="icon-trash" />,
  ChevronUp: () => <svg />,
  ChevronDown: () => <svg />,
  ChevronLeft: () => <svg />,
  ChevronRight: () => <svg />,
  Filter: () => <svg />,
  Search: () => <svg />,
  X: () => <svg />,
}));

jest.mock('../LeafletChunkDetailModal', () => ({
  __esModule: true,
  default: ({ onClose }: { onClose: () => void }) => (
    <div data-testid="chunk-detail-modal">
      <button onClick={onClose}>Zavřít</button>
    </div>
  ),
}));

jest.mock('../../../api/hooks/useLeaflet', () => ({
  useLeafletDocumentsQuery: jest.fn(),
  useLeafletContentTypesQuery: jest.fn(),
  useDeleteLeafletDocumentMutation: jest.fn(),
}));

const mockUseLeafletDocumentsQuery = useLeafletHooks.useLeafletDocumentsQuery as jest.Mock;
const mockUseLeafletContentTypesQuery = useLeafletHooks.useLeafletContentTypesQuery as jest.Mock;
const mockUseDeleteLeafletDocumentMutation = useLeafletHooks.useDeleteLeafletDocumentMutation as jest.Mock;

const makeDocument = (overrides: Partial<useLeafletHooks.LeafletDocumentSummary> = {}): useLeafletHooks.LeafletDocumentSummary => ({
  id: 'doc-1',
  filename: 'test-leaflet.pdf',
  status: 'indexed',
  contentType: 'application/pdf',
  createdAt: '2024-01-15T10:00:00Z',
  indexedAt: '2024-01-15T10:05:00Z',
  firstChunkId: 'chunk-1',
  ...overrides,
});

const mockDocumentsResponse = (documents: useLeafletHooks.LeafletDocumentSummary[] = [makeDocument()]) => ({
  data: {
    success: true,
    documents,
    totalCount: documents.length,
    pageNumber: 1,
    pageSize: 20,
    totalPages: 1,
  },
  isLoading: false,
  error: null,
});

function renderTab(canDelete = false) {
  return render(
    <MemoryRouter>
      <LeafletDocumentsTab canDelete={canDelete} />
    </MemoryRouter>
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  mockUseLeafletContentTypesQuery.mockReturnValue({ data: { contentTypes: ['application/pdf'] } });
  mockUseDeleteLeafletDocumentMutation.mockReturnValue({ mutateAsync: jest.fn() });
});

describe('LeafletDocumentsTab', () => {
  it('renders document list', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse());
    renderTab();
    expect(screen.getByText('test-leaflet.pdf')).toBeInTheDocument();
  });

  it('renders without table rows while loading', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    });
    renderTab();
    // No document content visible while loading
    expect(screen.queryByText('test-leaflet.pdf')).not.toBeInTheDocument();
    expect(screen.queryByText('Nepodařilo se načíst dokumenty.')).not.toBeInTheDocument();
  });

  it('shows error message on fetch failure', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Network error'),
    });
    renderTab();
    expect(screen.getByText('Nepodařilo se načíst dokumenty.')).toBeInTheDocument();
  });

  it('shows empty state when no documents match', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse([]));
    renderTab();
    expect(screen.getByText('Žádné dokumenty neodpovídají zadaným filtrům.')).toBeInTheDocument();
  });

  it('does not show delete button when canDelete is false', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse());
    renderTab(false);
    expect(screen.queryByTestId('icon-trash')).not.toBeInTheDocument();
  });

  it('shows delete button when canDelete is true', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse());
    renderTab(true);
    expect(screen.getByTestId('icon-trash')).toBeInTheDocument();
  });

  it('opens confirm dialog when delete button clicked', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse());
    renderTab(true);
    fireEvent.click(screen.getByTitle('Smazat dokument'));
    expect(screen.getByText('Smazat dokument?')).toBeInTheDocument();
    expect(screen.getAllByText('test-leaflet.pdf').length).toBeGreaterThanOrEqual(1);
  });

  it('closes confirm dialog when Zrušit is clicked', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse());
    renderTab(true);
    fireEvent.click(screen.getByTitle('Smazat dokument'));
    fireEvent.click(screen.getByRole('button', { name: 'Zrušit' }));
    expect(screen.queryByText('Smazat dokument?')).not.toBeInTheDocument();
  });

  it('calls deleteDocument mutation when Smazat confirmed', async () => {
    const mockMutateAsync = jest.fn().mockResolvedValue({ success: true });
    mockUseDeleteLeafletDocumentMutation.mockReturnValue({ mutateAsync: mockMutateAsync });
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse());
    renderTab(true);
    fireEvent.click(screen.getByTitle('Smazat dokument'));
    fireEvent.click(screen.getByRole('button', { name: 'Smazat' }));
    expect(mockMutateAsync).toHaveBeenCalledWith('doc-1');
  });

  it('opens chunk detail modal when row with firstChunkId is clicked', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse());
    renderTab();
    fireEvent.click(screen.getByText('test-leaflet.pdf'));
    expect(screen.getByTestId('chunk-detail-modal')).toBeInTheDocument();
  });

  it('does not open chunk detail modal when row has no firstChunkId', () => {
    const doc = makeDocument({ firstChunkId: null });
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse([doc]));
    renderTab();
    fireEvent.click(screen.getByText('test-leaflet.pdf'));
    expect(screen.queryByTestId('chunk-detail-modal')).not.toBeInTheDocument();
  });

  it('renders filename and status columns but no DocumentType column', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse());
    renderTab();
    expect(screen.getByText('Soubor')).toBeInTheDocument();
    expect(screen.getByText('Stav')).toBeInTheDocument();
    // No "Typ" column header (DocumentType is absent in leaflet tab)
    expect(screen.queryByText('Typ')).not.toBeInTheDocument();
  });

  it('applies filename filter on Enter key', () => {
    mockUseLeafletDocumentsQuery.mockReturnValue(mockDocumentsResponse());
    renderTab();
    const filenameInput = screen.getByPlaceholderText('Název souboru...');
    fireEvent.change(filenameInput, { target: { value: 'leaflet' } });
    fireEvent.keyDown(filenameInput, { key: 'Enter' });
    // After applying filter, query should be called with filenameFilter
    expect(mockUseLeafletDocumentsQuery).toHaveBeenCalledWith(
      expect.objectContaining({ filenameFilter: 'leaflet' })
    );
  });
});
