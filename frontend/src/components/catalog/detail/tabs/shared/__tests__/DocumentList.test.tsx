import { render, screen } from '@testing-library/react';
import DocumentList from '../DocumentList';
import type { CatalogDocumentDto } from '../../../../../../api/hooks/useCatalogDocuments';

const makeFile = (overrides?: Partial<CatalogDocumentDto>): CatalogDocumentDto => ({
  name: 'COA__L001__Bisabolol.pdf',
  webUrl: 'https://sp.example.com/file.pdf',
  sizeBytes: 102400,
  modifiedAt: '2026-05-01T12:00:00Z',
  ...overrides,
});

describe('DocumentList', () => {
  it('shows empty state when no files', () => {
    render(<DocumentList files={[]} isLoading={false} />);
    expect(screen.getByText(/Žádné dokumenty/i)).toBeInTheDocument();
  });

  it('shows loading state', () => {
    render(<DocumentList files={[]} isLoading={true} />);
    expect(screen.getByText(/Načítání/i)).toBeInTheDocument();
  });

  it('renders filename and size', () => {
    render(<DocumentList files={[makeFile()]} isLoading={false} />);
    expect(screen.getByText('COA__L001__Bisabolol.pdf')).toBeInTheDocument();
    expect(screen.getByText(/100 KB/i)).toBeInTheDocument();
  });

  it('renders a link that opens webUrl in new tab', () => {
    render(<DocumentList files={[makeFile()]} isLoading={false} />);
    const link = screen.getByRole('link', { name: /COA__L001__Bisabolol.pdf/i });
    expect(link).toHaveAttribute('href', 'https://sp.example.com/file.pdf');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
  });
});
